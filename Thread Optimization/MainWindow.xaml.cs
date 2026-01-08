using System.Windows;
using CoreX.Services;
using CoreX.ViewModels;

namespace CoreX;

/// <summary>
/// MainWindow.xaml 的交互逻辑
/// </summary>
public partial class MainWindow : Window
{
    private TrayService? _trayService;
    private bool _isClosing;

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
            _trayService?.Show();
            _trayService?.ShowBalloonTip("Test", "程序已最小化到系统托盘");
        }
        else
        {
            _trayService?.Hide();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isClosing)
        {
            // 最小化到托盘而不是关闭
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Cleanup();
        }

        _trayService?.Dispose();
    }
}
