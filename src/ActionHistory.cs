using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Types of actions that can be recorded
    /// </summary>
    public enum ActionType
    {
        AutoKill,           // Game Mode auto-killed a process
        ManualKill,         // User manually killed a process
        WhitelistAdd,       // User whitelisted a process/service/task/autorun
        ServiceDisabled,    // Service was disabled
        ServiceManual,      // Service startup changed to manual
        TaskDisabled,       // Task was disabled
        AutorunDisabled,    // Autorun was disabled
        Snooze              // Notification was snoozed
    }

    /// <summary>
    /// Single action entry in history
    /// </summary>
    public class ActionHistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public ActionType Action { get; set; }
        public string TargetType { get; set; } // Process, Service, Task, Autorun
        public string TargetName { get; set; }
        public string TargetPath { get; set; }
        public int? ProcessId { get; set; }
        public double? GpuUsage { get; set; } // If applicable
        public string Reason { get; set; } // Why the action was taken
        public string Source { get; set; } // "GameMode", "User", "AI", etc.

        // Formatted display properties
        public string TimeAgo => GetTimeAgo(Timestamp);
        public string ActionIcon => GetActionIcon(Action);
        public string ActionDescription => GetActionDescription(Action, TargetName, Source);

        private string GetTimeAgo(DateTime time)
        {
            var span = DateTime.Now - time;
            if (span.TotalMinutes < 1) return "just now";
            if (span.TotalHours < 1) return $"{span.Minutes}m ago";
            if (span.TotalDays < 1) return $"{span.Hours}h ago";
            return $"{span.Days}d ago";
        }

        private string GetActionIcon(ActionType action)
        {
            return action switch
            {
                ActionType.AutoKill => "🤖",
                ActionType.ManualKill => "🔴",
                ActionType.WhitelistAdd => "🛡️",
                ActionType.ServiceDisabled => "⛔",
                ActionType.ServiceManual => "⚙️",
                ActionType.TaskDisabled => "📅",
                ActionType.AutorunDisabled => "🚀",
                ActionType.Snooze => "😴",
                _ => "⚡"
            };
        }

        private string GetActionDescription(ActionType action, string targetName, string source)
        {
            var actionText = action switch
            {
                ActionType.AutoKill => "Auto-killed",
                ActionType.ManualKill => "Killed",
                ActionType.WhitelistAdd => "Whitelisted",
                ActionType.ServiceDisabled => "Disabled service",
                ActionType.ServiceManual => "Set service to manual",
                ActionType.TaskDisabled => "Disabled task",
                ActionType.AutorunDisabled => "Disabled autorun",
                ActionType.Snooze => "Snoozed",
                _ => "Action"
            };

            var sourceText = string.IsNullOrEmpty(source) ? "" : $" ({source})";
            return $"{actionText} {targetName}{sourceText}";
        }
    }

    /// <summary>
    /// Manager for action history
    /// </summary>
    public static class ActionHistory
    {
        private static readonly string HistoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOptimizer.AI",
            "action_history.json");

        private static ObservableCollection<ActionHistoryEntry> _entries = new();
        private static readonly int MaxEntries = 1000;

        // Event fired when a new entry is added
        public static event Action<ActionHistoryEntry> OnEntryAdded;

        static ActionHistory()
        {
            Load();
        }

        public static ObservableCollection<ActionHistoryEntry> Entries => _entries;

        public static void Record(ActionType action, string targetType, string targetName,
            string targetPath = "", int? processId = null, double? gpuUsage = null,
            string reason = "", string source = "")
        {
            var entry = new ActionHistoryEntry
            {
                Action = action,
                TargetType = targetType,
                TargetName = targetName,
                TargetPath = targetPath,
                ProcessId = processId,
                GpuUsage = gpuUsage,
                Reason = reason,
                Source = source
            };

            _entries.Insert(0, entry); // Add to top (newest first)

            // Keep only last MaxEntries
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(_entries.Count - 1);
            }

            Save();
            
            // Notify subscribers
            OnEntryAdded?.Invoke(entry);
        }

        public static void Clear()
        {
            _entries.Clear();
            Save();
        }

        public static List<ActionHistoryEntry> GetFiltered(string actionType = null, string search = null)
        {
            var filtered = _entries.AsEnumerable();

            if (!string.IsNullOrEmpty(actionType) && actionType != "All")
            {
                filtered = filtered.Where(e => e.TargetType.Equals(actionType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(search))
            {
                var searchLower = search.ToLower();
                filtered = filtered.Where(e =>
                    e.TargetName?.ToLower().Contains(searchLower) == true ||
                    e.Reason?.ToLower().Contains(searchLower) == true ||
                    e.Source?.ToLower().Contains(searchLower) == true);
            }

            return filtered.ToList();
        }

        public static string ExportToJson()
        {
            return JsonConvert.SerializeObject(_entries, Formatting.Indented);
        }

        private static void Load()
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (File.Exists(HistoryPath))
                {
                    var json = File.ReadAllText(HistoryPath);
                    var loaded = JsonConvert.DeserializeObject<List<ActionHistoryEntry>>(json);
                    if (loaded != null)
                    {
                        _entries = new ObservableCollection<ActionHistoryEntry>(loaded.OrderByDescending(e => e.Timestamp));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load action history: {ex.Message}");
            }
        }

        private static void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(HistoryPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonConvert.SerializeObject(_entries, Formatting.Indented);
                File.WriteAllText(HistoryPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save action history: {ex.Message}");
            }
        }
    }
}
