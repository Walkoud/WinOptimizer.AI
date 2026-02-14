using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Runtime.InteropServices;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Game Mode configuration for protecting game processes and auto-killing intruders
    /// </summary>
    public class GameModeConfig
    {
        public string Name { get; set; }
        public List<string> GameProcesses { get; set; } = new();
        public List<string> ProtectedWhitelist { get; set; } = new();
        public bool AutoKillEnabled { get; set; } = false;
        public double GpuThresholdForAutoKill { get; set; } = 10.0;
        public bool NotificationsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Manages Game Mode functionality
    /// </summary>
    public class GameModeManager
    {
        private readonly List<GameModeConfig> _configs = new();
        private GameModeConfig _activeConfig;
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private readonly GpuMonitor _gpuMonitor;
        private readonly Dictionary<string, DateTime> _recentlyKilledProcesses = new Dictionary<string, DateTime>();
        private List<string> _userGames = new();
        private const string UserGamesFile = "Games.json";
        
        // Restore feature
        private List<string> _restorableProcesses = new List<string>();

        public bool IsActive => _activeConfig != null;
        public GameModeConfig ActiveConfig => _activeConfig;
        public event Action<string> OnLog;
        public event Action<ProcessData> OnIntruderDetected;
        public event Action<ProcessData> OnIntruderKilled;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        public GameModeManager(GpuMonitor gpuMonitor)
        {
            _gpuMonitor = gpuMonitor;
            EnsureDefaultWhitelist();
            LoadDefaultConfigs();
            LoadUserGames();
        }

        private void EnsureDefaultWhitelist()
        {
            // If the whitelist was loaded from disk, we respect user's existing config
            // and do not force-add defaults again (allowing user to remove them).
            if (WhitelistManager.LoadedFromDisk) return;

            var defaults = new[] { "discord", "obs", "nvidia", "amd", "steam", "epicgameslauncher", "battle.net" };
            foreach (var app in defaults)
            {
                if (!WhitelistManager.IsWhitelisted(app))
                {
                    WhitelistManager.Add("Process", app);
                }
            }
        }

        private void LoadUserGames()
        {
            try
            {
                if (File.Exists(UserGamesFile))
                {
                    var json = File.ReadAllText(UserGamesFile);
                    _userGames = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[GameMode] Failed to load user games: {ex.Message}");
            }
        }

        public void AddUserGame(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            
            processName = processName.ToLowerInvariant().Replace(".exe", "");
            if (!_userGames.Contains(processName))
            {
                _userGames.Add(processName);
                SaveUserGames();
                OnLog?.Invoke($"[GameMode] Added '{processName}' to user games library.");
            }
        }

        private void SaveUserGames()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_userGames, Formatting.Indented);
                File.WriteAllText(UserGamesFile, json);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[GameMode] Failed to save user games: {ex.Message}");
            }
        }

        private void LoadDefaultConfigs()
        {
            // Default Steam config
            _configs.Add(new GameModeConfig
            {
                Name = "Steam Games",
                GameProcesses = new List<string> { "steam", "cs2", "dota2", "tf2", "left4dead2" },
                ProtectedWhitelist = new List<string>(), // Handled by global whitelist
                AutoKillEnabled = false,
                GpuThresholdForAutoKill = 10.0
            });

            // Default Epic Games config
            _configs.Add(new GameModeConfig
            {
                Name = "Epic Games",
                GameProcesses = new List<string> { "epicgameslauncher", "fortnite", "unreal" },
                ProtectedWhitelist = new List<string>(),
                AutoKillEnabled = false,
                GpuThresholdForAutoKill = 10.0
            });

            // Default Battle.net config
            _configs.Add(new GameModeConfig
            {
                Name = "Battle.net",
                GameProcesses = new List<string> { "battle.net", "overwatch", "wow", "diablo" },
                ProtectedWhitelist = new List<string>(),
                AutoKillEnabled = false,
                GpuThresholdForAutoKill = 10.0
            });
        }

        public List<GameModeConfig> GetConfigs() => _configs;

        public void AddCustomConfig(string name, List<string> gameProcesses)
        {
            _configs.Add(new GameModeConfig
            {
                Name = name,
                GameProcesses = gameProcesses,
                ProtectedWhitelist = new List<string>(), // Handled by global whitelist
                AutoKillEnabled = false,
                GpuThresholdForAutoKill = 10.0
            });
        }

        public void Activate(GameModeConfig config)
        {
            if (_activeConfig != null)
                Deactivate();

            _activeConfig = config;
            _cancellationTokenSource = new CancellationTokenSource();
            _restorableProcesses.Clear(); // Reset restore list on new session
            
            // Set Game Mode Active Flag
            _gpuMonitor.IsGameModeActive = true;
            
            OnLog?.Invoke($"[GameMode] Activated: {config.Name}");
            OnLog?.Invoke($"[GameMode] Protected processes: {string.Join(", ", config.GameProcesses)}");
            
            if (!_gpuMonitor.IsMonitoring)
            {
                OnLog?.Invoke("[GameMode] Starting GPU Monitor to track game performance...");
                _gpuMonitor.StartMonitoring();
            }
            
            // Immediate check!
            OnLog?.Invoke("[GameMode] Performing initial optimization check...");
            Task.Run(() => EnforceGameModeRules()); // Run immediately but don't block UI

            if (config.AutoKillEnabled)
            {
                OnLog?.Invoke($"[GameMode] AutoKill enabled at {config.GpuThresholdForAutoKill}% GPU");
            }
            
            // Start periodic monitoring
            StartMonitoring();
        }

        public void Deactivate()
        {
            _cancellationTokenSource?.Cancel();
            _monitoringTask?.Wait(1000);
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            _activeConfig = null;
            
            // Set Game Mode Active Flag
            _gpuMonitor.IsGameModeActive = false;
            
            // User requested that stopping Game Mode should stop monitoring too
            if (_gpuMonitor.IsMonitoring)
            {
                 OnLog?.Invoke("[GameMode] Stopping GPU Monitor as Game Mode is deactivated...");
                 _gpuMonitor.StopMonitoring();
            }

            OnLog?.Invoke("[GameMode] Deactivated");
        }
        
        public int RestoreKilledProcesses()
        {
            int restored = 0;
            var pathsToRestore = _restorableProcesses.Distinct().ToList();
            
            foreach (var path in pathsToRestore)
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;
                
                try
                {
                    Process.Start(path);
                    restored++;
                    OnLog?.Invoke($"[GameMode] Restored: {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[GameMode] Failed to restore {Path.GetFileName(path)}: {ex.Message}");
                }
            }
            
            _restorableProcesses.Clear();
            return restored;
        }

        private void StartMonitoring()
        {
            _monitoringTask = Task.Run(async () =>
            {
                while (!_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    try
                    {
                        EnforceGameModeRules();
                        await Task.Delay(5000, _cancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke($"[GameMode] Error: {ex.Message}");
                    }
                }
            }, _cancellationTokenSource.Token);
        }

        private bool IsGameProcess(ProcessData processData)
        {
            if (processData == null) return false;
            
            // 1. Check User Games
            if (_userGames.Contains(processData.ProcessName.ToLowerInvariant())) return true;
            
            // 2. Check Active Config
            if (_activeConfig != null && _activeConfig.GameProcesses.Any(g => 
                processData.ProcessName.IndexOf(g, StringComparison.OrdinalIgnoreCase) >= 0)) return true;

            // 3. Heuristic: Foreground + Graphics DLLs
            try 
            {
                // Only check heuristic if it's the foreground window
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(hwnd, out int foregroundPid);
                    if (foregroundPid == processData.ProcessId)
                    {
                        return HasGraphicsModules(processData.ProcessId);
                    }
                }
            }
            catch { }

            return false;
        }

        private bool HasGraphicsModules(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    foreach (ProcessModule module in process.Modules)
                    {
                        var name = module.ModuleName.ToLowerInvariant();
                        if (name.StartsWith("d3d") || // d3d9, d3d10, d3d11, d3d12
                            name.StartsWith("vulkan") || 
                            name.StartsWith("opengl") ||
                            name.StartsWith("ati") || 
                            name.StartsWith("nv"))
                        {
                            return true;
                        }
                    }
                }
            }
            catch 
            { 
                // Access denied or process exited
            }
            return false;
        }

        private void EnforceGameModeRules()
        {
            if (_activeConfig == null) return;

            var processes = _gpuMonitor.GetGpuProcesses();
            
            // Identify the running game(s) using robust detection
            var runningGames = processes.Where(IsGameProcess).ToList();

            // Cleanup old cache entries
            var now = DateTime.Now;
            var expiredKeys = _recentlyKilledProcesses.Where(kvp => (now - kvp.Value).TotalSeconds > 60).Select(kvp => kvp.Key).ToList();
            foreach (var key in expiredKeys) _recentlyKilledProcesses.Remove(key);

            // Targets list
            var targets = new List<ProcessData>();

            // 1. Check Blacklisted Processes (ALWAYS ENFORCE if AutoKill is TRUE for that item)
            // We must scan ALL running processes, not just those with GPU usage
            try
            {
                var allProcesses = Process.GetProcesses();
                foreach (var p in allProcesses)
                {
                    try 
                    {
                        // Check if this process is blacklisted and should be auto-killed
                        if (BlacklistManager.ShouldAutoKill(p.ProcessName))
                        {
                            // Avoid re-killing too frequently
                            if (_recentlyKilledProcesses.ContainsKey(p.ProcessName)) continue;

                            // Create a ProcessData object for the kill method
                            var pData = new ProcessData
                            {
                                ProcessName = p.ProcessName,
                                ProcessId = p.Id,
                                GpuUsage = 0, // Assume 0 if not in GPU list
                                CpuUsage = 0
                            };

                            // Try to get path for restore feature
                            try { pData.Path = p.MainModule.FileName; } catch { }

                            // Add to targets if not already added
                            if (!targets.Any(t => t.ProcessId == p.Id))
                            {
                                targets.Add(pData);
                            }
                        }
                    }
                    catch { /* Skip process if access denied */ }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[GameMode] Error scanning blacklisted processes: {ex.Message}");
            }
            
            // 2. Check Intruders (High GPU) - ONLY if Global AutoKill is enabled
            if (_activeConfig.AutoKillEnabled && runningGames.Any())
            {
                var intruders = processes.Where(p =>
                    !IsGameProcess(p) &&
                    !_recentlyKilledProcesses.ContainsKey(p.ProcessName) &&
                    !targets.Any(t => t.ProcessId == p.ProcessId) && // Avoid duplicates
                    (
                        p.GpuUsage >= _activeConfig.GpuThresholdForAutoKill &&
                        !_activeConfig.ProtectedWhitelist.Any(w => p.ProcessName?.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0) &&
                        !WhitelistManager.IsWhitelisted(p.ProcessName)
                    )
                ).ToList();
                
                targets.AddRange(intruders);
            }

            // Execute kills
            var killedApps = new HashSet<string>();

            foreach (var target in targets)
            {
                if (KillProcess(target, silent: true))
                {
                    killedApps.Add(target.ProcessName);
                }
            }

            // Show summary notification
            if (killedApps.Count > 0 && _activeConfig.NotificationsEnabled)
            {
                // Group messages to avoid spamming the same app name
                var appNames = killedApps.ToList();
                string msg = appNames.Count == 1
                    ? $"Terminated blacklisted app: {appNames[0]}"
                    : $"Terminated {appNames.Count} apps: {string.Join(", ", appNames)}";
                
                NotificationManager.ShowInfoNotification("Game Mode", msg);
            }
        }

        private bool KillProcess(ProcessData target, bool silent = false)
        {
            OnIntruderDetected?.Invoke(target);
            OnLog?.Invoke($"[GameMode] Enforcing rules on: {target.ProcessName} ({target.GpuUsage:F1}% GPU)");

            try
            {
                // Store path for restore feature
                if (!string.IsNullOrEmpty(target.Path) && !_restorableProcesses.Contains(target.Path))
                {
                    _restorableProcesses.Add(target.Path);
                }

                // Use taskkill /F /IM to kill ALL processes with this name
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /IM \"{target.ProcessName}.exe\" /T",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };
                
                using (var proc = Process.Start(startInfo))
                {
                    proc?.WaitForExit(2000);
                    var output = proc?.StandardOutput.ReadToEnd();
                    var error = proc?.StandardError.ReadToEnd();
                    
                    if (proc != null && proc.ExitCode != 0 && proc.ExitCode != 128)
                    {
                         OnLog?.Invoke($"[GameMode] taskkill warning: {error}");
                    }
                }
                
                OnIntruderKilled?.Invoke(target);
                OnLog?.Invoke($"[GameMode] Terminated: {target.ProcessName}");
                
                if (!silent)
                {
                    NotificationManager.ShowInfoNotification("Game Mode", $"Terminated {target.ProcessName}");
                }

                if (!_recentlyKilledProcesses.ContainsKey(target.ProcessName))
                {
                    _recentlyKilledProcesses[target.ProcessName] = DateTime.Now;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[GameMode] Failed to kill {target.ProcessName}: {ex.Message}");
                
                // Fallback
                try 
                {
                    var process = Process.GetProcessById(target.ProcessId);
                    if (!string.IsNullOrEmpty(target.Path) && !_restorableProcesses.Contains(target.Path))
                    {
                        _restorableProcesses.Add(target.Path);
                    }
                    process.Kill(); 
                    OnLog?.Invoke($"[GameMode] Fallback kill successful for PID {target.ProcessId}");
                    return true;
                } catch { }
            }
            
            return false;
        }

        public bool IsGameRunning()
        {
            if (_activeConfig == null) return false;
            
            return _gpuMonitor.GetGpuProcesses().Any(IsGameProcess);
        }

        public string GetCurrentGameProcess()
        {
            if (_activeConfig == null) return null;
            
            var game = _gpuMonitor.GetGpuProcesses().FirstOrDefault(IsGameProcess);
            
            return game?.ProcessName;
        }
    }
}
