using System.Diagnostics;
using ThreadOptimization.Models;

namespace ThreadOptimization.Services;

/// <summary>
/// 进程管理服务
/// </summary>
public class ProcessService
{
    /// <summary>
    /// 根据进程名称查找进程
    /// </summary>
    public List<ProcessInfo> FindProcessesByName(string processName)
    {
        var result = new List<ProcessInfo>();
        
        try
        {
            // 移除 .exe 后缀
            var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName[..^4]
                : processName;

            var processes = Process.GetProcessesByName(name);
            
            foreach (var process in processes)
            {
                try
                {
                    result.Add(ProcessInfo.FromProcess(process));
                }
                catch
                {
                    // 忽略无权限访问的进程
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // 忽略错误
        }

        return result;
    }

    /// <summary>
    /// 获取所有运行中的进程
    /// </summary>
    public List<ProcessInfo> GetAllProcesses()
    {
        var result = new List<ProcessInfo>();
        
        try
        {
            var processes = Process.GetProcesses();
            
            foreach (var process in processes)
            {
                try
                {
                    // 只添加有窗口标题的进程
                    if (!string.IsNullOrEmpty(process.MainWindowTitle))
                    {
                        result.Add(ProcessInfo.FromProcess(process));
                    }
                }
                catch
                {
                    // 忽略无权限访问的进程
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // 忽略错误
        }

        return result.OrderBy(p => p.ProcessName).ToList();
    }

    /// <summary>
    /// 检查进程是否仍在运行
    /// </summary>
    public bool IsProcessRunning(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            var running = !process.HasExited;
            process.Dispose();
            return running;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取进程的当前亲和性掩码
    /// </summary>
    public long GetProcessAffinity(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return (long)process.ProcessorAffinity;
        }
        catch
        {
            return 0;
        }
    }
}
