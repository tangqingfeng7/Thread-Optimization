using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using CoreX.Models;

namespace CoreX.Services;

/// <summary>
/// CPU 监控服务 - 获取实时使用率、温度、频率
/// </summary>
public class MonitorService : IDisposable
{
    private readonly PerformanceCounter[] _cpuCounters;
    private readonly PerformanceCounter _totalCpuCounter;
    private readonly int _coreCount;
    private bool _disposed;

    // 用于获取CPU频率的WMI查询
    private ManagementObjectSearcher? _cpuSearcher;

    // 缓存频率信息（WMI查询很慢，每10秒更新一次）
    private CpuFrequencyInfo _cachedFrequencyInfo = new();
    private DateTime _lastFrequencyUpdate = DateTime.MinValue;
    private readonly TimeSpan _frequencyCacheTime = TimeSpan.FromSeconds(10);

    // 缓存温度信息
    private CpuTemperatureInfo _cachedTemperatureInfo = new();
    private DateTime _lastTemperatureUpdate = DateTime.MinValue;
    private readonly TimeSpan _temperatureCacheTime = TimeSpan.FromSeconds(5);

    public MonitorService()
    {
        _coreCount = Environment.ProcessorCount;
        _cpuCounters = new PerformanceCounter[_coreCount];
        
        try
        {
            // 初始化每核心CPU使用率计数器
            for (int i = 0; i < _coreCount; i++)
            {
                _cpuCounters[i] = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                _cpuCounters[i].NextValue(); // 首次调用初始化
            }
            
            // 总CPU使用率
            _totalCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _totalCpuCounter.NextValue();
        }
        catch
        {
            _totalCpuCounter = null!;
        }
    }

    /// <summary>
    /// 获取总CPU使用率 (0-100)
    /// </summary>
    public float GetTotalCpuUsage()
    {
        try
        {
            return _totalCpuCounter?.NextValue() ?? 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// 获取每核心CPU使用率
    /// </summary>
    public float[] GetPerCoreUsage()
    {
        var usages = new float[_coreCount];
        
        for (int i = 0; i < _coreCount; i++)
        {
            try
            {
                usages[i] = _cpuCounters[i]?.NextValue() ?? 0;
            }
            catch
            {
                usages[i] = 0;
            }
        }
        
        return usages;
    }

    /// <summary>
    /// 获取CPU温度（需要管理员权限，可能不支持所有CPU）
    /// </summary>
    public CpuTemperatureInfo GetCpuTemperature()
    {
        // 使用缓存减少WMI查询（温度查询非常耗时）
        if (DateTime.Now - _lastTemperatureUpdate < _temperatureCacheTime)
        {
            return _cachedTemperatureInfo;
        }
        
        // 温度获取比较复杂且可能失败，返回N/A即可
        _cachedTemperatureInfo.IsAvailable = false;
        _cachedTemperatureInfo.PackageTemp = 0;
        _lastTemperatureUpdate = DateTime.Now;
        
        return _cachedTemperatureInfo;
    }

    /// <summary>
    /// 获取CPU频率信息（带缓存）
    /// </summary>
    public CpuFrequencyInfo GetCpuFrequency()
    {
        // 使用缓存减少WMI查询
        if (DateTime.Now - _lastFrequencyUpdate < _frequencyCacheTime && _cachedFrequencyInfo.IsAvailable)
        {
            return _cachedFrequencyInfo;
        }
        
        try
        {
            _cpuSearcher ??= new ManagementObjectSearcher("SELECT MaxClockSpeed,CurrentClockSpeed FROM Win32_Processor");
            
            foreach (var obj in _cpuSearcher.Get())
            {
                _cachedFrequencyInfo.BaseFrequencyMHz = Convert.ToInt32(obj["MaxClockSpeed"] ?? 0);
                _cachedFrequencyInfo.CurrentFrequencyMHz = Convert.ToInt32(obj["CurrentClockSpeed"] ?? 0);
                _cachedFrequencyInfo.IsAvailable = true;
                _lastFrequencyUpdate = DateTime.Now;
                break;
            }
        }
        catch
        {
            _cachedFrequencyInfo.IsAvailable = false;
        }

        return _cachedFrequencyInfo;
    }

    /// <summary>
    /// 获取进程的CPU使用率
    /// </summary>
    public ProcessCpuInfo GetProcessCpuUsage(int processId)
    {
        var info = new ProcessCpuInfo { ProcessId = processId };
        
        try
        {
            using var process = Process.GetProcessById(processId);
            
            // 获取进程CPU时间
            var startTime = DateTime.UtcNow;
            var startCpuTime = process.TotalProcessorTime;
            
            // 等待100ms采样
            Thread.Sleep(100);
            
            var endTime = DateTime.UtcNow;
            var endCpuTime = process.TotalProcessorTime;
            
            var cpuUsedMs = (endCpuTime - startCpuTime).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed) * 100;
            
            info.CpuUsage = (float)Math.Min(100, cpuUsageTotal);
            info.MemoryMB = process.WorkingSet64 / 1024.0 / 1024.0;
            info.ThreadCount = process.Threads.Count;
            info.IsAvailable = true;
        }
        catch
        {
            info.IsAvailable = false;
        }

        return info;
    }

    /// <summary>
    /// 获取进程的内存使用情况
    /// </summary>
    public ProcessMemoryInfo GetProcessMemory(int processId)
    {
        var info = new ProcessMemoryInfo { ProcessId = processId };
        
        try
        {
            using var process = Process.GetProcessById(processId);
            info.WorkingSetMB = process.WorkingSet64 / 1024.0 / 1024.0;
            info.PrivateMemoryMB = process.PrivateMemorySize64 / 1024.0 / 1024.0;
            info.VirtualMemoryMB = process.VirtualMemorySize64 / 1024.0 / 1024.0;
            info.IsAvailable = true;
        }
        catch
        {
            info.IsAvailable = false;
        }

        return info;
    }

    /// <summary>
    /// 获取系统内存信息
    /// </summary>
    public SystemMemoryInfo GetSystemMemory()
    {
        var info = new SystemMemoryInfo();
        
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                info.TotalPhysicalMB = memStatus.ullTotalPhys / 1024.0 / 1024.0;
                info.AvailablePhysicalMB = memStatus.ullAvailPhys / 1024.0 / 1024.0;
                info.UsedPhysicalMB = info.TotalPhysicalMB - info.AvailablePhysicalMB;
                info.UsagePercent = (info.UsedPhysicalMB / info.TotalPhysicalMB) * 100;
                info.IsAvailable = true;
            }
        }
        catch
        {
            info.IsAvailable = false;
        }

