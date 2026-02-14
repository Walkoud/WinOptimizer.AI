using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace WinOptimizer.AI
{
    public partial class ProcessSelectorWindow : Window
    {
        private List<ProcessItem> _allProcesses;
        public List<string> SelectedProcessNames { get; private set; } = new List<string>();

        public ProcessSelectorWindow(string title = "Select Processes", string buttonText = "Add Selected")
        {
            InitializeComponent();
            Title = title;
            if (BtnAddSelected != null) BtnAddSelected.Content = buttonText;
            LoadProcesses();
        }

        private void LoadProcesses()
        {
            try
            {
                var processes = Process.GetProcesses();
                _allProcesses = processes
                    .Select(p => {
                        try {
                            return new ProcessItem
                            {
                                Id = p.Id,
                                Name = p.ProcessName,
                                WindowTitle = p.MainWindowTitle,
                                RamUsageBytes = p.WorkingSet64
                            };
                        } catch { return null; }
                    })
                    .Where(p => p != null)
                    .OrderByDescending(p => p.RamUsageBytes)
                    .ToList();

                ApplyFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading processes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            if (_allProcesses == null) return;

            var filter = SearchBox.Text?.ToLower() ?? "";
            var filtered = _allProcesses.Where(p => 
                p.Name.ToLower().Contains(filter) || 
                (p.WindowTitle != null && p.WindowTitle.ToLower().Contains(filter))
            ).ToList();

            ProcessGrid.ItemsSource = filtered;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnAddSelected_Click(object sender, RoutedEventArgs e)
        {
            var selected = ProcessGrid.ItemsSource as List<ProcessItem>;
            if (selected == null) return; // Should catch filtered list

            // Get selected items from the bound collection
            // Since we're filtering, we need to check the IsSelected property on the items
            var selectedItems = _allProcesses.Where(p => p.IsSelected).ToList();

            if (selectedItems.Count == 0)
            {
                MessageBox.Show("Please select at least one process.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedProcessNames = selectedItems.Select(p => p.Name).Distinct().ToList();
            DialogResult = true;
            Close();
        }

        private void SelectAll_Checked(object sender, RoutedEventArgs e)
        {
            var items = ProcessGrid.ItemsSource as List<ProcessItem>;
            if (items != null) foreach (var item in items) item.IsSelected = true;
        }

        private void SelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            var items = ProcessGrid.ItemsSource as List<ProcessItem>;
            if (items != null) foreach (var item in items) item.IsSelected = false;
        }
    }

    public class ProcessItem : INotifyPropertyChanged
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string WindowTitle { get; set; }
        public long RamUsageBytes { get; set; }

        public string RamUsageFormatted
        {
            get
            {
                double mb = RamUsageBytes / 1024.0 / 1024.0;
                return $"{mb:F1} MB";
            }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}