using System.Diagnostics;
using System.Runtime.InteropServices;
using ThreadOptimization.Models;

namespace ThreadOptimization.Services;

/// <summary>
/// CPU 亲和性管理服务
/// </summary>
public class AffinityService
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessAffinityMask(IntPtr hProcess, IntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenThread(int dwDesiredAccess, bool bInheritHandle, int dwThreadId);
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(IntPtr hProcess, uint dwPriorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessPriorityBoost(IntPtr hProcess, bool disablePriorityBoost);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetInformationProcess(IntPtr processHandle, int processInformationClass, ref int processInformation, int processInformationLength);
    
    private const int THREAD_SET_INFORMATION = 0x0020;
    private const int THREAD_QUERY_INFORMATION = 0x0040;
    private const int ProcessIoPriority = 33;
    
    // IO 优先级常量
    public const int IO_PRIORITY_VERY_LOW = 0;
    public const int IO_PRIORITY_LOW = 1;
    public const int IO_PRIORITY_NORMAL = 2;
    public const int IO_PRIORITY_HIGH = 3;

    private readonly ProcessService _processService;
    private CancellationTokenSource? _monitorCts;
    private Task? _monitorTask;
    
    public RunStatistics Statistics { get; } = new();

    public AffinityService(ProcessService processService)
    {
        _processService = processService;
    }

    /// <summary>
    /// 设置进程的 CPU 亲和性
    /// </summary>
    public bool SetProcessAffinity(int processId, long affinityMask)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.ProcessorAffinity = (IntPtr)affinityMask;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置亲和性失败: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// 设置进程优先级
    /// </summary>
    public bool SetProcessPriority(int processId, ProcessPriorityLevel priority)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            process.PriorityClass = priority switch
            {
                ProcessPriorityLevel.Idle => ProcessPriorityClass.Idle,
                ProcessPriorityLevel.BelowNormal => ProcessPriorityClass.BelowNormal,
                ProcessPriorityLevel.Normal => ProcessPriorityClass.Normal,
                ProcessPriorityLevel.AboveNormal => ProcessPriorityClass.AboveNormal,
                ProcessPriorityLevel.High => ProcessPriorityClass.High,
                ProcessPriorityLevel.RealTime => ProcessPriorityClass.RealTime,
                _ => ProcessPriorityClass.Normal
            };
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    /// <summary>
    /// 设置进程所有线程的亲和性
    /// </summary>
    public int SetThreadsAffinity(int processId, long affinityMask)
    {
        int successCount = 0;
        try
        {
            using var process = Process.GetProcessById(processId);
            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    IntPtr hThread = OpenThread(THREAD_SET_INFORMATION | THREAD_QUERY_INFORMATION, false, thread.Id);
                    if (hThread != IntPtr.Zero)
                    {
                        SetThreadAffinityMask(hThread, (IntPtr)affinityMask);
                        CloseHandle(hThread);
                        successCount++;
                    }
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return successCount;
    }

    /// <summary>
    /// 根据选中的核心计算亲和性掩码
    /// </summary>
    public long CalculateAffinityMask(IEnumerable<CpuCore> selectedCores)
    {
        long mask = 0;
        foreach (var core in selectedCores.Where(c => c.IsSelected))
        {
            mask |= core.AffinityMask;
        }
        return mask;
    }

    /// <summary>
    /// 开始监控并绑定进程
    /// </summary>
    public void StartMonitoring(
        string processName, 
        long affinityMask, 
        Models.BindingMode bindingMode,
        int? priorityCoreIndex,
        ProcessPriorityLevel priority,
        bool applyToThreads,
        int monitorInterval,
        Action<string> onStatusChanged,
        Action<bool> onProcessFound)
    {
        StopMonitoring();

        _monitorCts = new CancellationTokenSource();
        var token = _monitorCts.Token;
        
        Statistics.StartTime = DateTime.Now;
        Statistics.ApplyCount = 0;
        Statistics.ProcessDetectedCount = 0;

        _monitorTask = Task.Run(async () =>
        {
            var boundProcessIds = new HashSet<int>();
            var processWithPrioritySet = new HashSet<int>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var processes = _processService.FindProcessesByName(processName);
                    
                    if (processes.Count > 0)
                    {
                        onProcessFound(true);
                        
                        foreach (var processInfo in processes)
                        {
                            bool isNewProcess = !boundProcessIds.Contains(processInfo.ProcessId);
                            
                            if (isNewProcess)
                            {
                                Statistics.ProcessDetectedCount++;
                            }
                            
                            // 根据绑定模式处理
                            var effectiveMask = bindingMode switch
                            {
                                Models.BindingMode.Static => affinityMask,
                                Models.BindingMode.Dynamic => affinityMask,
                                Models.BindingMode.D2 => CalculateD2Mask(affinityMask, priorityCoreIndex),
                                Models.BindingMode.D3PowerSave => CalculateD3Mask(affinityMask),
                                Models.BindingMode.RoundRobin => CalculateRoundRobinMask(affinityMask, processInfo.ProcessId),
                                Models.BindingMode.LoadBalance => affinityMask, // 需要监控服务提供核心使用率
                                _ => affinityMask
                            };
                            
                            if (isNewProcess || bindingMode == Models.BindingMode.Dynamic || bindingMode == Models.BindingMode.D2)
                            {
                                if (SetProcessAffinity(processInfo.ProcessId, effectiveMask))
                                {
                                    if (isNewProcess)
                                    {
                                        boundProcessIds.Add(processInfo.ProcessId);
                                        Statistics.ApplyCount++;
                                        onStatusChanged($"已绑定 [{processInfo.ProcessId}] 掩码 0x{effectiveMask:X}");
                                    }
                                    
                                    // 应用到子线程
                                    if (applyToThreads)
                                    {
                                        SetThreadsAffinity(processInfo.ProcessId, effectiveMask);
                                    }
                                }
                            }
                            
                            // 设置进程优先级（只设置一次）
                            if (!processWithPrioritySet.Contains(processInfo.ProcessId) && priority != ProcessPriorityLevel.Normal)
                            {
                                if (SetProcessPriority(processInfo.ProcessId, priority))
                                {
                                    processWithPrioritySet.Add(processInfo.ProcessId);
                                    onStatusChanged($"已设置优先级: {GetPriorityName(priority)}");
                                }
                            }
                        }
                    }
                    else
                    {
                        onProcessFound(false);
                        boundProcessIds.Clear();
                        processWithPrioritySet.Clear();
                    }

                    // 清理已退出的进程
                    boundProcessIds.RemoveWhere(pid => !_processService.IsProcessRunning(pid));
                    processWithPrioritySet.RemoveWhere(pid => !_processService.IsProcessRunning(pid));

                    // 检查间隔
                    int interval = bindingMode == Models.BindingMode.Dynamic ? Math.Max(500, monitorInterval / 2) : monitorInterval;
                    await Task.Delay(interval, token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    onStatusChanged($"错误: {ex.Message}");
                    await Task.Delay(5000, token);
                }
            }
            
            Statistics.TotalRunTime = DateTime.Now - Statistics.StartTime;
        }, token);
    }
    
    /// <summary>
    /// 简化版开始监控（向后兼容）
    /// </summary>
    public void StartMonitoring(
        string processName, 
        long affinityMask, 
        Models.BindingMode bindingMode,
        int? priorityCoreIndex,
        Action<string> onStatusChanged,
        Action<bool> onProcessFound)
    {
        StartMonitoring(processName, affinityMask, bindingMode, priorityCoreIndex, 
            ProcessPriorityLevel.Normal, true, 1000, onStatusChanged, onProcessFound);
    }

    /// <summary>
    /// 停止监控
    /// </summary>
    public void StopMonitoring()
    {
        _monitorCts?.Cancel();
        _monitorCts?.Dispose();
        _monitorCts = null;
        _monitorTask = null;
    }

    private long CalculateD2Mask(long baseMask, int? priorityCoreIndex)
    {
        if (priorityCoreIndex.HasValue)
        {
            long priorityMask = 1L << priorityCoreIndex.Value;
            return baseMask | priorityMask;
        }
        return baseMask;
    }

    private long CalculateD3Mask(long baseMask)
    {
        return baseMask;
    }

    /// <summary>
    /// 轮询核心索引（用于RoundRobin模式）
    /// </summary>
    private int _roundRobinIndex = 0;
    
    /// <summary>
    /// 计算轮询模式的掩码
    /// </summary>
    private long CalculateRoundRobinMask(long baseMask, int processId)
    {
        // 获取baseMask中设置的核心索引列表
        var coreIndices = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            if ((baseMask & (1L << i)) != 0)
            {
                coreIndices.Add(i);
            }
        }
        
        if (coreIndices.Count == 0)
            return baseMask;
        
        // 轮询选择核心
        int selectedCore = coreIndices[_roundRobinIndex % coreIndices.Count];
        _roundRobinIndex++;
        
        // 返回单核心掩码
        return 1L << selectedCore;
    }

    /// <summary>
    /// 计算负载均衡模式的掩码
    /// </summary>
    private long CalculateLoadBalanceMask(long baseMask, float[] coreUsage)
    {
        if (coreUsage == null || coreUsage.Length == 0)
            return baseMask;
        
        // 获取baseMask中设置的核心索引列表
        var coreIndices = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            if ((baseMask & (1L << i)) != 0)
            {
                coreIndices.Add(i);
            }
        }
        
        if (coreIndices.Count == 0)
            return baseMask;
        
        // 找到负载最低的核心
        int lowestUsageCore = coreIndices[0];
        float lowestUsage = float.MaxValue;
        
        foreach (var coreIndex in coreIndices)
        {
            if (coreIndex < coreUsage.Length && coreUsage[coreIndex] < lowestUsage)
            {
                lowestUsage = coreUsage[coreIndex];
                lowestUsageCore = coreIndex;
            }
        }
        
        // 返回负载最低的核心掩码
        return 1L << lowestUsageCore;
    }
    
    private string GetPriorityName(ProcessPriorityLevel priority)
    {
        return priority switch
        {
            ProcessPriorityLevel.Idle => "空闲",
            ProcessPriorityLevel.BelowNormal => "低于正常",
            ProcessPriorityLevel.Normal => "正常",
            ProcessPriorityLevel.AboveNormal => "高于正常",
            ProcessPriorityLevel.High => "高",
            ProcessPriorityLevel.RealTime => "实时",
            _ => "未知"
        };
    }

    /// <summary>
    /// 一次性应用亲和性设置
    /// </summary>
    public (int success, int failed) ApplyAffinityOnce(string processName, long affinityMask, bool applyToThreads = false)
    {
        int success = 0;
        int failed = 0;

        var processes = _processService.FindProcessesByName(processName);
        
        foreach (var process in processes)
        {
            if (SetProcessAffinity(process.ProcessId, affinityMask))
            {
                success++;
                if (applyToThreads)
                {
                    SetThreadsAffinity(process.ProcessId, affinityMask);
                }
            }
            else
            {
                failed++;
            }
        }

        return (success, failed);
    }

    /// <summary>
    /// 设置进程的IO优先级
    /// </summary>
    public bool SetProcessIoPriority(int processId, int ioPriority)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            int priority = ioPriority;
            int result = NtSetInformationProcess(process.Handle, ProcessIoPriority, ref priority, sizeof(int));
            return result == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 禁用进程的优先级提升（可以提高性能稳定性）
    /// </summary>
    public bool DisablePriorityBoost(int processId, bool disable = true)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return SetProcessPriorityBoost(process.Handle, disable);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 应用完整的游戏优化配置
    /// </summary>
    public GameOptimizeResult OptimizeForGaming(int processId, long affinityMask, bool applyToThreads = true)
    {
        var result = new GameOptimizeResult { ProcessId = processId };

        try
        {
            // 1. 设置CPU亲和性
            result.AffinitySet = SetProcessAffinity(processId, affinityMask);
            
            // 2. 设置线程亲和性
            if (applyToThreads)
            {
                result.ThreadsAffected = SetThreadsAffinity(processId, affinityMask);
            }

            // 3. 设置高优先级
            result.PrioritySet = SetProcessPriority(processId, ProcessPriorityLevel.High);

            // 4. 设置高IO优先级
            result.IoPrioritySet = SetProcessIoPriority(processId, IO_PRIORITY_HIGH);

            // 5. 禁用优先级提升（提高稳定性）
            result.PriorityBoostDisabled = DisablePriorityBoost(processId, true);

            result.Success = result.AffinitySet && result.PrioritySet;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 降低后台进程优先级（释放资源给游戏）
    /// </summary>
    public int ThrottleBackgroundProcesses(IEnumerable<string> excludeProcessNames)
    {
        int throttledCount = 0;
        var excludeSet = new HashSet<string>(excludeProcessNames, StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    // 跳过排除的进程
                    if (excludeSet.Contains(process.ProcessName))
                    {
                        process.Dispose();
                        continue;
                    }

                    // 跳过系统进程
                    if (process.SessionId == 0)
                    {
                        process.Dispose();
                        continue;
                    }

                    // 跳过已经是低优先级的进程
                    if (process.PriorityClass <= ProcessPriorityClass.BelowNormal)
                    {
                        process.Dispose();
                        continue;
                    }

                    // 降低优先级
                    process.PriorityClass = ProcessPriorityClass.BelowNormal;
                    
                    // 设置低IO优先级
                    SetProcessIoPriority(process.Id, IO_PRIORITY_LOW);
                    
                    throttledCount++;
                }
                catch
                {
                    // 忽略无权限的进程
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // 忽略错误
        }

        return throttledCount;
    }
}

/// <summary>
/// 游戏优化结果
/// </summary>
public class GameOptimizeResult
{
    public int ProcessId { get; set; }
    public bool Success { get; set; }
    public bool AffinitySet { get; set; }
    public int ThreadsAffected { get; set; }
    public bool PrioritySet { get; set; }
    public bool IoPrioritySet { get; set; }
    public bool PriorityBoostDisabled { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;

    public string Summary => Success 
        ? $"优化成功: 亲和性={AffinitySet}, 线程={ThreadsAffected}, 优先级={PrioritySet}"
        : $"优化失败: {ErrorMessage}";
}
