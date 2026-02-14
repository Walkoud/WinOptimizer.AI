using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WinOptimizer.AI
{
    public partial class BlacklistWindow : Window
    {
        public BlacklistWindow()
        {
            InitializeComponent();
            LoadBlacklist();
        }

        private void LoadBlacklist()
        {
            var items = BlacklistManager.GetItems();
            BlacklistDataGrid.ItemsSource = items;
            CountText.Text = $"({items.Count} items)";
        }

        private void BtnAddManual_Click(object sender, RoutedEventArgs e)
        {
            var name = ManualInputBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a process name.", "Input Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = System.IO.Path.GetFileNameWithoutExtension(name);

            BlacklistManager.Add(name, ManualAutoKillChk.IsChecked == true);
            ManualInputBox.Clear();
            LoadBlacklist();
        }

        private void BtnAddFromRunning_Click(object sender, RoutedEventArgs e)
        {
            var selector = new ProcessSelectorWindow("Select Processes to Blacklist", "Add to Blacklist");
            selector.Owner = this;
            if (selector.ShowDialog() == true)
            {
                int count = 0;
                foreach (var processName in selector.SelectedProcessNames)
                {
                    BlacklistManager.Add(processName, true); // Default to Auto-Kill true
                    count++;
                }

                if (count > 0)
                {
                    LoadBlacklist();
                }
            }
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is BlacklistItem item)
            {
                if (MessageBox.Show($"Remove '{item.Name}' from blacklist?", "Confirm", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    BlacklistManager.Remove(item.Name);
                    LoadBlacklist();
                }
            }
        }

        private void OnAutoKillChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.DataContext is BlacklistItem item)
            {
                // Update the manager
                BlacklistManager.Add(item.Name, chk.IsChecked == true);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
