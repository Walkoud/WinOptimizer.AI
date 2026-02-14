using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WinOptimizer.AI
{
    public partial class HistoryWindow : Window
    {
        public HistoryWindow()
        {
            InitializeComponent();
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
                var selected = item.Content.ToString();
                filtered = selected switch
                {
                    "🤖 AutoKill" => filtered.Where(e => e.Action == ActionType.AutoKill),
                    "🔴 Manual Kill" => filtered.Where(e => e.Action == ActionType.ManualKill),
                    "🛡️ Whitelist" => filtered.Where(e => e.Action == ActionType.WhitelistAdd),
                    "😴 Snooze" => filtered.Where(e => e.Action == ActionType.Snooze),
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
