using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WinOptimizer.AI
{
    public partial class WhitelistWindow : Window
    {
        private List<WhitelistEntryDisplay> _allEntries;

        public WhitelistWindow()
        {
            InitializeComponent();
            LoadWhitelist();
        }

        private void LoadWhitelist()
        {
            _allEntries = new List<WhitelistEntryDisplay>();

            // Load from old whitelist system
            var oldEntries = WhitelistManager.GetAllEntries();
            foreach (var entry in oldEntries)
            {
                _allEntries.Add(new WhitelistEntryDisplay
                {
                    Type = entry.Type,
                    Name = entry.Name,
                    Path = entry.Path,
                    AddedAt = entry.AddedAt,
                    Source = "Legacy"
                });
            }

            // Load from new custom whitelist system
            var activeWhitelist = CustomWhitelistManager.GetActiveWhitelist();
            if (activeWhitelist != null)
            {
                foreach (var entry in activeWhitelist.Entries)
                {
                    // Skip if already in legacy entries
                    if (_allEntries.Any(e => e.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    _allEntries.Add(new WhitelistEntryDisplay
                    {
                        Type = entry.Type,
                        Name = entry.Name,
                        Path = entry.Path,
                        AddedAt = entry.AddedAt,
                        Source = activeWhitelist.Name,
                        WhitelistId = activeWhitelist.Id
                    });
                }
            }

            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (_allEntries == null) return;
            
            var filtered = _allEntries.AsEnumerable(); 

            // Filter by type
            var selectedType = TypeFilterComboBox.SelectedItem is ComboBoxItem item ? item.Content.ToString() : "All";
            if (selectedType != "All")
            {
                filtered = filtered.Where(e => e.Type.Equals(selectedType, StringComparison.OrdinalIgnoreCase));
            }

            // Filter by search
            var searchText = SearchTextBox.Text?.ToLower() ?? "";
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                filtered = filtered.Where(e =>
                    (e.Name?.ToLower().Contains(searchText) == true) ||
                    (e.Path?.ToLower().Contains(searchText) == true));
            }

            WhitelistDataGrid.ItemsSource = filtered.ToList();
            CountText.Text = $"({_allEntries.Count} total, {filtered.Count()} shown)";
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
            SearchTextBox.Text = string.Empty;
            ApplyFilters();
        }

        private void BtnAddProcess_Click(object sender, RoutedEventArgs e)
        {
            var selector = new ProcessSelectorWindow("Select Processes to Whitelist", "Add to Whitelist");
            selector.Owner = this;
            if (selector.ShowDialog() == true)
            {
                int count = 0;
                foreach (var processName in selector.SelectedProcessNames)
                {
                    // Check if already exists
                    if (_allEntries.Any(entry => entry.Name.Equals(processName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    // Add to custom whitelist
                    CustomWhitelistManager.AddToActiveWhitelist("Process", processName);
                    count++;
                }

                if (count > 0)
                {
                    MessageBox.Show($"Added {count} processes to the whitelist.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    LoadWhitelist(); // Reload to show new entries
                }
            }
        }

        private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WhitelistEntryDisplay entry)
            {
                if (!string.IsNullOrEmpty(entry.Path) && File.Exists(entry.Path))
                {
                    try
                    {
                        Process.Start("explorer.exe", $"/select,\"{entry.Path}\"");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (!string.IsNullOrEmpty(entry.Path) && Directory.Exists(Path.GetDirectoryName(entry.Path)))
                {
                    try
                    {
                        var folder = Path.GetDirectoryName(entry.Path);
                        Process.Start("explorer.exe", folder);
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

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is WhitelistEntryDisplay entry)
            {
                var result = MessageBox.Show(
                    $"Remove '{entry.Name}' from whitelist?\n\nIt will be re-detected during next scan.",
                    "Remove from Whitelist",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Remove from legacy system
                    WhitelistManager.Remove(entry.Type, entry.Name);

                    // Remove from new system if applicable
                    if (!string.IsNullOrEmpty(entry.WhitelistId))
                    {
                        CustomWhitelistManager.RemoveFromActiveWhitelist(entry.Name);
                    }

                    // Remove from our list
                    _allEntries.Remove(entry);
                    ApplyFilters();
                }
            }
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (_allEntries.Count == 0)
            {
                MessageBox.Show("No items to clear.", "Whitelist Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Clear all {_allEntries.Count} whitelisted items?\n\nThis action cannot be undone.",
                "Clear All Whitelist",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Clear all systems
                WhitelistManager.ClearAll();
                CustomWhitelistManager.SetActiveWhitelist(CustomWhitelistManager.GetAllWhitelists().FirstOrDefault()?.Id);
                var active = CustomWhitelistManager.GetActiveWhitelist();
                if (active != null)
                {
                    active.Entries.Clear();
                }

                _allEntries.Clear();
                ApplyFilters();

                MessageBox.Show("All whitelist entries have been removed.", "Whitelist Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_allEntries, Newtonsoft.Json.Formatting.Indented);
                Clipboard.SetText(json);
                MessageBox.Show("Whitelist exported to clipboard as JSON.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// Display model for whitelist entries
    /// </summary>
    public class WhitelistEntryDisplay
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime AddedAt { get; set; }
        public string Source { get; set; }
        public string WhitelistId { get; set; }
    }
}
