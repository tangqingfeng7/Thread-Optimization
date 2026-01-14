using System.Windows;
using System.Windows.Input;

namespace ThreadOptimization;

/// <summary>
/// 关闭确认对话框
/// </summary>
public partial class CloseConfirmDialog : Window
{
    /// <summary>
    /// 是否最小化到托盘
    /// </summary>
    public bool MinimizeToTray { get; private set; } = true;

    /// <summary>
    /// 是否不再询问
    /// </summary>
    public bool DontAskAgain { get; private set; }

    public CloseConfirmDialog()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }

    private void BtnOK_Click(object sender, RoutedEventArgs e)
    {
        MinimizeToTray = rbMinimize.IsChecked == true;
        DontAskAgain = cbDontAsk.IsChecked == true;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
