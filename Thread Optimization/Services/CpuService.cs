using System.Management;
using System.Runtime.InteropServices;
using ThreadOptimization.Models;

namespace ThreadOptimization.Services;

/// <summary>
/// CPU 信息服务
/// </summary>
public class CpuService
{
    #region Windows API 定义

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP RelationshipType,
        IntPtr Buffer,
        ref uint ReturnedLength);

    private enum LOGICAL_PROCESSOR_RELATIONSHIP
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationProcessorDie = 5,
        RelationNumaNodeEx = 6,
        RelationProcessorModule = 7,
        RelationAll = 0xffff
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_RELATIONSHIP
    {
        public byte Flags;
        public byte EfficiencyClass;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
        public byte[] Reserved;
        public ushort GroupCount;
        // GROUP_AFFINITY 数组跟随其后
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GROUP_AFFINITY
    {
        public UIntPtr Mask;
        public ushort Group;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        public ushort[] Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX
    {
        public LOGICAL_PROCESSOR_RELATIONSHIP Relationship;
        public uint Size;
        // 后续数据根据 Relationship 类型不同而不同
    }

    // 核心拓扑信息
    private class CoreTopologyInfo
    {
        public int LogicalIndex { get; set; }
        public int PhysicalCoreId { get; set; }
        public byte EfficiencyClass { get; set; } // 0 = E核, 1 = P核 (Intel混合架构)
        public bool IsHyperThread { get; set; }
    }

    #endregion

    /// <summary>
    /// 获取 CPU 信息
    /// </summary>
    public CpuInfo GetCpuInfo()
    {
        var cpuInfo = new CpuInfo
        {
            LogicalProcessorCount = Environment.ProcessorCount
        };

        // 获取 CPU 型号名称和物理核心数
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                cpuInfo.Name = obj["Name"]?.ToString() ?? "Unknown CPU";
                cpuInfo.PhysicalCoreCount = Convert.ToInt32(obj["NumberOfCores"]);
                break;
            }
        }
        catch
        {
            cpuInfo.Name = "Unknown CPU";
            cpuInfo.PhysicalCoreCount = cpuInfo.LogicalProcessorCount / 2;
        }

        // 检测 CPU 制造商
        cpuInfo.Vendor = DetectCpuVendor(cpuInfo.Name);

        // 检测是否支持 SMT/超线程
        cpuInfo.HasSmt = cpuInfo.LogicalProcessorCount > cpuInfo.PhysicalCoreCount;

        // 尝试使用 Windows API 获取真实的核心拓扑
        var coreTopology = GetCoreTopologyFromApi();

        // 根据制造商处理
        if (cpuInfo.Vendor == CpuVendor.Intel)
        {
            cpuInfo.IsHybridArchitecture = IsIntelHybridCpu(cpuInfo.Name);
            if (cpuInfo.IsHybridArchitecture)
            {
                if (coreTopology != null && coreTopology.Count > 0)
                {
                    // 使用 API 获取的真实拓扑生成核心列表
                    GenerateIntelHybridCoreListFromTopology(cpuInfo, coreTopology);
                }
                else
                {
                    // 后备：使用配置表
                    GenerateIntelHybridCoreList(cpuInfo);
                }
            }
            else
            {
                GenerateTraditionalCoreList(cpuInfo);
            }
        }
        else if (cpuInfo.Vendor == CpuVendor.AMD)
        {
            cpuInfo.IsX3D = IsAmdX3DCpu(cpuInfo.Name);
            cpuInfo.IsHybridArchitecture = cpuInfo.IsX3D;
            GenerateAmdCoreList(cpuInfo);
        }
        else
        {
            GenerateTraditionalCoreList(cpuInfo);
        }

        // 最终验证：确保生成的核心数与系统报告的一致
        ValidateAndFixCoreList(cpuInfo);

        return cpuInfo;
    }

    /// <summary>
    /// 使用 Windows API 获取核心拓扑信息
    /// </summary>
    private List<CoreTopologyInfo>? GetCoreTopologyFromApi()
    {
        try
        {
            uint returnLength = 0;
            GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, IntPtr.Zero, ref returnLength);

            if (returnLength == 0)
                return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)returnLength);
            try
            {
                if (!GetLogicalProcessorInformationEx(LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore, buffer, ref returnLength))
                    return null;

                var result = new List<CoreTopologyInfo>();
                int offset = 0;
                int physicalCoreId = 0;

                while (offset < returnLength)
                {
                    var info = Marshal.PtrToStructure<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>(buffer + offset);

                    if (info.Relationship == LOGICAL_PROCESSOR_RELATIONSHIP.RelationProcessorCore)
                    {
                        // 读取 PROCESSOR_RELATIONSHIP 结构
                        IntPtr procRelPtr = buffer + offset + Marshal.SizeOf<SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX>();
                        var procRel = Marshal.PtrToStructure<PROCESSOR_RELATIONSHIP>(procRelPtr);

                        // 读取 GROUP_AFFINITY
                        IntPtr groupAffinityPtr = procRelPtr + Marshal.SizeOf<PROCESSOR_RELATIONSHIP>();
                        var groupAffinity = Marshal.PtrToStructure<GROUP_AFFINITY>(groupAffinityPtr);

                        // 解析掩码获取逻辑处理器索引
                        ulong mask = groupAffinity.Mask.ToUInt64();
                        bool isFirstThread = true;

                        for (int bit = 0; bit < 64; bit++)
                        {
                            if ((mask & (1UL << bit)) != 0)
                            {
                                result.Add(new CoreTopologyInfo
                                {
                                    LogicalIndex = bit,
                                    PhysicalCoreId = physicalCoreId,
                                    EfficiencyClass = procRel.EfficiencyClass,
                                    IsHyperThread = !isFirstThread
                                });
                                isFirstThread = false;
                            }
                        }

                        physicalCoreId++;
                    }

                    offset += (int)info.Size;
                }

                return result.Count > 0 ? result.OrderBy(c => c.LogicalIndex).ToList() : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 使用 API 拓扑信息生成 Intel 混合架构核心列表
    /// </summary>
    private void GenerateIntelHybridCoreListFromTopology(CpuInfo cpuInfo, List<CoreTopologyInfo> topology)
    {
        // 统计 P 核和 E 核数量
        var pCorePhysicalIds = topology.Where(t => t.EfficiencyClass > 0 && !t.IsHyperThread)
            .Select(t => t.PhysicalCoreId).Distinct().ToList();
        var eCorePhysicalIds = topology.Where(t => t.EfficiencyClass == 0 && !t.IsHyperThread)
            .Select(t => t.PhysicalCoreId).Distinct().ToList();

        cpuInfo.PCoreCount = pCorePhysicalIds.Count;
        cpuInfo.ECoreCount = eCorePhysicalIds.Count;

        // 按逻辑索引排序生成核心列表
        foreach (var coreInfo in topology.OrderBy(t => t.LogicalIndex))
        {
            bool isPCore = coreInfo.EfficiencyClass > 0;
            cpuInfo.Cores.Add(new CpuCore
            {
                Index = coreInfo.LogicalIndex,
                CoreType = isPCore ? CoreType.PCore : CoreType.ECore,
                IsHyperThread = coreInfo.IsHyperThread,
                PhysicalCoreId = coreInfo.PhysicalCoreId,
                IsSelected = isPCore // 默认选中 P 核
            });
        }
    }

    /// <summary>
    /// 验证并修复核心列表，确保与系统报告的逻辑处理器数一致
    /// </summary>
    private void ValidateAndFixCoreList(CpuInfo cpuInfo)
    {
        int expectedCount = cpuInfo.LogicalProcessorCount;
        int actualCount = cpuInfo.Cores.Count;

        if (actualCount == expectedCount)
            return;

        System.Diagnostics.Debug.WriteLine($"核心数量不匹配: 预期 {expectedCount}, 实际 {actualCount}，正在修复...");

        // 核心数量不匹配，使用保守方法重新生成
        cpuInfo.Cores.Clear();

        if (cpuInfo.Vendor == CpuVendor.Intel && cpuInfo.IsHybridArchitecture)
        {
            // 使用实际逻辑处理器数反推配置
            // Intel 混合架构: 总线程 = P核×2 + E核
            // 已知: 物理核心数 = P核 + E核, 逻辑处理器数 = P核×2 + E核
            // 推导: P核 = 逻辑处理器数 - 物理核心数
            int pCores = expectedCount - cpuInfo.PhysicalCoreCount;
            int eCores = cpuInfo.PhysicalCoreCount - pCores;

            // 验证推导是否合理
            if (pCores < 0 || eCores < 0 || pCores > cpuInfo.PhysicalCoreCount)
            {
                // 推导失败，使用传统方式
                GenerateTraditionalCoreList(cpuInfo);
                return;
            }

            cpuInfo.PCoreCount = pCores;
            cpuInfo.ECoreCount = eCores;

            int logicalIndex = 0;
            int physicalCoreId = 0;

            // P核（带超线程）
            for (int i = 0; i < pCores; i++)
            {
                cpuInfo.Cores.Add(new CpuCore
                {
                    Index = logicalIndex++,
                    CoreType = CoreType.PCore,
                    IsHyperThread = false,
                    PhysicalCoreId = physicalCoreId,
                    IsSelected = true
                });

                cpuInfo.Cores.Add(new CpuCore
                {
                    Index = logicalIndex++,
                    CoreType = CoreType.PCore,
                    IsHyperThread = true,
                    PhysicalCoreId = physicalCoreId,
                    IsSelected = true
                });

                physicalCoreId++;
            }

            // E核（无超线程）
            for (int i = 0; i < eCores; i++)
            {
                cpuInfo.Cores.Add(new CpuCore
                {
                    Index = logicalIndex++,
                    CoreType = CoreType.ECore,
                    IsHyperThread = false,
                    PhysicalCoreId = physicalCoreId++,
                    IsSelected = false
                });
            }
        }
        else
        {
            // 非混合架构或 AMD，使用传统方式
            GenerateTraditionalCoreList(cpuInfo);
        }
    }

    /// <summary>
    /// 检测 CPU 制造商
    /// </summary>
    private CpuVendor DetectCpuVendor(string cpuName)
    {
        if (cpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase))
            return CpuVendor.Intel;
        if (cpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) || 
            cpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase))
            return CpuVendor.AMD;
        return CpuVendor.Unknown;
    }

    /// <summary>
    /// 检测是否为 Intel 混合架构 CPU
    /// </summary>
    private bool IsIntelHybridCpu(string cpuName)
    {
        var intelHybridPatterns = new[]
        {
            "12th Gen", "13th Gen", "14th Gen", "15th Gen",
            "i9-12", "i7-12", "i5-12", "i3-12",
            "i9-13", "i7-13", "i5-13", "i3-13",
            "i9-14", "i7-14", "i5-14", "i3-14",
            "Core Ultra"
        };

        return intelHybridPatterns.Any(pattern => 
            cpuName.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 检测是否为 AMD X3D 处理器
    /// </summary>
    private bool IsAmdX3DCpu(string cpuName)
    {
        return cpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase) ||
               cpuName.Contains("3D V-Cache", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 生成 Intel 混合架构核心列表
    /// </summary>
    private void GenerateIntelHybridCoreList(CpuInfo cpuInfo)
    {
        var (pCores, eCores) = ParseIntelHybridCoreCount(cpuInfo.Name, cpuInfo.PhysicalCoreCount, cpuInfo.LogicalProcessorCount);
        
        cpuInfo.PCoreCount = pCores;
        cpuInfo.ECoreCount = eCores;

        int logicalIndex = 0;
        int physicalCoreId = 0;

        // P核（带超线程）
        for (int i = 0; i < pCores; i++)
        {
            cpuInfo.Cores.Add(new CpuCore
            {
                Index = logicalIndex++,
                CoreType = CoreType.PCore,
                IsHyperThread = false,
                PhysicalCoreId = physicalCoreId,
                IsSelected = true
            });

            cpuInfo.Cores.Add(new CpuCore
            {
                Index = logicalIndex++,
                CoreType = CoreType.PCore,
                IsHyperThread = true,
                PhysicalCoreId = physicalCoreId,
                IsSelected = true
            });

            physicalCoreId++;
        }

        // E核（无超线程）
        for (int i = 0; i < eCores; i++)
        {
            cpuInfo.Cores.Add(new CpuCore
            {
                Index = logicalIndex++,
                CoreType = CoreType.ECore,
                IsHyperThread = false,
                PhysicalCoreId = physicalCoreId++,
                IsSelected = false
            });
        }
    }

    /// <summary>
    /// 生成 AMD 核心列表
    /// </summary>
    private void GenerateAmdCoreList(CpuInfo cpuInfo)
    {
        var amdConfig = ParseAmdCoreConfig(cpuInfo.Name, cpuInfo.PhysicalCoreCount);
        
        cpuInfo.CcdCount = amdConfig.CcdCount;
        cpuInfo.VCacheCoreCount = amdConfig.VCacheCores;

        int logicalIndex = 0;
        int physicalCoreId = 0;
        int coresPerCcd = cpuInfo.PhysicalCoreCount / Math.Max(1, amdConfig.CcdCount);
        int threadsPerCore = cpuInfo.HasSmt ? 2 : 1;

        for (int ccd = 0; ccd < amdConfig.CcdCount; ccd++)
        {
            int coresInThisCcd = (ccd == amdConfig.CcdCount - 1) 
                ? cpuInfo.PhysicalCoreCount - (coresPerCcd * ccd) 
                : coresPerCcd;

            for (int coreInCcd = 0; coreInCcd < coresInThisCcd; coreInCcd++)
            {
                // 判断是否为 V-Cache 核心
                // X3D 处理器通常 CCD0 带 V-Cache
                bool isVCacheCore = cpuInfo.IsX3D && ccd == 0;
                var coreType = isVCacheCore ? CoreType.VCache : CoreType.Standard;

                // 默认选中 V-Cache 核心（游戏优化）
                bool defaultSelected = cpuInfo.IsX3D ? isVCacheCore : true;

                for (int thread = 0; thread < threadsPerCore; thread++)
                {
                    cpuInfo.Cores.Add(new CpuCore
                    {
                        Index = logicalIndex++,
                        CoreType = coreType,
                        IsHyperThread = thread > 0,
                        PhysicalCoreId = physicalCoreId,
                        CcdId = ccd,
                        CcxId = coreInCcd / 4, // 每个 CCX 通常 4 核心
                        IsSelected = defaultSelected
                    });
                }

                physicalCoreId++;
            }
        }
    }

    /// <summary>
    /// 生成传统架构核心列表
    /// </summary>
    private void GenerateTraditionalCoreList(CpuInfo cpuInfo)
    {
        int threadsPerCore = cpuInfo.HasSmt ? 2 : 1;

        int logicalIndex = 0;
        for (int physicalCore = 0; physicalCore < cpuInfo.PhysicalCoreCount; physicalCore++)
        {
            for (int thread = 0; thread < threadsPerCore; thread++)
            {
                cpuInfo.Cores.Add(new CpuCore
                {
                    Index = logicalIndex++,
                    CoreType = CoreType.Unknown,
                    IsHyperThread = thread > 0,
                    PhysicalCoreId = physicalCore,
                    IsSelected = true
                });
            }
        }
    }

    /// <summary>
    /// 解析 Intel 混合架构核心配置
    /// </summary>
    private (int pCores, int eCores) ParseIntelHybridCoreCount(string cpuName, int totalPhysicalCores, int logicalProcessorCount)
    {
        var knownConfigs = new Dictionary<string, (int p, int e)>
        {
            // 12代桌面版
            { "i9-12900KS", (8, 8) }, { "i9-12900KF", (8, 8) }, { "i9-12900K", (8, 8) }, 
            { "i9-12900F", (8, 8) }, { "i9-12900", (8, 8) },
            { "i7-12700KF", (8, 4) }, { "i7-12700K", (8, 4) }, { "i7-12700F", (8, 4) }, { "i7-12700", (8, 4) },
            { "i5-12600KF", (6, 4) }, { "i5-12600K", (6, 4) }, { "i5-12600", (6, 0) },
            { "i5-12500", (6, 0) }, { "i5-12490F", (6, 0) }, { "i5-12400F", (6, 0) }, { "i5-12400", (6, 0) },
            { "i3-12300", (4, 0) }, { "i3-12100F", (4, 0) }, { "i3-12100", (4, 0) },
            
            // 12代移动版
            { "i9-12950HX", (8, 8) }, { "i9-12900HX", (8, 8) }, { "i9-12900HK", (6, 8) }, { "i9-12900H", (6, 8) },
            { "i7-12850HX", (8, 8) }, { "i7-12800HX", (8, 8) }, { "i7-12800H", (6, 8) },
            { "i7-12700H", (6, 8) }, { "i7-12650H", (6, 4) },
            { "i5-12600HX", (4, 8) }, { "i5-12600H", (4, 8) }, { "i5-12500H", (4, 8) }, { "i5-12450H", (4, 4) },
            
            // 13代桌面版
            { "i9-13900KS", (8, 16) }, { "i9-13900KF", (8, 16) }, { "i9-13900K", (8, 16) },
            { "i9-13900F", (8, 16) }, { "i9-13900", (8, 16) },
            { "i7-13700KF", (8, 8) }, { "i7-13700K", (8, 8) }, { "i7-13700F", (8, 8) }, { "i7-13700", (8, 8) },
            { "i5-13600KF", (6, 8) }, { "i5-13600K", (6, 8) }, { "i5-13600", (6, 8) },
            { "i5-13500", (6, 8) }, { "i5-13490F", (6, 8) }, { "i5-13400F", (6, 4) }, { "i5-13400", (6, 4) },
            
            // 13代移动版 - 完整列表
            { "i9-13980HX", (8, 16) }, { "i9-13950HX", (8, 16) }, { "i9-13900HX", (8, 16) },
            { "i9-13900HK", (6, 8) }, { "i9-13900H", (6, 8) },
            { "i7-13850HX", (8, 8) }, { "i7-13800H", (6, 8) },
            { "i7-13700HX", (8, 8) }, { "i7-13700H", (6, 8) },
            { "i7-13650HX", (6, 8) }, // 6P + 8E = 14核, 6×2 + 8 = 20线程
            { "i7-13620H", (6, 4) },
            { "i5-13600HX", (6, 8) }, { "i5-13600H", (4, 8) },
            { "i5-13505H", (4, 8) }, { "i5-13500HX", (6, 8) }, { "i5-13500H", (4, 8) },
            { "i5-13450HX", (4, 8) }, { "i5-13420H", (4, 4) },
            
            // 14代桌面版
            { "i9-14900KS", (8, 16) }, { "i9-14900KF", (8, 16) }, { "i9-14900K", (8, 16) },
            { "i9-14900F", (8, 16) }, { "i9-14900", (8, 16) },
            { "i7-14700KF", (8, 12) }, { "i7-14700K", (8, 12) }, { "i7-14700F", (8, 12) }, { "i7-14700", (8, 12) },
            { "i5-14600KF", (6, 8) }, { "i5-14600K", (6, 8) }, { "i5-14600", (6, 8) },
            { "i5-14500", (6, 8) }, { "i5-14490F", (6, 4) }, { "i5-14400F", (6, 4) }, { "i5-14400", (6, 4) },
            
            // 14代移动版
            { "i9-14900HX", (8, 16) },
            { "i7-14700HX", (8, 8) }, { "i7-14650HX", (6, 8) },
            { "i5-14600HX", (6, 8) }, { "i5-14500HX", (6, 8) }, { "i5-14450HX", (4, 8) },
            
            // Core Ultra 1代 (Meteor Lake)
            { "Ultra 9 185H", (6, 8) }, { "Ultra 7 165H", (6, 8) }, { "Ultra 7 164U", (2, 8) },
            { "Ultra 7 155H", (6, 8) }, { "Ultra 7 155U", (2, 8) },
            { "Ultra 5 135H", (4, 8) }, { "Ultra 5 134U", (2, 8) }, { "Ultra 5 125H", (4, 8) }, { "Ultra 5 125U", (2, 8) },
            
            // Core Ultra 2代 (Arrow Lake / Lunar Lake)
            { "Ultra 9 285K", (8, 16) }, { "Ultra 9 285", (8, 16) },
            { "Ultra 7 265K", (8, 12) }, { "Ultra 7 265KF", (8, 12) }, { "Ultra 7 265", (8, 12) },
            { "Ultra 5 245K", (6, 8) }, { "Ultra 5 245KF", (6, 8) }, { "Ultra 5 245", (6, 8) },
        };

        foreach (var config in knownConfigs)
        {
            if (cpuName.Contains(config.Key, StringComparison.OrdinalIgnoreCase))
            {
                return config.Value;
            }
        }

        // 使用智能估算：根据实际逻辑处理器数反推
        // Intel 混合架构: 总线程 = P核×2 + E核
        // 已知: 物理核心数 = P核 + E核, 逻辑处理器数 = P核×2 + E核
        // 推导: P核 = 逻辑处理器数 - 物理核心数
        int estimatedPCores = logicalProcessorCount - totalPhysicalCores;
        int estimatedECores = totalPhysicalCores - estimatedPCores;

        // 验证估算是否合理
        if (estimatedPCores >= 0 && estimatedECores >= 0 && 
            estimatedPCores <= totalPhysicalCores &&
            (estimatedPCores * 2 + estimatedECores) == logicalProcessorCount)
        {
            return (estimatedPCores, estimatedECores);
        }

        // 如果估算失败，假设全部是传统带超线程的核心
        return (totalPhysicalCores, 0);
    }

    /// <summary>
    /// 解析 AMD 核心配置
    /// </summary>
    private (int CcdCount, int VCacheCores) ParseAmdCoreConfig(string cpuName, int physicalCores)
    {
        // AMD Ryzen 处理器配置
        var knownConfigs = new Dictionary<string, (int ccds, int vcache)>
        {
            // Ryzen 9000 系列 (Zen 5)
            { "9950X", (2, 0) }, { "9900X", (2, 0) },
            { "9700X", (1, 0) }, { "9600X", (1, 0) },
            
            // Ryzen 9000X3D 系列 (Zen 5 + 3D V-Cache)
            { "9950X3D", (2, 8) }, { "9900X3D", (2, 8) },
            { "9800X3D", (1, 8) },
            
            // Ryzen 7000 系列 (Zen 4)
            { "7950X", (2, 0) }, { "7900X", (2, 0) }, { "7900", (2, 0) },
            { "7800X", (1, 0) }, { "7700X", (1, 0) }, { "7700", (1, 0) },
            { "7600X", (1, 0) }, { "7600", (1, 0) },
            
            // Ryzen 7000X3D 系列 (Zen 4 + 3D V-Cache)
            { "7950X3D", (2, 8) }, // CCD0 带 V-Cache (8核)
            { "7900X3D", (2, 6) }, // CCD0 带 V-Cache (6核)
            { "7800X3D", (1, 8) }, // 单 CCD 全部带 V-Cache
            
            // Ryzen 5000 系列 (Zen 3)
            { "5950X", (2, 0) }, { "5900X", (2, 0) },
            { "5800X", (1, 0) }, { "5700X", (1, 0) },
            { "5600X", (1, 0) }, { "5600", (1, 0) },
            
            // Ryzen 5000X3D 系列
            { "5800X3D", (1, 8) },
            { "5700X3D", (1, 8) },
            
            // Ryzen 3000 系列 (Zen 2)
            { "3950X", (2, 0) }, { "3900X", (2, 0) }, { "3900XT", (2, 0) },
            { "3800X", (1, 0) }, { "3800XT", (1, 0) },
            { "3700X", (1, 0) }, { "3600X", (1, 0) }, { "3600", (1, 0) },
            
            // Threadripper
            { "7980X", (8, 0) }, { "7970X", (4, 0) }, { "7960X", (2, 0) },
            { "5995WX", (8, 0) }, { "5975WX", (4, 0) }, { "5965WX", (3, 0) },
        };

        foreach (var config in knownConfigs)
        {
            if (cpuName.Contains(config.Key, StringComparison.OrdinalIgnoreCase))
            {
                return config.Value;
            }
        }

        // 默认估算 CCD 数量（从大到小匹配）
        int estimatedCcds = physicalCores switch
        {
            >= 32 => 4,
            >= 24 => 3,
            >= 16 => 2,
            _ => 1
        };

        return (estimatedCcds, 0);
    }

    /// <summary>
    /// 获取核心信息摘要
    /// </summary>
    public string GetCoreSummary(CpuInfo cpuInfo)
    {
        if (cpuInfo.Vendor == CpuVendor.Intel && cpuInfo.IsHybridArchitecture)
        {
            int pThreads = cpuInfo.PCoreCount * 2;
            int eThreads = cpuInfo.ECoreCount;
            return $"{cpuInfo.PCoreCount}P + {cpuInfo.ECoreCount}E = {pThreads + eThreads}T";
        }
        else if (cpuInfo.Vendor == CpuVendor.AMD)
        {
            if (cpuInfo.IsX3D)
            {
                int vcacheThreads = cpuInfo.VCacheCoreCount * (cpuInfo.HasSmt ? 2 : 1);
                int normalCores = cpuInfo.PhysicalCoreCount - cpuInfo.VCacheCoreCount;
                int normalThreads = normalCores * (cpuInfo.HasSmt ? 2 : 1);
                return $"{cpuInfo.VCacheCoreCount}C(3D) + {normalCores}C = {vcacheThreads + normalThreads}T";
            }
            else if (cpuInfo.CcdCount > 1)
            {
                return $"{cpuInfo.CcdCount}CCD × {cpuInfo.PhysicalCoreCount / cpuInfo.CcdCount}C = {cpuInfo.LogicalProcessorCount}T";
            }
        }
        
        return $"{cpuInfo.PhysicalCoreCount}C / {cpuInfo.LogicalProcessorCount}T";
    }

    /// <summary>
    /// 获取仅主线程的核心（排除HT/SMT）
    /// </summary>
    public List<int> GetPrimaryThreadCores(CpuInfo cpuInfo)
    {
        return cpuInfo.Cores
            .Where(c => !c.IsHyperThread)
            .Select(c => c.Index)
            .ToList();
    }

    /// <summary>
    /// 按物理核心分组获取核心
    /// </summary>
    public Dictionary<int, List<CpuCore>> GetCoresByPhysicalCore(CpuInfo cpuInfo)
    {
        return cpuInfo.Cores
            .GroupBy(c => c.PhysicalCoreId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// 按CCD分组获取核心
    /// </summary>
    public Dictionary<int, List<CpuCore>> GetCoresByCcd(CpuInfo cpuInfo)
    {
        return cpuInfo.Cores
            .GroupBy(c => c.CcdId)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// 按CCX分组获取核心
    /// </summary>
    public Dictionary<(int ccd, int ccx), List<CpuCore>> GetCoresByCcx(CpuInfo cpuInfo)
    {
        return cpuInfo.Cores
            .GroupBy(c => (c.CcdId, c.CcxId))
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// 获取隔核选择的核心索引
    /// </summary>
    public List<int> GetAlternatingCores(CpuInfo cpuInfo, bool startWithFirst = true)
    {
        var primaryCores = cpuInfo.Cores
            .Where(c => !c.IsHyperThread)
            .OrderBy(c => c.Index)
            .ToList();

        var result = new List<int>();
        for (int i = startWithFirst ? 0 : 1; i < primaryCores.Count; i += 2)
        {
            result.Add(primaryCores[i].Index);
            // 如果有超线程，也添加对应的HT线程
            var htThread = cpuInfo.Cores.FirstOrDefault(c => 
                c.PhysicalCoreId == primaryCores[i].PhysicalCoreId && c.IsHyperThread);
            if (htThread != null)
            {
                result.Add(htThread.Index);
            }
        }
        return result;
    }

    /// <summary>
    /// 获取指定CCD的所有核心索引
    /// </summary>
    public List<int> GetCcdCores(CpuInfo cpuInfo, int ccdId)
    {
        return cpuInfo.Cores
            .Where(c => c.CcdId == ccdId)
            .Select(c => c.Index)
            .ToList();
    }

    /// <summary>
    /// 获取指定CCX的所有核心索引
    /// </summary>
    public List<int> GetCcxCores(CpuInfo cpuInfo, int ccdId, int ccxId)
    {
        return cpuInfo.Cores
            .Where(c => c.CcdId == ccdId && c.CcxId == ccxId)
            .Select(c => c.Index)
            .ToList();
    }

    /// <summary>
    /// 获取高性能核心（P核或V-Cache核心）
    /// </summary>
    public List<int> GetHighPerformanceCores(CpuInfo cpuInfo)
    {
        if (cpuInfo.Vendor == CpuVendor.Intel && cpuInfo.IsHybridArchitecture)
        {
            return cpuInfo.Cores
                .Where(c => c.CoreType == CoreType.PCore)
                .Select(c => c.Index)
                .ToList();
        }
        else if (cpuInfo.Vendor == CpuVendor.AMD && cpuInfo.IsX3D)
        {
            return cpuInfo.Cores
                .Where(c => c.CoreType == CoreType.VCache)
                .Select(c => c.Index)
                .ToList();
        }
        
        // 非混合架构返回所有核心
        return cpuInfo.Cores.Select(c => c.Index).ToList();
    }

    /// <summary>
    /// 获取低功耗核心（E核或非V-Cache核心）
    /// </summary>
    public List<int> GetEfficiencyCores(CpuInfo cpuInfo)
    {
        if (cpuInfo.Vendor == CpuVendor.Intel && cpuInfo.IsHybridArchitecture)
        {
            return cpuInfo.Cores
                .Where(c => c.CoreType == CoreType.ECore)
                .Select(c => c.Index)
                .ToList();
        }
        else if (cpuInfo.Vendor == CpuVendor.AMD && cpuInfo.IsX3D)
        {
            return cpuInfo.Cores
                .Where(c => c.CoreType == CoreType.Standard)
                .Select(c => c.Index)
                .ToList();
        }
        
        return new List<int>();
    }

    /// <summary>
    /// 获取唯一的CCX列表（用于UI显示）
    /// </summary>
    public List<(int CcdId, int CcxId, int CoreCount)> GetUniqueCcxList(CpuInfo cpuInfo)
    {
        return cpuInfo.Cores
            .GroupBy(c => (c.CcdId, c.CcxId))
            .Select(g => (g.Key.CcdId, g.Key.CcxId, g.Count()))
            .OrderBy(x => x.CcdId)
            .ThenBy(x => x.CcxId)
            .ToList();
    }
}
