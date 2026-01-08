using System.Runtime.InteropServices;
using ThreadOptimization.Models;

namespace ThreadOptimization.Services;

/// <summary>
/// NUMA 节点优化服务
/// </summary>
public class NumaService
{
    #region Win32 API

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaHighestNodeNumber(out uint HighestNodeNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaNodeProcessorMaskEx(ushort Node, out GROUP_AFFINITY ProcessorMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaAvailableMemoryNode(byte Node, out ulong AvailableBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessAffinityMask(IntPtr hProcess, UIntPtr dwProcessAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessAffinityMask(IntPtr hProcess, out UIntPtr lpProcessAffinityMask, out UIntPtr lpSystemAffinityMask);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetThreadIdealProcessor(IntPtr hThread, uint dwIdealProcessor);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadIdealProcessorEx(IntPtr hThread, ref PROCESSOR_NUMBER lpIdealProcessor, out PROCESSOR_NUMBER lpPreviousIdealProcessor);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaProcessorNode(byte Processor, out byte NodeNumber);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNumaProximityNode(uint ProximityId, out byte NodeNumber);

    [StructLayout(LayoutKind.Sequential)]
    private struct GROUP_AFFINITY
    {
        public UIntPtr Mask;
        public ushort Group;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_NUMBER
    {
        public ushort Group;
        public byte Number;
        public byte Reserved;
    }

    private const uint PROCESS_ALL_ACCESS = 0x001F0FFF;
    private const uint PROCESS_SET_INFORMATION = 0x0200;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    #endregion

    private readonly List<NumaNodeInfo> _numaNodes = new();

    /// <summary>
    /// 获取所有 NUMA 节点
    /// </summary>
    public IReadOnlyList<NumaNodeInfo> NumaNodes => _numaNodes.AsReadOnly();

    /// <summary>
    /// 系统是否支持 NUMA
    /// </summary>
    public bool IsNumaSupported { get; private set; }

    /// <summary>
    /// NUMA 节点数量
    /// </summary>
    public int NodeCount => _numaNodes.Count;

    /// <summary>
    /// 初始化 NUMA 信息
    /// </summary>
    public void Initialize()
    {
        _numaNodes.Clear();

        try
        {
            if (!GetNumaHighestNodeNumber(out uint highestNode))
            {
                IsNumaSupported = false;
                return;
            }

            IsNumaSupported = highestNode > 0;

            for (ushort nodeId = 0; nodeId <= highestNode; nodeId++)
            {
                var nodeInfo = new NumaNodeInfo { NodeId = nodeId };

                // 获取节点的处理器掩码
                if (GetNumaNodeProcessorMaskEx(nodeId, out GROUP_AFFINITY affinity))
                {
                    nodeInfo.ProcessorMask = (ulong)affinity.Mask;
                    nodeInfo.CoreIndices = GetCoreIndicesFromMask(nodeInfo.ProcessorMask);
                }

                // 获取节点的可用内存
                if (GetNumaAvailableMemoryNode((byte)nodeId, out ulong availableBytes))
                {
                    nodeInfo.MemoryMB = (long)(availableBytes / (1024 * 1024));
                }

                _numaNodes.Add(nodeInfo);
            }
        }
        catch
        {
            IsNumaSupported = false;
        }
    }

    /// <summary>
    /// 从掩码获取核心索引列表
    /// </summary>
    private List<int> GetCoreIndicesFromMask(ulong mask)
    {
        var indices = new List<int>();
        for (int i = 0; i < 64; i++)
        {
            if ((mask & (1UL << i)) != 0)
            {
                indices.Add(i);
            }
        }
        return indices;
    }

    /// <summary>
    /// 获取指定核心所属的 NUMA 节点
    /// </summary>
    public int? GetNumaNodeForCore(int coreIndex)
    {
        foreach (var node in _numaNodes)
        {
            if (node.CoreIndices.Contains(coreIndex))
            {
                return node.NodeId;
            }
        }
        return null;
    }

    /// <summary>
    /// 获取指定 NUMA 节点的亲和性掩码
    /// </summary>
    public ulong GetNodeAffinityMask(int nodeId)
    {
        var node = _numaNodes.FirstOrDefault(n => n.NodeId == nodeId);
        return node?.ProcessorMask ?? 0;
    }

    /// <summary>
    /// 获取多个 NUMA 节点的组合亲和性掩码
    /// </summary>
    public ulong GetNodesAffinityMask(IEnumerable<int> nodeIds)
    {
        ulong mask = 0;
        foreach (var nodeId in nodeIds)
        {
            mask |= GetNodeAffinityMask(nodeId);
        }
        return mask;
    }

    /// <summary>
    /// 设置进程的 NUMA 节点亲和性
    /// </summary>
    public bool SetProcessNumaAffinity(int processId, int nodeId)
    {
        var mask = GetNodeAffinityMask(nodeId);
        if (mask == 0) return false;

        return SetProcessAffinityMaskByPid(processId, mask);
    }

    /// <summary>
    /// 设置进程的多 NUMA 节点亲和性
    /// </summary>
    public bool SetProcessNumaAffinity(int processId, IEnumerable<int> nodeIds)
    {
        var mask = GetNodesAffinityMask(nodeIds);
        if (mask == 0) return false;

        return SetProcessAffinityMaskByPid(processId, mask);
    }

    /// <summary>
    /// 通过 PID 设置进程亲和性掩码
    /// </summary>
    private bool SetProcessAffinityMaskByPid(int processId, ulong mask)
    {
        IntPtr hProcess = IntPtr.Zero;
        try
        {
            hProcess = OpenProcess(PROCESS_SET_INFORMATION | PROCESS_QUERY_INFORMATION, false, processId);
            if (hProcess == IntPtr.Zero) return false;

            return SetProcessAffinityMask(hProcess, new UIntPtr(mask));
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hProcess != IntPtr.Zero)
            {
                CloseHandle(hProcess);
            }
        }
    }

    /// <summary>
    /// 获取针对游戏优化的 NUMA 节点建议
    /// </summary>
    public NumaOptimizationSuggestion GetGameOptimizationSuggestion()
    {
        var suggestion = new NumaOptimizationSuggestion();

        if (!IsNumaSupported || _numaNodes.Count <= 1)
        {
            suggestion.Reason = "系统只有一个 NUMA 节点，无需优化";
            return suggestion;
        }

        // 找到核心数最多的节点作为游戏节点
        var gameNode = _numaNodes.OrderByDescending(n => n.CoreCount).First();
        suggestion.RecommendedGameNode = gameNode.NodeId;
        suggestion.GameNodeCores = gameNode.CoreIndices;

        // 其他节点用于后台任务
        suggestion.BackgroundNodes = _numaNodes
            .Where(n => n.NodeId != gameNode.NodeId)
            .Select(n => n.NodeId)
            .ToList();

        suggestion.Reason = $"建议将游戏绑定到 NUMA {gameNode.NodeId}（{gameNode.CoreCount} 核心），" +
                           $"后台任务使用其他节点，避免跨节点内存访问延迟";

        return suggestion;
    }

    /// <summary>
    /// 获取 NUMA 拓扑摘要信息
    /// </summary>
    public string GetNumaTopologySummary()
    {
        if (!IsNumaSupported)
        {
            return "系统不支持 NUMA 或只有单节点";
        }

        var summary = $"{_numaNodes.Count} 个 NUMA 节点: ";
        summary += string.Join(", ", _numaNodes.Select(n => 
            $"Node{n.NodeId}({n.CoreCount}C/{n.MemoryMB / 1024.0:F1}GB)"));

        return summary;
    }

    /// <summary>
    /// 检查选定的核心是否跨越多个 NUMA 节点
    /// </summary>
    public bool IsCrossNumaSelection(IEnumerable<int> coreIndices)
    {
        if (!IsNumaSupported || _numaNodes.Count <= 1)
            return false;

        var nodes = new HashSet<int>();
        foreach (var coreIndex in coreIndices)
        {
            var node = GetNumaNodeForCore(coreIndex);
            if (node.HasValue)
            {
                nodes.Add(node.Value);
            }
        }

        return nodes.Count > 1;
    }

    /// <summary>
    /// 获取跨 NUMA 警告信息
    /// </summary>
    public string? GetCrossNumaWarning(IEnumerable<int> coreIndices)
    {
        if (!IsCrossNumaSelection(coreIndices))
            return null;

        return "警告：选中的核心跨越多个 NUMA 节点，可能导致额外的内存访问延迟。" +
               "建议将进程绑定到单个 NUMA 节点以获得最佳性能。";
    }
}

/// <summary>
/// NUMA 优化建议
/// </summary>
public class NumaOptimizationSuggestion
{
    /// <summary>
    /// 推荐的游戏 NUMA 节点
    /// </summary>
    public int? RecommendedGameNode { get; set; }

    /// <summary>
    /// 游戏节点包含的核心
    /// </summary>
    public List<int> GameNodeCores { get; set; } = new();

    /// <summary>
    /// 后台任务节点
    /// </summary>
    public List<int> BackgroundNodes { get; set; } = new();

    /// <summary>
    /// 建议原因
    /// </summary>
    public string Reason { get; set; } = string.Empty;
}
