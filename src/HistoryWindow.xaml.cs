using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json.Linq;

namespace WinOptimizer.AI
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
            ApplyLanguage();
            LoadHistory();
            
            // Subscribe to new entries to auto-refresh
            ActionHistory.OnEntryAdded += OnHistoryEntryAdded;
        }

        private void OnHistoryEntryAdded(ActionHistoryEntry entry)
        {
            // Refresh UI when new entry is added from notification or other sources
            Dispatcher.Invoke(() =>
            {
                ApplyFilters();
                UpdateStats();
            });
        }

        private void LoadHistory()
        {
            ApplyFilters();
            UpdateStats();
        }

        private void UpdateStats()
        {
            var entries = ActionHistory.Entries;
            AutoKillCount.Text = $"🤖 AutoKills: {entries.Count(e => e.Action == ActionType.AutoKill)}";
            ManualKillCount.Text = $"🔴 Manual Kills: {entries.Count(e => e.Action == ActionType.ManualKill)}";
            WhitelistCount.Text = $"🛡️ Whitelists: {entries.Count(e => e.Action == ActionType.WhitelistAdd)}";
            SnoozeCount.Text = $"😴 Snoozes: {entries.Count(e => e.Action == ActionType.Snooze)}";
        }

        private void ApplyFilters()
        {
            // Guard against calls during InitializeComponent when UI isn't ready
            if (HistoryDataGrid == null || CountText == null || TypeFilterComboBox == null || SearchTextBox == null)
                return;

            var entries = ActionHistory.Entries;
            if (entries == null) return;

            var filtered = entries.AsEnumerable();

            // Filter by action type
            if (TypeFilterComboBox.SelectedItem is ComboBoxItem item)
            {
                var selected = (item.Tag?.ToString() ?? "all").ToLowerInvariant();
                filtered = selected switch
                {
                    "autokill" => filtered.Where(e => e.Action == ActionType.AutoKill),
                    "manualkill" => filtered.Where(e => e.Action == ActionType.ManualKill),
                    "whitelist" => filtered.Where(e => e.Action == ActionType.WhitelistAdd),
                    "service" => filtered.Where(e => e.TargetType == "Service"),
                    "task" => filtered.Where(e => e.TargetType == "Task"),
                    "autorun" => filtered.Where(e => e.TargetType == "Autorun"),
                    "snooze" => filtered.Where(e => e.Action == ActionType.Snooze),
                    _ => filtered
                };
            }

            // Filter by search
            var searchText = SearchTextBox.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(e =>
                    e.TargetName?.ToLower().Contains(searchText) == true ||
                    e.Reason?.ToLower().Contains(searchText) == true);
            }

            HistoryDataGrid.ItemsSource = filtered.ToList();
            CountText.Text = $"({filtered.Count()} / {entries.Count} actions)";
        }

        private void TypeFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            ApplyFilters();
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ActionHistoryEntry entry)
            {
                if (!string.IsNullOrEmpty(entry.TargetPath) && File.Exists(entry.TargetPath))
                {
                    try
                    {
                        Process.Start("explorer.exe", $"/select,\"{entry.TargetPath}\"");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Path not available or file does not exist.", "Cannot Open Folder", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnWhitelistHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ActionHistoryEntry entry)
            {
                if (MessageBox.Show($"Add '{entry.TargetName}' to Whitelist?", "Whitelist", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    WhitelistManager.Add(entry.TargetType, entry.TargetName, entry.TargetPath);
                    MessageBox.Show($"Added '{entry.TargetName}' to Whitelist.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnKillAgain_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ActionHistoryEntry entry)
            {
                try
                {
                    // Try to find process by name
                    var processes = Process.GetProcessesByName(entry.TargetName);
                    if (processes.Length > 0)
                    {
                        foreach (var proc in processes)
                        {
                            proc.Kill();
                        }
                        ActionHistory.Record(ActionType.ManualKill, entry.TargetType, entry.TargetName,
                            entry.TargetPath, reason: "Killed again from history", source: "History");
                        LoadHistory();
                        MessageBox.Show($"Killed {processes.Length} process(es) named '{entry.TargetName}'", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"No running process found with name '{entry.TargetName}'", "Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to kill: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnRestoreFromHistory_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.DataContext is not ActionHistoryEntry entry)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(entry.PreviousStateJson))
            {
                MessageBox.Show("No previous state stored for this entry.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                switch (entry.TargetType)
                {
                    case "Service":
                        RestoreServiceFromHistory(entry);
                        break;
                    case "Task":
                        RestoreTaskFromHistory(entry);
                        break;
                    case "Autorun":
                        RestoreAutorunFromHistory(entry);
                        break;
                    default:
                        MessageBox.Show("Restore is available only for Service/Task/Autorun entries.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                }

                ActionHistory.Record(ActionType.StateRestored, entry.TargetType, entry.TargetName, entry.TargetPath,
                    reason: "Restored from history", source: "History",
                    previousStateJson: entry.AppliedStateJson, appliedStateJson: entry.PreviousStateJson);

                LoadHistory();
                MessageBox.Show($"State restored for '{entry.TargetName}'.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Restore failed: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void RestoreServiceFromHistory(ActionHistoryEntry entry)
        {
            var state = JObject.Parse(entry.PreviousStateJson);
            var startup = state["Startup"]?.ToString() ?? "Manual";
            var status = state["State"]?.ToString() ?? "Stopped";

            var backupEntry = new ServiceBackupEntry
            {
                ServiceName = entry.TargetName,
                StartupType = startup.ToLowerInvariant() switch
                {
                    "automatic" => 2,
                    "delayed" => 2,
                    "disabled" => 4,
                    _ => 3
                },
                IsRunning = status.Equals("Running", StringComparison.OrdinalIgnoreCase)
            };

            OptimizationBackupManager.RestoreService(backupEntry);
        }

        private static void RestoreTaskFromHistory(ActionHistoryEntry entry)
        {
            var state = JObject.Parse(entry.PreviousStateJson);
            var isEnabled = state["Enabled"]?.Value<bool>() ?? true;
            var taskPath = string.IsNullOrWhiteSpace(entry.TargetPath) ? entry.TargetName : entry.TargetPath;

            var backupEntry = new TaskBackupEntry
            {
                TaskPath = taskPath,
                IsEnabled = isEnabled
            };

            OptimizationBackupManager.RestoreTask(backupEntry);
        }

        private static void RestoreAutorunFromHistory(ActionHistoryEntry entry)
        {
            var state = JObject.Parse(entry.PreviousStateJson);
            var present = state["Present"]?.Value<bool>() ?? false;
            if (!present)
            {
                throw new InvalidOperationException("Previous state indicates this autorun entry did not exist.");
            }

            var backupEntry = new AutorunBackupEntry
            {
                Location = state["Location"]?.ToString(),
                EntryName = state["Name"]?.ToString() ?? entry.TargetName,
                Command = state["Command"]?.ToString()
            };

            OptimizationBackupManager.RestoreAutorun(backupEntry);
        }

        private void ApplyLanguage()
        {
            Title = LanguageManager.GetString("HistoryTitle");
            HistoryTitleText.Text = LanguageManager.GetString("HistoryHeader");
            FilterLabelText.Text = LanguageManager.GetString("FilterLabel");
            BtnExport.Content = LanguageManager.GetString("BtnExportHistoryJson");
            BtnClearAll.Content = LanguageManager.GetString("BtnClearHistory");
            BtnClose.Content = LanguageManager.GetString("PromptEditorBtnClose");

            if (TypeFilterComboBox.Items.Count >= 8)
            {
                ((ComboBoxItem)TypeFilterComboBox.Items[0]).Content = LanguageManager.GetString("FilterAllActions");
                ((ComboBoxItem)TypeFilterComboBox.Items[1]).Content = LanguageManager.GetString("FilterAutoKill");
                ((ComboBoxItem)TypeFilterComboBox.Items[2]).Content = LanguageManager.GetString("FilterManualKill");
                ((ComboBoxItem)TypeFilterComboBox.Items[3]).Content = LanguageManager.GetString("FilterWhitelist");
                ((ComboBoxItem)TypeFilterComboBox.Items[4]).Content = LanguageManager.GetString("FilterServiceChanges");
                ((ComboBoxItem)TypeFilterComboBox.Items[5]).Content = LanguageManager.GetString("FilterTaskChanges");
                ((ComboBoxItem)TypeFilterComboBox.Items[6]).Content = LanguageManager.GetString("FilterAutorunChanges");
                ((ComboBoxItem)TypeFilterComboBox.Items[7]).Content = LanguageManager.GetString("FilterSnooze");
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = ActionHistory.ExportToJson();
                Clipboard.SetText(json);
                MessageBox.Show("Action history exported to clipboard as JSON.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (ActionHistory.Entries.Count == 0)
            {
                MessageBox.Show("No history to clear.", "History Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Clear all {ActionHistory.Entries.Count} action history entries?\n\nThis action cannot be undone.",
                "Clear All History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                ActionHistory.Clear();
                LoadHistory();
                MessageBox.Show("Action history has been cleared.", "History Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
