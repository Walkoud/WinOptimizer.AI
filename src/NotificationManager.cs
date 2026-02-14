using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Windows.ApplicationModel.Activation;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Manages interactive Windows notifications with Kill/Whitelist/Snooze actions
    /// </summary>
    public static class NotificationManager
    {
        private const string APP_ID = "WinOptimizer.AI";

        // Events to notify MainWindow of actions from notifications
        public static event Action<string, int?> OnProcessKilledFromNotification;
        public static event Action<string, string> OnProcessWhitelistedFromNotification;
        public static event Action<string> OnProcessSnoozedFromNotification;

        public static void Initialize()
        {
            // Register for protocol activation (for notification button clicks)
            ToastNotificationManagerCompat.OnActivated += OnToastActivated;
        }

        private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
        {
            var args = ToastArguments.Parse(e.Argument);
            var action = args.Get("action");
            
            Application.Current?.Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "kill":
                        var pid = args.Get("processId");
                        var processName = args.Get("processName");
                        if (!string.IsNullOrEmpty(pid) && int.TryParse(pid, out int processId))
                        {
                            KillProcess(processId, processName);
                            // Record to history
                            ActionHistory.Record(ActionType.ManualKill, "Process", processName, 
                                args.Get("path") ?? "", processId, reason: "Killed from notification", source: "Notification");
                            // Notify UI
                            OnProcessKilledFromNotification?.Invoke(processName, processId);
                        }
                        break;

                    case "whitelist":
                        var targetName = args.Get("processName");
                        var path = args.Get("path");
                        WhitelistManager.Add("Process", targetName, path);
                        // Record to history
                        ActionHistory.Record(ActionType.WhitelistAdd, "Process", targetName, 
                            path ?? "", reason: "Whitelisted from notification", source: "Notification");
                        // Notify UI
                        OnProcessWhitelistedFromNotification?.Invoke(targetName, path ?? "");
                        ShowInfoNotification("Whitelisted", $"{targetName} added to whitelist");
                        break;

                    case "snooze":
                        var snoozeProcess = args.Get("processName");
                        // Add to snooze list for this session
                        GpuAlertSnoozer.Snooze(snoozeProcess);
                        // Record to history
                        ActionHistory.Record(ActionType.Snooze, "Process", snoozeProcess, 
                            reason: "Snoozed from notification", source: "Notification");
                        // Notify UI
                        OnProcessSnoozedFromNotification?.Invoke(snoozeProcess);
                        ShowInfoNotification("Snoozed", $"Alerts for {snoozeProcess} snoozed for this session");
                        break;

                    case "open_app":
                    case "gpu_alert":
                    case "resource_alert":
                        BringAppToFront();
                        break;
                }
            });
        }

        /// <summary>
        /// Show interactive notification when a process is using high GPU
        /// </summary>
        public static void ShowHighGpuNotification(ProcessData process)
        {
            if (!IsNotificationsEnabled()) return;
            if (GpuAlertSnoozer.IsSnoozed(process.ProcessName)) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddArgument("action", "gpu_alert")
                    .AddArgument("processId", process.ProcessId.ToString())
                    .AddArgument("processName", process.ProcessName)
                    .AddArgument("path", process.Path ?? "")
                    .AddText($"⚠️ High GPU: {process.ProcessName}")
                    .AddText($"Using {process.GpuUsage:F1}% GPU (~{process.EstimatedFpsLoss} FPS lost)");

                // Add action buttons
                if (AppSettings.Instance.ShowKillButtonInNotification)
                {
                    builder.AddButton(new ToastButton()
                        .SetContent("🔴 Kill")
                        .AddArgument("action", "kill")
                        .AddArgument("processId", process.ProcessId.ToString())
                        .AddArgument("processName", process.ProcessName)
                        .SetBackgroundActivation());
                }

                if (AppSettings.Instance.ShowWhitelistButtonInNotification)
                {
                    builder.AddButton(new ToastButton()
                        .SetContent("✅ Whitelist")
                        .AddArgument("action", "whitelist")
                        .AddArgument("processName", process.ProcessName)
                        .AddArgument("path", process.Path ?? "")
                        .SetBackgroundActivation());
                }

                builder.AddButton(new ToastButton()
                    .SetContent("😴 Snooze")
                    .AddArgument("action", "snooze")
                    .AddArgument("processName", process.ProcessName)
                    .SetBackgroundActivation());

                builder.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show interactive notification when a process is using high resources (CPU, RAM, Network)
        /// </summary>
        public static void ShowHighResourceNotification(ProcessData process, string violationType)
        {
            if (!IsNotificationsEnabled()) return;
            if (GpuAlertSnoozer.IsSnoozed(process.ProcessName)) return;

            try
            {
                string usageText = "";
                string icon = "⚠️";
                
                switch (violationType)
                {
                    case "CPU":
                        usageText = $"Using {process.CpuUsage:F1}% CPU";
                        break;
                    case "RAM":
                        usageText = $"Using {process.RamUsageMB:F0} MB RAM";
                        break;
                    case "Network":
                        usageText = $"Using {process.NetworkUsageMB:F2} MB/s Network";
                        break;
                    default:
                        usageText = $"High {violationType} usage detected";
                        break;
                }

                var builder = new ToastContentBuilder()
                    .AddArgument("action", "resource_alert")
                    .AddArgument("violationType", violationType)
                    .AddArgument("processId", process.ProcessId.ToString())
                    .AddArgument("processName", process.ProcessName)
                    .AddArgument("path", process.Path ?? "")
                    .AddText($"{icon} High {violationType}: {process.ProcessName}")
                    .AddText(usageText);

                // Add action buttons (similar to GPU)
                if (AppSettings.Instance.ShowKillButtonInNotification)
                {
                    builder.AddButton(new ToastButton()
                        .SetContent("🔴 Kill")
                        .AddArgument("action", "kill")
                        .AddArgument("processId", process.ProcessId.ToString())
                        .AddArgument("processName", process.ProcessName)
                        .SetBackgroundActivation());
                }

                if (AppSettings.Instance.ShowWhitelistButtonInNotification)
                {
                    builder.AddButton(new ToastButton()
                        .SetContent("✅ Whitelist")
                        .AddArgument("action", "whitelist")
                        .AddArgument("processName", process.ProcessName)
                        .AddArgument("path", process.Path ?? "")
                        .SetBackgroundActivation());
                }

                builder.AddButton(new ToastButton()
                    .SetContent("😴 Snooze")
                    .AddArgument("action", "snooze")
                    .AddArgument("processName", process.ProcessName)
                    .SetBackgroundActivation());

                builder.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show notification for AI recommendation
        /// </summary>
        public static void ShowAiRecommendationNotification(string targetName, string action, string reason, string path = "")
        {
            if (!IsNotificationsEnabled()) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddArgument("action", "ai_recommendation")
                    .AddArgument("target", targetName)
                    .AddText($"🤖 AI: {targetName}")
                    .AddText($"Action: {action}")
                    .AddText(reason.Length > 60 ? reason.Substring(0, 60) + "..." : reason)
                    .AddButton(new ToastButton()
                        .SetContent("📂 Open WinOptimizer")
                        .AddArgument("action", "open_app")
                        .SetBackgroundActivation());

                builder.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show notification for service recommendation
        /// </summary>
        public static void ShowServiceRecommendationNotification(ServiceData service)
        {
            if (!IsNotificationsEnabled()) return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddArgument("action", "service_recommendation")
                    .AddArgument("serviceName", service.ServiceName)
                    .AddText($"🔧 Service: {service.DisplayName}")
                    .AddText($"Recommended: {service.UserSelectedStartup}")
                    .AddText(service.AiReason?.Length > 60 ? service.AiReason.Substring(0, 60) + "..." : service.AiReason)
                    .AddButton(new ToastButton()
                        .SetContent("📂 Open WinOptimizer")
                        .AddArgument("action", "open_app")
                        .SetBackgroundActivation());

                builder.Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Show a simple info notification
        /// </summary>
        public static void ShowInfoNotification(string title, string message)
        {
            if (!IsNotificationsEnabled()) return;

            try
            {
                new ToastContentBuilder()
                    .AddText(title)
                    .AddText(message)
                    .Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show notification: {ex.Message}");
            }
        }

        private static void KillProcess(int processId, string processName)
        {
            try
            {
                // Use taskkill to forcefully terminate the entire process tree by name
                // This ensures we kill the main application (e.g. Chrome, Brave) even if the alert was for a child process
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /IM \"{processName}.exe\" /T",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    proc?.WaitForExit(5000);
                    if (proc != null && proc.ExitCode != 0)
                    {
                        // Throw to trigger fallback
                        throw new Exception($"Taskkill exited with code {proc.ExitCode}");
                    }
                }

                ShowInfoNotification("Process Killed", $"{processName} and all related processes have been terminated");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to kill process tree {processName}: {ex.Message}");
                
                // Fallback: Try to kill by PID if taskkill failed
                try
                {
                    var process = Process.GetProcessById(processId);
                    process.Kill(true); // .NET Core/5+ tree kill
                    ShowInfoNotification("Process Killed", $"{processName} (PID {processId}) terminated");
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"Fallback kill failed: {fallbackEx.Message}");
                    ShowInfoNotification("Error", $"Could not kill {processName}");
                }
            }
        }

        private static void BringAppToFront()
        {
            if (Application.Current?.MainWindow != null)
            {
                Application.Current.MainWindow.Activate();
                Application.Current.MainWindow.WindowState = WindowState.Normal;
                Application.Current.MainWindow.Topmost = true;
                Application.Current.MainWindow.Topmost = false;
            }
        }

        private static bool IsNotificationsEnabled()
        {
            try
            {
                return AppSettings.Instance.EnableWindowsNotifications;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// Helper class to manage snoozed processes for current session
    /// </summary>
    public static class GpuAlertSnoozer
    {
        private static readonly System.Collections.Generic.HashSet<string> _snoozedProcesses = new();

        public static void Snooze(string processName)
        {
            if (!string.IsNullOrEmpty(processName))
                _snoozedProcesses.Add(processName.ToLowerInvariant());
        }

        public static bool IsSnoozed(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            return _snoozedProcesses.Contains(processName.ToLowerInvariant());
        }

        public static void ClearSnooze()
        {
            _snoozedProcesses.Clear();
        }
    }
}
