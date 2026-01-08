using CommunityToolkit.Mvvm.ComponentModel;
using System.Diagnostics;

namespace CoreX.Models;

/// <summary>
/// 进程信息
/// </summary>
public partial class ProcessInfo : ObservableObject
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
    /// 当前亲和性掩码
    /// </summary>
    [ObservableProperty]
    private long _affinityMask;

    /// <summary>
    /// 进程是否存在
    /// </summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>
    /// 显示名称
    /// </summary>
    public string DisplayName => string.IsNullOrEmpty(WindowTitle) 
        ? ProcessName 
        : $"{ProcessName} - {WindowTitle}";

    /// <summary>
    /// 从 Process 对象创建 ProcessInfo
    /// </summary>
    public static ProcessInfo FromProcess(Process process)
    {
        try
        {
            return new ProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                WindowTitle = process.MainWindowTitle,
                AffinityMask = (long)process.ProcessorAffinity,
                IsRunning = !process.HasExited
            };
        }
        catch
        {
            return new ProcessInfo
            {
                ProcessId = process.Id,
                ProcessName = process.ProcessName,
                IsRunning = false
            };
        }
    }
}
