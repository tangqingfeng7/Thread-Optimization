using CommunityToolkit.Mvvm.ComponentModel;

namespace ThreadOptimization.Models;

/// <summary>
/// 游戏平台类型
/// </summary>
public enum GamePlatform
{
    Steam,
    Epic,
    Xbox,
    Custom
}

/// <summary>
/// 游戏信息模型
/// </summary>
public partial class GameInfo : ObservableObject
{
    /// <summary>
    /// 游戏唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 游戏名称
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// 游戏可执行文件路径
    /// </summary>
    [ObservableProperty]
    private string _executablePath = string.Empty;

    /// <summary>
    /// 进程名称（不含.exe）
    /// </summary>
    [ObservableProperty]
    private string _processName = string.Empty;

    /// <summary>
    /// 游戏平台
    /// </summary>
    [ObservableProperty]
    private GamePlatform _platform = GamePlatform.Custom;

    /// <summary>
    /// 平台游戏ID（Steam AppId 等）
    /// </summary>
    public string PlatformGameId { get; set; } = string.Empty;

    /// <summary>
    /// 游戏图标路径
    /// </summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>
    /// 安装路径
    /// </summary>
    public string InstallPath { get; set; } = string.Empty;

    /// <summary>
    /// 是否已配置核心绑定
    /// </summary>
    [ObservableProperty]
    private bool _hasConfiguration;

    /// <summary>
    /// 绑定的核心索引
    /// </summary>
    public List<int> SelectedCoreIndices { get; set; } = new();

    /// <summary>
    /// 优先核心索引
    /// </summary>
    public int? PriorityCoreIndex { get; set; }

    /// <summary>
    /// 绑定模式
    /// </summary>
    public BindingMode BindingMode { get; set; } = BindingMode.Dynamic;

    /// <summary>
    /// 进程优先级
    /// </summary>
    public ProcessPriorityLevel ProcessPriority { get; set; } = ProcessPriorityLevel.High;

    /// <summary>
    /// NUMA 节点偏好
    /// </summary>
    public int? PreferredNumaNode { get; set; }

    /// <summary>
    /// 是否自动应用配置
    /// </summary>
    [ObservableProperty]
    private bool _autoApply = true;

    /// <summary>
    /// 是否正在运行
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 上次运行时间
    /// </summary>
    public DateTime? LastRunTime { get; set; }

    /// <summary>
    /// 平台显示名称
    /// </summary>
    public string PlatformName => Platform switch
    {
        GamePlatform.Steam => "Steam",
        GamePlatform.Epic => "Epic Games",
        GamePlatform.Xbox => "Xbox/Microsoft Store",
        GamePlatform.Custom => "自定义",
        _ => "未知"
    };

    /// <summary>
    /// 配置摘要
    /// </summary>
    public string ConfigSummary => HasConfiguration 
        ? $"{SelectedCoreIndices.Count} 核心 | {BindingMode}"
        : "未配置";
}

/// <summary>
/// NUMA 节点信息
/// </summary>
public partial class NumaNodeInfo : ObservableObject
{
    /// <summary>
    /// NUMA 节点 ID
    /// </summary>
    public int NodeId { get; set; }

    /// <summary>
    /// 节点包含的处理器掩码
    /// </summary>
    public ulong ProcessorMask { get; set; }

    /// <summary>
    /// 节点包含的核心索引列表
    /// </summary>
    public List<int> CoreIndices { get; set; } = new();

    /// <summary>
    /// 节点核心数量
    /// </summary>
    public int CoreCount => CoreIndices.Count;

    /// <summary>
    /// 节点内存大小（MB）
    /// </summary>
    public long MemoryMB { get; set; }

    /// <summary>
    /// 是否选中
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName => $"NUMA {NodeId}";

    /// <summary>
    /// 详细信息
    /// </summary>
    public string DetailInfo => $"{CoreCount} 核心 | {MemoryMB / 1024.0:F1} GB";
}
