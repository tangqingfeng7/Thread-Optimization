using System.Windows;
using System.Windows.Input;
using ThreadOptimization.Models;
using ThreadOptimization.Services;

namespace ThreadOptimization.Views;

/// <summary>
/// 进程选择器窗口
/// </summary>
public partial class ProcessSelectorWindow : Window
{
    private readonly ProcessService _processService;
    private List<ProcessInfo> _allProcesses = new();

    /// <summary>
    /// 选中的进程名
    /// </summary>
    public string? SelectedProcessName { get; private set; }

    public ProcessSelectorWindow()
    {
        InitializeComponent();
        _processService = new ProcessService();
        LoadProcesses();
    }

    private void LoadProcesses()
    {
        _allProcesses = _processService.GetAllProcesses();
        ProcessList.ItemsSource = _allProcesses;
    }

    private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var searchText = SearchBox.Text.Trim().ToLower();
        
        if (string.IsNullOrEmpty(searchText))
        {
            ProcessList.ItemsSource = _allProcesses;
        }
        else
        {
            ProcessList.ItemsSource = _allProcesses
                .Where(p => p.ProcessName.ToLower().Contains(searchText) ||
                            p.WindowTitle.ToLower().Contains(searchText))
                .ToList();
        }
    }

    private void ProcessList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        SelectProcess();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadProcesses();
        SearchBox.Clear();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        SelectProcess();
    }

    private void SelectProcess()
    {
        if (ProcessList.SelectedItem is ProcessInfo process)
        {
            SelectedProcessName = process.ProcessName;
            DialogResult = true;
            Close();
        }
    }
}
