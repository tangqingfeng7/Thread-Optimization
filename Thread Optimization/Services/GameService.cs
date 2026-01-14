using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ThreadOptimization.Models;

namespace ThreadOptimization.Services;

/// <summary>
/// 游戏检测与管理服务
/// </summary>
public class GameService : IDisposable
{
    private readonly List<GameInfo> _games = new();
    private readonly string _configPath;
    private readonly ProcessService _processService;
    private System.Timers.Timer? _gameMonitorTimer;
    private bool _disposed;
    
    // 懒加载游戏进程数据库
    private static Lazy<Dictionary<string, string[]>>? _knownGameProcesses;
    private static Lazy<Dictionary<string, string[]>>? _knownXboxGames;
    private static Lazy<Dictionary<string, string[]>>? _knownWeGameGames;

    public event Action<GameInfo>? OnGameStarted;
    public event Action<GameInfo>? OnGameStopped;

    /// <summary>
    /// 常见游戏进程名数据库 - 懒加载以节省内存
    /// </summary>
    private static Dictionary<string, string[]> KnownGameProcesses => 
        (_knownGameProcesses ??= new Lazy<Dictionary<string, string[]>>(BuildKnownGameProcesses)).Value;

    /// <summary>
    /// 构建游戏进程数据库（仅在首次使用时调用）
    /// </summary>
    private static Dictionary<string, string[]> BuildKnownGameProcesses() => new(StringComparer.OrdinalIgnoreCase)
    {
        // 热门游戏（精简列表，保留最常用的）
        { "Counter-Strike 2", new[] { "cs2" } },
        { "Dota 2", new[] { "dota2" } },
        { "PUBG", new[] { "TslGame", "PUBG" } },
        { "GTA5", new[] { "GTA5", "PlayGTAV" } },
        { "Apex Legends", new[] { "r5apex" } },
        { "Elden Ring", new[] { "eldenring" } },
        { "Cyberpunk 2077", new[] { "Cyberpunk2077" } },
        { "Baldur's Gate 3", new[] { "bg3", "bg3_dx11" } },
        { "Palworld", new[] { "Palworld-Win64-Shipping" } },
        { "Path of Exile", new[] { "PathOfExile", "PathOfExile_x64" } },
        { "Destiny 2", new[] { "destiny2" } },
        { "Warframe", new[] { "Warframe.x64" } },
        { "Rainbow Six Siege", new[] { "RainbowSix", "RainbowSix_Vulkan" } },
        { "Forza Horizon 5", new[] { "ForzaHorizon5" } },
        { "Civilization VI", new[] { "CivilizationVI", "CivilizationVI_DX12" } },
        { "Diablo IV", new[] { "Diablo IV" } },
        { "Valheim", new[] { "valheim" } },
        { "Starfield", new[] { "Starfield" } },
        { "Fortnite", new[] { "FortniteClient-Win64-Shipping" } },
        { "魔兽世界", new[] { "Wow", "WowClassic" } },
        { "守望先锋", new[] { "Overwatch" } },
        { "炉石传说", new[] { "Hearthstone" } },
        { "英雄联盟", new[] { "League of Legends" } },
        { "VALORANT", new[] { "VALORANT-Win64-Shipping" } },
        { "原神", new[] { "GenshinImpact", "YuanShen" } },
        { "崩坏：星穹铁道", new[] { "StarRail" } },
        { "绝区零", new[] { "ZenlessZoneZero" } },
        { "鸣潮", new[] { "Wuthering Waves", "Client-Win64-Shipping" } },
        { "永劫无间", new[] { "NarakaBladepoint" } },
        { "三角洲行动", new[] { "DeltaForce", "DeltaForce-Win64-Shipping", "deltaforce" } },
        { "DNF", new[] { "DNF" } },
        { "穿越火线", new[] { "crossfire" } },
    };

    /// <summary>
    /// 需要排除的可执行文件关键词
    /// </summary>
    private static readonly string[] ExcludedExeKeywords = 
    {
        "unins", "uninst", "setup", "install", "config", "crash", "report", 
        "updater", "update", "redist", "vcredist", "dxsetup", "dotnet", 
        "ue4prereq", "physx", "directx", "easyanticheat", "battleye",
        "helper", "tool", "util", "editor", "server", "dedicated",
        "benchmark", "diagnostic", "repair", "patch", "prerequisite"
    };

    /// <summary>
    /// 应该优先选择的可执行文件关键词
    /// </summary>
    private static readonly string[] PreferredExeKeywords =
    {
        "game", "play", "start", "main", "win64", "x64", "shipping", "client"
    };

