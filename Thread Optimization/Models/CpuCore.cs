using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ThreadOptimization.Models;

/// <summary>
/// CPU 制造商
/// </summary>
public enum CpuVendor
{
    Intel,
    AMD,
    Unknown
}

/// <summary>
/// CPU 核心类型
/// </summary>
public enum CoreType
{
    /// <summary>
    /// 性能核心 (Performance Core) - Intel
    /// </summary>
    PCore,
    
    /// <summary>
    /// 能效核心 (Efficient Core) - Intel
    /// </summary>
    ECore,
    
    /// <summary>
    /// 标准核心 - AMD Ryzen 普通核心
    /// </summary>
    Standard,
    
    /// <summary>
    /// V-Cache 核心 - AMD Ryzen X3D 带 3D V-Cache 的核心
    /// </summary>
    VCache,
    
    /// <summary>
    /// 未知类型
    /// </summary>
    Unknown
}

/// <summary>
/// CPU 核心信息
/// </summary>
public partial class CpuCore : ObservableObject
{
    /// <summary>
    /// 核心索引（逻辑处理器编号）
    /// </summary>
    [ObservableProperty]
    private int _index;

    /// <summary>
    /// 核心类型
    /// </summary>
    [ObservableProperty]
    private CoreType _coreType;

    /// <summary>
    /// 是否为超线程/SMT
    /// </summary>
    [ObservableProperty]
    private bool _isHyperThread;

    /// <summary>
    /// 物理核心ID
    /// </summary>
    [ObservableProperty]
    private int _physicalCoreId;

    /// <summary>
    /// CCD ID (AMD) 或 模块ID
    /// </summary>
    [ObservableProperty]
    private int _ccdId;

    /// <summary>
    /// CCX ID (AMD Zen架构)
    /// </summary>
    [ObservableProperty]
    private int _ccxId;

    /// <summary>
    /// 是否被选中（在调度池中）
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 当前使用率 (0-100)
    /// </summary>
    [ObservableProperty]
    private float _usage;

    /// <summary>
    /// 当前频率 (MHz)
    /// </summary>
    [ObservableProperty]
    private int _frequencyMHz;

    /// <summary>
    /// 当前温度 (°C)
    /// </summary>
    [ObservableProperty]
    private double _temperature;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName => $"核心 {Index}";

    /// <summary>
    /// 使用率显示文本
    /// </summary>
    public string UsageText => $"{Usage:F0}%";

    /// <summary>
    /// 频率显示文本 (GHz)
    /// </summary>
    public string FrequencyText => FrequencyMHz > 0 ? $"{FrequencyMHz / 1000.0:F2} GHz" : "";

    /// <summary>
    /// 核心类型标签
    /// </summary>
    public string TypeLabel
    {
        get
        {
            var typeStr = CoreType switch
            {
                CoreType.PCore => "P核",
                CoreType.ECore => "E核",
                CoreType.VCache => "3D",
                CoreType.Standard => "",
                _ => ""
            };
            
            if (IsHyperThread)
            {
                if (CoreType == CoreType.PCore)
                    return $"{typeStr} HT";
                else if (CoreType == CoreType.Standard || CoreType == CoreType.VCache)
                    return string.IsNullOrEmpty(typeStr) ? "SMT" : $"{typeStr} SMT";
            }
            
            return typeStr;
        }
    }

    /// <summary>
    /// 获取该核心的亲和性掩码
    /// </summary>
    public long AffinityMask => 1L << Index;

    /// <summary>
    /// 物理核心显示名称（用于分组）
    /// </summary>
    public string PhysicalCoreDisplayName => $"物理核心 {PhysicalCoreId}";

    /// <summary>
    /// CCD/CCX 分组显示名称
    /// </summary>
    public string CcdCcxDisplayName => CcdId >= 0 ? $"CCD{CcdId}" + (CcxId >= 0 ? $" CCX{CcxId}" : "") : "";

    /// <summary>
    /// 线程类型标识（主线程/超线程）
    /// </summary>
    public string ThreadTypeLabel => IsHyperThread ? "HT/SMT" : "主线程";

    /// <summary>
    /// 详细信息提示
    /// </summary>
    public string DetailTooltip
    {
        get
        {
            var lines = new List<string>
            {
                $"逻辑核心: {Index}",
                $"物理核心: {PhysicalCoreId}",
                $"类型: {TypeLabel}",
                $"线程: {ThreadTypeLabel}"
            };
            
            if (CcdId >= 0)
                lines.Add($"CCD: {CcdId}");
            if (CcxId >= 0)
                lines.Add($"CCX: {CcxId}");
            if (FrequencyMHz > 0)
                lines.Add($"频率: {FrequencyText}");
            if (Usage > 0)
                lines.Add($"使用率: {UsageText}");
                
            return string.Join("\n", lines);
        }
    }
}

/// <summary>
/// CPU 信息
/// </summary>
public class CpuInfo
{
    /// <summary>
    /// CPU 型号名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// CPU 制造商
    /// </summary>
    public CpuVendor Vendor { get; set; } = CpuVendor.Unknown;

    /// <summary>
    /// 逻辑处理器数量
    /// </summary>
    public int LogicalProcessorCount { get; set; }

    /// <summary>
    /// 物理核心数量
    /// </summary>
    public int PhysicalCoreCount { get; set; }

    /// <summary>
    /// P核数量 (Intel)
    /// </summary>
    public int PCoreCount { get; set; }

    /// <summary>
    /// E核数量 (Intel)
    /// </summary>
    public int ECoreCount { get; set; }

    /// <summary>
    /// CCD 数量 (AMD)
    /// </summary>
    public int CcdCount { get; set; }

    /// <summary>
    /// V-Cache 核心数量 (AMD X3D)
    /// </summary>
    public int VCacheCoreCount { get; set; }

    /// <summary>
    /// 是否为混合架构（Intel大小核 或 AMD X3D）
    /// </summary>
    public bool IsHybridArchitecture { get; set; }

    /// <summary>
    /// 是否为 X3D 处理器 (AMD)
    /// </summary>
    public bool IsX3D { get; set; }

    /// <summary>
    /// 是否支持 SMT/超线程
    /// </summary>
    public bool HasSmt { get; set; }

    /// <summary>
    /// 所有核心列表
    /// </summary>
    public List<CpuCore> Cores { get; set; } = new();
}
