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
    D3PowerSave
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
