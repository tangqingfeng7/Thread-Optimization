using System.Management;
using System.Runtime.InteropServices;
using ThreadOptimization.Models;

namespace ThreadOptimization.Services;

/// <summary>
/// CPU 信息服务
/// </summary>
public class CpuService
{
    [DllImport("kernel32.dll")]
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
        RelationAll = 0xffff
    }

    /// <summary>
    /// 获取 CPU 信息
    /// </summary>
    public CpuInfo GetCpuInfo()
    {
        var cpuInfo = new CpuInfo
        {
            LogicalProcessorCount = Environment.ProcessorCount
        };

        // 获取 CPU 型号名称
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

        // 根据制造商处理
        if (cpuInfo.Vendor == CpuVendor.Intel)
        {
            cpuInfo.IsHybridArchitecture = IsIntelHybridCpu(cpuInfo.Name);
            if (cpuInfo.IsHybridArchitecture)
            {
                GenerateIntelHybridCoreList(cpuInfo);
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

        return cpuInfo;
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
        var (pCores, eCores) = ParseIntelHybridCoreCount(cpuInfo.Name, cpuInfo.PhysicalCoreCount);
        
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
    private (int pCores, int eCores) ParseIntelHybridCoreCount(string cpuName, int totalPhysicalCores)
    {
        var knownConfigs = new Dictionary<string, (int p, int e)>
        {
            // 12代桌面版
            { "i9-12900K", (8, 8) }, { "i9-12900", (8, 8) },
            { "i7-12700K", (8, 4) }, { "i7-12700", (8, 4) },
            { "i5-12600K", (6, 4) }, { "i5-12600", (6, 4) },
            { "i5-12500", (6, 0) }, { "i5-12400", (6, 0) },
            { "i3-12300", (4, 0) }, { "i3-12100", (4, 0) },
            
            // 12代移动版
            { "i9-12900HX", (8, 8) }, { "i9-12900HK", (6, 8) }, { "i9-12900H", (6, 8) },
            { "i7-12800HX", (8, 8) }, { "i7-12700H", (6, 8) }, { "i7-12650H", (6, 8) },
            { "i5-12600HX", (4, 8) }, { "i5-12500H", (4, 8) }, { "i5-12450H", (4, 4) },
            
            // 13代桌面版
            { "i9-13900K", (8, 16) }, { "i9-13900", (8, 16) },
            { "i7-13700K", (8, 8) }, { "i7-13700", (8, 8) },
            { "i5-13600K", (6, 8) }, { "i5-13600", (6, 8) },
            { "i5-13500", (6, 8) }, { "i5-13400", (6, 4) },
            
            // 13代移动版
            { "i9-13900HX", (8, 16) }, { "i9-13900HK", (6, 8) }, { "i9-13900H", (6, 8) },
            { "i7-13800HX", (8, 8) }, { "i7-13700HX", (8, 8) }, { "i7-13700H", (6, 8) },
            { "i5-13600HX", (6, 8) }, { "i5-13500H", (4, 8) }, { "i5-13420H", (4, 4) },
            
            // 14代桌面版
            { "i9-14900K", (8, 16) }, { "i9-14900", (8, 16) },
            { "i7-14700K", (8, 12) }, { "i7-14700", (8, 12) },
            { "i5-14600K", (6, 8) }, { "i5-14600", (6, 8) },
            { "i5-14500", (6, 8) }, { "i5-14400", (6, 4) },
            
            // 14代移动版
            { "i9-14900HX", (8, 16) }, { "i7-14700HX", (8, 8) },
            { "i5-14600HX", (6, 8) }, { "i5-14500HX", (6, 8) },
            
            // Core Ultra (Meteor Lake)
            { "Ultra 9 185H", (6, 8) }, { "Ultra 7 165H", (6, 8) },
            { "Ultra 7 155H", (6, 8) }, { "Ultra 5 135H", (4, 8) },
            { "Ultra 5 125H", (4, 8) },
        };

        foreach (var config in knownConfigs)
        {
            if (cpuName.Contains(config.Key, StringComparison.OrdinalIgnoreCase))
            {
                return config.Value;
            }
        }

        // 默认估算
        int estimatedPCores = totalPhysicalCores / 2;
        int estimatedECores = totalPhysicalCores - estimatedPCores;
        return (estimatedPCores, estimatedECores);
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
}
