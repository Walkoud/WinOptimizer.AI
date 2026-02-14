using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Real-time GPU monitor that sends Windows notifications when apps use too much GPU
    /// </summary>
    public class GpuMonitor : IDisposable
    {
        private readonly Dictionary<int, PerformanceCounter> _ioCounters = new();
        private readonly Dictionary<int, (TimeSpan TotalProcessorTime, DateTime Time)> _cpuUsageCache = new();
        private readonly Dictionary<int, GpuProcessTracker> _gpuTrackers = new();

        private EtwGpuUsageProvider _etwGpu;
        private bool _etwAvailable;
        private bool _usePerformanceCounterFallback;
        private readonly Dictionary<string, PerformanceCounter> _gpuPerformanceCounters = new();
        
        private CancellationTokenSource _cancellationTokenSource;
        private Task _monitoringTask;
        private bool _isMonitoring;
        private DateTime _lastGpuInstanceRefresh = DateTime.MinValue;
        private List<Process> _processSnapshot = new();
        private int _processScanIndex = 0;
        private int _processBatchSize = 50;
        private readonly Dictionary<int, ProcessData> _processCache = new();
        private readonly object _processCacheLock = new object();
        private DateTime _lastSnapshotTime = DateTime.MinValue;
        private readonly HashSet<int> _activeGpuPids = new();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

        private int GetActiveProcessId()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return 0;
                GetWindowThreadProcessId(hwnd, out int pid);
                return pid;
            }
            catch { return 0; }
        }
        
        // Configuration
        public bool IsGameModeActive { get; set; } = false;

        public double GpuThresholdPercent
        {
            get => AppSettings.Instance.GpuThresholdPercent;
            set => AppSettings.Instance.GpuThresholdPercent = value;
        }
        public double CpuThresholdPercent
        {
            get => AppSettings.Instance.CpuThresholdPercent;
            set => AppSettings.Instance.CpuThresholdPercent = value;
        }
        public double RamThresholdMB
        {
            get => AppSettings.Instance.RamThresholdMB;
            set => AppSettings.Instance.RamThresholdMB = value;
        }
        public double NetworkThresholdMB
        {
            get => AppSettings.Instance.NetworkThresholdMB;
            set => AppSettings.Instance.NetworkThresholdMB = value;
        }
        
        public bool MonitorGpu
        {
            get => AppSettings.Instance.MonitorGpu;
            set => AppSettings.Instance.MonitorGpu = value;
        }
        public bool MonitorCpu
        {
            get => AppSettings.Instance.MonitorCpu;
            set => AppSettings.Instance.MonitorCpu = value;
        }
        public bool MonitorRam
        {
            get => AppSettings.Instance.MonitorRam;
            set => AppSettings.Instance.MonitorRam = value;
        }
        public bool MonitorNetwork
        {
            get => AppSettings.Instance.MonitorNetwork;
            set => AppSettings.Instance.MonitorNetwork = value;
        }
        
        // Global Auto-Kill (Always Active)
        public bool AutoKillGpuGlobal
        {
            get => AppSettings.Instance.AutoKillGpuGlobal;
            set => AppSettings.Instance.AutoKillGpuGlobal = value;
        }
        public bool AutoKillCpuGlobal
        {
            get => AppSettings.Instance.AutoKillCpuGlobal;
            set => AppSettings.Instance.AutoKillCpuGlobal = value;
        }
        public bool AutoKillRamGlobal
        {
            get => AppSettings.Instance.AutoKillRamGlobal;
            set => AppSettings.Instance.AutoKillRamGlobal = value;
        }
        public bool AutoKillNetworkGlobal
        {
            get => AppSettings.Instance.AutoKillNetworkGlobal;
            set => AppSettings.Instance.AutoKillNetworkGlobal = value;
        }

        // Game Mode Auto-Kill (Active only when IsGameModeActive is true)
        public bool AutoKillGpuGameMode
        {
            get => AppSettings.Instance.AutoKillGpuGameMode;
            set => AppSettings.Instance.AutoKillGpuGameMode = value;
        }
        public bool AutoKillCpuGameMode
        {
            get => AppSettings.Instance.AutoKillCpuGameMode;
            set => AppSettings.Instance.AutoKillCpuGameMode = value;
        }
        public bool AutoKillRamGameMode
        {
            get => AppSettings.Instance.AutoKillRamGameMode;
            set => AppSettings.Instance.AutoKillRamGameMode = value;
        }
        public bool AutoKillNetworkGameMode
        {
            get => AppSettings.Instance.AutoKillNetworkGameMode;
            set => AppSettings.Instance.AutoKillNetworkGameMode = value;
        }

        // Notification Toggles (Mapped to AppSettings)
        public bool NotifyGpu
        {
            get => AppSettings.Instance.AlertGpu;
            set => AppSettings.Instance.AlertGpu = value;
        }
        public bool NotifyCpu
        {
            get => AppSettings.Instance.AlertCpu;
            set => AppSettings.Instance.AlertCpu = value;
        }
        public bool NotifyRam
        {
            get => AppSettings.Instance.AlertRam;
            set => AppSettings.Instance.AlertRam = value;
        }
        public bool NotifyNetwork
        {
            get => AppSettings.Instance.AlertNetwork;
            set => AppSettings.Instance.AlertNetwork = value;
        }

        public int CheckIntervalSeconds
        {
            get => AppSettings.Instance.CheckIntervalSeconds;
            set => AppSettings.Instance.CheckIntervalSeconds = value;
        }
        public int GpuAlertDurationMinutes { get; set; } = 5;
        
        public bool LowImpactMode
        {
            get => AppSettings.Instance.LowImpactMode;
            set => AppSettings.Instance.LowImpactMode = value;
        }
        
        public int GpuCountersRefreshIntervalSeconds { get; set; } = 60;

        public bool DevMode { get; set; } = false;
        
        public int AlertDurationSeconds
        {
            get => AppSettings.Instance.AlertDurationSeconds;
            set => AppSettings.Instance.AlertDurationSeconds = value;
        }
        
        public bool IsEtwAvailable => _etwAvailable;
        
        // Separate thresholds for display vs alert
        public double GpuDisplayThresholdPercent
        {
            get => AppSettings.Instance.GpuDisplayThresholdPercent;
            set => AppSettings.Instance.GpuDisplayThresholdPercent = value;
        }  // Show processes with >0.1% GPU
        public double CpuDisplayThresholdPercent { get; set; } = 1.0;  // Show processes with >1.0% CPU (for Top 3 widget)
        
        // Events
        public event Action<ProcessData> OnGpuAlert;
        public event Action<ProcessData, string> OnProcessKilled;
        public event Action<string> OnLogMessage;
        public event Action<List<ProcessData>> OnProcessesUpdated;
        public event Action<bool> OnMonitoringStateChanged;
        
        public bool IsMonitoring => _isMonitoring;
        
        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            OnLogMessage?.Invoke("[DIAG] Starting GPU monitoring initialization...");

            if (!DevMode)
            {
                OnLogMessage?.Invoke("[DIAG] Using PerformanceCounters for GPU monitoring (ETW doesn't provide utilization data)");
                _etwAvailable = false; // ETW disabled - PerformanceCounters are more reliable
            }
            else
            {
                OnLogMessage?.Invoke("[DIAG] DevMode enabled - GPU simulated from CPU");
            }
            
            _isMonitoring = true;
            _cancellationTokenSource = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitoringLoop(_cancellationTokenSource.Token));
            
            OnLogMessage?.Invoke(LanguageManager.GetString("LogMonitoringStarted"));
            OnMonitoringStateChanged?.Invoke(true);
        }
        
        public void StopMonitoring()
        {
            if (!_isMonitoring) return;
            
            _isMonitoring = false;
            _cancellationTokenSource?.Cancel();
            try
            {
                _monitoringTask?.Wait(250);
            }
            catch
            {
            }
            
            OnLogMessage?.Invoke(LanguageManager.GetString("LogMonitoringStopped"));

            try
            {
                _etwGpu?.Stop();
            }
            catch
            {
            }
            OnMonitoringStateChanged?.Invoke(false);
        }
        
        private async Task MonitoringLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var processes = ScanProcesses();
                    OnProcessesUpdated?.Invoke(processes);
                    
                    CheckGpuAlerts(processes);
                    var interval = LowImpactMode ? Math.Max(CheckIntervalSeconds, 30) : CheckIntervalSeconds;
                    await Task.Delay(TimeSpan.FromSeconds(interval), token);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    OnLogMessage?.Invoke("[WARN] Monitoring permission issue (Win32 Accès refusé). Continuing.");
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (UnauthorizedAccessException)
                {
                    OnLogMessage?.Invoke("[WARN] Monitoring permission issue (Accès refusé). Falling back and continuing.");
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
                catch (Exception ex)
                {
                    OnLogMessage?.Invoke($"Monitoring error: {ex.Message}");
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                }
            }
        }
        
        private List<ProcessData> ScanProcesses()
        {
            lock (_processCacheLock)
            {
                return DoScanProcesses();
            }
        }

        private List<ProcessData> DoScanProcesses()
        {
            var processes = new List<ProcessData>();
            if (LowImpactMode)
            {
                var now = DateTime.Now;
                var refresh = Math.Max(CheckIntervalSeconds, 60);
                if (_lastSnapshotTime == DateTime.MinValue || (now - _lastSnapshotTime).TotalSeconds >= refresh || _processScanIndex >= _processSnapshot.Count)
                {
                    try
                    {
                        // Don't check .HasExited here as it throws Access Denied for system processes
                        // Process.GetProcesses() returns currently running processes anyway
                        _processSnapshot = Process.GetProcesses().ToList();
                        _processScanIndex = 0;
                        _lastSnapshotTime = now;
                        var snapshotPids = new HashSet<int>(_processSnapshot.Select(p => p.Id));
                        var toRemove = _processCache.Keys.Where(pid => !snapshotPids.Contains(pid)).ToList();
                        foreach (var pid in toRemove)
                        {
                            _processCache.Remove(pid);
                            _cpuUsageCache.Remove(pid);
                            if (_ioCounters.TryGetValue(pid, out var counter))
                            {
                                counter?.Dispose();
                                _ioCounters.Remove(pid);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage?.Invoke($"[WARN] Process snapshot refresh failed: {ex.Message}");
                    }
                }
                
                // Low Impact Mode - Scan all processes but use caching and optimizations
                // We used to batch this, but it caused missing CPU usage data for processes not in the current batch
                // Since we optimized CPU monitoring to use lightweight Process.TotalProcessorTime, scanning all is fine.
                
                var targetPids = new HashSet<int>(_processSnapshot.Select(p => p.Id));
                
                // Smart Scan: Always include active window and recent GPU users
                int activePid = GetActiveProcessId();
                
                // In Low Impact Mode, we must scan ALL GPU counters to detect background processes (like Browser GPU process)
                // Passing null triggers a full read of cached counters (fast) without refreshing instances (slow, done periodically)
                var gpuCountersLm = GetGpuCounters(null); 
                
                // Update active GPU list and build a set of PIDs to process
                _activeGpuPids.Clear();
                var pidsToProcess = new HashSet<int>();
                
                // 1. Add all processes from snapshot
                foreach(var p in _processSnapshot) pidsToProcess.Add(p.Id);

                // 2. Add Active Window (if not in snapshot)
                if (activePid > 0) pidsToProcess.Add(activePid);

                // 3. Add any process that is CURRENTLY using GPU
                foreach(var kvp in gpuCountersLm)
                {
                    if (kvp.Value > 0.0) // Any usage
                    {
                        _activeGpuPids.Add(kvp.Key);
                        pidsToProcess.Add(kvp.Key);
                    }
                }
                
                // Prepare the final list of Process objects
                var processesToAnalyze = new List<Process>();
                
                // Map PIDs to Process objects
                // First, try to find them in our snapshot (fastest)
                var snapshotMap = _processSnapshot.ToDictionary(p => p.Id, p => p);
                
                foreach(var pid in pidsToProcess)
                {
                    if (snapshotMap.TryGetValue(pid, out var proc))
                    {
                        processesToAnalyze.Add(proc);
                    }
                    else
                    {
                        // Not in snapshot (maybe new?), try to get it directly
                        try 
                        {
                            var missingProc = Process.GetProcessById(pid);
                            processesToAnalyze.Add(missingProc);
                        }
                        catch { }
                    }
                }

                int lmCriticalFiltered = 0;
                int lmCompanyFiltered = 0;
                int lmLowUsageFiltered = 0;
                double lmMaxGpuFound = 0;

                foreach (var process in processesToAnalyze)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        if (IsCriticalProcess(process.ProcessName))
                        {
                            lmCriticalFiltered++;
                            continue;
                        }
                        
                        // Optimize: Skip resource checks if monitoring is disabled
                        double cpuUsageLm = 0;
                        double ioUsageLm = 0;
                        
                        if (MonitorCpu) cpuUsageLm = GetProcessCpuUsage(process);
                        if (MonitorNetwork) ioUsageLm = GetProcessIoUsage(process);
                        
                        var gpuUsageLm = GetProcessGpuUsage(process.Id, gpuCountersLm);
                        
                        // Check if we should display this process based on enabled monitors
                        bool showProcess = false;
                        if (MonitorCpu && cpuUsageLm > CpuDisplayThresholdPercent) showProcess = true;
                        if (MonitorGpu && gpuUsageLm > GpuDisplayThresholdPercent) showProcess = true;
                        if (MonitorNetwork && ioUsageLm > 0.1) showProcess = true;
                        if (MonitorRam && (process.WorkingSet64 / (1024.0 * 1024.0)) > 100) showProcess = true; // Show > 100MB RAM
                        
                        if (!showProcess)
                        {
                            lmLowUsageFiltered++;
                            continue;
                        }
                        
                        // Optimization: Reuse cached static data (Path, Company) to avoid disk I/O
                        string processPathLm = "";
                        string companyLm = "";
                        
                        if (_processCache.TryGetValue(process.Id, out var cachedData) && !string.IsNullOrEmpty(cachedData.Path))
                        {
                            processPathLm = cachedData.Path;
                            companyLm = cachedData.Company;
                        }
                        else
                        {
                            processPathLm = GetProcessPath(process);
                            companyLm = GetFileCompanyName(processPathLm);
                        }

                        if (ShouldIgnoreProcess(companyLm))
                        {
                            lmCompanyFiltered++;
                            continue;
                        }
                        
                        var ramUsageLm = process.WorkingSet64 / (1024.0 * 1024.0);
                        var processDataLm = new ProcessData
                        {
                            ProcessName = process.ProcessName,
                            ProcessId = process.Id,
                            Path = processPathLm,
                            Company = companyLm,
                            CpuUsage = Math.Round(cpuUsageLm, 2),
                            GpuUsage = Math.Round(gpuUsageLm, 2),
                            RamUsageMB = Math.Round(ramUsageLm, 2),
                            NetworkUsageMB = ioUsageLm,
                            WindowTitle = process.MainWindowTitle,
                            Status = process.Responding ? "Running" : "Not Responding",
                            IsResponding = process.Responding
                        };
                        processDataLm.CalculatePerformanceLoss();
                        _processCache[process.Id] = processDataLm;
                        if (gpuUsageLm > lmMaxGpuFound) lmMaxGpuFound = gpuUsageLm;
                    }
                    catch { }
                }
                
                // Group processes by name to aggregate usage (matches Task Manager behavior)
                return GroupProcesses(_processCache.Values);
            }
            var gpuCounters = GetGpuCounters();
            
            int totalProcesses = 0;
            int criticalFiltered = 0;
            int companyFiltered = 0;
            int lowUsageFiltered = 0;
            double maxGpuFound = 0;
            
            OnLogMessage?.Invoke($"[DIAG] Starting process scan. ETW available: {_etwAvailable}, DevMode: {DevMode}");
            OnLogMessage?.Invoke($"[DIAG] GPU counters collected: {gpuCounters.Count} processes with GPU data");
            
            Process[] procList = Array.Empty<Process>();
            try
            {
                procList = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[WARN] Process enumeration failed: {ex.Message}");
                return _processCache.Values.OrderByDescending(p => p.GpuUsage).ToList();
            }
            
            foreach (var process in procList)
            {
                try
                {
                    totalProcesses++;
                    if (process.HasExited) continue;

                    if (IsCriticalProcess(process.ProcessName))
                    {
                        criticalFiltered++;
                        continue;
                    }
                    
                    if (LowImpactMode)
                    {
                        var cpuUsageLm = GetProcessCpuUsage(process);
                        var gpuUsageLm = GetProcessGpuUsage(process.Id, gpuCounters);
                        if (DevMode)
                        {
                            gpuUsageLm = Math.Min(100, Math.Max(gpuUsageLm, cpuUsageLm * 1.2));
                        }
                        if (cpuUsageLm <= CpuThresholdPercent && gpuUsageLm <= GpuDisplayThresholdPercent)
                        {
                            lowUsageFiltered++;
                            continue;
                        }
                        var processPathLm = GetProcessPath(process);
                        var companyLm = GetFileCompanyName(processPathLm);
                        if (ShouldIgnoreProcess(companyLm))
                        {
                            companyFiltered++;
                            continue;
                        }
                        var ramUsageLm = process.WorkingSet64 / (1024.0 * 1024.0);
                        var processDataLm = new ProcessData
                        {
                            ProcessName = process.ProcessName,
                            ProcessId = process.Id,
                            Path = processPathLm,
                            Company = companyLm,
                            CpuUsage = Math.Round(cpuUsageLm, 2),
                            GpuUsage = Math.Round(gpuUsageLm, 2),
                            RamUsageMB = Math.Round(ramUsageLm, 2),
                            WindowTitle = process.MainWindowTitle,
                            Status = process.Responding ? "Running" : "Not Responding",
                            IsResponding = process.Responding
                        };
                        processDataLm.CalculatePerformanceLoss();
                        processes.Add(processDataLm);
                        if (gpuUsageLm > maxGpuFound)
                            maxGpuFound = gpuUsageLm;
                        continue;
                    }
                    
                    var processPath = GetProcessPath(process);
                    var company = GetFileCompanyName(processPath);
                    if (ShouldIgnoreProcess(company))
                    {
                        companyFiltered++;
                        continue;
                    }
                    
                    var cpuUsage = GetProcessCpuUsage(process);
                    var ioUsage = GetProcessIoUsage(process);
                    var gpuUsage = GetProcessGpuUsage(process.Id, gpuCounters);
                    
                    if (gpuUsage > maxGpuFound)
                        maxGpuFound = gpuUsage;
                    
                    if (DevMode)
                    {
                        // In DevMode, simulate GPU usage using CPU as a proxy so alerts can be tested.
                        // Clamp to a realistic range.
                        gpuUsage = Math.Min(100, Math.Max(gpuUsage, cpuUsage * 1.2));
                    }
                    var ramUsage = process.WorkingSet64 / (1024.0 * 1024.0);
                    
                    // Only track processes with significant resource usage
                    // Check if we should display this process based on enabled monitors
                    bool showProcess = false;
                    if (MonitorCpu && cpuUsage > CpuDisplayThresholdPercent) showProcess = true;
                    if (MonitorGpu && gpuUsage > GpuDisplayThresholdPercent) showProcess = true;
                    if (MonitorNetwork && ioUsage > 0.1) showProcess = true;
                    if (MonitorRam && ramUsage > 100) showProcess = true; // Show > 100MB RAM
                    
                    if (showProcess)
                    {
                        var processData = new ProcessData
                        {
                            ProcessName = process.ProcessName,
                            ProcessId = process.Id,
                            Path = processPath,
                            Company = company,
                            CpuUsage = Math.Round(cpuUsage, 2),
                            GpuUsage = Math.Round(gpuUsage, 2),
                            RamUsageMB = Math.Round(ramUsage, 2),
                            NetworkUsageMB = ioUsage,
                            WindowTitle = process.MainWindowTitle,
                            Status = process.Responding ? "Running" : "Not Responding",
                            IsResponding = process.Responding
                        };
                        
                        processData.CalculatePerformanceLoss();
                        processes.Add(processData);
                    }
                    else
                    {
                        lowUsageFiltered++;
                        // Log first few filtered processes for debugging
                        if (lowUsageFiltered <= 3 && (cpuUsage > 1 || gpuUsage > 0))
                        {
                            OnLogMessage?.Invoke($"[DIAG] Filtered: {process.ProcessName} (CPU: {cpuUsage:F1}%, GPU: {gpuUsage:F1}%) - Below thresholds");
                        }
                    }
                }
                catch { /* Skip inaccessible processes */ }
            }
            
            OnLogMessage?.Invoke($"[DIAG] Scan complete: {totalProcesses} total, {processes.Count} shown, {criticalFiltered} critical filtered, {companyFiltered} company filtered, {lowUsageFiltered} low usage filtered");
            OnLogMessage?.Invoke($"[DIAG] Max GPU usage found: {maxGpuFound:F2}%");
            
            if (processes.Count == 0 && !_etwAvailable && !DevMode)
            {
                OnLogMessage?.Invoke("[WARN] No processes displayed. ETW unavailable and DevMode disabled.");
                OnLogMessage?.Invoke("[HINT] Enable DevMode to test with simulated GPU data, or run as Administrator for ETW.");
            }
            
            return GroupProcesses(processes);
        }

        private List<ProcessData> GroupProcesses(IEnumerable<ProcessData> flatList)
        {
            var grouped = new List<ProcessData>();
            
            // Group by ProcessName (case insensitive just in case)
            var groups = flatList.GroupBy(p => p.ProcessName.ToLowerInvariant());

            foreach (var g in groups)
            {
                // Skip if group is empty
                if (!g.Any()) continue;

                // Find the "Main" process to represent the group
                // Heuristics:
                // 1. Process with a Window Title (likely the UI)
                // 2. Process with highest Memory usage (often the main process for Electron apps)
                // 3. First process in list
                
                var mainProc = g.FirstOrDefault(p => !string.IsNullOrEmpty(p.WindowTitle)) 
                               ?? g.OrderByDescending(p => p.RamUsageMB).FirstOrDefault()
                               ?? g.First();

                var totalCpu = g.Sum(p => p.CpuUsage);
                var totalGpu = g.Sum(p => p.GpuUsage);
                var totalRam = g.Sum(p => p.RamUsageMB);
                var totalNet = g.Sum(p => p.NetworkUsageMB);
                
                // Create aggregated process data
                var aggregated = new ProcessData
                {
                    ProcessName = mainProc.ProcessName, // Use original casing from main process
                    ProcessId = mainProc.ProcessId,
                    Path = mainProc.Path,
                    Company = mainProc.Company,
                    WindowTitle = mainProc.WindowTitle,
                    Status = mainProc.Status,
                    IsResponding = mainProc.IsResponding,
                    
                    CpuUsage = Math.Round(totalCpu, 2),
                    GpuUsage = Math.Round(totalGpu, 2),
                    RamUsageMB = Math.Round(totalRam, 2),
                    NetworkUsageMB = Math.Round(totalNet, 2)
                };
                
                aggregated.CalculatePerformanceLoss();
                grouped.Add(aggregated);
            }
            
            return grouped.OrderByDescending(p => p.GpuUsage).ThenByDescending(p => p.CpuUsage).ToList();
        }

        /// <summary>
        /// Get current GPU processes for external use (e.g., Game Mode)
        /// </summary>
        public List<ProcessData> GetGpuProcesses()
        {
            if (IsMonitoring)
            {
                lock (_processCacheLock)
                {
                    return _processCache.Values.OrderByDescending(p => p.GpuUsage).ToList();
                }
            }
            // If monitoring is not running, perform a scan (which will lock internally)
            return ScanProcesses();
        }
        
        private void CheckGpuAlerts(List<ProcessData> processes)
        {
            foreach (var process in processes)
            {
                // Skip whitelisted processes
                if (WhitelistManager.IsWhitelisted(process.ProcessName))
                {
                    continue;
                }
                
                bool thresholdExceeded = false;
                string violationType = "";
                
                // Check GPU
                if (MonitorGpu && process.GpuUsage > GpuThresholdPercent)
                {
                    thresholdExceeded = true;
                    violationType = "GPU";
                }
                // Check CPU
                else if (MonitorCpu && process.CpuUsage > CpuThresholdPercent)
                {
                    thresholdExceeded = true;
                    violationType = "CPU";
                }
                // Check RAM
                else if (MonitorRam && process.RamUsageMB > RamThresholdMB)
                {
                    thresholdExceeded = true;
                    violationType = "RAM";
                }
                // Check Network
                else if (MonitorNetwork && process.NetworkUsageMB > NetworkThresholdMB)
                {
                    thresholdExceeded = true;
                    violationType = "Network";
                }
                
                if (!thresholdExceeded)
                {
                    // Reset tracker if usage dropped
                    if (_gpuTrackers.ContainsKey(process.ProcessId))
                    {
                        _gpuTrackers.Remove(process.ProcessId);
                    }
                    continue;
                }
                
                // Track usage duration
                if (!_gpuTrackers.TryGetValue(process.ProcessId, out var tracker))
                {
                    tracker = new GpuProcessTracker { ProcessId = process.ProcessId, StartTime = DateTime.Now };
                    _gpuTrackers[process.ProcessId] = tracker;
                }
                
                var duration = DateTime.Now - tracker.StartTime;
                process.GpuUsageDuration = duration.TotalMinutes; // Reusing property for generic duration

                var requiredSeconds = AlertDurationSeconds > 0 ? AlertDurationSeconds : 300;
                
                // Action if threshold exceeded for long enough
                if (duration.TotalSeconds >= requiredSeconds && !tracker.Notified)
                {
                    tracker.Notified = true;
                    
                    // Check Auto-Kill Logic
                    bool shouldKill = false;
                    
                    if (violationType == "GPU")
                    {
                        if (AutoKillGpuGlobal) shouldKill = true;
                        else if (IsGameModeActive && AutoKillGpuGameMode) shouldKill = true;
                    }
                    else if (violationType == "CPU")
                    {
                        if (AutoKillCpuGlobal) shouldKill = true;
                        else if (IsGameModeActive && AutoKillCpuGameMode) shouldKill = true;
                    }
                    else if (violationType == "RAM")
                    {
                        if (AutoKillRamGlobal) shouldKill = true;
                        else if (IsGameModeActive && AutoKillRamGameMode) shouldKill = true;
                    }
                    else if (violationType == "Network")
                    {
                        if (AutoKillNetworkGlobal) shouldKill = true;
                        else if (IsGameModeActive && AutoKillNetworkGameMode) shouldKill = true;
                    }
                    
                    if (shouldKill)
                    {
                        try
                        {
                            var procToKill = Process.GetProcessById(process.ProcessId);
                            procToKill.Kill();
                            OnLogMessage?.Invoke($"[AUTO-KILL] Killed {process.ProcessName} for high {violationType} usage.");
                            OnProcessKilled?.Invoke(process, violationType);
                            
                            // Show notification for kill
                            NotificationManager.ShowInfoNotification("Auto-Kill", $"{process.ProcessName} killed due to high {violationType}");
                        }
                        catch (Exception ex)
                        {
                            OnLogMessage?.Invoke($"[ERROR] Failed to auto-kill {process.ProcessName}: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Set violation reason for UI display
                        process.ViolationReason = violationType;

                        // Always notify UI (MainWindow) so it appears in AI Recommendations list
                        // This ensures users see recommendations even if they disabled toast alerts
                        OnGpuAlert?.Invoke(process);

                        // Check if we should send Windows Toast Notification
                        bool sendToast = false;
                        if (violationType == "GPU" && NotifyGpu) sendToast = true;
                        else if (violationType == "CPU" && NotifyCpu) sendToast = true;
                        else if (violationType == "RAM" && NotifyRam) sendToast = true;
                        else if (violationType == "Network" && NotifyNetwork) sendToast = true;
                        
                        if (sendToast)
                        {
                            SendGpuNotification(process, violationType);
                        }
                    }
                }
            }
            
            // Cleanup old trackers
            var oldTrackers = _gpuTrackers.Keys.Where(pid => !processes.Any(p => p.ProcessId == pid)).ToList();
            foreach (var pid in oldTrackers)
            {
                _gpuTrackers.Remove(pid);
            }
        }
        
        private void SendGpuNotification(ProcessData process, string violationType)
        {
            try
            {
                var message = "";
                if (violationType == "GPU")
                    message = $"⚠️ {process.ProcessName}: {process.GpuUsage}% GPU = ~{process.EstimatedFpsLoss} FPS lost";
                else if (violationType == "CPU")
                    message = $"⚠️ {process.ProcessName}: {process.CpuUsage}% CPU";
                else if (violationType == "RAM")
                    message = $"⚠️ {process.ProcessName}: {process.RamUsageMB:F0} MB RAM";
                else if (violationType == "Network")
                    message = $"⚠️ {process.ProcessName}: {process.NetworkUsageMB:F2} MB/s Network";
                
                OnLogMessage?.Invoke(message);
                
                // Show Windows notification (reuse existing method or update it)
                // For now, reuse ShowHighGpuNotification but ideally we should update it to support generic message
                // Or just rely on OnLogMessage if NotificationManager is specific to GPU.
                // But NotificationManager.ShowHighGpuNotification takes ProcessData, so maybe it only shows GPU info?
                // I should probably update NotificationManager too, but for now let's use the GPU one or a generic one.
                // I'll stick to OnGpuAlert which updates UI banner.
                
                // If violation is NOT GPU, we might want to skip ShowHighGpuNotification if it hardcodes "GPU".
                if (violationType == "GPU")
                    NotificationManager.ShowHighGpuNotification(process);
                else
                    // Fallback or new notification
                    NotificationManager.ShowHighResourceNotification(process, violationType); 
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"Notification error: {ex.Message}");
            }
        }

        private bool IsCriticalProcess(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return true;

            var name = processName.ToLowerInvariant();
            var critical = new[]
            {
                "system",
                "idle",
                "csrss",
                "winlogon",
                "services",
                "lsass",
                "smss",
                "explorer",
                "dwm",
                "svchost"
            };

            return critical.Contains(name);
        }
        
        private bool ShouldIgnoreProcess(string company)
        {
            if (string.IsNullOrEmpty(company)) return false;
            
            var ignoredCompanies = new[] { 
                "Microsoft", "NVIDIA", "AMD", "Intel",
                "Microsoft Corporation", "NVIDIA Corporation",
                "Advanced Micro Devices", "Intel Corporation",
                "Windows", "Intel(R) Corporation"
            };
            
            return ignoredCompanies.Any(c => 
                company.IndexOf(c, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        
        private void TryInitializePerformanceCounterFallback()
        {
            try
            {
                // Try to use Windows built-in GPU Performance Counters
                // These are available on Windows 10/11 with modern GPU drivers
                var gpuEngineCategory = PerformanceCounterCategory.GetCategories()
                    .FirstOrDefault(c => c.CategoryName.Contains("GPU") || c.CategoryName.Contains("GPU Engine"));
                
                if (gpuEngineCategory != null)
                {
                    OnLogMessage?.Invoke($"[DIAG] Found GPU counter category: {gpuEngineCategory.CategoryName}");
                    _usePerformanceCounterFallback = true;
                }
                else
                {
                    OnLogMessage?.Invoke("[WARN] No GPU PerformanceCounter category found.");
                    OnLogMessage?.Invoke("[HINT] GPU monitoring requires either ETW (admin) or GPU Performance Counters (drivers).");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[ERROR] PerformanceCounter fallback init failed: {ex.Message}");
            }
        }
        
        private Dictionary<int, double> GetGpuCounters(HashSet<int> targetPids = null)
        {
            if (DevMode)
                return new Dictionary<int, double>();

            return GetGpuCountersFromPerformanceCounters(targetPids);
        }
        
        private Dictionary<int, double> GetGpuCountersFromPerformanceCounters(HashSet<int> targetPids)
        {
            var result = new Dictionary<int, double>();
            
            try
            {
                // GPU Engine category provides per-process GPU utilization
                // Instance format: "pid_<PID>_luid_<LUID>_phys_<IDX>_eng_<ENG>_<TYPE>"
                if (PerformanceCounterCategory.Exists("GPU Engine"))
                {
                    var now = DateTime.Now;
                    // In Low Impact Mode, check for new instances every 15s to catch new games quickly
                    // The overhead of GetInstanceNames is acceptable compared to reading values
                    var refreshInterval = LowImpactMode ? 15 : GpuCountersRefreshIntervalSeconds;
                    
                    if (_lastGpuInstanceRefresh == DateTime.MinValue || (now - _lastGpuInstanceRefresh).TotalSeconds >= refreshInterval)
                    {
                        var category = new PerformanceCounterCategory("GPU Engine");
                        var instances = category.GetInstanceNames();
                        OnLogMessage?.Invoke($"[DIAG] Found {instances.Length} GPU Engine instances");
                        foreach (var instance in instances)
                        {
                            try
                            {
                                if (instance.StartsWith("pid_"))
                                {
                                    var parts = instance.Split('_');
                                    if (parts.Length >= 2 && int.TryParse(parts[1], out int pid))
                                    {
                                        if (!_gpuPerformanceCounters.ContainsKey(instance))
                                        {
                                            _gpuPerformanceCounters[instance] = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        _lastGpuInstanceRefresh = now;
                    }
                    
                    foreach (var kvp in _gpuPerformanceCounters.ToList())
                    {
                        try
                        {
                            var instance = kvp.Key;
                            var counter = kvp.Value;
                            
                            // Parse PID from instance name
                            if (instance.StartsWith("pid_"))
                            {
                                var parts = instance.Split('_');
                                if (parts.Length >= 2 && int.TryParse(parts[1], out int pid))
                                {
                                    if (targetPids != null && !targetPids.Contains(pid)) continue;
                                    var value = counter.NextValue();
                                    
                                    // Accumulate across all engines for this PID
                                    if (result.ContainsKey(pid))
                                        result[pid] += value;
                                    else
                                        result[pid] = value;
                                }
                            }
                        }
                        catch 
                        { 
                            // Remove problematic counters
                            _gpuPerformanceCounters.Remove(kvp.Key);
                        }
                    }
                    
                    if (result.Count > 0)
                    {
                        // Cap at 100% per process (multiple engines can exceed 100% combined)
                        foreach (var pid in result.Keys.ToList())
                        {
                            result[pid] = Math.Min(100, result[pid]);
                        }
                        
                        var nonZeroProcesses = result.Count(r => r.Value > 0.1);
                        var maxGpu = result.Values.DefaultIfEmpty(0).Max();
                        
                        OnLogMessage?.Invoke($"[DIAG] PerformanceCounter: {result.Count} processes with GPU data, {nonZeroProcesses} with >0.1% usage");
                        OnLogMessage?.Invoke($"[DIAG] Max GPU usage found: {maxGpu:F2}%");
                    }
                    else
                    {
                        // Only warn if we expected a full scan (targetPids == null) or if we specifically looked for active processes and found nothing
                        if (targetPids == null)
                            OnLogMessage?.Invoke("[WARN] GPU Engine counters exist but returned no data");
                        else if (targetPids.Count > 0)
                             OnLogMessage?.Invoke($"[DIAG] No GPU usage detected in batch of {targetPids.Count} processes");
                    }
                }
                else
                {
                    OnLogMessage?.Invoke("[WARN] GPU Engine PerformanceCounter category not found");
                    OnLogMessage?.Invoke("[HINT] GPU monitoring requires GPU Performance Counters (install GPU drivers)");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage?.Invoke($"[ERROR] PerformanceCounter read failed: {ex.Message}");
            }
            
            return result;
        }
        
        private double GetProcessGpuUsage(int processId, Dictionary<int, double> gpuCounters)
        {
            return gpuCounters.ContainsKey(processId) ? gpuCounters[processId] : 0;
        }
        
        private double GetProcessCpuUsage(Process process)
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var currentTotalProcessorTime = process.TotalProcessorTime;
                
                if (_cpuUsageCache.TryGetValue(process.Id, out var previousCache))
                {
                    var timeDelta = (currentTime - previousCache.Time).TotalMilliseconds;
                    var cpuDelta = (currentTotalProcessorTime - previousCache.TotalProcessorTime).TotalMilliseconds;
                    
                    // Update cache
                    _cpuUsageCache[process.Id] = (currentTotalProcessorTime, currentTime);
                    
                    if (timeDelta > 0)
                    {
                        var usage = (cpuDelta / timeDelta) / Environment.ProcessorCount * 100;
                        return Math.Max(0, usage);
                    }
                    return 0;
                }
                else
                {
                    _cpuUsageCache[process.Id] = (currentTotalProcessorTime, currentTime);
                    return 0;
                }
            }
            catch { return 0; }
        }

        private double GetProcessIoUsage(Process process)
        {
            try
            {
                if (!_ioCounters.ContainsKey(process.Id))
                {
                    var counter = new PerformanceCounter("Process", "IO Data Bytes/sec", process.ProcessName, true);
                    _ioCounters[process.Id] = counter;
                }
                
                var existingCounter = _ioCounters[process.Id];
                var bytesPerSec = existingCounter.NextValue();
                
                // Convert to MB/s
                return Math.Round(bytesPerSec / (1024.0 * 1024.0), 2);
            }
            catch { return 0; }
        }
        
        private string GetProcessPath(Process process)
        {
            try { return process.MainModule?.FileName ?? ""; }
            catch { return ""; }
        }
        
        private string GetFileCompanyName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                    return "";
                
                var fileVersionInfo = FileVersionInfo.GetVersionInfo(filePath);
                return fileVersionInfo.CompanyName ?? "";
            }
            catch { return ""; }
        }
        
        public void Dispose()
        {
            StopMonitoring();
            
            foreach (var counter in _ioCounters.Values)
            {
                counter?.Dispose();
            }
            _ioCounters.Clear();
            
            foreach (var counter in _gpuPerformanceCounters.Values)
            {
                counter?.Dispose();
            }
            _gpuPerformanceCounters.Clear();
        }
        
        private class GpuProcessTracker
        {
            public int ProcessId { get; set; }
            public DateTime StartTime { get; set; }
            public bool Notified { get; set; }
        }
    }
}
