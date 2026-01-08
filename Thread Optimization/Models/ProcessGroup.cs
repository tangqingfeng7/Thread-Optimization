using CommunityToolkit.Mvvm.ComponentModel;

namespace CoreX.Models;

/// <summary>
/// 进程组 - 支持多进程同时管理
/// </summary>
public partial class ProcessGroup : ObservableObject
{
    /// <summary>
    /// 组ID
    /// </summary>
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    /// <summary>
    /// 组名称
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 进程名称列表（支持多个进程）
    /// </summary>
    [ObservableProperty]
    private List<string> _processNames = new();

    /// <summary>
    /// 选中的核心索引
    /// </summary>
    [ObservableProperty]
    private List<int> _selectedCoreIndices = new();

    /// <summary>
    /// 优先核心索引
    /// </summary>
    [ObservableProperty]
    private int? _priorityCoreIndex;

    /// <summary>
    /// 绑定模式
    /// </summary>
    [ObservableProperty]
    private BindingMode _bindingMode = BindingMode.Dynamic;

    /// <summary>
    /// 进程优先级
    /// </summary>
    [ObservableProperty]
    private ProcessPriorityLevel _processPriority = ProcessPriorityLevel.Normal;

    /// <summary>
    /// 是否启用
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>
    /// 是否正在运行（监控中）
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 当前状态文本
    /// </summary>
    [ObservableProperty]
    private string _statusText = "待机";

    /// <summary>
    /// 当前检测到的进程数
    /// </summary>
    [ObservableProperty]
    private int _detectedProcessCount;

    /// <summary>
    /// 创建时间
    /// </summary>
    [ObservableProperty]
    private DateTime _createdAt = DateTime.Now;

    /// <summary>
    /// 进程名称显示文本
    /// </summary>
    public string ProcessNamesText => ProcessNames.Count > 0 
        ? string.Join(", ", ProcessNames) 
        : "无进程";

    /// <summary>
    /// 核心数量显示
    /// </summary>
    public string CoreCountText => $"{SelectedCoreIndices.Count} 核心";
}

/// <summary>
/// 受监控的进程实例信息
/// </summary>
public partial class MonitoredProcess : ObservableObject
{
    /// <summary>
    /// 进程ID
    /// </summary>
    [ObservableProperty]
    private int _processId;

    /// <summary>
    /// 进程名称
    /// </summary>
    [ObservableProperty]
    private string _processName = string.Empty;

    /// <summary>
    /// 窗口标题
    /// </summary>
    [ObservableProperty]
    private string _windowTitle = string.Empty;

    /// <summary>
    /// 所属进程组ID
    /// </summary>
    [ObservableProperty]
    private string _groupId = string.Empty;

    /// <summary>
    /// CPU使用率
    /// </summary>
    [ObservableProperty]
    private float _cpuUsage;

    /// <summary>
    /// 内存使用 (MB)
    /// </summary>
    [ObservableProperty]
    private double _memoryMB;

    /// <summary>
    /// 线程数
    /// </summary>
    [ObservableProperty]
    private int _threadCount;

    /// <summary>
    /// 当前亲和性掩码
    /// </summary>
    [ObservableProperty]
    private long _affinityMask;

    /// <summary>
    /// 是否已应用配置
    /// </summary>
    [ObservableProperty]
    private bool _isConfigApplied;

    /// <summary>
    /// 启动时间
    /// </summary>
    [ObservableProperty]
    private DateTime _startTime;

    /// <summary>
    /// 运行时长显示
    /// </summary>
    public string RunTimeText
    {
        get
        {
            var elapsed = DateTime.Now - StartTime;
            if (elapsed.TotalHours >= 1)
                return $"{elapsed:hh\\:mm\\:ss}";
            return $"{elapsed:mm\\:ss}";
        }
    }

    /// <summary>
    /// CPU使用率显示
    /// </summary>
    public string CpuUsageText => $"{CpuUsage:F1}%";

    /// <summary>
    /// 内存使用显示
    /// </summary>
    public string MemoryText => MemoryMB >= 1024 
        ? $"{MemoryMB / 1024:F1} GB" 
        : $"{MemoryMB:F0} MB";
}

/// <summary>
/// 进程规则类型
/// </summary>
public enum ProcessRuleType
{
    /// <summary>
    /// 白名单 - 只处理列表中的进程
    /// </summary>
    Whitelist,
    
    /// <summary>
    /// 黑名单 - 排除列表中的进程
    /// </summary>
    Blacklist
}

/// <summary>
/// 进程规则配置
/// </summary>
public class ProcessRuleConfig
{
    /// <summary>
    /// 规则类型
    /// </summary>
    public ProcessRuleType RuleType { get; set; } = ProcessRuleType.Whitelist;

    /// <summary>
    /// 进程名称列表
    /// </summary>
    public List<string> ProcessNames { get; set; } = new();

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
