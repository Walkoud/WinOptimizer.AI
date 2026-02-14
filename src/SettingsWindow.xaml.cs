using System;
using System.Windows;
using System.Windows.Controls;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Settings window for GPU monitoring configuration
    /// </summary>
    public partial class SettingsWindow : Window
    {
        private readonly GpuMonitor _gpuMonitor;

        public SettingsWindow(GpuMonitor gpuMonitor)
        {
            _gpuMonitor = gpuMonitor;
            InitializeComponent();
            LoadSettings();
        }

        private void InitializeComponent()
        {
            Title = "Settings - WinOptimizer.AI";
            Width = 500;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = System.Windows.Media.Brushes.White;
            
            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Padding = new Thickness(20)
            };
            var headerText = new TextBlock
            {
                Text = "Settings",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White
            };
            header.Child = headerText;
            Grid.SetRow(header, 0);
            rootGrid.Children.Add(header);

            // Content scroll area
            var scrollViewer = new ScrollViewer { Margin = new Thickness(20) };
            var contentPanel = new StackPanel { Orientation = Orientation.Vertical };

            // GPU Settings Section
            contentPanel.Children.Add(CreateSectionHeader("GPU Monitoring Settings"));
            
            contentPanel.Children.Add(CreateSettingRow("GPU Threshold (%):", "Min GPU % to trigger alerts", out GpuThresholdBox));
            GpuThresholdBox.Text = _gpuMonitor?.GpuThresholdPercent.ToString() ?? "5";

            contentPanel.Children.Add(CreateSettingRow("Alert Duration (sec):", "Seconds above threshold before alert", out DurationBox));
            DurationBox.Text = _gpuMonitor?.AlertDurationSeconds.ToString() ?? "300";

            contentPanel.Children.Add(CreateSettingRow("Check Interval (sec):", "How often to scan processes", out IntervalBox));
            IntervalBox.Text = _gpuMonitor?.CheckIntervalSeconds.ToString() ?? "10";

            contentPanel.Children.Add(CreateSettingRow("Display Threshold (%):", "Min GPU % to show in list", out DisplayThresholdBox));
            DisplayThresholdBox.Text = _gpuMonitor?.GpuDisplayThresholdPercent.ToString() ?? "0.1";
            
            LowImpactModeBox = new CheckBox
            {
                Content = "Mode faible impact CPU",
                IsChecked = _gpuMonitor?.LowImpactMode ?? true,
                Margin = new Thickness(0, 10, 0, 0)
            };
            contentPanel.Children.Add(LowImpactModeBox);

            // Alert Settings Section
            contentPanel.Children.Add(CreateSectionHeader("Alert Triggers"));
            
            AlertGpuBox = new CheckBox { Content = "Alert on High GPU", IsChecked = _gpuMonitor?.NotifyGpu ?? true, Margin = new Thickness(0, 5, 0, 0) };
            contentPanel.Children.Add(AlertGpuBox);
            
            AlertCpuBox = new CheckBox { Content = "Alert on High CPU", IsChecked = _gpuMonitor?.NotifyCpu ?? false, Margin = new Thickness(0, 5, 0, 0) };
            contentPanel.Children.Add(AlertCpuBox);
            
            AlertRamBox = new CheckBox { Content = "Alert on High RAM", IsChecked = _gpuMonitor?.NotifyRam ?? false, Margin = new Thickness(0, 5, 0, 0) };
            contentPanel.Children.Add(AlertRamBox);
            
            AlertNetworkBox = new CheckBox { Content = "Alert on High Network", IsChecked = _gpuMonitor?.NotifyNetwork ?? false, Margin = new Thickness(0, 5, 0, 0) };
            contentPanel.Children.Add(AlertNetworkBox);

            // Notifications Section
            contentPanel.Children.Add(CreateSectionHeader("Notifications"));

            var notificationsPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
            EnableWindowsNotificationsBox = new CheckBox
            {
                Content = "Enable Windows notifications for AI recommendations",
                IsChecked = true,
                Margin = new Thickness(0, 5, 0, 5)
            };
            notificationsPanel.Children.Add(EnableWindowsNotificationsBox);

            ShowKillButtonInNotificationBox = new CheckBox
            {
                Content = "Show 'Kill Process' button in notifications",
                IsChecked = true,
                Margin = new Thickness(20, 5, 0, 5)
            };
            notificationsPanel.Children.Add(ShowKillButtonInNotificationBox);

            ShowWhitelistButtonInNotificationBox = new CheckBox
            {
                Content = "Show 'Whitelist' button in notifications",
                IsChecked = true,
                Margin = new Thickness(20, 5, 0, 5)
            };
            notificationsPanel.Children.Add(ShowWhitelistButtonInNotificationBox);

            contentPanel.Children.Add(notificationsPanel);

            scrollViewer.Content = contentPanel;
            Grid.SetRow(scrollViewer, 1);
            rootGrid.Children.Add(scrollViewer);

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20)
            };

            var cancelBtn = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(20, 10, 20, 10),
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(0)
            };
            cancelBtn.Click += (s, e) => Close();

            var saveBtn = new Button
            {
                Content = "Save Settings",
                Padding = new Thickness(20, 10, 20, 10),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0)
            };
            saveBtn.Click += SaveBtn_Click;

            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(saveBtn);
            Grid.SetRow(buttonPanel, 2);
            rootGrid.Children.Add(buttonPanel);

            Content = rootGrid;
        }

        private TextBox GpuThresholdBox;
        private TextBox DurationBox;
        private TextBox IntervalBox;
        private TextBox DisplayThresholdBox;
        private CheckBox EnableWindowsNotificationsBox;
        private CheckBox ShowKillButtonInNotificationBox;
        private CheckBox ShowWhitelistButtonInNotificationBox;
        private CheckBox LowImpactModeBox;
        private CheckBox AlertGpuBox;
        private CheckBox AlertCpuBox;
        private CheckBox AlertRamBox;
        private CheckBox AlertNetworkBox;

        private void LoadSettings()
        {
            // Settings are loaded in InitializeComponent
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_gpuMonitor != null)
                {
                    if (double.TryParse(GpuThresholdBox.Text, out var gpuThreshold))
                        _gpuMonitor.GpuThresholdPercent = gpuThreshold;

                    if (int.TryParse(DurationBox.Text, out var duration))
                        _gpuMonitor.AlertDurationSeconds = duration;

                    if (int.TryParse(IntervalBox.Text, out var interval))
                        _gpuMonitor.CheckIntervalSeconds = interval;

                    if (double.TryParse(DisplayThresholdBox.Text, out var displayThreshold))
                        _gpuMonitor.GpuDisplayThresholdPercent = displayThreshold;
                    
                    _gpuMonitor.LowImpactMode = LowImpactModeBox.IsChecked ?? false;
                    
                    _gpuMonitor.NotifyGpu = AlertGpuBox.IsChecked ?? true;
                    _gpuMonitor.NotifyCpu = AlertCpuBox.IsChecked ?? false;
                    _gpuMonitor.NotifyRam = AlertRamBox.IsChecked ?? false;
                    _gpuMonitor.NotifyNetwork = AlertNetworkBox.IsChecked ?? false;
                }

                Properties.Settings.Default.EnableWindowsNotifications = EnableWindowsNotificationsBox.IsChecked ?? true;
                Properties.Settings.Default.ShowKillButtonInNotification = ShowKillButtonInNotificationBox.IsChecked ?? true;
                Properties.Settings.Default.ShowWhitelistButtonInNotification = ShowWhitelistButtonInNotificationBox.IsChecked ?? true;
                
                // Save AppSettings (JSON) for persistent monitoring settings
                AppSettings.Instance.EnableWindowsNotifications = EnableWindowsNotificationsBox.IsChecked ?? true;
                AppSettings.Instance.ShowKillButtonInNotification = ShowKillButtonInNotificationBox.IsChecked ?? true;
                AppSettings.Instance.ShowWhitelistButtonInNotification = ShowWhitelistButtonInNotificationBox.IsChecked ?? true;
                AppSettings.Instance.Save();

                try
                {
                    Properties.Settings.Default.Save();
                }
                catch
                {
                    MessageBox.Show("Paramètres appliqués. Sauvegarde non persistée (accès refusé).", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                MessageBox.Show("Settings saved successfully!", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Border CreateSectionHeader(string title)
        {
            return new Border
            {
                BorderThickness = new Thickness(0, 0, 0, 2),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                Margin = new Thickness(0, 20, 0, 10),
                Child = new TextBlock
                {
                    Text = title,
                    FontSize = 16,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 120, 212)),
                    Margin = new Thickness(0, 0, 0, 5)
                }
            };
        }

        private static Grid CreateSettingRow(string label, string tooltip, out TextBox textBox)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Margin = new Thickness(0, 5, 0, 5);

            var labelBlock = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tooltip
            };
            Grid.SetColumn(labelBlock, 0);

            textBox = new TextBox
            {
                Width = 80,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(5),
                ToolTip = tooltip
            };
            Grid.SetColumn(textBox, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(textBox);

            return grid;
        }
    }
}
