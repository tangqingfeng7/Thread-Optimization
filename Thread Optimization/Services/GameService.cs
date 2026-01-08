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
