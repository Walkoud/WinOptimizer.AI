# System Resource Monitoring Documentation (GPU, CPU, RAM, Network)

## Overview

The monitoring system tracks process resource usage in real-time and alerts when apps consume excessive resources that could impact gaming performance. Originally focused on GPU, it now covers CPU, RAM, and Network I/O.

## Multi-Resource Monitoring

### Architecture

```
GpuMonitor (Main Controller)
├── StartMonitoring()
│   └── Initializes PerformanceCounters for all resources
├── MonitoringLoop()
│   ├── ScanProcesses() every 10 seconds
│   ├── Collects Data:
│   │   ├── GPU: "GPU Engine" PerformanceCounters
│   │   ├── CPU: "Process\% Processor Time"
│   │   ├── RAM: "Process\Working Set"
│   │   └── Network: "Process\IO Data Bytes/sec"
│   └── CheckGpuAlerts() (renamed to CheckResourceAlerts internally)
│       ├── Checks enabled monitors (MonitorCpu, MonitorGpu, etc.)
│       ├── Checks thresholds
│       ├── Determines action (Notify vs Auto-Kill)
│       └── Sends notification if needed
└── Events
    ├── OnProcessesUpdated → Updates UI grid
    ├── OnGpuAlert → Triggers Windows notification
    └── OnProcessKilled → Logs kill action
```

## Configuration Parameters

```csharp
// In GpuMonitor.cs
public bool MonitorGpu { get; set; } = true;
public bool MonitorCpu { get; set; } = true;
public bool MonitorRam { get; set; } = true;
public bool MonitorNetwork { get; set; } = true;

public double GpuThresholdPercent { get; set; } = 5.0;
public double CpuThresholdPercent { get; set; } = 50.0;
public double RamThresholdMB { get; set; } = 2048.0;
public double NetworkThresholdMB { get; set; } = 10.0;

// Auto-Kill Logic
public bool AutoKillGpuGameMode { get; set; } // Only in Game Mode
public bool AutoKillGpuGlobal { get; set; }   // Always active
// ... same for CPU, RAM, Network
```

### Alert & Auto-Kill Logic

1. **Scan**: Every `CheckIntervalSeconds`, processes are scanned.
2. **Threshold Check**: If a process exceeds the configured limit for a monitored resource.
3. **Duration Check**: Must persist for `AlertDurationSeconds`.
4. **Action Determination**:
   - If `AutoKill[Resource]Global` is TRUE -> **KILL**.
   - If `IsGameModeActive` AND `AutoKill[Resource]GameMode` is TRUE -> **KILL**.
   - Otherwise, if `Notify[Resource]` is TRUE -> **NOTIFY**.

## Why ETW Was Replaced (Legacy GPU Info)

**The ETW Problem:**

ETW provider `Microsoft-Windows-DxgKrnl` was initially used, but analysis of the events revealed a critical issue:

```
[DIAG-ETW] Event with PID=16544 but no utilization. Payloads: NewUsage, OldUsage, pDxgAdapter, ProcessId...
[DIAG-ETW] Event with PID=16544 but no utilization. Payloads: Commitment, OldCommitment, pDxgAdapter, ProcessId...
```

**ETW provides GPU memory allocation events, NOT GPU utilization percentage.**

The events captured were:
- `NewUsage`, `OldUsage` - GPU memory sizes
- `Commitment`, `OldCommitment` - Memory commitments

**No utilization percentage field exists** in standard DxgKrnl events.

## How It Works Now (PerformanceCounters)

### Architecture

```
GpuMonitor
├── StartMonitoring()
│   └── Initializes PerformanceCounter reader
├── MonitoringLoop()
│   ├── ScanProcesses() every 10 seconds
│   ├── GetGpuCounters() from "GPU Engine" PerformanceCounters
│   │   ├── Instance name: "pid_<PID>_luid_<LUID>_phys_<IDX>_eng_<ENG>_<TYPE>"
│   │   ├── Counter: "Utilization Percentage"
│   │   └── Accumulate across all engines per PID
│   ├── GetProcessCpuUsage() from "Process\% Processor Time"
│   └── CheckGpuAlerts()
│       └── Tracks duration above threshold
│       └── Sends notification after duration exceeded
└── Events
    ├── OnProcessesUpdated → Updates UI grid
    ├── OnGpuAlert → Triggers Windows notification
    └── OnLogMessage → Logs to console
```

### GPU Engine PerformanceCounters

Windows provides per-process GPU counters via the **"GPU Engine"** category:

| Counter | Description |
|---------|-------------|
| `Utilization Percentage` | GPU engine utilization % |
| `Running Time` | Total running time |

**Instance format:** `pid_<PID>_luid_<LUID>_phys_<IDX>_eng_<ENG>_<TYPE>`

Example: `pid_1234_luid_0x00000000_0x0000D507_phys_0_eng_0_3D`
- PID: 1234
- Engine: 3D (rendering)
- Physical adapter: 0

Multiple engines per process are accumulated and capped at 100%.

## Configuration Parameters

```csharp
// In GpuMonitor.cs
public double GpuThresholdPercent { get; set; } = 5.0;      // Min GPU % to trigger alert
public double CpuThresholdPercent { get; set; } = 10.0;      // Min CPU % to appear in list  
public double GpuDisplayThresholdPercent { get; set; } = 0.1; // Show processes with >0.1% GPU
public int CheckIntervalSeconds { get; set; } = 10;         // Scan frequency
public int AlertDurationSeconds { get; set; } = 300;       // Seconds above threshold before alert
```

