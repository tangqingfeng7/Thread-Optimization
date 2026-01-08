using System.IO;
using System.Text.Json;

namespace ThreadOptimization.Models;

/// <summary>
/// 应用配置
/// </summary>
public class AppConfig
{
    public string TargetProcessName { get; set; } = string.Empty;
    public List<int> SelectedCoreIndices { get; set; } = new();
    public int? PriorityCoreIndex { get; set; }
    public BindingMode BindingMode { get; set; } = BindingMode.Dynamic;
    public bool AutoStart { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoApplyOnStart { get; set; }
    public ProcessPriorityLevel ProcessPriority { get; set; } = ProcessPriorityLevel.Normal;
    public bool ApplyToChildThreads { get; set; } = true;
    public int MonitorInterval { get; set; } = 1000;
    
    /// <summary>
    /// 多配置档案
    /// </summary>
    public List<ProfileConfig> Profiles { get; set; } = new();
    
    /// <summary>
    /// 当前选中的配置档案索引
    /// </summary>
    public int CurrentProfileIndex { get; set; } = -1;

    /// <summary>
    /// 进程组列表（多进程管理）
    /// </summary>
    public List<ProcessGroupConfig> ProcessGroups { get; set; } = new();

    /// <summary>
    /// 是否启用实时监控显示
    /// </summary>
    public bool EnableRealtimeMonitor { get; set; } = true;

    /// <summary>
    /// 监控刷新间隔（毫秒）- 默认2秒减少性能开销
    /// </summary>
    public int MonitorRefreshInterval { get; set; } = 2000;

    /// <summary>
    /// 是否显示每核心使用率 - 默认关闭减少性能开销
    /// </summary>
    public bool ShowPerCoreUsage { get; set; } = false;

    /// <summary>
    /// 进程过滤规则
    /// </summary>
    public ProcessRuleConfig ProcessRule { get; set; } = new();

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Test",
        "config.json");

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(ConfigPath, json);
        }
        catch
        {
        }
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                var json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
            }
        }
        catch
        {
        }
        return new AppConfig();
    }
    
    public string Export()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
    }
    
    public static AppConfig? Import(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(json);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// 配置档案
/// </summary>
public class ProfileConfig
{
    public string Name { get; set; } = string.Empty;
    public string ProcessName { get; set; } = string.Empty;
    public List<int> SelectedCoreIndices { get; set; } = new();
    public int? PriorityCoreIndex { get; set; }
    public BindingMode BindingMode { get; set; } = BindingMode.Dynamic;
    public ProcessPriorityLevel ProcessPriority { get; set; } = ProcessPriorityLevel.Normal;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 进程优先级
/// </summary>
public enum ProcessPriorityLevel
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    RealTime
}

/// <summary>
/// 预设配置
/// </summary>
public class PresetConfig
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PresetType Type { get; set; }
    public string Icon { get; set; } = string.Empty;
}

/// <summary>
/// 预设类型
/// </summary>
public enum PresetType
{
    Gaming,
    PowerSave,
    Productivity,
    SingleCcd,
    Custom
}

/// <summary>
/// 运行统计
/// </summary>
public class RunStatistics
{
    public DateTime StartTime { get; set; }
    public TimeSpan TotalRunTime { get; set; }
    public int ApplyCount { get; set; }
    public int ProcessDetectedCount { get; set; }
}

/// <summary>
/// 进程组配置（用于序列化）
/// </summary>
public class ProcessGroupConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public List<string> ProcessNames { get; set; } = new();
    public List<int> SelectedCoreIndices { get; set; } = new();
    public int? PriorityCoreIndex { get; set; }
    public BindingMode BindingMode { get; set; } = BindingMode.Dynamic;
    public ProcessPriorityLevel ProcessPriority { get; set; } = ProcessPriorityLevel.Normal;
    public bool IsEnabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