    public GameService(ProcessService processService)
    {
        _processService = processService;
        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ThreadOptimization",
            "games.json");
    }

    /// <summary>
    /// 获取所有已配置的游戏
    /// </summary>
    public IReadOnlyList<GameInfo> Games => _games.AsReadOnly();

    /// <summary>
    /// 扫描所有平台的游戏
    /// </summary>
    public async Task<List<GameInfo>> ScanAllGamesAsync()
    {
        var allGames = new List<GameInfo>();

        await Task.Run(() =>
        {
            // 扫描 Steam 游戏
            var steamGames = ScanSteamGames();
            allGames.AddRange(steamGames);

            // 扫描 Epic Games
            var epicGames = ScanEpicGames();
            allGames.AddRange(epicGames);

            // 扫描 Xbox/Microsoft Store 游戏
            var xboxGames = ScanXboxGames();
            allGames.AddRange(xboxGames);

            // 扫描 GOG Galaxy 游戏
            var gogGames = ScanGOGGames();
            allGames.AddRange(gogGames);

            // 扫描 Ubisoft Connect 游戏
            var ubisoftGames = ScanUbisoftGames();
            allGames.AddRange(ubisoftGames);

            // 扫描 EA App 游戏
            var eaGames = ScanEAGames();
            allGames.AddRange(eaGames);

            // 扫描 Battle.net 游戏
            var battlenetGames = ScanBattlenetGames();
            allGames.AddRange(battlenetGames);

            // 扫描 Riot Games 游戏
            var riotGames = ScanRiotGames();
            allGames.AddRange(riotGames);

            // 扫描 WeGame 游戏
            var wegameGames = ScanWeGameGames();
            allGames.AddRange(wegameGames);

            // 扫描网易游戏（永劫无间等）
            var neteaseGames = ScanNeteaseGames();
            allGames.AddRange(neteaseGames);

            // 扫描腾讯游戏（三角洲行动等）
            var tencentGames = ScanTencentGames();
            allGames.AddRange(tencentGames);
        });

        return allGames;
    }

    /// <summary>
    /// 扫描腾讯游戏（三角洲行动等）
    /// </summary>
    public List<GameInfo> ScanTencentGames()
    {
        var games = new List<GameInfo>();

        // 腾讯游戏定义
        var tencentGameDefs = new Dictionary<string, string[]>
        {
            { "三角洲行动", new[] { "DeltaForce.exe", "DeltaForce-Win64-Shipping.exe", "deltaforce.exe", "game.exe" } },
            { "Delta Force", new[] { "DeltaForce.exe", "DeltaForce-Win64-Shipping.exe", "deltaforce.exe", "game.exe" } },
            { "穿越火线", new[] { "crossfire.exe", "cf.exe" } },
            { "CrossFire", new[] { "crossfire.exe", "cf.exe" } },
            { "QQ飞车", new[] { "GameApp.exe", "QQSpeed.exe" } },
            { "QQ炫舞", new[] { "QQX5.exe", "x5.exe" } },
            { "DNF", new[] { "DNF.exe" } },
            { "地下城与勇士", new[] { "DNF.exe" } },
            { "逆战", new[] { "NZ.exe", "nz.exe" } },
            { "使命召唤手游", new[] { "CODM.exe" } },
            { "王者荣耀", new[] { "GameCenter.exe" } },
            { "和平精英", new[] { "PUBGM.exe", "GameLoop.exe" } },
        };

        try
        {
            // 1. 从注册表查找三角洲行动
            var deltaForcePath = GetDeltaForceInstallPathFromRegistry();
            if (!string.IsNullOrEmpty(deltaForcePath) && Directory.Exists(deltaForcePath))
            {
                TryAddDeltaForce(games, deltaForcePath);
            }

            // 2. 搜索常见安装路径
            var searchPaths = new List<string>
            {
                // 腾讯游戏目录
                @"C:\腾讯游戏",
                @"D:\腾讯游戏",
                @"E:\腾讯游戏",
                @"F:\腾讯游戏",
                @"C:\Tencent Games",
                @"D:\Tencent Games",
                @"E:\Tencent Games",
                @"C:\Program Files\腾讯游戏",
                @"D:\Program Files\腾讯游戏",
                @"C:\Program Files (x86)\腾讯游戏",
                @"D:\Program Files (x86)\腾讯游戏",
                
                // 三角洲行动常见路径
                @"C:\三角洲行动",
                @"D:\三角洲行动",
                @"E:\三角洲行动",
                @"C:\Delta Force",
                @"D:\Delta Force",
                @"E:\Delta Force",
                @"C:\DeltaForce",
                @"D:\DeltaForce",
                @"E:\DeltaForce",
                
                // Games 目录
                @"C:\Games",
                @"D:\Games",
                @"E:\Games",
                @"C:\Program Files\Tencent",
                @"D:\Program Files\Tencent",
                
                // WeGame 目录下也可能有
                @"C:\WeGame",
                @"D:\WeGame",
                @"E:\WeGame",
                @"C:\Program Files (x86)\WeGame",
                @"D:\Program Files (x86)\WeGame",
            };

            foreach (var basePath in searchPaths.Distinct())
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    var dirName = Path.GetFileName(basePath);
                    
                    // 检查是否直接是三角洲目录
                    if (dirName.Contains("三角洲") || 
                        dirName.Contains("Delta", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Contains("DeltaForce", StringComparison.OrdinalIgnoreCase))
                    {
                        TryAddDeltaForce(games, basePath);
                        continue;
                    }

                    // 搜索子目录
                    foreach (var gameDir in Directory.GetDirectories(basePath))
                    {
                        var subDirName = Path.GetFileName(gameDir);
                        
                        // 三角洲行动
                        if (subDirName.Contains("三角洲") || 
                            subDirName.Contains("Delta Force", StringComparison.OrdinalIgnoreCase) ||
                            subDirName.Contains("DeltaForce", StringComparison.OrdinalIgnoreCase))
                        {
                            TryAddDeltaForce(games, gameDir);
                        }
                        // 其他腾讯游戏
                        else
                        {
                            foreach (var (gameName, exeNames) in tencentGameDefs)
                            {
                                if (subDirName.Contains(gameName, StringComparison.OrdinalIgnoreCase))
                                {
                                    foreach (var exeName in exeNames)
                                    {
                                        var exePath = FindFileRecursive(gameDir, exeName);
                                        if (!string.IsNullOrEmpty(exePath) && 
                                            !games.Any(g => g.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            games.Add(new GameInfo
                                            {
                                                Name = gameName,
                                                Platform = GamePlatform.WeGame,
                                                InstallPath = gameDir,
                                                ExecutablePath = exePath,
                                                ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                            });
                                            break;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    
                    // 如果是 WeGame 目录，还要检查 games 子目录
                    if (dirName.Contains("WeGame", StringComparison.OrdinalIgnoreCase))
                    {
                        var gamesSubDir = Path.Combine(basePath, "games");
                        if (Directory.Exists(gamesSubDir))
                        {
                            foreach (var gameDir in Directory.GetDirectories(gamesSubDir))
                            {
                                var subDirName = Path.GetFileName(gameDir);
                                if (subDirName.Contains("三角洲") || 
                                    subDirName.Contains("Delta", StringComparison.OrdinalIgnoreCase))
                                {
                                    TryAddDeltaForce(games, gameDir);
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 权限不足等
                }
            }
        }
        catch
        {
            // 忽略扫描错误
        }

        return games;
    }

    /// <summary>
    /// 从注册表获取三角洲行动安装路径
    /// </summary>
    private string? GetDeltaForceInstallPathFromRegistry()
    {
        string[] registryPaths = 
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\三角洲行动",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\三角洲行动",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Delta Force",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Delta Force",
            @"SOFTWARE\WOW6432Node\Tencent\DeltaForce",
            @"SOFTWARE\Tencent\DeltaForce",
            @"SOFTWARE\WOW6432Node\腾讯游戏\三角洲行动",
            @"SOFTWARE\腾讯游戏\三角洲行动",
        };

        foreach (var regPath in registryPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key != null)
                {
                    var path = key.GetValue("InstallLocation")?.ToString() 
                            ?? key.GetValue("InstallPath")?.ToString()
                            ?? key.GetValue("Path")?.ToString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// 尝试添加三角洲行动
    /// </summary>
    private void TryAddDeltaForce(List<GameInfo> games, string gameDir)
    {
        if (games.Any(g => g.InstallPath.Equals(gameDir, StringComparison.OrdinalIgnoreCase)))
            return;

        // 尝试多种可能的可执行文件名
        string[] possibleExeNames = 
        { 
            "DeltaForce.exe", 
            "DeltaForce-Win64-Shipping.exe", 
            "deltaforce.exe",
            "game.exe",
            "launcher.exe"
        };

        foreach (var exeName in possibleExeNames)
        {
            var exePath = FindFileRecursive(gameDir, exeName);
            if (!string.IsNullOrEmpty(exePath))
            {
                games.Add(new GameInfo
                {
                    Name = "三角洲行动 (Delta Force)",
                    Platform = GamePlatform.WeGame,
                    InstallPath = gameDir,
                    ExecutablePath = exePath,
                    ProcessName = Path.GetFileNameWithoutExtension(exePath)
                });
                return;
            }
        }
    }

    /// <summary>
    /// 扫描网易游戏（永劫无间等）
    /// </summary>
    public List<GameInfo> ScanNeteaseGames()
    {
        var games = new List<GameInfo>();

        // 网易游戏定义
        var neteaseGameDefs = new Dictionary<string, string[]>
        {
            { "永劫无间", new[] { "NarakaBladepoint.exe", "Naraka.exe" } },
            { "NARAKA", new[] { "NarakaBladepoint.exe", "Naraka.exe" } },
            { "逆水寒", new[] { "nshn.exe", "nsh.exe" } },
            { "天谕", new[] { "ty.exe", "tianyu.exe" } },
            { "梦幻西游", new[] { "my.exe", "mhxy.exe" } },
            { "大话西游", new[] { "dhxy.exe", "xy2.exe" } },
            { "阴阳师", new[] { "onmyoji.exe" } },
            { "第五人格", new[] { "dwrg.exe", "IdentityV.exe" } },
            { "荒野行动", new[] { "hyxd.exe" } },
            { "明日之后", new[] { "mrzh.exe" } },
        };

        try
        {
            // 1. 从注册表查找永劫无间
            var narakaPath = GetNarakaInstallPathFromRegistry();
            if (!string.IsNullOrEmpty(narakaPath) && Directory.Exists(narakaPath))
            {
                TryAddNaraka(games, narakaPath);
            }

            // 2. 搜索常见安装路径
            var searchPaths = new List<string>
            {
                // 网易游戏目录
                @"C:\网易游戏",
                @"D:\网易游戏",
                @"E:\网易游戏",
                @"F:\网易游戏",
                @"C:\NetEase Games",
                @"D:\NetEase Games",
                @"E:\NetEase Games",
                @"C:\Netease",
                @"D:\Netease",
                
                // 永劫无间常见路径
                @"C:\永劫无间",
                @"D:\永劫无间",
                @"E:\永劫无间",
                @"C:\NARAKA BLADEPOINT",
                @"D:\NARAKA BLADEPOINT",
                @"E:\NARAKA BLADEPOINT",
                @"C:\Naraka",
                @"D:\Naraka",
                
                // Games 目录
                @"C:\Games",
                @"D:\Games",
                @"E:\Games",
                @"C:\Program Files\NetEase",
                @"D:\Program Files\NetEase",
                @"C:\Program Files (x86)\NetEase",
            };

            foreach (var basePath in searchPaths.Distinct())
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    var dirName = Path.GetFileName(basePath);
                    
                    // 检查是否直接是游戏目录
                    if (dirName.Contains("永劫无间") || 
                        dirName.Contains("NARAKA", StringComparison.OrdinalIgnoreCase) ||
                        dirName.Contains("Naraka", StringComparison.OrdinalIgnoreCase))
                    {
                        TryAddNaraka(games, basePath);
                        continue;
                    }

                    // 搜索子目录
                    foreach (var gameDir in Directory.GetDirectories(basePath))
                    {
                        var subDirName = Path.GetFileName(gameDir);
                        
                        // 永劫无间
                        if (subDirName.Contains("永劫无间") || 
                            subDirName.Contains("NARAKA", StringComparison.OrdinalIgnoreCase) ||
                            subDirName.Contains("Naraka", StringComparison.OrdinalIgnoreCase))
                        {
                            TryAddNaraka(games, gameDir);
                        }
                        // 其他网易游戏
                        else
                        {
                            foreach (var (gameName, exeNames) in neteaseGameDefs)
                            {
                                if (subDirName.Contains(gameName, StringComparison.OrdinalIgnoreCase))
                                {
                                    foreach (var exeName in exeNames)
                                    {
                                        var exePath = FindFileRecursive(gameDir, exeName);
                                        if (!string.IsNullOrEmpty(exePath) && 
                                            !games.Any(g => g.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                                        {
                                            games.Add(new GameInfo
                                            {
                                                Name = gameName,
                                                Platform = GamePlatform.Custom,
                                                InstallPath = gameDir,
                                                ExecutablePath = exePath,
                                                ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                            });
                                            break;
                                        }
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // 权限不足等
                }
            }
        }
        catch
        {
            // 忽略扫描错误
        }

        return games;
    }

    /// <summary>
    /// 从注册表获取永劫无间安装路径
    /// </summary>
    private string? GetNarakaInstallPathFromRegistry()
    {
        string[] registryPaths = 
        {
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\NARAKA BLADEPOINT",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\NARAKA BLADEPOINT",
            @"SOFTWARE\WOW6432Node\NetEase\NARAKA",
            @"SOFTWARE\NetEase\NARAKA",
        };

        foreach (var regPath in registryPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key != null)
                {
                    var path = key.GetValue("InstallLocation")?.ToString() 
                            ?? key.GetValue("InstallPath")?.ToString()
                            ?? key.GetValue("Path")?.ToString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// 尝试添加永劫无间
    /// </summary>
    private void TryAddNaraka(List<GameInfo> games, string gameDir)
    {
        if (games.Any(g => g.InstallPath.Equals(gameDir, StringComparison.OrdinalIgnoreCase)))
            return;

        var exePath = FindFileRecursive(gameDir, "NarakaBladepoint.exe");
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = FindFileRecursive(gameDir, "Naraka.exe");
        }
        if (!string.IsNullOrEmpty(exePath))
        {
            games.Add(new GameInfo
            {
                Name = "永劫无间 (NARAKA: BLADEPOINT)",
                Platform = GamePlatform.Custom,
                InstallPath = gameDir,
                ExecutablePath = exePath,
                ProcessName = Path.GetFileNameWithoutExtension(exePath)
            });
        }
    }

    /// <summary>
    /// 扫描 Steam 游戏
    /// </summary>
    public List<GameInfo> ScanSteamGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // 获取 Steam 安装路径
            var steamPath = GetSteamInstallPath();
            if (string.IsNullOrEmpty(steamPath)) return games;

            // 读取 libraryfolders.vdf 获取所有游戏库路径
            var libraryFolders = GetSteamLibraryFolders(steamPath);

            foreach (var libraryPath in libraryFolders)
            {
                var steamAppsPath = Path.Combine(libraryPath, "steamapps");
                if (!Directory.Exists(steamAppsPath)) continue;

                // 扫描 appmanifest 文件
                var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");

                foreach (var manifestFile in manifestFiles)
                {
                    try
                    {
                        var game = ParseSteamManifest(manifestFile, steamAppsPath);
                        if (game != null)
                        {
                            games.Add(game);
                        }
                    }
                    catch
                    {
                        // 忽略解析失败的游戏
                    }
                }
            }
        }
        catch
        {
            // 忽略 Steam 扫描错误
        }

        return games;
    }

    /// <summary>
    /// 获取 Steam 安装路径
    /// </summary>
    private string? GetSteamInstallPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            return key?.GetValue("SteamPath")?.ToString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取所有 Steam 游戏库文件夹
    /// </summary>
    private List<string> GetSteamLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        try
        {
            var libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFile)) return folders;

            var content = File.ReadAllText(libraryFile);
            
            // 简单解析 VDF 格式获取路径
            var pathPattern = new Regex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase);
            var matches = pathPattern.Matches(content);

            foreach (Match match in matches)
            {
                var path = match.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path) && !folders.Contains(path))
                {
                    folders.Add(path);
                }
            }
        }
        catch
        {
            // 忽略解析错误
        }

        return folders;
    }

    /// <summary>
    /// 解析 Steam 游戏清单文件
    /// </summary>
    private GameInfo? ParseSteamManifest(string manifestPath, string steamAppsPath)
    {
        var content = File.ReadAllText(manifestPath);

        // 提取 AppId
        var appIdMatch = Regex.Match(content, @"""appid""\s+""(\d+)""");
        if (!appIdMatch.Success) return null;
        var appId = appIdMatch.Groups[1].Value;

        // 提取游戏名称
        var nameMatch = Regex.Match(content, @"""name""\s+""([^""]+)""");
        if (!nameMatch.Success) return null;
        var name = nameMatch.Groups[1].Value;

        // 提取安装目录
        var installDirMatch = Regex.Match(content, @"""installdir""\s+""([^""]+)""");
        if (!installDirMatch.Success) return null;
        var installDir = installDirMatch.Groups[1].Value;

        var installPath = Path.Combine(steamAppsPath, "common", installDir);
        if (!Directory.Exists(installPath)) return null;

        // 1. 首先尝试从 Steam 本地配置获取可执行文件信息
        var exePath = GetSteamGameExecutable(steamAppsPath, appId, installPath, name);
        
        // 2. 如果没找到，使用智能查找
        if (string.IsNullOrEmpty(exePath))
        {
            exePath = FindMainExecutable(installPath, name);
        }
        
        var processName = !string.IsNullOrEmpty(exePath) 
            ? Path.GetFileNameWithoutExtension(exePath) 
            : string.Empty;

        return new GameInfo
        {
            Name = name,
            Platform = GamePlatform.Steam,
            PlatformGameId = appId,
            InstallPath = installPath,
            ExecutablePath = exePath ?? string.Empty,
            ProcessName = processName
        };
    }

    /// <summary>
    /// 从 Steam 本地配置获取游戏可执行文件
    /// </summary>
    private string? GetSteamGameExecutable(string steamAppsPath, string appId, string installPath, string gameName)
    {
        try
        {
            // 尝试读取 Steam 的本地配置文件
            var steamPath = Path.GetDirectoryName(steamAppsPath);
            if (string.IsNullOrEmpty(steamPath)) return null;

            // 方法1: 检查 appinfo.vdf 解析后的缓存 (Steam 会在 userdata 中保存启动配置)
            var userdataPath = Path.Combine(steamPath, "userdata");
            if (Directory.Exists(userdataPath))
            {
                foreach (var userDir in Directory.GetDirectories(userdataPath))
                {
                    var localConfigPath = Path.Combine(userDir, "config", "localconfig.vdf");
                    if (File.Exists(localConfigPath))
                    {
                        var exePath = ParseSteamLocalConfig(localConfigPath, appId, installPath);
                        if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                        {
                            return exePath;
                        }
                    }
                }
            }

            // 方法2: 检查 Steam 的快捷方式配置
            var shortcutsVdf = Path.Combine(steamPath, "config", "shortcuts.vdf");
            if (File.Exists(shortcutsVdf))
            {
                var exePath = ParseSteamShortcuts(shortcutsVdf, gameName);
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    return exePath;
                }
            }

            // 方法3: 在已知游戏数据库中精确匹配
            if (KnownGameProcesses.TryGetValue(gameName, out var processNames))
            {
                foreach (var processName in processNames)
                {
                    var exePath = FindFileRecursive(installPath, $"{processName}.exe");
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
        }
        catch
        {
            // 忽略解析错误
        }

        return null;
    }

    /// <summary>
    /// 解析 Steam localconfig.vdf 获取游戏启动配置
    /// </summary>
    private string? ParseSteamLocalConfig(string configPath, string appId, string installPath)
    {
        try
        {
            var content = File.ReadAllText(configPath);
            
            // 查找对应 AppId 的配置块
            var appPattern = new Regex($@"""{appId}""[^{{]*\{{([^}}]*(?:\{{[^}}]*\}}[^}}]*)*)\}}", RegexOptions.Singleline);
            var appMatch = appPattern.Match(content);
            
            if (appMatch.Success)
            {
                var appContent = appMatch.Groups[1].Value;
                
                // 查找 LaunchOptions 中的可执行文件路径
                var exePattern = new Regex(@"""LaunchOptions""\s+""([^""]+)""");
                var exeMatch = exePattern.Match(appContent);
                
                if (exeMatch.Success)
                {
                    var launchOptions = exeMatch.Groups[1].Value;
                    // 提取可能的 exe 路径
                    var pathMatch = Regex.Match(launchOptions, @"([A-Za-z]:\\[^\s""]+\.exe|""[^""]+\.exe"")");
                    if (pathMatch.Success)
                    {
                        var exePath = pathMatch.Groups[1].Value.Trim('"');
                        if (File.Exists(exePath))
                        {
                            return exePath;
                        }
                    }
                }
            }
        }
        catch
        {
            // 解析失败
        }
        return null;
    }

    /// <summary>
    /// 解析 Steam shortcuts.vdf
    /// </summary>
    private string? ParseSteamShortcuts(string shortcutsPath, string gameName)
    {
        try
        {
            // shortcuts.vdf 是二进制格式，这里简单处理
            var content = File.ReadAllBytes(shortcutsPath);
            var text = System.Text.Encoding.UTF8.GetString(content);
            
            var nameClean = Regex.Replace(gameName, @"[^a-zA-Z0-9]", "").ToLower();
            
            // 查找游戏名称和对应的 exe 路径
            var exePattern = new Regex(@"exe[^\x00]*([A-Za-z]:\\[^\x00]+\.exe)", RegexOptions.IgnoreCase);
            var matches = exePattern.Matches(text);
            
            foreach (Match match in matches)
            {
                var exePath = match.Groups[1].Value;
                var exeNameClean = Regex.Replace(Path.GetFileNameWithoutExtension(exePath), @"[^a-zA-Z0-9]", "").ToLower();
                
                if (exeNameClean.Contains(nameClean) || nameClean.Contains(exeNameClean))
                {
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
        }
        catch
        {
            // 解析失败
        }
        return null;
    }

    /// <summary>
    /// 扫描 Epic Games 游戏
    /// </summary>
    public List<GameInfo> ScanEpicGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // Epic Games 清单文件路径
            var manifestPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic", "EpicGamesLauncher", "Data", "Manifests");

            if (!Directory.Exists(manifestPath)) return games;

            var manifestFiles = Directory.GetFiles(manifestPath, "*.item");

            foreach (var file in manifestFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    var name = root.GetProperty("DisplayName").GetString() ?? "";
                    var installLocation = root.GetProperty("InstallLocation").GetString() ?? "";
                    var launchExecutable = root.TryGetProperty("LaunchExecutable", out var le) 
                        ? le.GetString() ?? "" 
                        : "";
                    var appName = root.GetProperty("AppName").GetString() ?? "";

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installLocation))
                        continue;

                    var exePath = !string.IsNullOrEmpty(launchExecutable)
                        ? Path.Combine(installLocation, launchExecutable)
                        : FindMainExecutable(installLocation, name);

                    games.Add(new GameInfo
                    {
                        Name = name,
                        Platform = GamePlatform.Epic,
                        PlatformGameId = appName,
                        InstallPath = installLocation,
                        ExecutablePath = exePath ?? string.Empty,
                        ProcessName = !string.IsNullOrEmpty(exePath) 
                            ? Path.GetFileNameWithoutExtension(exePath) 
                            : string.Empty
                    });
                }
                catch
                {
                    // 忽略解析失败的游戏
                }
            }
        }
        catch
        {
            // 忽略 Epic 扫描错误
        }

        return games;
    }

    /// <summary>
    /// Xbox/Microsoft Store 已知游戏白名单 - 懒加载
    /// </summary>
    private static Dictionary<string, string[]> KnownXboxGames => 
        (_knownXboxGames ??= new Lazy<Dictionary<string, string[]>>(BuildKnownXboxGames)).Value;

    private static Dictionary<string, string[]> BuildKnownXboxGames() => new(StringComparer.OrdinalIgnoreCase)
    {
        // 精简为最热门的游戏
        { "Halo Infinite", new[] { "HaloInfinite" } },
        { "Forza Horizon 5", new[] { "ForzaHorizon5" } },
        { "Microsoft Flight Simulator", new[] { "FlightSimulator" } },
        { "Sea of Thieves", new[] { "SeaofThieves" } },
        { "Age of Empires IV", new[] { "AgeofEmpiresIV" } },
        { "Minecraft", new[] { "Minecraft" } },
        { "Starfield", new[] { "Starfield" } },
        { "Hi-Fi Rush", new[] { "HiFiRush" } },
    };

    /// <summary>
    /// 扫描 Xbox/Microsoft Store 游戏 - 使用白名单模式
    /// </summary>
    public List<GameInfo> ScanXboxGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // Xbox 游戏目录
            var xboxPaths = new[]
            {
                @"C:\XboxGames",
                @"D:\XboxGames",
                @"E:\XboxGames",
                @"F:\XboxGames"
            };

            foreach (var xboxPath in xboxPaths)
            {
                if (!Directory.Exists(xboxPath)) continue;

                try
                {
                    foreach (var gameDir in Directory.GetDirectories(xboxPath))
                    {
                        var dirName = Path.GetFileName(gameDir);
                        
                        // 只匹配白名单中的游戏
                        foreach (var (gameName, patterns) in KnownXboxGames)
                        {
                            if (patterns.Any(p => dirName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                            {
                                var exePath = FindMainExecutable(gameDir, gameName);
                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    games.Add(new GameInfo
                                    {
                                        Name = gameName,
                                        Platform = GamePlatform.Xbox,
                                        InstallPath = gameDir,
                                        ExecutablePath = exePath,
                                        ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                    });
                                }
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // 权限不足
                }
            }

            // 从注册表扫描已安装的 Xbox 游戏
            ScanXboxGamesFromRegistry(games);
        }
        catch
        {
            // 忽略扫描错误
        }

        return games;
    }

    /// <summary>
    /// 从注册表扫描 Xbox 游戏
    /// </summary>
    private void ScanXboxGamesFromRegistry(List<GameInfo> games)
    {
        try
        {
            // Xbox 游戏在卸载注册表中
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstallKey == null) return;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using var subKey = uninstallKey.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName")?.ToString() ?? "";
                    var installPath = subKey.GetValue("InstallLocation")?.ToString() ?? "";
                    var publisher = subKey.GetValue("Publisher")?.ToString() ?? "";

                    if (string.IsNullOrEmpty(displayName) || string.IsNullOrEmpty(installPath)) 
                        continue;
                    if (!Directory.Exists(installPath)) 
                        continue;

                    // 检查是否匹配白名单中的游戏
                    foreach (var (gameName, patterns) in KnownXboxGames)
                    {
                        if (patterns.Any(p => displayName.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
                            displayName.Contains(gameName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (games.Any(g => g.InstallPath.Equals(installPath, StringComparison.OrdinalIgnoreCase)))
                                break;

                            var exePath = FindMainExecutable(installPath, gameName);
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                games.Add(new GameInfo
                                {
                                    Name = gameName,
                                    Platform = GamePlatform.Xbox,
                                    InstallPath = installPath,
                                    ExecutablePath = exePath,
                                    ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                });
                            }
                            break;
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// 扫描 GOG Galaxy 游戏
    /// </summary>
    public List<GameInfo> ScanGOGGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // GOG Galaxy 在注册表中存储游戏信息
            using var gogKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (gogKey == null) return games;

            foreach (var gameId in gogKey.GetSubKeyNames())
            {
                try
                {
                    using var gameKey = gogKey.OpenSubKey(gameId);
                    if (gameKey == null) continue;

                    var name = gameKey.GetValue("gameName")?.ToString() ?? "";
                    var path = gameKey.GetValue("path")?.ToString() ?? "";
                    var exePath = gameKey.GetValue("exe")?.ToString() ?? "";

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) continue;

                    // 如果没有 exe 信息，尝试查找
                    if (string.IsNullOrEmpty(exePath))
                    {
                        exePath = FindMainExecutable(path, name) ?? "";
                    }

                    games.Add(new GameInfo
                    {
                        Name = name,
                        Platform = GamePlatform.GOG,
                        PlatformGameId = gameId,
                        InstallPath = path,
                        ExecutablePath = exePath,
                        ProcessName = !string.IsNullOrEmpty(exePath)
                            ? Path.GetFileNameWithoutExtension(exePath)
                            : string.Empty
                    });
                }
                catch
                {
                    // 忽略单个游戏解析错误
                }
            }
        }
        catch
        {
            // 忽略 GOG 扫描错误
        }

        return games;
    }

    /// <summary>
    /// 扫描 Ubisoft Connect 游戏
    /// </summary>
    public List<GameInfo> ScanUbisoftGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // Ubisoft Connect 注册表路径
            using var ubiKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
            if (ubiKey == null) return games;

            foreach (var gameId in ubiKey.GetSubKeyNames())
            {
                try
                {
                    using var gameKey = ubiKey.OpenSubKey(gameId);
                    if (gameKey == null) continue;

                    var installDir = gameKey.GetValue("InstallDir")?.ToString() ?? "";
                    if (string.IsNullOrEmpty(installDir) || !Directory.Exists(installDir)) continue;

                    // 提取游戏名称
                    var name = Path.GetFileName(installDir.TrimEnd(Path.DirectorySeparatorChar));
                    var exePath = FindMainExecutable(installDir, name);

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        games.Add(new GameInfo
                        {
                            Name = name,
                            Platform = GamePlatform.Ubisoft,
                            PlatformGameId = gameId,
                            InstallPath = installDir,
                            ExecutablePath = exePath,
                            ProcessName = Path.GetFileNameWithoutExtension(exePath)
                        });
                    }
                }
                catch
                {
                    // 忽略单个游戏解析错误
                }
            }
        }
        catch
        {
            // 忽略 Ubisoft 扫描错误
        }

        return games;
    }

    /// <summary>
    /// 扫描 EA App 游戏
    /// </summary>
    public List<GameInfo> ScanEAGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // EA App 安装数据路径
            var eaDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "EA Desktop", "InstallData");

            // 也检查旧版 Origin 路径
            var originDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Origin", "LocalContent");

            var eaPaths = new[] { eaDataPath, originDataPath };

            foreach (var dataPath in eaPaths)
            {
                if (!Directory.Exists(dataPath)) continue;

                try
                {
                    foreach (var gameDir in Directory.GetDirectories(dataPath))
                    {
                        // 查找 installerdata.xml 或其他配置文件
                        var xmlFiles = Directory.GetFiles(gameDir, "*.xml", SearchOption.AllDirectories);
                        foreach (var xmlFile in xmlFiles)
                        {
                            try
                            {
                                var content = File.ReadAllText(xmlFile);
                                
                                // 简单解析获取游戏路径
                                var pathMatch = Regex.Match(content, @"<filePath[^>]*>([^<]+)</filePath>");
                                if (!pathMatch.Success) continue;

                                var installPath = pathMatch.Groups[1].Value;
                                if (!Directory.Exists(installPath)) continue;

                                var name = Path.GetFileName(gameDir);
                                var exePath = FindMainExecutable(installPath, name);

                                if (!string.IsNullOrEmpty(exePath))
                                {
                                    games.Add(new GameInfo
                                    {
                                        Name = name,
                                        Platform = GamePlatform.EA,
                                        InstallPath = installPath,
                                        ExecutablePath = exePath,
                                        ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                    });
                                }
                                break; // 找到一个有效配置即可
                            }
                            catch
                            {
                                // 忽略解析错误
                            }
                        }
                    }
                }
                catch
                {
                    // 权限不足等
                }
            }

            // 从注册表获取 EA 游戏
            ScanEAGamesFromRegistry(games);
        }
        catch
        {
            // 忽略 EA 扫描错误
        }

        return games;
    }

    /// <summary>
    /// 从注册表扫描 EA 游戏
    /// </summary>
    private void ScanEAGamesFromRegistry(List<GameInfo> games)
    {
        try
        {
            using var uninstallKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstallKey == null) return;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                try
                {
                    using var subKey = uninstallKey.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var publisher = subKey.GetValue("Publisher")?.ToString() ?? "";
                    if (!publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)) continue;

                    var name = subKey.GetValue("DisplayName")?.ToString() ?? "";
                    var installPath = subKey.GetValue("InstallLocation")?.ToString() ?? "";

                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installPath)) continue;
                    if (!Directory.Exists(installPath)) continue;
                    if (games.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

                    var exePath = FindMainExecutable(installPath, name);
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        games.Add(new GameInfo
                        {
                            Name = name,
                            Platform = GamePlatform.EA,
                            InstallPath = installPath,
                            ExecutablePath = exePath,
                            ProcessName = Path.GetFileNameWithoutExtension(exePath)
                        });
                    }
                }
                catch
                {
                    // 忽略
                }
            }
        }
        catch
        {
            // 忽略注册表扫描错误
        }
    }

    /// <summary>
    /// 扫描 Battle.net 游戏
    /// </summary>
    public List<GameInfo> ScanBattlenetGames()
    {
        var games = new List<GameInfo>();

        // 常见暴雪游戏的进程名和默认安装位置
        var blizzardGames = new Dictionary<string, string[]>
        {
            { "魔兽世界", new[] { "Wow.exe", "WowClassic.exe" } },
            { "World of Warcraft", new[] { "Wow.exe", "WowClassic.exe" } },
            { "暗黑破坏神IV", new[] { "Diablo IV.exe" } },
            { "Diablo IV", new[] { "Diablo IV.exe" } },
            { "暗黑破坏神III", new[] { "Diablo III64.exe", "Diablo III.exe" } },
            { "守望先锋", new[] { "Overwatch.exe" } },
            { "Overwatch", new[] { "Overwatch.exe" } },
            { "炉石传说", new[] { "Hearthstone.exe" } },
            { "Hearthstone", new[] { "Hearthstone.exe" } },
            { "星际争霸II", new[] { "SC2_x64.exe", "SC2.exe" } },
            { "StarCraft II", new[] { "SC2_x64.exe", "SC2.exe" } },
            { "风暴英雄", new[] { "HeroesOfTheStorm_x64.exe" } },
            { "Heroes of the Storm", new[] { "HeroesOfTheStorm_x64.exe" } },
            { "使命召唤", new[] { "cod.exe", "ModernWarfare.exe", "BlackOpsColdWar.exe" } },
            { "Call of Duty", new[] { "cod.exe", "ModernWarfare.exe", "BlackOpsColdWar.exe" } }
        };

        try
        {
            // 获取 Battle.net 安装路径
            string? battlenetPath = null;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Blizzard Entertainment\Battle.net"))
            {
                battlenetPath = key?.GetValue("InstallPath")?.ToString();
            }

            // 常见安装路径
            var searchPaths = new List<string>
            {
                @"C:\Program Files (x86)\Blizzard",
                @"C:\Program Files\Blizzard",
                @"D:\Blizzard",
                @"E:\Blizzard",
                @"D:\Games\Blizzard",
                @"C:\Program Files (x86)\Battle.net",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Blizzard")
            };

            if (!string.IsNullOrEmpty(battlenetPath))
            {
                var parentPath = Path.GetDirectoryName(battlenetPath);
                if (!string.IsNullOrEmpty(parentPath))
                {
                    searchPaths.Insert(0, parentPath);
                }
            }

            foreach (var basePath in searchPaths.Distinct())
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    foreach (var gameDir in Directory.GetDirectories(basePath))
                    {
                        var dirName = Path.GetFileName(gameDir);
                        
                        foreach (var (gameName, exeNames) in blizzardGames)
                        {
                            foreach (var exeName in exeNames)
                            {
                                var exePath = Path.Combine(gameDir, exeName);
                                if (File.Exists(exePath))
                                {
                                    if (!games.Any(g => g.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        games.Add(new GameInfo
                                        {
                                            Name = dirName,
                                            Platform = GamePlatform.Battlenet,
                                            InstallPath = gameDir,
                                            ExecutablePath = exePath,
                                            ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                        });
                                    }
                                    break;
                                }
                            }
                        }

                        // 如果没有匹配到预定义的，尝试查找
                        if (!games.Any(g => g.InstallPath.Equals(gameDir, StringComparison.OrdinalIgnoreCase)))
                        {
                            var exePath = FindMainExecutable(gameDir, dirName);
                            if (!string.IsNullOrEmpty(exePath) && 
                                !games.Any(g => g.ExecutablePath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
                            {
                                games.Add(new GameInfo
                                {
                                    Name = dirName,
                                    Platform = GamePlatform.Battlenet,
                                    InstallPath = gameDir,
                                    ExecutablePath = exePath,
                                    ProcessName = Path.GetFileNameWithoutExtension(exePath)
                                });
                            }
                        }
                    }
                }
                catch
                {
                    // 权限不足等
                }
            }
        }
        catch
        {
            // 忽略 Battle.net 扫描错误
        }

        return games;
    }

    /// <summary>
    /// 扫描 Riot Games 和腾讯游戏（英雄联盟国服等）
    /// </summary>
    public List<GameInfo> ScanRiotGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // 1. 从注册表查找英雄联盟
            var lolPath = GetLOLInstallPathFromRegistry();
            if (!string.IsNullOrEmpty(lolPath) && Directory.Exists(lolPath))
            {
                var lolExe = FindFileRecursive(lolPath, "LeagueClient.exe");
                if (!string.IsNullOrEmpty(lolExe))
                {
                    games.Add(new GameInfo
                    {
                        Name = "英雄联盟 (League of Legends)",
                        Platform = GamePlatform.Riot,
                        InstallPath = lolPath,
                        ExecutablePath = lolExe,
                        ProcessName = "LeagueClient"
                    });
                }
            }

            // 2. 搜索常见安装路径
            var searchPaths = new List<string>
            {
                // Riot Games 国际服
                @"C:\Riot Games",
                @"D:\Riot Games",
                @"E:\Riot Games",
                @"F:\Riot Games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Riot Games"),
                
                // 腾讯游戏目录（国服英雄联盟）
                @"C:\腾讯游戏",
                @"D:\腾讯游戏",
                @"E:\腾讯游戏",
                @"F:\腾讯游戏",
                @"C:\Games\腾讯游戏",
                @"D:\Games\腾讯游戏",
                @"C:\Program Files\腾讯游戏",
                @"D:\Program Files\腾讯游戏",
                @"C:\Program Files (x86)\腾讯游戏",
                @"D:\Program Files (x86)\腾讯游戏",
                
                // Tencent Games
                @"C:\Tencent Games",
                @"D:\Tencent Games",
                @"E:\Tencent Games",
                
                // 英雄联盟直接目录
                @"C:\英雄联盟",
                @"D:\英雄联盟",
                @"E:\英雄联盟",
                @"C:\League of Legends",
                @"D:\League of Legends",
                @"E:\League of Legends",
            };

            foreach (var basePath in searchPaths.Distinct())
            {
                if (!Directory.Exists(basePath)) continue;

                try
                {
                    // 检查是否直接是英雄联盟目录
                    var dirName = Path.GetFileName(basePath);
                    if (dirName.Contains("英雄联盟") || dirName.Contains("League of Legends", StringComparison.OrdinalIgnoreCase))
                    {
                        TryAddLOL(games, basePath);
                        continue;
                    }

                    // 搜索子目录
                    foreach (var gameDir in Directory.GetDirectories(basePath))
                    {
                        var subDirName = Path.GetFileName(gameDir);
                        
                        // 英雄联盟
                        if (subDirName.Contains("英雄联盟") || 
                            subDirName.Contains("League of Legends", StringComparison.OrdinalIgnoreCase) ||
                            subDirName.Equals("LOL", StringComparison.OrdinalIgnoreCase))
                        {
                            TryAddLOL(games, gameDir);
                        }
                        // VALORANT
                        else if (subDirName.Contains("VALORANT", StringComparison.OrdinalIgnoreCase))
                        {
                            TryAddValorant(games, gameDir);
                        }
                    }
                }
                catch
                {
                    // 权限不足等
                }
            }
        }
        catch
        {
            // 忽略扫描错误
        }

        return games;
    }

    /// <summary>
    /// 从注册表获取英雄联盟安装路径
    /// </summary>
    private string? GetLOLInstallPathFromRegistry()
    {
        string[] registryPaths = 
        {
            @"SOFTWARE\WOW6432Node\Tencent\LOL",
            @"SOFTWARE\Tencent\LOL",
            @"SOFTWARE\WOW6432Node\腾讯游戏\英雄联盟",
            @"SOFTWARE\腾讯游戏\英雄联盟",
            @"SOFTWARE\Riot Games\League of Legends",
            @"SOFTWARE\WOW6432Node\Riot Games\League of Legends",
        };

        foreach (var regPath in registryPaths)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(regPath);
                if (key != null)
                {
                    var path = key.GetValue("InstallPath")?.ToString() 
                            ?? key.GetValue("Install_Dir")?.ToString()
                            ?? key.GetValue("Path")?.ToString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(regPath);
                if (key != null)
                {
                    var path = key.GetValue("InstallPath")?.ToString() 
                            ?? key.GetValue("Install_Dir")?.ToString()
                            ?? key.GetValue("Path")?.ToString();
                    if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    {
                        return path;
                    }
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// 尝试添加英雄联盟
    /// </summary>
    private void TryAddLOL(List<GameInfo> games, string gameDir)
    {
        // 检查是否已添加
        if (games.Any(g => g.InstallPath.Equals(gameDir, StringComparison.OrdinalIgnoreCase)))
            return;

        var lolExe = FindFileRecursive(gameDir, "LeagueClient.exe");
        if (!string.IsNullOrEmpty(lolExe))
        {
            games.Add(new GameInfo
            {
                Name = "英雄联盟 (League of Legends)",
                Platform = GamePlatform.Riot,
                InstallPath = gameDir,
                ExecutablePath = lolExe,
                ProcessName = "LeagueClient"
            });
        }
    }

    /// <summary>
    /// 尝试添加 VALORANT
    /// </summary>
    private void TryAddValorant(List<GameInfo> games, string gameDir)
    {
        if (games.Any(g => g.InstallPath.Equals(gameDir, StringComparison.OrdinalIgnoreCase)))
            return;

        var valExe = FindFileRecursive(gameDir, "VALORANT.exe");
        if (string.IsNullOrEmpty(valExe))
        {
            valExe = FindFileRecursive(gameDir, "VALORANT-Win64-Shipping.exe");
        }
        if (!string.IsNullOrEmpty(valExe))
        {
            games.Add(new GameInfo
            {
                Name = "VALORANT",
                Platform = GamePlatform.Riot,
                InstallPath = gameDir,
                ExecutablePath = valExe,
                ProcessName = Path.GetFileNameWithoutExtension(valExe)
            });
        }
    }

    /// <summary>
    /// WeGame 已知游戏白名单 - 懒加载
    /// </summary>
    private static Dictionary<string, string[]> KnownWeGameGames => 
        (_knownWeGameGames ??= new Lazy<Dictionary<string, string[]>>(BuildKnownWeGameGames)).Value;

    private static Dictionary<string, string[]> BuildKnownWeGameGames() => new(StringComparer.OrdinalIgnoreCase)
    {
        // 精简为最热门的游戏
        { "英雄联盟", new[] { "英雄联盟", "LOL", "LeagueClient.exe" } },
        { "穿越火线", new[] { "穿越火线", "crossfire.exe" } },
        { "地下城与勇士", new[] { "DNF", "DNF.exe" } },
        { "三角洲行动", new[] { "三角洲", "DeltaForce" } },
    };

    /// <summary>
    /// 扫描 WeGame 游戏 - 使用白名单模式
    /// </summary>
    public List<GameInfo> ScanWeGameGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // WeGame 注册表路径
            string? wegamePath = null;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Tencent\WeGame"))
            {
                wegamePath = key?.GetValue("InstallPath")?.ToString();
            }

            // 常见 WeGame 目录
            var wegamePaths = new List<string>
            {
                @"C:\WeGame",
                @"D:\WeGame",
                @"E:\WeGame",
                @"F:\WeGame",
                @"C:\Program Files (x86)\WeGame",
                @"D:\Program Files (x86)\WeGame"
            };

            if (!string.IsNullOrEmpty(wegamePath))
            {
                wegamePaths.Insert(0, wegamePath);
            }

            foreach (var basePath in wegamePaths.Distinct())
            {
                if (!Directory.Exists(basePath)) continue;

                // WeGame 游戏在 games 子目录
                var gamesDir = Path.Combine(basePath, "games");
                if (Directory.Exists(gamesDir))
                {
                    ScanWeGameDirectory(games, gamesDir);
                }
                
                // 也扫描根目录
                ScanWeGameDirectory(games, basePath);
            }
        }
        catch
        {
            // 忽略扫描错误
        }

        return games;
    }

    /// <summary>
    /// 扫描 WeGame 目录 - 只匹配白名单游戏
    /// </summary>
    private void ScanWeGameDirectory(List<GameInfo> games, string directory)
    {
        try
        {
            foreach (var gameDir in Directory.GetDirectories(directory))
            {
                var dirName = Path.GetFileName(gameDir);
                
                // 只匹配白名单中的游戏
                foreach (var (gameName, patterns) in KnownWeGameGames)
                {
                    if (patterns.Any(p => dirName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        // 避免重复
                        if (games.Any(g => g.InstallPath.Equals(gameDir, StringComparison.OrdinalIgnoreCase)))
                            break;

                        var exePath = FindMainExecutable(gameDir, gameName);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            games.Add(new GameInfo
                            {
                                Name = gameName,
                                Platform = GamePlatform.WeGame,
                                InstallPath = gameDir,
                                ExecutablePath = exePath,
                                ProcessName = Path.GetFileNameWithoutExtension(exePath)
                            });
                        }
                        break;
                    }
                }
            }
        }
        catch
        {
            // 权限不足
        }
    }

    /// <summary>
    /// 递归查找文件
    /// </summary>
    private string? FindFileRecursive(string directory, string fileName)
    {
        try
        {
            var files = Directory.GetFiles(directory, fileName, SearchOption.AllDirectories);
            return files.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 查找主要可执行文件 - 使用智能评分系统
    /// </summary>
    private string? FindMainExecutable(string installPath, string gameName)
    {
        try
        {
            if (!Directory.Exists(installPath)) return null;

            // 首先检查是否有已知的游戏进程名
            var knownExe = FindKnownGameExecutable(installPath, gameName);
            if (knownExe != null) return knownExe;

            // 获取所有 exe 文件（限制搜索深度以提高性能）
            var exeFiles = GetExecutableFiles(installPath, maxDepth: 3);
            if (exeFiles.Count == 0) return null;

            // 对每个可执行文件进行评分
            var scoredExes = exeFiles
                .Select(f => new { Path = f, Score = ScoreExecutable(f, gameName, installPath) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            return scoredExes.FirstOrDefault()?.Path;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 查找已知游戏的可执行文件
    /// </summary>
    private string? FindKnownGameExecutable(string installPath, string gameName)
    {
        // 在已知游戏数据库中查找
        foreach (var (knownName, processNames) in KnownGameProcesses)
        {
            // 模糊匹配游戏名称
            if (FuzzyMatch(gameName, knownName))
            {
                foreach (var processName in processNames)
                {
                    var exePath = FindFileRecursive(installPath, $"{processName}.exe");
                    if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 模糊匹配游戏名称
    /// </summary>
    private bool FuzzyMatch(string name1, string name2)
    {
        var clean1 = Regex.Replace(name1, @"[^a-zA-Z0-9\u4e00-\u9fa5]", "").ToLower();
        var clean2 = Regex.Replace(name2, @"[^a-zA-Z0-9\u4e00-\u9fa5]", "").ToLower();
        
        return clean1.Contains(clean2) || clean2.Contains(clean1) ||
               LevenshteinSimilarity(clean1, clean2) > 0.7;
    }

    /// <summary>
    /// 计算 Levenshtein 相似度
    /// </summary>
    private double LevenshteinSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            return 0;
            
        var maxLen = Math.Max(s1.Length, s2.Length);
        if (maxLen == 0) return 1;
        
        var distance = LevenshteinDistance(s1, s2);
        return 1.0 - (double)distance / maxLen;
    }

    /// <summary>
    /// 计算 Levenshtein 距离
    /// </summary>
    private int LevenshteinDistance(string s1, string s2)
    {
        var m = s1.Length;
        var n = s2.Length;
        var dp = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++) dp[i, 0] = i;
        for (var j = 0; j <= n; j++) dp[0, j] = j;

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + cost);
            }
        }
        return dp[m, n];
    }

    /// <summary>
    /// 获取可执行文件（限制搜索深度）
    /// </summary>
    private List<string> GetExecutableFiles(string path, int maxDepth)
    {
        var result = new List<string>();
        try
        {
            SearchDirectory(path, 0, maxDepth, result);
        }
        catch
        {
            // 忽略访问错误
        }
        return result;
    }

    private void SearchDirectory(string path, int currentDepth, int maxDepth, List<string> result)
    {
        if (currentDepth > maxDepth) return;

        try
        {
            // 获取当前目录的 exe 文件
            var files = Directory.GetFiles(path, "*.exe");
            result.AddRange(files);

            // 递归子目录
            if (currentDepth < maxDepth)
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var dirName = Path.GetFileName(dir).ToLower();
                    // 跳过一些明显不包含游戏的目录
                    if (dirName is "_redist" or "redist" or "redistrib" or 
                        "__installer" or "directx" or "vcredist" or 
                        "support" or "docs" or "documentation" or
                        "localization" or "languages" or "logs")
                        continue;
                    
                    SearchDirectory(dir, currentDepth + 1, maxDepth, result);
                }
            }
        }
        catch
        {
            // 权限不足等情况
        }
    }

    /// <summary>
    /// 对可执行文件进行评分
    /// </summary>
    private int ScoreExecutable(string exePath, string gameName, string installPath)
    {
        var score = 0;
        var fileName = Path.GetFileName(exePath).ToLower();
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(exePath).ToLower();
        var relativePath = exePath.Substring(installPath.Length).TrimStart(Path.DirectorySeparatorChar).ToLower();

        // 1. 排除系统/工具可执行文件 (-1000 分直接排除)
        if (ExcludedExeKeywords.Any(k => fileName.Contains(k)))
        {
            return -1000;
        }

        // 2. 名称匹配加分 (+200)
        var nameClean = Regex.Replace(gameName, @"[^a-zA-Z0-9]", "").ToLower();
        var exeNameClean = Regex.Replace(fileNameWithoutExt, @"[^a-zA-Z0-9]", "");
        if (!string.IsNullOrEmpty(nameClean) && !string.IsNullOrEmpty(exeNameClean))
        {
            if (exeNameClean.Contains(nameClean) || nameClean.Contains(exeNameClean))
            {
                score += 200;
            }
            else if (LevenshteinSimilarity(exeNameClean, nameClean) > 0.5)
            {
                score += 100;
            }
        }

        // 3. 优先关键词加分 (+50 每个)
        foreach (var keyword in PreferredExeKeywords)
        {
            if (fileName.Contains(keyword))
            {
                score += 50;
            }
        }

        // 4. 文件位置评分 - 根目录或 bin 目录的文件更可能是主程序
        var pathParts = relativePath.Split(Path.DirectorySeparatorChar);
        if (pathParts.Length == 1)
        {
            score += 100; // 在根目录
        }
        else if (pathParts.Length == 2 && 
                 (pathParts[0] is "bin" or "binaries" or "game" or "x64" or "win64"))
        {
            score += 80; // 在 bin 类目录下
        }
        else if (pathParts.Length > 3)
        {
            score -= 30; // 嵌套太深的减分
        }

        // 5. 文件大小评分 - 游戏主程序通常较大
        try
        {
            var fileInfo = new FileInfo(exePath);
            var sizeMB = fileInfo.Length / (1024.0 * 1024.0);
            
            if (sizeMB > 100)
                score += 80;  // 大型游戏
            else if (sizeMB > 50)
                score += 60;
            else if (sizeMB > 20)
                score += 40;
            else if (sizeMB > 5)
                score += 20;
            else if (sizeMB < 1)
                score -= 20;  // 太小的可能是工具
        }
        catch
        {
            // 无法获取大小
        }

        // 6. 检查是否有版本信息 - 正规游戏通常有版本信息
        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
            if (!string.IsNullOrEmpty(versionInfo.ProductName))
            {
                score += 30;
                
                // 如果产品名称匹配游戏名称，大加分
                var productClean = Regex.Replace(versionInfo.ProductName, @"[^a-zA-Z0-9]", "").ToLower();
                if (!string.IsNullOrEmpty(productClean) && 
                    (productClean.Contains(nameClean) || nameClean.Contains(productClean)))
                {
                    score += 150;
                }
            }
            if (!string.IsNullOrEmpty(versionInfo.CompanyName))
            {
                score += 20;
                
                // 知名游戏公司加分
                var company = versionInfo.CompanyName.ToLower();
                if (company.Contains("valve") || company.Contains("epic") || 
                    company.Contains("ubisoft") || company.Contains("ea ") ||
                    company.Contains("blizzard") || company.Contains("riot") ||
                    company.Contains("rockstar") || company.Contains("bethesda") ||
                    company.Contains("capcom") || company.Contains("sega") ||
                    company.Contains("bandai") || company.Contains("konami") ||
                    company.Contains("square enix") || company.Contains("mihoyo") ||
                    company.Contains("cd projekt"))
                {
                    score += 50;
                }
            }
        }
        catch
        {
            // 无法获取版本信息
        }

        // 7. 特殊规则 - 某些特定命名模式
        if (fileName.EndsWith("-win64-shipping.exe"))
        {
            score += 100; // Unreal Engine 游戏的典型命名
        }
        if (fileName.Contains("_x64.exe") || fileName.Contains("-x64.exe"))
        {
            score += 30; // 64位版本通常是主程序
        }
        if (fileName == "game.exe" || fileName == "play.exe" || fileName == "start.exe")
        {
            score += 80;
        }

        return score;
    }

    /// <summary>
    /// 判断是否为系统可执行文件
    /// </summary>
    private bool IsSystemExecutable(string path)
    {
        var name = Path.GetFileName(path).ToLower();
        return ExcludedExeKeywords.Any(k => name.Contains(k));
    }

    /// <summary>
    /// 启动游戏监控（降低频率以减少资源占用）
    /// </summary>
    public void StartGameMonitor(int intervalMs = 3000) // 从 2000ms 提高到 3000ms
    {
        _gameMonitorTimer?.Stop();
        _gameMonitorTimer?.Dispose();
        _gameMonitorTimer = new System.Timers.Timer(intervalMs);
        _gameMonitorTimer.Elapsed += GameMonitorTimer_Elapsed;
        _gameMonitorTimer.AutoReset = true;
        _gameMonitorTimer.Start();
    }

    private void GameMonitorTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_disposed) return;
        CheckRunningGames();
    }

    /// <summary>
    /// 停止游戏监控
    /// </summary>
    public void StopGameMonitor()
    {
        if (_gameMonitorTimer != null)
        {
            _gameMonitorTimer.Stop();
            _gameMonitorTimer.Elapsed -= GameMonitorTimer_Elapsed;
            _gameMonitorTimer.Dispose();
            _gameMonitorTimer = null;
        }
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        StopGameMonitor();
        
        // 清理事件
        OnGameStarted = null;
        OnGameStopped = null;
        
        // 清理游戏列表
        _games.Clear();
        _games.TrimExcess();
        
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 检查正在运行的游戏
    /// </summary>
    private void CheckRunningGames()
    {
        foreach (var game in _games)
        {
            if (string.IsNullOrEmpty(game.ProcessName)) continue;

            var processes = _processService.FindProcessesByName(game.ProcessName);
            var wasRunning = game.IsRunning;
            game.IsRunning = processes.Count > 0;

            if (game.IsRunning && !wasRunning)
            {
                game.LastRunTime = DateTime.Now;
                OnGameStarted?.Invoke(game);
            }
            else if (!game.IsRunning && wasRunning)
            {
                OnGameStopped?.Invoke(game);
            }
        }
    }

    /// <summary>
    /// 启动游戏
    /// </summary>
    public bool LaunchGame(GameInfo game)
    {
        try
        {
            if (game.Platform == GamePlatform.Steam && !string.IsNullOrEmpty(game.PlatformGameId))
            {
                // 使用 Steam 协议启动
                Process.Start(new ProcessStartInfo
                {
                    FileName = $"steam://rungameid/{game.PlatformGameId}",
                    UseShellExecute = true
                });
                return true;
            }
            else if (!string.IsNullOrEmpty(game.ExecutablePath) && File.Exists(game.ExecutablePath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = game.ExecutablePath,
                    WorkingDirectory = Path.GetDirectoryName(game.ExecutablePath),
                    UseShellExecute = true
                });
                return true;
            }
        }
        catch
        {
            // 启动失败
        }
        return false;
    }

    /// <summary>
    /// 添加游戏
    /// </summary>
    public void AddGame(GameInfo game)
    {
        if (!_games.Any(g => g.Id == game.Id))
        {
            _games.Add(game);
        }
    }

    /// <summary>
    /// 移除游戏
    /// </summary>
    public void RemoveGame(string gameId)
    {
        var game = _games.FirstOrDefault(g => g.Id == gameId);
        if (game != null)
        {
            _games.Remove(game);
        }
    }

    /// <summary>
    /// 保存游戏配置
    /// </summary>
    public void SaveGames()
    {
        try
        {
            var dir = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(_games, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_configPath, json);
        }
        catch
        {
            // 保存失败
        }
    }

    /// <summary>
    /// 加载游戏配置
    /// </summary>
    public void LoadGames()
    {
        try
        {
            if (!File.Exists(_configPath)) return;

            var json = File.ReadAllText(_configPath);
            var games = JsonSerializer.Deserialize<List<GameInfo>>(json);
            
            if (games != null)
            {
                _games.Clear();
                _games.AddRange(games);
            }
        }
        catch
        {
            // 加载失败
        }
    }
}