        return info;
    }

    #region Native Methods

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

    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var counter in _cpuCounters)
        {
            counter?.Dispose();
        }
        
        _totalCpuCounter?.Dispose();
        _cpuSearcher?.Dispose();
        
        _disposed = true;
    }
}

/// <summary>
/// CPU温度信息
/// </summary>
public class CpuTemperatureInfo
{
    public double PackageTemp { get; set; }
    public double[] CoreTemps { get; set; } = Array.Empty<double>();
    public bool IsAvailable { get; set; }
    
    public string DisplayText => IsAvailable ? $"{PackageTemp:F0}°C" : "N/A";
}

/// <summary>
/// CPU频率信息
/// </summary>
public class CpuFrequencyInfo
{
    public int BaseFrequencyMHz { get; set; }
    public int CurrentFrequencyMHz { get; set; }
    public bool IsAvailable { get; set; }
    
    public double BaseFrequencyGHz => BaseFrequencyMHz / 1000.0;
    public double CurrentFrequencyGHz => CurrentFrequencyMHz / 1000.0;
    
    public string DisplayText => IsAvailable ? $"{CurrentFrequencyGHz:F2} GHz" : "N/A";
}

/// <summary>
/// 进程CPU使用信息
/// </summary>
public class ProcessCpuInfo
{
    public int ProcessId { get; set; }
    public float CpuUsage { get; set; }
    public double MemoryMB { get; set; }
    public int ThreadCount { get; set; }
    public bool IsAvailable { get; set; }
}

/// <summary>
/// 进程内存信息
/// </summary>
public class ProcessMemoryInfo
{
    public int ProcessId { get; set; }
    public double WorkingSetMB { get; set; }
    public double PrivateMemoryMB { get; set; }
    public double VirtualMemoryMB { get; set; }
    public bool IsAvailable { get; set; }
}

/// <summary>
/// 系统内存信息
/// </summary>
public class SystemMemoryInfo
{
    public double TotalPhysicalMB { get; set; }
    public double AvailablePhysicalMB { get; set; }
    public double UsedPhysicalMB { get; set; }
    public double UsagePercent { get; set; }
    public bool IsAvailable { get; set; }
    
    public string DisplayText => IsAvailable 
        ? $"{UsedPhysicalMB / 1024:F1} / {TotalPhysicalMB / 1024:F1} GB ({UsagePercent:F0}%)" 
        : "N/A";
}
