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
public class GameService
{
    private readonly List<GameInfo> _games = new();
    private readonly string _configPath;
    private readonly ProcessService _processService;
    private System.Timers.Timer? _gameMonitorTimer;

    public event Action<GameInfo>? OnGameStarted;
    public event Action<GameInfo>? OnGameStopped;

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
        });

        return allGames;
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

        // 尝试找到主要可执行文件
        var exePath = FindMainExecutable(installPath, name);
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
    /// 扫描 Xbox/Microsoft Store 游戏
    /// </summary>
    public List<GameInfo> ScanXboxGames()
    {
        var games = new List<GameInfo>();

        try
        {
            // Xbox 游戏通常安装在 WindowsApps 或 XboxGames 目录
            var xboxPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps"),
                @"C:\XboxGames",
                @"D:\XboxGames",
                @"E:\XboxGames"
            };

            foreach (var xboxPath in xboxPaths)
            {
                if (!Directory.Exists(xboxPath)) continue;

                try
                {
                    var gameDirs = Directory.GetDirectories(xboxPath);
                    foreach (var gameDir in gameDirs)
                    {
                        // 跳过系统包
                        var dirName = Path.GetFileName(gameDir);
                        if (dirName.StartsWith("Microsoft.") || 
                            dirName.StartsWith("Windows.") ||
                            dirName.Contains("_neutral_"))
                            continue;

                        var exePath = FindMainExecutable(gameDir, dirName);
                        if (string.IsNullOrEmpty(exePath)) continue;

                        // 提取游戏名称（去除版本号等）
                        var name = Regex.Replace(dirName, @"_[\d\.]+_.*$", "");
                        name = Regex.Replace(name, @"([a-z])([A-Z])", "$1 $2");

                        games.Add(new GameInfo
                        {
                            Name = name,
                            Platform = GamePlatform.Xbox,
                            InstallPath = gameDir,
                            ExecutablePath = exePath,
                            ProcessName = Path.GetFileNameWithoutExtension(exePath)
                        });
                    }
                }
                catch
                {
                    // 权限不足等情况
                }
            }
        }
        catch
        {
            // 忽略 Xbox 扫描错误
        }

        return games;
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
    /// 扫描 Riot Games 游戏
    /// </summary>
    public List<GameInfo> ScanRiotGames()
    {
        var games = new List<GameInfo>();

        // Riot 游戏定义
        var riotGames = new Dictionary<string, (string exeName, string[] altPaths)>
        {
            { "英雄联盟", ("LeagueClient.exe", new[] { "League of Legends", "英雄联盟" }) },
            { "VALORANT", ("VALORANT.exe", new[] { "VALORANT", "Valorant" }) },
            { "云顶之弈", ("LeagueClient.exe", new[] { "Teamfight Tactics" }) },
            { "Legends of Runeterra", ("LoR.exe", new[] { "LoR", "Legends of Runeterra" }) }
        };

        try
        {
            // Riot 游戏默认安装路径
            var riotPaths = new[]
            {
                @"C:\Riot Games",
                @"D:\Riot Games",
                @"E:\Riot Games",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Games"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Riot Games")
            };

            foreach (var riotPath in riotPaths)
            {
                if (!Directory.Exists(riotPath)) continue;

                try
                {
                    foreach (var gameDir in Directory.GetDirectories(riotPath))
                    {
                        var dirName = Path.GetFileName(gameDir);
                        
                        // 英雄联盟特殊处理
                        if (dirName.Contains("League of Legends") || dirName.Contains("英雄联盟"))
                        {
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
                        // VALORANT 特殊处理
                        else if (dirName.Contains("VALORANT", StringComparison.OrdinalIgnoreCase))
                        {
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
                        else
                        {
                            var exePath = FindMainExecutable(gameDir, dirName);
                            if (!string.IsNullOrEmpty(exePath))
                            {
                                games.Add(new GameInfo
                                {
                                    Name = dirName,
                                    Platform = GamePlatform.Riot,
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
            // 忽略 Riot 扫描错误
        }

        return games;
    }

    /// <summary>
    /// 扫描 WeGame 游戏
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

            // 常见 WeGame 游戏目录
            var wegamePaths = new List<string>
            {
                @"D:\WeGame",
                @"E:\WeGame",
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

                // WeGame 游戏通常在 games 子目录
                var gamesDir = Path.Combine(basePath, "games");
                if (!Directory.Exists(gamesDir))
                {
                    gamesDir = basePath;
                }

                try
                {
                    foreach (var gameDir in Directory.GetDirectories(gamesDir))
                    {
                        var dirName = Path.GetFileName(gameDir);
                        
                        // 跳过 WeGame 自身
                        if (dirName.Equals("wegame", StringComparison.OrdinalIgnoreCase)) continue;
                        if (dirName.Equals("downloads", StringComparison.OrdinalIgnoreCase)) continue;

                        var exePath = FindMainExecutable(gameDir, dirName);
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            games.Add(new GameInfo
                            {
                                Name = dirName,
                                Platform = GamePlatform.WeGame,
                                InstallPath = gameDir,
                                ExecutablePath = exePath,
                                ProcessName = Path.GetFileNameWithoutExtension(exePath)
                            });
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
            // 忽略 WeGame 扫描错误
        }

        return games;
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
    /// 查找主要可执行文件
    /// </summary>
    private string? FindMainExecutable(string installPath, string gameName)
    {
        try
        {
            if (!Directory.Exists(installPath)) return null;

            // 获取所有 exe 文件
            var exeFiles = Directory.GetFiles(installPath, "*.exe", SearchOption.AllDirectories)
                .Where(f => !IsSystemExecutable(f))
                .ToList();

            if (exeFiles.Count == 0) return null;

            // 优先匹配游戏名称
            var nameClean = Regex.Replace(gameName, @"[^a-zA-Z0-9]", "").ToLower();
            var matchedExe = exeFiles.FirstOrDefault(f =>
            {
                var exeName = Path.GetFileNameWithoutExtension(f).ToLower();
                exeName = Regex.Replace(exeName, @"[^a-zA-Z0-9]", "");
                return exeName.Contains(nameClean) || nameClean.Contains(exeName);
            });

            if (matchedExe != null) return matchedExe;

            // 排除常见的非游戏可执行文件
            var gameExe = exeFiles.FirstOrDefault(f =>
            {
                var name = Path.GetFileName(f).ToLower();
                return !name.Contains("unins") &&
                       !name.Contains("setup") &&
                       !name.Contains("install") &&
                       !name.Contains("crash") &&
                       !name.Contains("report") &&
                       !name.Contains("launcher") &&
                       !name.Contains("updater") &&
                       !name.Contains("redist");
            });

            return gameExe ?? exeFiles.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 判断是否为系统可执行文件
    /// </summary>
    private bool IsSystemExecutable(string path)
    {
        var name = Path.GetFileName(path).ToLower();
        var systemNames = new[]
        {
            "unins", "setup", "install", "vcredist", "dxsetup",
            "dotnet", "ue4prereq", "physx", "directx"
        };
        return systemNames.Any(s => name.Contains(s));
    }

    /// <summary>
    /// 启动游戏监控
    /// </summary>
    public void StartGameMonitor(int intervalMs = 2000)
    {
        _gameMonitorTimer?.Stop();
        _gameMonitorTimer = new System.Timers.Timer(intervalMs);
        _gameMonitorTimer.Elapsed += (s, e) => CheckRunningGames();
        _gameMonitorTimer.Start();
    }

    /// <summary>
    /// 停止游戏监控
    /// </summary>
    public void StopGameMonitor()
    {
        _gameMonitorTimer?.Stop();
        _gameMonitorTimer?.Dispose();
        _gameMonitorTimer = null;
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
