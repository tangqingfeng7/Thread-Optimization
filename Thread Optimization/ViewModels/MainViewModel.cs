using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThreadOptimization.Models;
using ThreadOptimization.Services;
using ThreadOptimization.Views;
using Microsoft.Win32;

using Application = System.Windows.Application;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace ThreadOptimization.ViewModels;

public partial class MainViewModel : ObservableObject
{
    #region Native Methods for Memory Optimization
    
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);
    
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    
    #endregion

    private readonly CpuService _cpuService;
    private readonly ProcessService _processService;
    private readonly AffinityService _affinityService;
    private readonly GameService _gameService;
    private readonly NumaService _numaService;
    private readonly DispatcherTimer _autoScanTimer;
    private readonly DispatcherTimer _statsTimer;
    private readonly DispatcherTimer _memoryOptimizeTimer; // 定期内存优化
    private readonly List<WeakReference<CpuCore>> _subscribedCores = new(); // 跟踪订阅的核心
    private AppConfig _config;
    private DateTime _lastGcTime = DateTime.MinValue;
    private const int GC_INTERVAL_SECONDS = 300; // 5分钟GC一次

    [ObservableProperty]
    private CpuInfo? _cpuInfo;

    [ObservableProperty]
    private string _cpuName = "检测中...";

    [ObservableProperty]
    private string _coreSummary = "";

    [ObservableProperty]
    private string _targetProcessName = "";

    [ObservableProperty]
    private string _processStatus = "等待中";

    [ObservableProperty]
    private AppStatus _appStatus = AppStatus.Standby;

    [ObservableProperty]
    private string _statusText = "STANDBY";

    [ObservableProperty]
    private Models.BindingMode _selectedBindingMode = Models.BindingMode.Dynamic;

    [ObservableProperty]
    private CpuCore? _priorityCore;

    [ObservableProperty]
    private string _logMessage = "";

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isProcessFound;

    [ObservableProperty]
    private CpuVendor _cpuVendor = CpuVendor.Unknown;

    [ObservableProperty]
    private bool _isAmd;

    [ObservableProperty]
    private bool _isIntel;

    [ObservableProperty]
    private bool _isX3D;

    [ObservableProperty]
    private bool _isHybrid;

    [ObservableProperty]
    private bool _hasMutipleCcds;

    [ObservableProperty]
    private PresetType _selectedPreset = PresetType.Custom;

    [ObservableProperty]
    private int _selectedCoreCount;

    [ObservableProperty]
    private bool _autoStartMonitoring;

    [ObservableProperty]
    private bool _autoApplyOnProcessStart;
    
    [ObservableProperty]
    private ProcessPriorityLevel _selectedPriority = ProcessPriorityLevel.Normal;
    
    [ObservableProperty]
    private bool _applyToChildThreads = true;
    
    [ObservableProperty]
    private string _runTimeText = "00:00:00";
    
    [ObservableProperty]
    private int _applyCount;
    
    [ObservableProperty]
    private ProfileConfig? _selectedProfile;

    // ===== 多进程管理属性 =====
    [ObservableProperty]
    private ProcessGroup? _selectedProcessGroup;

    [ObservableProperty]
    private string _newProcessName = string.Empty;

    [ObservableProperty]
    private int _totalMonitoredProcesses;

    // ===== 游戏优化属性 =====
    [ObservableProperty]
    private GameInfo? _selectedGame;

    [ObservableProperty]
    private bool _isGameMonitorEnabled = true;

    [ObservableProperty]
    private bool _isScanningGames;

    [ObservableProperty]
    private int _runningGamesCount;

    // ===== NUMA 优化属性 =====
    [ObservableProperty]
    private bool _isNumaSupported;

    [ObservableProperty]
    private string _numaTopology = "";

    [ObservableProperty]
    private NumaNodeInfo? _selectedNumaNode;

    [ObservableProperty]
    private bool _enableNumaOptimization = true;

    [ObservableProperty]
    private string? _crossNumaWarning;

    public ObservableCollection<CpuCore> Cores { get; } = new();
    public ObservableCollection<ProcessGroup> ProcessGroups { get; } = new();
    public ObservableCollection<MonitoredProcess> MonitoredProcesses { get; } = new();
    public ObservableCollection<CpuCore> AvailablePriorityCores { get; } = new();
    public ObservableCollection<PresetConfig> Presets { get; } = new();
    public ObservableCollection<ProfileConfig> Profiles { get; } = new();
    public ObservableCollection<GameInfo> Games { get; } = new();
    public ObservableCollection<NumaNodeInfo> NumaNodes { get; } = new();
    
    public static IEnumerable<ProcessPriorityLevel> PriorityLevels => Enum.GetValues<ProcessPriorityLevel>();

    public MainViewModel()
    {
        _cpuService = new CpuService();
        _processService = new ProcessService();
        _affinityService = new AffinityService(_processService);
        _gameService = new GameService(_processService);
        _numaService = new NumaService();
        _config = AppConfig.Load();

        _autoScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; // 降低频率 3->5秒
        _autoScanTimer.Tick += AutoScanTimer_Tick;
        
        _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statsTimer.Tick += StatsTimer_Tick;

        // 定期内存优化定时器（每5分钟检查一次）
        _memoryOptimizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _memoryOptimizeTimer.Tick += MemoryOptimizeTimer_Tick;
        _memoryOptimizeTimer.Start();

        InitializeCpuInfo();
        InitializePresets();
        InitializeNuma();
        InitializeGameService();
        LoadConfig();
        LoadProfiles();
        LoadProcessGroups();
        LoadGames();
        UpdateSelectedCoreCount();

        // 初始化完成后释放一次内存
        OptimizeMemoryAsync();

        if (_config.AutoApplyOnStart && !string.IsNullOrEmpty(TargetProcessName))
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyConfig();
            }), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// 定期内存优化
    /// </summary>
    private void MemoryOptimizeTimer_Tick(object? sender, EventArgs e)
    {
        // 检查是否需要 GC
        if ((DateTime.Now - _lastGcTime).TotalSeconds >= GC_INTERVAL_SECONDS)
        {
            OptimizeMemoryAsync();
        }
    }

    /// <summary>
    /// 异步优化内存
    /// </summary>
    private async void OptimizeMemoryAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                // 清理集合多余容量
                TrimCollections();
                
                // 强制 GC
                GC.Collect(2, GCCollectionMode.Optimized, false);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Optimized, false);

                // 释放工作集（将不活跃内存换到页面文件）
                SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
                
                _lastGcTime = DateTime.Now;
            }
            catch
            {
                // 忽略优化错误
            }
        });
    }

    /// <summary>
    /// 清理集合多余容量
    /// </summary>
    private void TrimCollections()
    {
        try
        {
            // ObservableCollection 没有 TrimExcess，但我们可以清理弱引用列表
            _subscribedCores.RemoveAll(wr => !wr.TryGetTarget(out _));
        }
        catch
        {
            // 忽略
        }
    }
    
    private void StatsTimer_Tick(object? sender, EventArgs e)
    {
        if (IsRunning)
        {
            var stats = _affinityService.Statistics;
            var elapsed = DateTime.Now - stats.StartTime;
            RunTimeText = elapsed.ToString(@"hh\:mm\:ss");
            ApplyCount = stats.ApplyCount;
        }
    }

    private void AutoScanTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsRunning && AutoApplyOnProcessStart && !string.IsNullOrWhiteSpace(TargetProcessName))
        {
            var processes = _processService.FindProcessesByName(TargetProcessName);
            if (processes.Count > 0)
            {
                LogMessage = $"检测到 {TargetProcessName}，自动应用";
                ApplyConfig();
            }
        }
    }

    partial void OnAutoApplyOnProcessStartChanged(bool value)
    {
        if (value)
        {
            _autoScanTimer.Start();
            LogMessage = "已启用进程自动监控";
        }
        else
        {
            _autoScanTimer.Stop();
            LogMessage = "已禁用进程自动监控";
        }
    }

    private void InitializeCpuInfo()
    {
        try
        {
            // 先取消之前的订阅
            UnsubscribeCoreEvents();
            
            CpuInfo = _cpuService.GetCpuInfo();
            CpuName = CpuInfo.Name;
            CoreSummary = _cpuService.GetCoreSummary(CpuInfo);
            CpuVendor = CpuInfo.Vendor;
            IsAmd = CpuInfo.Vendor == CpuVendor.AMD;
            IsIntel = CpuInfo.Vendor == CpuVendor.Intel;
            IsX3D = CpuInfo.IsX3D;
            IsHybrid = CpuInfo.IsHybridArchitecture;
            HasMutipleCcds = CpuInfo.CcdCount > 1;

            Cores.Clear();
            AvailablePriorityCores.Clear();

            foreach (var core in CpuInfo.Cores)
            {
                // 使用命名方法而非 lambda，便于取消订阅
                core.PropertyChanged += Core_PropertyChanged;
                _subscribedCores.Add(new WeakReference<CpuCore>(core));
                
                Cores.Add(core);
                AvailablePriorityCores.Add(core);
            }

            if (AvailablePriorityCores.Count > 0)
            {
                PriorityCore = AvailablePriorityCores[0];
            }
        }
        catch (Exception ex)
        {
            CpuName = "CPU 检测失败";
            LogMessage = $"错误: {ex.Message}";
        }
    }

    /// <summary>
    /// 核心属性变化处理（避免使用 lambda 导致内存泄漏）
    /// </summary>
    private void Core_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CpuCore.IsSelected))
        {
            UpdateSelectedCoreCount();
        }
    }

    /// <summary>
    /// 取消所有核心事件订阅
    /// </summary>
    private void UnsubscribeCoreEvents()
    {
        foreach (var weakRef in _subscribedCores)
        {
            if (weakRef.TryGetTarget(out var core))
            {
                core.PropertyChanged -= Core_PropertyChanged;
            }
        }
        _subscribedCores.Clear();
    }

    private void UpdateSelectedCoreCount()
    {
        SelectedCoreCount = Cores.Count(c => c.IsSelected);
    }

    private void InitializePresets()
    {
        Presets.Clear();
        
        Presets.Add(new PresetConfig 
        { 
            Name = "游戏模式",
            Icon = "\uE7FC",
            Description = "优先使用高性能核心",
            Type = PresetType.Gaming 
        });
        
        Presets.Add(new PresetConfig 
        { 
            Name = "省电模式",
            Icon = "\uEBB5",
            Description = "优先使用低功耗核心",
            Type = PresetType.PowerSave 
        });
        
        Presets.Add(new PresetConfig 
        { 
            Name = "生产力模式",
            Icon = "\uE8A5",
            Description = "使用全部核心",
            Type = PresetType.Productivity 
        });
        
        if (IsAmd && HasMutipleCcds)
        {
            Presets.Add(new PresetConfig 
            { 
                Name = "单CCD模式",
                Icon = "\uE950",
                Description = "仅使用CCD0（低延迟）",
                Type = PresetType.SingleCcd 
            });
        }
    }

    private void LoadConfig()
    {
        if (!string.IsNullOrEmpty(_config.TargetProcessName))
        {
            TargetProcessName = _config.TargetProcessName;
        }

        if (_config.SelectedCoreIndices.Count > 0)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = _config.SelectedCoreIndices.Contains(core.Index);
            }
        }

        if (_config.PriorityCoreIndex.HasValue)
        {
            PriorityCore = Cores.FirstOrDefault(c => c.Index == _config.PriorityCoreIndex.Value);
        }

        SelectedBindingMode = _config.BindingMode;
        AutoStartMonitoring = _config.AutoApplyOnStart;
        SelectedPriority = _config.ProcessPriority;
        ApplyToChildThreads = _config.ApplyToChildThreads;
    }
    
    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _config.Profiles)
        {
            Profiles.Add(profile);
        }
    }

    /// <summary>
    /// 加载进程组配置
    /// </summary>
    private void LoadProcessGroups()
    {
        ProcessGroups.Clear();
        foreach (var groupConfig in _config.ProcessGroups)
        {
            var group = new ProcessGroup
            {
                Id = groupConfig.Id,
                Name = groupConfig.Name,
                ProcessNames = groupConfig.ProcessNames,
                SelectedCoreIndices = groupConfig.SelectedCoreIndices,
                PriorityCoreIndex = groupConfig.PriorityCoreIndex,
                BindingMode = groupConfig.BindingMode,
                ProcessPriority = groupConfig.ProcessPriority,
                IsEnabled = groupConfig.IsEnabled,
                CreatedAt = groupConfig.CreatedAt
            };
            ProcessGroups.Add(group);
        }
    }

    /// <summary>
    /// 保存进程组配置
    /// </summary>
    private void SaveProcessGroups()
    {
        _config.ProcessGroups.Clear();
        foreach (var group in ProcessGroups)
        {
            _config.ProcessGroups.Add(new ProcessGroupConfig
            {
                Id = group.Id,
                Name = group.Name,
                ProcessNames = group.ProcessNames,
                SelectedCoreIndices = group.SelectedCoreIndices,
                PriorityCoreIndex = group.PriorityCoreIndex,
                BindingMode = group.BindingMode,
                ProcessPriority = group.ProcessPriority,
                IsEnabled = group.IsEnabled,
                CreatedAt = group.CreatedAt
            });
        }
        _config.Save();
    }

    private void SaveConfig()
    {
        _config.TargetProcessName = TargetProcessName;
        _config.SelectedCoreIndices = Cores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
        _config.PriorityCoreIndex = PriorityCore?.Index;
        _config.BindingMode = SelectedBindingMode;
        _config.AutoApplyOnStart = AutoStartMonitoring;
        _config.ProcessPriority = SelectedPriority;
        _config.ApplyToChildThreads = ApplyToChildThreads;
        _config.Save();
    }

    [RelayCommand]
    private void ApplyPreset(PresetType presetType)
    {
        SelectedPreset = presetType;

        switch (presetType)
        {
            case PresetType.Gaming:
                ApplyGamingPreset();
                break;
            case PresetType.PowerSave:
                ApplyPowerSavePreset();
                break;
            case PresetType.Productivity:
                SelectAll();
                break;
            case PresetType.SingleCcd:
                SelectCcd0();
                break;
        }
        
        UpdateSelectedCoreCount();
    }

    private void ApplyGamingPreset()
    {
        SelectedPriority = ProcessPriorityLevel.High;
        
        if (IsIntel && IsHybrid)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = core.CoreType == CoreType.PCore;
            }
            LogMessage = "游戏模式：P核 + 高优先级";
        }
        else if (IsAmd && IsX3D)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = core.CoreType == CoreType.VCache;
            }
            LogMessage = "游戏模式：3D V-Cache 核心 + 高优先级";
        }
        else if (IsAmd && HasMutipleCcds)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = core.CcdId == 0;
            }
            LogMessage = "游戏模式：CCD0 + 高优先级";
        }
        else
        {
            SelectAll();
            LogMessage = "游戏模式：全核心 + 高优先级";
        }

        SelectedBindingMode = Models.BindingMode.Dynamic;
    }

    private void ApplyPowerSavePreset()
    {
        SelectedPriority = ProcessPriorityLevel.BelowNormal;
        
        if (IsIntel && IsHybrid)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = core.CoreType == CoreType.ECore;
            }
            LogMessage = "省电模式：E核 + 低优先级";
        }
        else if (IsAmd && HasMutipleCcds)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = core.CcdId == 0 && !core.IsHyperThread;
            }
            LogMessage = "省电模式：CCD0 主线程 + 低优先级";
        }
        else
        {
            int count = 0;
            int halfCount = Cores.Count / 2;
            foreach (var core in Cores)
            {
                core.IsSelected = count++ < halfCount;
            }
            LogMessage = "省电模式：半数核心 + 低优先级";
        }

        SelectedBindingMode = Models.BindingMode.D3PowerSave;
    }

    [RelayCommand]
    private void SelectAllPCores()
    {
        foreach (var core in Cores) core.IsSelected = core.CoreType == CoreType.PCore;
        LogMessage = "已选择所有 P 核";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void SelectAllECores()
    {
        foreach (var core in Cores) core.IsSelected = core.CoreType == CoreType.ECore;
        LogMessage = "已选择所有 E 核";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void SelectVCacheCores()
    {
        foreach (var core in Cores) core.IsSelected = core.CoreType == CoreType.VCache;
        LogMessage = "已选择所有 3D V-Cache 核心";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void SelectStandardCores()
    {
        foreach (var core in Cores) core.IsSelected = core.CoreType == CoreType.Standard;
        LogMessage = "已选择所有标准核心";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void SelectCcd0()
    {
        foreach (var core in Cores) core.IsSelected = core.CcdId == 0;
        LogMessage = "已选择 CCD0";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void SelectCcd1()
    {
        foreach (var core in Cores) core.IsSelected = core.CcdId == 1;
        LogMessage = "已选择 CCD1";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 仅选择主线程（排除HT/SMT）
    /// </summary>
    [RelayCommand]
    private void SelectPrimaryThreadsOnly()
    {
        if (CpuInfo == null) return;
        
        var primaryCores = _cpuService.GetPrimaryThreadCores(CpuInfo);
        foreach (var core in Cores)
        {
            core.IsSelected = primaryCores.Contains(core.Index);
        }
        LogMessage = $"已选择 {primaryCores.Count} 个主线程";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 隔核选择（奇数核心）
    /// </summary>
    [RelayCommand]
    private void SelectAlternatingOdd()
    {
        if (CpuInfo == null) return;
        
        var alternateCores = _cpuService.GetAlternatingCores(CpuInfo, startWithFirst: true);
        foreach (var core in Cores)
        {
            core.IsSelected = alternateCores.Contains(core.Index);
        }
        LogMessage = $"已隔核选择 {alternateCores.Count} 个核心（奇数）";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 隔核选择（偶数核心）
    /// </summary>
    [RelayCommand]
    private void SelectAlternatingEven()
    {
        if (CpuInfo == null) return;
        
        var alternateCores = _cpuService.GetAlternatingCores(CpuInfo, startWithFirst: false);
        foreach (var core in Cores)
        {
            core.IsSelected = alternateCores.Contains(core.Index);
        }
        LogMessage = $"已隔核选择 {alternateCores.Count} 个核心（偶数）";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 选择高性能核心（P核/V-Cache）
    /// </summary>
    [RelayCommand]
    private void SelectHighPerformanceCores()
    {
        if (CpuInfo == null) return;
        
        var highPerfCores = _cpuService.GetHighPerformanceCores(CpuInfo);
        foreach (var core in Cores)
        {
            core.IsSelected = highPerfCores.Contains(core.Index);
        }
        
        var typeLabel = IsIntel && IsHybrid ? "P核" : (IsX3D ? "V-Cache核心" : "全部核心");
        LogMessage = $"已选择 {highPerfCores.Count} 个{typeLabel}";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 选择低功耗核心（E核/标准核心）
    /// </summary>
    [RelayCommand]
    private void SelectEfficiencyCores()
    {
        if (CpuInfo == null) return;
        
        var effCores = _cpuService.GetEfficiencyCores(CpuInfo);
        foreach (var core in Cores)
        {
            core.IsSelected = effCores.Contains(core.Index);
        }
        
        var typeLabel = IsIntel && IsHybrid ? "E核" : (IsX3D ? "标准核心" : "");
        LogMessage = effCores.Count > 0 
            ? $"已选择 {effCores.Count} 个{typeLabel}" 
            : "当前CPU无低功耗核心";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 按物理核心选择（选中指定物理核心的所有线程）
    /// </summary>
    [RelayCommand]
    private void SelectByPhysicalCore(int physicalCoreId)
    {
        foreach (var core in Cores)
        {
            if (core.PhysicalCoreId == physicalCoreId)
            {
                core.IsSelected = !core.IsSelected;
            }
        }
        LogMessage = $"切换物理核心 {physicalCoreId}";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 选择指定CCX的核心
    /// </summary>
    [RelayCommand]
    private void SelectCcx(string ccxParam)
    {
        if (CpuInfo == null || string.IsNullOrEmpty(ccxParam)) return;
        
        var parts = ccxParam.Split(',');
        if (parts.Length != 2) return;
        
        if (int.TryParse(parts[0], out int ccdId) && int.TryParse(parts[1], out int ccxId))
        {
            var ccxCores = _cpuService.GetCcxCores(CpuInfo, ccdId, ccxId);
            foreach (var core in Cores)
            {
                core.IsSelected = ccxCores.Contains(core.Index);
            }
            LogMessage = $"已选择 CCD{ccdId} CCX{ccxId} 的 {ccxCores.Count} 个核心";
            SelectedPreset = PresetType.Custom;
            UpdateSelectedCoreCount();
        }
    }

    /// <summary>
    /// 反选核心
    /// </summary>
    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var core in Cores)
        {
            core.IsSelected = !core.IsSelected;
        }
        LogMessage = "已反选";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 选择前N个核心
    /// </summary>
    [RelayCommand]
    private void SelectFirstNCores(int count)
    {
        int selected = 0;
        foreach (var core in Cores.OrderBy(c => c.Index))
        {
            core.IsSelected = selected < count;
            selected++;
        }
        LogMessage = $"已选择前 {count} 个核心";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    /// <summary>
    /// 选择后N个核心
    /// </summary>
    [RelayCommand]
    private void SelectLastNCores(int count)
    {
        int startIndex = Math.Max(0, Cores.Count - count);
        int index = 0;
        foreach (var core in Cores.OrderBy(c => c.Index))
        {
            core.IsSelected = index >= startIndex;
            index++;
        }
        LogMessage = $"已选择后 {count} 个核心";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var core in Cores) core.IsSelected = true;
        LogMessage = "已全选";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var core in Cores) core.IsSelected = false;
        LogMessage = "已清空";
        SelectedPreset = PresetType.Custom;
        UpdateSelectedCoreCount();
    }

    [RelayCommand]
    private void ApplySelection()
    {
        var selectedCores = Cores.Where(c => c.IsSelected).ToList();
        if (selectedCores.Count == 0)
        {
            LogMessage = "请至少选择一个核心";
            return;
        }

        var mask = _affinityService.CalculateAffinityMask(selectedCores);
        LogMessage = $"掩码: 0x{mask:X}，{selectedCores.Count} 核心";
        SaveConfig();
    }

    [RelayCommand]
    private void ScanProcess()
    {
        if (string.IsNullOrWhiteSpace(TargetProcessName))
        {
            ProcessStatus = "请输入进程名";
            return;
        }

        var processes = _processService.FindProcessesByName(TargetProcessName);
        
        if (processes.Count > 0)
        {
            ProcessStatus = $"找到 {processes.Count} 个";
            IsProcessFound = true;
        }
        else
        {
            ProcessStatus = "等待中";
            IsProcessFound = false;
        }
    }

    [RelayCommand]
    private void BrowseProcess()
    {
        var selector = new ProcessSelectorWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (selector.ShowDialog() == true && !string.IsNullOrEmpty(selector.SelectedProcessName))
        {
            TargetProcessName = selector.SelectedProcessName;
            ScanProcess();
            SaveConfig();
        }
    }

    [RelayCommand]
    private void ApplyConfig()
    {
        if (string.IsNullOrWhiteSpace(TargetProcessName))
        {
            LogMessage = "请输入目标进程名";
            return;
        }

        var selectedCores = Cores.Where(c => c.IsSelected).ToList();
        if (selectedCores.Count == 0)
        {
            LogMessage = "请至少选择一个核心";
            return;
        }

        var affinityMask = _affinityService.CalculateAffinityMask(selectedCores);
        int? priorityCoreIndex = PriorityCore?.Index;

        _affinityService.StartMonitoring(
            TargetProcessName,
            affinityMask,
            SelectedBindingMode,
            priorityCoreIndex,
            SelectedPriority,
            ApplyToChildThreads,
            _config.MonitorInterval,
            status => Application.Current.Dispatcher.Invoke(() => LogMessage = status),
            found => Application.Current.Dispatcher.Invoke(() =>
            {
                IsProcessFound = found;
                ProcessStatus = found ? "运行中" : "等待中";
            })
        );

        IsRunning = true;
        AppStatus = AppStatus.Running;
        StatusText = "RUNNING";
        _statsTimer.Start();
        LogMessage = $"监控 {TargetProcessName}，{selectedCores.Count} 核心，{GetPriorityName(SelectedPriority)}";
        
        SaveConfig();
    }

    [RelayCommand]
    private void Stop()
    {
        _affinityService.StopMonitoring();
        _statsTimer.Stop();
        
        IsRunning = false;
        AppStatus = AppStatus.Standby;
        StatusText = "STANDBY";
        ProcessStatus = "已停止";
        LogMessage = $"已停止，运行 {RunTimeText}，应用 {ApplyCount} 次";
    }

    [RelayCommand]
    private void ToggleAutoStart()
    {
        AutoStartMonitoring = !AutoStartMonitoring;
        _config.AutoApplyOnStart = AutoStartMonitoring;
        _config.Save();
        LogMessage = AutoStartMonitoring ? "已启用自启" : "已禁用自启";
    }
    
    [RelayCommand]
    private void SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(TargetProcessName))
        {
            LogMessage = "请先设置进程名";
            return;
        }
        
        var profile = new ProfileConfig
        {
            Name = $"{TargetProcessName} 配置",
            ProcessName = TargetProcessName,
            SelectedCoreIndices = Cores.Where(c => c.IsSelected).Select(c => c.Index).ToList(),
            PriorityCoreIndex = PriorityCore?.Index,
            BindingMode = SelectedBindingMode,
            ProcessPriority = SelectedPriority,
            CreatedAt = DateTime.Now
        };
        
        Profiles.Add(profile);
        _config.Profiles.Add(profile);
        _config.Save();
        
        LogMessage = $"已保存配置: {profile.Name}";
    }
    
    [RelayCommand]
    private void LoadProfile(ProfileConfig? profile)
    {
        if (profile == null) return;
        
        TargetProcessName = profile.ProcessName;
        
        foreach (var core in Cores)
        {
            core.IsSelected = profile.SelectedCoreIndices.Contains(core.Index);
        }
        
        if (profile.PriorityCoreIndex.HasValue)
        {
            PriorityCore = Cores.FirstOrDefault(c => c.Index == profile.PriorityCoreIndex.Value);
        }
        
        SelectedBindingMode = profile.BindingMode;
        SelectedPriority = profile.ProcessPriority;
        
        UpdateSelectedCoreCount();
        LogMessage = $"已加载: {profile.Name}";
    }
    
    [RelayCommand]
    private void DeleteProfile(ProfileConfig? profile)
    {
        if (profile == null) return;
        
        Profiles.Remove(profile);
        _config.Profiles.Remove(profile);
        _config.Save();
        
        LogMessage = $"已删除: {profile.Name}";
    }
    
    [RelayCommand]
    private void ExportConfig()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON 文件|*.json",
            FileName = "test_config.json"
        };
        
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = _config.Export();
                System.IO.File.WriteAllText(dialog.FileName, json);
                LogMessage = "配置已导出";
            }
            catch (Exception ex)
            {
                LogMessage = $"导出失败: {ex.Message}";
            }
        }
    }
    
    // ===== 多进程管理命令 =====

    [RelayCommand]
    private void CreateProcessGroup()
    {
        var selectedCores = Cores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
        if (selectedCores.Count == 0)
        {
            LogMessage = "请先选择核心";
            return;
        }

        var group = new ProcessGroup
        {
            Name = $"进程组 {ProcessGroups.Count + 1}",
            ProcessNames = new List<string>(),
            SelectedCoreIndices = selectedCores,
            PriorityCoreIndex = PriorityCore?.Index,
            BindingMode = SelectedBindingMode,
            ProcessPriority = SelectedPriority
        };

        ProcessGroups.Add(group);
        SelectedProcessGroup = group;
        SaveProcessGroups();
        LogMessage = $"已创建进程组: {group.Name}";
    }

    [RelayCommand]
    private void DeleteProcessGroup(ProcessGroup? group)
    {
        if (group == null) return;

        if (group.IsRunning)
        {
            StopProcessGroup(group);
        }

        ProcessGroups.Remove(group);
        SaveProcessGroups();
        LogMessage = $"已删除进程组: {group.Name}";
    }

    [RelayCommand]
    private void AddProcessToGroup()
    {
        if (SelectedProcessGroup == null)
        {
            LogMessage = "请先选择进程组";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewProcessName))
        {
            LogMessage = "请输入进程名";
            return;
        }

        var processName = NewProcessName.Trim();
        if (!SelectedProcessGroup.ProcessNames.Contains(processName, StringComparer.OrdinalIgnoreCase))
        {
            SelectedProcessGroup.ProcessNames.Add(processName);
            SaveProcessGroups();
            LogMessage = $"已添加进程: {processName}";
        }
        else
        {
            LogMessage = "进程已存在";
        }

        NewProcessName = string.Empty;
    }

    [RelayCommand]
    private void RemoveProcessFromGroup(string? processName)
    {
        if (SelectedProcessGroup == null || string.IsNullOrEmpty(processName)) return;

        SelectedProcessGroup.ProcessNames.Remove(processName);
        SaveProcessGroups();
        LogMessage = $"已移除进程: {processName}";
    }

    [RelayCommand]
    private void StartProcessGroup(ProcessGroup? group)
    {
        if (group == null || group.ProcessNames.Count == 0)
        {
            LogMessage = "进程组为空";
            return;
        }

        var affinityMask = _affinityService.CalculateAffinityMask(
            Cores.Where(c => group.SelectedCoreIndices.Contains(c.Index)));

        // 为每个进程名启动监控
        foreach (var processName in group.ProcessNames)
        {
            var processes = _processService.FindProcessesByName(processName);
            foreach (var proc in processes)
            {
                // 添加到监控列表
                if (!MonitoredProcesses.Any(p => p.ProcessId == proc.ProcessId))
                {
                    MonitoredProcesses.Add(new MonitoredProcess
                    {
                        ProcessId = proc.ProcessId,
                        ProcessName = proc.ProcessName,
                        WindowTitle = proc.WindowTitle,
                        GroupId = group.Id,
                        StartTime = DateTime.Now
                    });
                }

                // 应用亲和性
                _affinityService.SetProcessAffinity(proc.ProcessId, affinityMask);
                _affinityService.SetProcessPriority(proc.ProcessId, group.ProcessPriority);
            }
        }

        group.IsRunning = true;
        group.StatusText = "运行中";
        group.DetectedProcessCount = MonitoredProcesses.Count(p => p.GroupId == group.Id);
        SaveProcessGroups();
        LogMessage = $"已启动进程组: {group.Name}";
    }

    [RelayCommand]
    private void StopProcessGroup(ProcessGroup? group)
    {
        if (group == null) return;

        // 移除该组的监控进程
        var toRemove = MonitoredProcesses.Where(p => p.GroupId == group.Id).ToList();
        foreach (var proc in toRemove)
        {
            MonitoredProcesses.Remove(proc);
        }

        group.IsRunning = false;
        group.StatusText = "已停止";
        group.DetectedProcessCount = 0;
        SaveProcessGroups();
        LogMessage = $"已停止进程组: {group.Name}";
    }

    [RelayCommand]
    private void StartAllProcessGroups()
    {
        foreach (var group in ProcessGroups.Where(g => g.IsEnabled && !g.IsRunning))
        {
            StartProcessGroup(group);
        }
        LogMessage = "已启动所有进程组";
    }

    [RelayCommand]
    private void StopAllProcessGroups()
    {
        foreach (var group in ProcessGroups.Where(g => g.IsRunning))
        {
            StopProcessGroup(group);
        }
        LogMessage = "已停止所有进程组";
    }

    [RelayCommand]
    private void ImportConfig()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON 文件|*.json"
        };
        
        if (dialog.ShowDialog() == true)
        {
            try
            {
                var json = System.IO.File.ReadAllText(dialog.FileName);
                var imported = AppConfig.Import(json);
                
                if (imported != null)
                {
                    _config = imported;
                    LoadConfig();
                    LoadProfiles();
                    _config.Save();
                    LogMessage = "配置已导入";
                }
                else
                {
                    LogMessage = "导入失败：无效的配置文件";
                }
            }
            catch (Exception ex)
            {
                LogMessage = $"导入失败: {ex.Message}";
            }
        }
    }

    public void ToggleMonitoring()
    {
        if (IsRunning) Stop();
        else ApplyConfig();
    }

    private string GetPriorityName(ProcessPriorityLevel priority)
    {
        return priority switch
        {
            ProcessPriorityLevel.Idle => "空闲",
            ProcessPriorityLevel.BelowNormal => "低",
            ProcessPriorityLevel.Normal => "正常",
            ProcessPriorityLevel.AboveNormal => "较高",
            ProcessPriorityLevel.High => "高",
            ProcessPriorityLevel.RealTime => "实时",
            _ => "未知"
        };
    }
    
    private string GetBindingModeName(Models.BindingMode mode)
    {
        return mode switch
        {
            Models.BindingMode.Dynamic => "动态",
            Models.BindingMode.Static => "静态",
            Models.BindingMode.D2 => "D2",
            Models.BindingMode.D3PowerSave => "D3省电",
            _ => "未知"
        };
    }

    // ===== NUMA 初始化与命令 =====

    private void InitializeNuma()
    {
        _numaService.Initialize();
        IsNumaSupported = _numaService.IsNumaSupported;
        NumaTopology = _numaService.GetNumaTopologySummary();

        NumaNodes.Clear();
        foreach (var node in _numaService.NumaNodes)
        {
            NumaNodes.Add(node);
        }

        if (NumaNodes.Count > 0)
        {
            SelectedNumaNode = NumaNodes[0];
        }
    }

    [RelayCommand]
    private void SelectNumaNode(NumaNodeInfo? node)
    {
        if (node == null) return;

        // 选中该 NUMA 节点的所有核心
        foreach (var core in Cores)
        {
            core.IsSelected = node.CoreIndices.Contains(core.Index);
        }

        UpdateSelectedCoreCount();
        LogMessage = $"已选择 NUMA {node.NodeId} 的 {node.CoreCount} 个核心";
    }

    [RelayCommand]
    private void ApplyNumaOptimization()
    {
        var suggestion = _numaService.GetGameOptimizationSuggestion();
        
        if (suggestion.RecommendedGameNode.HasValue)
        {
            foreach (var core in Cores)
            {
                core.IsSelected = suggestion.GameNodeCores.Contains(core.Index);
            }
            UpdateSelectedCoreCount();
            LogMessage = suggestion.Reason;
        }
        else
        {
            LogMessage = "系统不支持 NUMA 优化";
        }
    }

    partial void OnSelectedCoreCountChanged(int value)
    {
        // 检查是否跨 NUMA 节点
        var selectedCores = Cores.Where(c => c.IsSelected).Select(c => c.Index);
        CrossNumaWarning = _numaService.GetCrossNumaWarning(selectedCores);
    }

    // ===== 游戏服务初始化与命令 =====

    private void InitializeGameService()
    {
        _gameService.OnGameStarted += game =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                game.IsRunning = true;
                RunningGamesCount = Games.Count(g => g.IsRunning);
                
                if (game.AutoApply && game.HasConfiguration)
                {
                    ApplyGameConfiguration(game);
                    LogMessage = $"检测到 {game.Name}，已自动应用配置";
                }
            });
        };

        _gameService.OnGameStopped += game =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                game.IsRunning = false;
                RunningGamesCount = Games.Count(g => g.IsRunning);
                LogMessage = $"{game.Name} 已退出";
            });
        };

        if (IsGameMonitorEnabled)
        {
            _gameService.StartGameMonitor();
        }
    }

    private void LoadGames()
    {
        _gameService.LoadGames();
        Games.Clear();
        foreach (var game in _gameService.Games)
        {
            Games.Add(game);
        }
    }

    [RelayCommand]
    private async Task ScanGamesAsync()
    {
        if (IsScanningGames) return;

        IsScanningGames = true;
        LogMessage = "正在扫描游戏库...";

        try
        {
            var scannedGames = await _gameService.ScanAllGamesAsync();

            Application.Current.Dispatcher.Invoke(() =>
            {
                int newCount = 0;
                foreach (var game in scannedGames)
                {
                    if (!Games.Any(g => g.PlatformGameId == game.PlatformGameId && g.Platform == game.Platform))
                    {
                        Games.Add(game);
                        _gameService.AddGame(game);
                        newCount++;
                    }
                }

                _gameService.SaveGames();
                LogMessage = $"扫描完成，发现 {scannedGames.Count} 个游戏，新增 {newCount} 个";
            });
        }
        catch (Exception ex)
        {
            LogMessage = $"扫描失败: {ex.Message}";
        }
        finally
        {
            IsScanningGames = false;
        }
    }

    [RelayCommand]
    private void ConfigureGame(GameInfo? game)
    {
        if (game == null) return;

        // 将当前核心选择应用到游戏配置
        game.SelectedCoreIndices = Cores.Where(c => c.IsSelected).Select(c => c.Index).ToList();
        game.PriorityCoreIndex = PriorityCore?.Index;
        game.BindingMode = SelectedBindingMode;
        game.ProcessPriority = SelectedPriority;
        game.PreferredNumaNode = SelectedNumaNode?.NodeId;
        game.HasConfiguration = true;

        _gameService.SaveGames();
        LogMessage = $"已保存 {game.Name} 的核心配置";
    }

    [RelayCommand]
    private void LoadGameConfiguration(GameInfo? game)
    {
        if (game == null || !game.HasConfiguration) return;

        // 加载游戏配置到当前设置
        foreach (var core in Cores)
        {
            core.IsSelected = game.SelectedCoreIndices.Contains(core.Index);
        }

        if (game.PriorityCoreIndex.HasValue)
        {
            PriorityCore = Cores.FirstOrDefault(c => c.Index == game.PriorityCoreIndex.Value);
        }

        SelectedBindingMode = game.BindingMode;
        SelectedPriority = game.ProcessPriority;
        TargetProcessName = game.ProcessName;

        if (game.PreferredNumaNode.HasValue)
        {
            SelectedNumaNode = NumaNodes.FirstOrDefault(n => n.NodeId == game.PreferredNumaNode.Value);
        }

        UpdateSelectedCoreCount();
        LogMessage = $"已加载 {game.Name} 的配置";
    }

    [RelayCommand]
    private void LaunchGame(GameInfo? game)
    {
        if (game == null) return;

        if (_gameService.LaunchGame(game))
        {
            LogMessage = $"正在启动 {game.Name}...";

            // 如果有配置，预设目标进程
            if (game.HasConfiguration)
            {
                LoadGameConfiguration(game);
            }
        }
        else
        {
            LogMessage = $"启动 {game.Name} 失败";
        }
    }

    [RelayCommand]
    private void RemoveGame(GameInfo? game)
    {
        if (game == null) return;

        Games.Remove(game);
        _gameService.RemoveGame(game.Id);
        _gameService.SaveGames();
        LogMessage = $"已移除 {game.Name}";
    }

    /// <summary>
    /// 当 IsGameMonitorEnabled 属性变化时自动调用
    /// 处理游戏监控的启动/停止逻辑
    /// </summary>
    partial void OnIsGameMonitorEnabledChanged(bool value)
    {
        if (value)
        {
            _gameService.StartGameMonitor();
            LogMessage = "游戏监控已启动";
        }
        else
        {
            _gameService.StopGameMonitor();
            LogMessage = "游戏监控已停止";
        }
    }

    private void ApplyGameConfiguration(GameInfo game)
    {
        if (string.IsNullOrEmpty(game.ProcessName)) return;

        var processes = _processService.FindProcessesByName(game.ProcessName);
        if (processes.Count == 0) return;

        var affinityMask = _affinityService.CalculateAffinityMask(
            Cores.Where(c => game.SelectedCoreIndices.Contains(c.Index)));

        foreach (var proc in processes)
        {
            _affinityService.SetProcessAffinity(proc.ProcessId, affinityMask);
            _affinityService.SetProcessPriority(proc.ProcessId, game.ProcessPriority);
        }
    }

    public void Cleanup()
    {
        // 停止所有定时器
        _autoScanTimer.Stop();
        _statsTimer.Stop();
        _memoryOptimizeTimer.Stop();
        
        // 取消核心事件订阅（防止内存泄漏）
        UnsubscribeCoreEvents();
        
        // 取消游戏服务事件
        _gameService.OnGameStarted -= null;
        _gameService.OnGameStopped -= null;
        
        // 停止服务
        _affinityService.StopMonitoring();
        _gameService.StopGameMonitor();
        
        // 保存配置
        _gameService.SaveGames();
        SaveConfig();
        SaveProcessGroups();
        
        // 清理集合
        Cores.Clear();
        AvailablePriorityCores.Clear();
        ProcessGroups.Clear();
        MonitoredProcesses.Clear();
        Games.Clear();
        NumaNodes.Clear();
        Profiles.Clear();
        Presets.Clear();
        
        // 最终内存清理
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
        SetProcessWorkingSetSize(GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
    }
}
