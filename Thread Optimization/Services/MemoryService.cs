using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ThreadOptimization.Services;

/// <summary>
/// 内存优化服务 - 提供进程和系统内存优化功能
/// </summary>
public class MemoryService
{
    #region Native Methods

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetSystemInformation(int SystemInformationClass, IntPtr SystemInformation, int SystemInformationLength);

    private const uint PROCESS_SET_QUOTA = 0x0100;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;
    private const int SystemMemoryListInformation = 80;
    private const int MemoryPurgeStandbyList = 4;

    #endregion

    private readonly ProcessService _processService;

    public MemoryService(ProcessService processService)
    {
        _processService = processService;
    }

    /// <summary>
    /// 清理指定进程的工作集（释放物理内存到页面文件）
    /// </summary>
    public MemoryOptimizeResult OptimizeProcessMemory(int processId)
    {
        var result = new MemoryOptimizeResult { ProcessId = processId };

        try
        {
            using var process = Process.GetProcessById(processId);
            
            // 记录优化前内存
            result.BeforeMemoryMB = process.WorkingSet64 / 1024.0 / 1024.0;

            // 清空工作集
            IntPtr hProcess = process.Handle;
            bool success = SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));

            if (success)
            {
                // 刷新进程信息
                process.Refresh();
                result.AfterMemoryMB = process.WorkingSet64 / 1024.0 / 1024.0;
                result.FreedMemoryMB = result.BeforeMemoryMB - result.AfterMemoryMB;
                result.Success = true;
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = $"SetProcessWorkingSetSize 失败: {Marshal.GetLastWin32Error()}";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 批量优化多个进程的内存
    /// </summary>
    public List<MemoryOptimizeResult> OptimizeProcessesMemory(IEnumerable<int> processIds)
    {
        var results = new List<MemoryOptimizeResult>();
        
        foreach (var pid in processIds)
        {
            results.Add(OptimizeProcessMemory(pid));
        }

        return results;
    }

    /// <summary>
    /// 优化所有用户进程的内存（不包括系统进程）
    /// </summary>
    public MemoryOptimizeSummary OptimizeAllUserProcesses()
    {
        var summary = new MemoryOptimizeSummary();
        var results = new List<MemoryOptimizeResult>();

        try
        {
            var processes = Process.GetProcesses();
            
            foreach (var process in processes)
            {
                try
                {
                    // 跳过系统进程
                    if (IsSystemProcess(process))
                    {
                        process.Dispose();
                        continue;
                    }

                    // 跳过内存使用很小的进程（小于50MB）
                    if (process.WorkingSet64 < 50 * 1024 * 1024)
                    {
                        process.Dispose();
                        continue;
                    }

                    var result = OptimizeProcessMemory(process.Id);
                    results.Add(result);
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
        catch (Exception ex)
        {
            summary.ErrorMessage = ex.Message;
        }

        summary.Results = results;
        summary.TotalProcesses = results.Count;
        summary.SuccessCount = results.Count(r => r.Success);
        summary.TotalFreedMB = results.Where(r => r.Success).Sum(r => r.FreedMemoryMB);

        return summary;
    }

    /// <summary>
    /// 清理系统待机内存（需要管理员权限）
    /// </summary>
    public SystemMemoryCleanResult CleanSystemStandbyMemory()
    {
        var result = new SystemMemoryCleanResult();

        try
        {
            // 获取清理前的内存状态
            var beforeInfo = GetSystemMemoryInfo();
            result.BeforeAvailableMB = beforeInfo.AvailablePhysicalMB;

            // 清理待机内存列表（需要管理员权限）
            int memoryPurge = MemoryPurgeStandbyList;
            IntPtr buffer = Marshal.AllocHGlobal(sizeof(int));
            Marshal.WriteInt32(buffer, memoryPurge);

            int status = NtSetSystemInformation(SystemMemoryListInformation, buffer, sizeof(int));
            Marshal.FreeHGlobal(buffer);

            if (status == 0)
            {
                // 等待一下让系统处理
                Thread.Sleep(100);
                
                var afterInfo = GetSystemMemoryInfo();
                result.AfterAvailableMB = afterInfo.AvailablePhysicalMB;
                result.FreedMB = result.AfterAvailableMB - result.BeforeAvailableMB;
                result.Success = true;
            }
            else
            {
                result.Success = false;
                result.ErrorMessage = $"NtSetSystemInformation 返回: {status}（可能需要管理员权限）";
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 执行完整的内存优化（进程 + 系统）
    /// </summary>
    public FullMemoryOptimizeResult PerformFullOptimization()
    {
        var result = new FullMemoryOptimizeResult();

        // 1. 优化所有用户进程
        result.ProcessOptimization = OptimizeAllUserProcesses();

        // 2. 强制GC（当前进程）
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // 3. 清理系统待机内存
        result.SystemCleanup = CleanSystemStandbyMemory();

        // 计算总释放量
        result.TotalFreedMB = result.ProcessOptimization.TotalFreedMB + 
                             (result.SystemCleanup.Success ? result.SystemCleanup.FreedMB : 0);

        return result;
    }

    /// <summary>
    /// 获取系统内存信息
    /// </summary>
    private SystemMemorySnapshot GetSystemMemoryInfo()
    {
        var info = new SystemMemorySnapshot();

        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                info.TotalPhysicalMB = memStatus.ullTotalPhys / 1024.0 / 1024.0;
                info.AvailablePhysicalMB = memStatus.ullAvailPhys / 1024.0 / 1024.0;
                info.UsedPhysicalMB = info.TotalPhysicalMB - info.AvailablePhysicalMB;
            }
        }
        catch
        {
            // 忽略
        }

        return info;
    }

    /// <summary>
    /// 判断是否为系统进程
    /// </summary>
    private bool IsSystemProcess(Process process)
    {
        try
        {
            // 系统进程通常 SessionId 为 0
            if (process.SessionId == 0)
                return true;

            // 特定系统进程名
            var systemProcesses = new[]
            {
                "System", "Idle", "smss", "csrss", "wininit", "services", "lsass",
                "svchost", "dwm", "fontdrvhost", "WmiPrvSE", "SearchIndexer",
                "SecurityHealthService", "MsMpEng", "NisSrv", "Registry",
                "spoolsv", "audiodg", "sihost", "taskhostw", "ctfmon"
            };

            return systemProcesses.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return true; // 无法判断的当作系统进程
        }
    }

    #region Native Structures

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    #endregion
}

#region Result Models

/// <summary>
/// 单个进程内存优化结果
/// </summary>
public class MemoryOptimizeResult
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public double BeforeMemoryMB { get; set; }
    public double AfterMemoryMB { get; set; }
    public double FreedMemoryMB { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 批量进程优化汇总
/// </summary>
public class MemoryOptimizeSummary
{
    public List<MemoryOptimizeResult> Results { get; set; } = new();
    public int TotalProcesses { get; set; }
    public int SuccessCount { get; set; }
    public double TotalFreedMB { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 系统内存清理结果
/// </summary>
public class SystemMemoryCleanResult
{
    public double BeforeAvailableMB { get; set; }
    public double AfterAvailableMB { get; set; }
    public double FreedMB { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

/// <summary>
/// 完整内存优化结果
/// </summary>
public class FullMemoryOptimizeResult
{
    public MemoryOptimizeSummary ProcessOptimization { get; set; } = new();
    public SystemMemoryCleanResult SystemCleanup { get; set; } = new();
    public double TotalFreedMB { get; set; }

    public string Summary => 
        $"优化 {ProcessOptimization.SuccessCount} 个进程，释放 {TotalFreedMB:F1} MB";
}

/// <summary>
/// 系统内存快照
/// </summary>
public class SystemMemorySnapshot
{
    public double TotalPhysicalMB { get; set; }
    public double AvailablePhysicalMB { get; set; }
    public double UsedPhysicalMB { get; set; }
}

#endregion
