using System.Windows;

// 明确指定使用 WPF 的类型，避免与 WinForms 冲突
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace ThreadOptimization;

/// <summary>
/// App.xaml 的交互逻辑
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 设置未处理异常处理
        DispatcherUnhandledException += (s, args) =>
        {
            MessageBox.Show($"发生错误：{args.Exception.Message}", "错误", 
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
