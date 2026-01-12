namespace ThreadOptimization.Models;

/// <summary>
/// 核心绑定模式
/// </summary>
public enum BindingMode
{
    /// <summary>
    /// 动态模式 - 允许系统在选定的核心间动态调度
    /// </summary>
    Dynamic,
    
    /// <summary>
    /// 静态模式 - 固定绑定到选定的核心
    /// </summary>
    Static,
    
    /// <summary>
    /// D2模式 (Beta) - 智能动态调度
    /// </summary>
    D2,
    
    /// <summary>
    /// D3省电模式 (Beta) - 优先使用E核，降低功耗
    /// </summary>
    D3PowerSave,
    
    /// <summary>
    /// 轮询模式 (Beta) - 在选定核心间轮流分配
    /// </summary>
    RoundRobin,
    
    /// <summary>
    /// 负载均衡模式 (Beta) - 根据核心负载动态分配
    /// </summary>
    LoadBalance
}

/// <summary>
/// 核心选择模式
/// </summary>
public enum CoreSelectionMode
{
    /// <summary>
    /// 自定义选择
    /// </summary>
    Custom,
    
    /// <summary>
    /// 仅主线程（排除HT/SMT）
    /// </summary>
    PrimaryThreadsOnly,
    
    /// <summary>
    /// 按物理核心选择
    /// </summary>
    ByPhysicalCore,
    
    /// <summary>
    /// 隔核选择
    /// </summary>
    Alternating,
    
    /// <summary>
    /// 按CCD分组
    /// </summary>
    ByCcd,
    
    /// <summary>
    /// 按CCX分组
    /// </summary>
    ByCcx
}

/// <summary>
/// 应用状态
/// </summary>
public enum AppStatus
{
    /// <summary>
    /// 待机
    /// </summary>
    Standby,
    
    /// <summary>
    /// 运行中
    /// </summary>
    Running,
    
    /// <summary>
    /// 错误
    /// </summary>
    Error
}
