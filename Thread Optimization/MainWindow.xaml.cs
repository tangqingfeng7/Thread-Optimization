using System.Windows;
using ThreadOptimization.Services;
using ThreadOptimization.ViewModels;

namespace ThreadOptimization;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    private TrayService? _trayService;
    private bool _isClosing;
    private bool _dontAskAgain;
    private bool _closeToTray = true; // 默认最小化到托盘

    public MainWindow()
    {
        InitializeComponent();
        InitializeTray();
    }

    private void InitializeTray()
    {
        _trayService = new TrayService(this);
        _trayService.Initialize();
        
        _trayService.OnShowWindow += () =>
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        };

        _trayService.OnExitApp += () =>
        {
            _isClosing = true;
            Close();
        };

        _trayService.OnToggleMonitoring += () =>
        {
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.ToggleMonitoring();
            }
        };
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _trayService?.ShowBalloonTip("Thread Optimization", "程序已最小化到系统托盘，双击图标可恢复窗口");
            
            // 最小化时释放内存
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing)
        {
            // 确认退出，执行清理
            if (DataContext is MainViewModel viewModel)
            {
                viewModel.Cleanup();
            }
            _trayService?.Dispose();
            return;
        }

        // 如果不再询问，直接执行上次的选择
        if (_dontAskAgain)
        {
            if (_closeToTray)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
            }
            else
            {
                _isClosing = true;
            }
            return;
        }

        // 取消关闭，延迟显示确认对话框
        e.Cancel = true;
        
        // 使用 Dispatcher 延迟调用，避免在 Closing 事件中显示对话框的问题
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ShowCloseDialog();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ShowCloseDialog()
    {
        var dialog = new CloseConfirmDialog();
        
        // 确保窗口可见且状态正常后再设置 Owner
        if (IsLoaded && IsVisible)
        {
            dialog.Owner = this;
        }
        
        if (dialog.ShowDialog() == true)
        {
            _dontAskAgain = dialog.DontAskAgain;
            _closeToTray = dialog.MinimizeToTray;

            if (dialog.MinimizeToTray)
            {
                WindowState = WindowState.Minimized;
            }
            else
            {
                _isClosing = true;
                Close();
            }
        }
    }
}