### 3. Alert System

```csharp
// Alert logic flow:
1. Scan all processes every 10 seconds (CheckIntervalSeconds)
2. Track processes with GPU > GpuThresholdPercent
3. When duration exceeds AlertDurationSeconds (5 min default)
4. Send notification: "⚠️ ProcessName: X% GPU = ~Y FPS lost"
```

### 4. Process Filtering

**Ignored Processes (Critical System):**
- system, idle, csrss, winlogon, services, lsass, smss, explorer, dwm, svchost

**Ignored Companies:**
- Microsoft, NVIDIA, AMD, Intel

## UI Controls

### Status Indicator

A visual indicator in the header shows the current GPU monitoring state:

| Icon | Status | Meaning |
|------|--------|---------|
| 🟢 | **GPU: Active (ETW)** | ETW provider working, real GPU monitoring active |
| 🔴 | **GPU: DevMode** | DevMode enabled, GPU simulated from CPU |
| ⚠️ | **GPU: Limited (No ETW)** | ETW unavailable, GPU will show 0% |

**Troubleshooting the Status:**
- If you see **⚠️ Limited (No ETW)**, check:
  - Run as Administrator
  - GPU drivers are up-to-date
  - Try DevMode to verify the UI works

### LblGpuThreshold (TextBox)
Sets the GPU usage threshold for alerts (default: 5%).

```csharp
if (double.TryParse(LblGpuThreshold.Text, out var threshold))
{
    _gpuMonitor.GpuThresholdPercent = threshold;
}
```

### GpuDisplayThreshold (New Property)
Separate threshold for **displaying** processes vs **alerting** on them.

```csharp
public double GpuDisplayThresholdPercent { get; set; } = 0.1;  // Show processes with >0.1% GPU
public double GpuThresholdPercent { get; set; } = 5.0;         // Alert only on >5% GPU
```

**Why two thresholds?**
- **Display threshold (0.1%)**: Shows all processes using any GPU, making the list feel "alive"
- **Alert threshold (5%)**: Only notifies on processes that significantly impact gaming

### TxtAlertDuration (TextBox)  
Sets how long a process must exceed the threshold before alerting (default: 300 seconds = 5 minutes).

```csharp
if (int.TryParse(TxtAlertDuration.Text, out var duration))
{
    _gpuMonitor.AlertDurationSeconds = duration;
}
```

### ChkDevMode (CheckBox)
Toggles DevMode which simulates GPU usage from CPU data for testing.

```csharp
_gpuMonitor.DevMode = ChkDevMode.IsChecked == true;
```

## Troubleshooting

### Issue: No processes with GPU usage appear

**Check 1:** Verify GPU PerformanceCounters are available
```powershell
# Run in PowerShell as admin
Get-Counter -ListSet "GPU Engine" | Select-Object -ExpandProperty CounterSetName
```

If this returns nothing, your GPU drivers don't expose PerformanceCounters.

**Check 2:** Check if processes are using GPU
```
1. Open Task Manager → Performance tab → GPU
2. Start a game or GPU-intensive app
3. Check if GPU usage shows in Task Manager
4. If Task Manager shows GPU%, PerformanceCounters should work
```

**Check 3:** Run app as Administrator
PerformanceCounters for GPU often require admin privileges to read all processes.

**Check 4:** Enable DevMode for testing
- Check "Dev Mode" checkbox
- Click "Start Monitoring"
- If processes appear → GPU PerformanceCounters are the issue

### Issue: GPU alerts not firing

**Requirements for alert:**
1. Process GPU usage > GpuThresholdPercent (default 5%)
2. Duration > AlertDurationSeconds (default 300 seconds)
3. Process not from Microsoft/NVIDIA/AMD/Intel
4. Process not in critical list (explorer, svchost, etc.)

**Quick test:**
```
1. Set GPU Threshold to 1%
2. Set Duration to 10 seconds  
3. Enable DevMode
4. Run a CPU-intensive app
5. Wait 10 seconds → Alert should fire
```

## Performance Impact

- **Scan interval**: Every 10 seconds
- **CPU overhead**: Minimal (<1% on modern systems)
- **Memory**: Tracks only processes above thresholds
- **ETW overhead**: Low, uses Windows built-in counters

## Code Architecture

```
GpuMonitor
├── StartMonitoring()
│   └── Initializes ETW provider (unless DevMode)
├── MonitoringLoop()
│   ├── ScanProcesses() every 10 seconds
│   ├── GetGpuCounters() from ETW
│   ├── GetProcessCpuUsage() from PerformanceCounters
│   └── CheckGpuAlerts()
│       └── Tracks duration above threshold
│       └── Sends notification after duration exceeded
└── Events
    ├── OnProcessesUpdated → Updates UI grid
    ├── OnGpuAlert → Triggers Windows notification
    └── OnLogMessage → Logs to console
```

## Future Improvements

1. **Fallback GPU detection** when ETW unavailable:
   - WMI query for GPU engine usage
   - NVIDIA/AMD SDK integration
   - DXGI frame statistics

2. **Configurable filtering**:
   - User-defined ignore list
   - Per-process whitelist
   - Game-specific profiles

3. **Alert customization**:
   - Custom alert sounds
   - Different thresholds per game
   - Discord/webhook integration
