using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;

namespace ThreadOptimization.Services;

/// <summary>
/// 系统托盘服务
/// </summary>
public class TrayService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private readonly Window _mainWindow;
    private bool _isDisposed;

    public event Action? OnShowWindow;
    public event Action? OnExitApp;
    public event Action? OnToggleMonitoring;

    public TrayService(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    /// <summary>
    /// 初始化托盘图标
    /// </summary>
    public void Initialize()
    {
        _notifyIcon = new NotifyIcon
        {
            Text = "Test - CPU 核心调度工具",
            Visible = false
        };

        // 使用系统图标
        _notifyIcon.Icon = SystemIcons.Application;

        // 创建右键菜单
        var contextMenu = new ContextMenuStrip();
        
        var showItem = new ToolStripMenuItem("显示主窗口");
        showItem.Click += (s, e) => OnShowWindow?.Invoke();
        contextMenu.Items.Add(showItem);

        var toggleItem = new ToolStripMenuItem("开始/停止监控");
        toggleItem.Click += (s, e) => OnToggleMonitoring?.Invoke();
        contextMenu.Items.Add(toggleItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (s, e) => OnExitApp?.Invoke();
        contextMenu.Items.Add(exitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
        _notifyIcon.DoubleClick += (s, e) => OnShowWindow?.Invoke();
    }

    /// <summary>
    /// 显示托盘图标
    /// </summary>
    public void Show()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = true;
        }
    }

    /// <summary>
    /// 隐藏托盘图标
    /// </summary>
    public void Hide()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
        }
    }

    /// <summary>
    /// 显示气泡通知
    /// </summary>
    public void ShowBalloonTip(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, text, icon);
    }

    /// <summary>
    /// 更新状态
    /// </summary>
    public void UpdateStatus(bool isRunning, string processName = "")
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Text = isRunning 
                ? $"Test - 正在监控: {processName}" 
                : "Test - 待机中";
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _notifyIcon?.Dispose();
            _isDisposed = true;
        }
    }
}
