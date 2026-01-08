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
    
    private const int THREAD_SET_INFORMATION = 0x0020;
    private const int THREAD_QUERY_INFORMATION = 0x0040;

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
}
