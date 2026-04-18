using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WinOptimizer.AI
{
    public partial class MainWindow : Window
    {
        private GpuMonitor _gpuMonitor;
        private GameModeManager _gameModeManager;
        private GpuDetectionHistory _gpuHistory;
        private ObservableCollection<ProcessData> _processes;
        private ObservableCollection<ProcessData> _processRecommendations;
        private ObservableCollection<ServiceData> _services;
        private ObservableCollection<ServiceData> _serviceRecommendations;
        private ObservableCollection<TaskData> _tasks;
        private ObservableCollection<AutorunData> _autoruns;
        private ObservableCollection<OptimizationBackupInfo> _backupInfos;
        private bool _uiReady;
        
        public MainWindow()
        {
            InitializeComponent();
            InitializeApplication();

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _uiReady = true;
            UpdateUILanguage();
            LoadSettingsToUI();
        }

        private void LoadSettingsToUI()
        {
            try
            {
                var settings = AppSettings.Instance;

                // Language
                SyncLanguageSelection(LanguageComboBox, settings.Language);
                SyncLanguageSelection(LanguageComboBoxSettings, settings.Language);

                // Thresholds
                if (GpuThresholdInput != null) GpuThresholdInput.Text = settings.GpuThresholdPercent.ToString();
                if (CpuThresholdInput != null) CpuThresholdInput.Text = settings.CpuThresholdPercent.ToString();
                if (RamThresholdInput != null) RamThresholdInput.Text = settings.RamThresholdMB.ToString();
                if (NetThresholdInput != null) NetThresholdInput.Text = settings.NetworkThresholdMB.ToString();

                if (IntervalInputSettings != null) IntervalInputSettings.Text = settings.CheckIntervalSeconds.ToString();
                if (DurationSecondsInput != null) DurationSecondsInput.Text = settings.AlertDurationSeconds.ToString();
                
                // Monitoring Toggles
                if (ChkMonitorGpu != null) ChkMonitorGpu.IsChecked = settings.MonitorGpu;
                if (ChkMonitorCpu != null) ChkMonitorCpu.IsChecked = settings.MonitorCpu;
                if (ChkMonitorRam != null) ChkMonitorRam.IsChecked = settings.MonitorRam;
                if (ChkMonitorNet != null) ChkMonitorNet.IsChecked = settings.MonitorNetwork;
                
                // Low Impact Mode
                if (ChkLowImpactMode != null) ChkLowImpactMode.IsChecked = settings.LowImpactMode;

                // Auto-Kill Global
                if (ChkKillGpuGlobal != null) ChkKillGpuGlobal.IsChecked = settings.AutoKillGpuGlobal;
                if (ChkKillCpuGlobal != null) ChkKillCpuGlobal.IsChecked = settings.AutoKillCpuGlobal;
                if (ChkKillRamGlobal != null) ChkKillRamGlobal.IsChecked = settings.AutoKillRamGlobal;
                if (ChkKillNetGlobal != null) ChkKillNetGlobal.IsChecked = settings.AutoKillNetworkGlobal;

                // Auto-Kill Game Mode
                if (ChkKillGpuGame != null) ChkKillGpuGame.IsChecked = settings.AutoKillGpuGameMode;
                if (ChkKillCpuGame != null) ChkKillCpuGame.IsChecked = settings.AutoKillCpuGameMode;
                if (ChkKillRamGame != null) ChkKillRamGame.IsChecked = settings.AutoKillRamGameMode;
                if (ChkKillNetGame != null) ChkKillNetGame.IsChecked = settings.AutoKillNetworkGameMode;

                // Notifications
                if (ChkNotifyGpu != null) ChkNotifyGpu.IsChecked = settings.AlertGpu;
                if (ChkNotifyCpu != null) ChkNotifyCpu.IsChecked = settings.AlertCpu;
                if (ChkNotifyRam != null) ChkNotifyRam.IsChecked = settings.AlertRam;
                if (ChkNotifyNet != null) ChkNotifyNet.IsChecked = settings.AlertNetwork;
            }
            catch (Exception ex)
            {
                LogMessage($"Error loading settings: {ex.Message}");
            }
        }

        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                if (int.TryParse(btn.Tag.ToString(), out int index))
                {
                    MainTabControl.SelectedIndex = index;
                    
                    // Update Title based on selection
                    switch (index)
                    {
                        case 0: PageTitle.Text = LanguageManager.GetString("TabDashboard").Replace("📊 ", "").Replace("📊", ""); break;
                        case 1: PageTitle.Text = LanguageManager.GetString("TabProcesses").Replace("⚡ ", "").Replace("⚡", ""); break;
                        case 2: PageTitle.Text = LanguageManager.GetString("TabGameMode").Replace("🎮 ", "").Replace("🎮", ""); break;
                        case 3: PageTitle.Text = LanguageManager.GetString("TabServices").Replace("⚙️ ", "").Replace("⚙️", ""); break;
                        case 4: PageTitle.Text = LanguageManager.GetString("TabTasks").Replace("📅 ", "").Replace("📅", ""); break;
                        case 5: PageTitle.Text = LanguageManager.GetString("TabAutoruns").Replace("🚀 ", "").Replace("🚀", ""); break;
                        case 6: PageTitle.Text = LanguageManager.GetString("TabBackups").Replace("💾 ", "").Replace("💾", ""); break;
                        case 7:
                            PageTitle.Text = LanguageManager.GetString("TabSettings").Replace("⚙️ ", "").Replace("⚙️", "");
                            UpdateMonitoringButtonsState(_gpuMonitor != null && _gpuMonitor.IsMonitoring);
                            break;
                        case 8: PageTitle.Text = LanguageManager.GetString("TabOtherApps").Replace("🛠️ ", "").Replace("🛠️", ""); break;
                    }
                }
            }
        }

        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                LogMessage($"Error opening link: {ex.Message}");
            }
        }

        private void InitializeApplication()
        {
            try
            {
                var langPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages.json");
                LanguageManager.LoadLanguages(langPath);
                var missingTranslations = LanguageManager.GetMissingTranslationKeys();
                foreach (var entry in missingTranslations)
                {
                    LogMessage($"[i18n] Missing keys in {entry.Key}: {entry.Value.Count} (fallback to English enabled)");
                }

                _processes = new ObservableCollection<ProcessData>();
                _processRecommendations = new ObservableCollection<ProcessData>();
                _services = new ObservableCollection<ServiceData>();
                _serviceRecommendations = new ObservableCollection<ServiceData>();
                _tasks = new ObservableCollection<TaskData>();
                _autoruns = new ObservableCollection<AutorunData>();
                _backupInfos = new ObservableCollection<OptimizationBackupInfo>();
                _gpuHistory = new GpuDetectionHistory();

                ProcessDataGrid.ItemsSource = _processes;
                ProcessRecsDataGrid.ItemsSource = _processRecommendations;
                GpuHistoryDataGrid.ItemsSource = _gpuHistory.Events;
                BackupsDataGrid.ItemsSource = _backupInfos;

                try
                {
                    _gpuMonitor = new GpuMonitor();
                    _gpuMonitor.OnProcessesUpdated += OnProcessesUpdated;
                    _gpuMonitor.OnGpuAlert += OnGpuAlert;
                    _gpuMonitor.OnLogMessage += LogMessage;
                    _gpuMonitor.OnMonitoringStateChanged += OnMonitoringStateChanged;
                    
                    _gameModeManager = new GameModeManager(_gpuMonitor);
                    _gameModeManager.OnLog += LogMessage;
                    _gameModeManager.OnIntruderDetected += OnGameModeIntruderDetected;
                    _gameModeManager.OnIntruderKilled += OnGameModeIntruderKilled;

            // Subscribe to notification action events
            NotificationManager.OnProcessKilledFromNotification += OnProcessKilledFromNotification;
            NotificationManager.OnProcessWhitelistedFromNotification += OnProcessWhitelistedFromNotification;
            NotificationManager.OnProcessSnoozedFromNotification += OnProcessSnoozedFromNotification;
                    
                    // Initialize Game Mode UI
                    InitializeGameModeUI();
                }
                catch (Exception ex)
                {
                    _gpuMonitor = null;
                    _gameModeManager = null;
                    if (BtnToggleMonitoring != null) BtnToggleMonitoring.IsEnabled = false;
                    LogMessage($"GpuMonitor init failed: {ex.Message}");
                }

                LogMessage(LanguageManager.GetString("LogReady"));
                RefreshBackupList();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Init error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        #region Language
        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_uiReady) return;

            if (sender is ComboBox sendingBox && sendingBox.SelectedItem is ComboBoxItem item)
            {
                string langCode = item.Tag as string;
                if (string.IsNullOrEmpty(langCode)) langCode = item.Content.ToString();

                // Update Settings
                AppSettings.Instance.Language = langCode;
                AppSettings.Instance.Save();

                // Update Manager
                LanguageManager.SetLanguage(langCode);
                UpdateUILanguage();

                // Sync other ComboBoxes
                SyncLanguageSelection(LanguageComboBox, langCode);
                SyncLanguageSelection(LanguageComboBoxSettings, langCode);
            }
        }

        private void SyncLanguageSelection(ComboBox box, string langCode)
        {
            if (box == null) return;
            
            // Check if already selected
            if (box.SelectedItem is ComboBoxItem current)
            {
                string currentLang = current.Tag as string ?? current.Content.ToString();
                if (currentLang == langCode) return;
            }

            // Find matching item
            foreach (ComboBoxItem cbItem in box.Items)
            {
                string itemLang = cbItem.Tag as string ?? cbItem.Content.ToString();
                if (itemLang == langCode)
                {
                    bool wasReady = _uiReady;
                    _uiReady = false;
                    box.SelectedItem = cbItem;
                    _uiReady = wasReady;
                    break;
                }
            }
        }

        private void UpdateUILanguage()
        {
            if (!_uiReady)
                return;

            // Header
            if (HeaderSubtitle != null) HeaderSubtitle.Text = LanguageManager.GetString("HeaderSubtitle");

            // Tabs (Sidebar Buttons)
            if (NavDashboard != null) NavDashboard.Content = LanguageManager.GetString("TabDashboard");
            if (NavProcesses != null) NavProcesses.Content = LanguageManager.GetString("TabProcesses");
            if (NavGameMode != null) NavGameMode.Content = LanguageManager.GetString("TabGameMode");
            if (NavServices != null) NavServices.Content = LanguageManager.GetString("TabServices");
            if (NavTasks != null) NavTasks.Content = LanguageManager.GetString("TabTasks");
            if (NavAutoruns != null) NavAutoruns.Content = LanguageManager.GetString("TabAutoruns");
            if (NavBackups != null) NavBackups.Content = LanguageManager.GetString("TabBackups");
            if (NavSettings != null) NavSettings.Content = LanguageManager.GetString("TabSettings");
            if (NavOtherApps != null) NavOtherApps.Content = LanguageManager.GetString("TabOtherApps");

            // Update Current Page Title based on selected index
            int index = MainTabControl.SelectedIndex;
            switch (index)
            {
                case 0: PageTitle.Text = LanguageManager.GetString("TabDashboard").Replace("📊 ", "").Replace("📊", ""); break;
                case 1: PageTitle.Text = LanguageManager.GetString("TabProcesses").Replace("⚡ ", "").Replace("⚡", ""); break;
                case 2: PageTitle.Text = LanguageManager.GetString("TabGameMode").Replace("🎮 ", "").Replace("🎮", ""); break;
                case 3: PageTitle.Text = LanguageManager.GetString("TabServices").Replace("⚙️ ", "").Replace("⚙️", ""); break;
                case 4: PageTitle.Text = LanguageManager.GetString("TabTasks").Replace("📅 ", "").Replace("📅", ""); break;
                case 5: PageTitle.Text = LanguageManager.GetString("TabAutoruns").Replace("🚀 ", "").Replace("🚀", ""); break;
                case 6: PageTitle.Text = LanguageManager.GetString("TabBackups").Replace("💾 ", "").Replace("💾", ""); break;
                case 7: PageTitle.Text = LanguageManager.GetString("TabSettings").Replace("⚙️ ", "").Replace("⚙️", ""); break;
                case 8: PageTitle.Text = LanguageManager.GetString("TabOtherApps").Replace("🛠️ ", "").Replace("🛠️", ""); break;
            }

            // Settings Page
            if (LblLanguage != null) LblLanguage.Text = LanguageManager.GetString("LblLanguage");

            // Global buttons
            if (BtnProcessSettings != null) BtnProcessSettings.ToolTip = LanguageManager.GetString("BtnSettingsShort");
            if (BtnProcessHelp != null) BtnProcessHelp.ToolTip = LanguageManager.GetString("BtnHelp");
            if (BtnGoToGameMode != null) BtnGoToGameMode.Content = LanguageManager.GetString("BtnGoToGameMode");
            if (BtnClearHistory != null) BtnClearHistory.Content = LanguageManager.GetString("BtnClear");
            if (BtnExportHistory != null) BtnExportHistory.Content = LanguageManager.GetString("BtnExport");
            if (BtnViewWhitelist != null) BtnViewWhitelist.Content = LanguageManager.GetString("BtnManageWhitelist");
            if (BtnViewHistory != null) BtnViewHistory.Content = LanguageManager.GetString("BtnViewHistory");

            // Game mode
            if (BtnRestoreClosedApps != null) BtnRestoreClosedApps.Content = LanguageManager.GetString("BtnRestoreApps");
            if (BtnManageWhitelist != null) BtnManageWhitelist.Content = LanguageManager.GetString("BtnWhitelist");
            if (BtnManageBlacklist != null) BtnManageBlacklist.Content = LanguageManager.GetString("BtnBlacklist");
            if (BtnAddGameProcess != null) BtnAddGameProcess.Content = LanguageManager.GetString("BtnAddGame");
            if (AutoKillCheckBox != null) AutoKillCheckBox.Content = LanguageManager.GetString("ChkAutoKillIntruders");

            // Services
            if (BtnScanServices != null) BtnScanServices.Content = LanguageManager.GetString("BtnScanServices");
            if (BtnCopyServicePrompt != null) BtnCopyServicePrompt.Content = LanguageManager.GetString("BtnCopyPrompt");
            if (BtnPasteServiceAI != null) BtnPasteServiceAI.Content = LanguageManager.GetString("BtnPasteAI");
            if (BtnEditServicePrompt != null) BtnEditServicePrompt.Content = LanguageManager.GetString("BtnEditPrompt");
            if (BtnServiceHistory != null) BtnServiceHistory.Content = LanguageManager.GetString("BtnHistory");
            if (BtnBackupServices != null) BtnBackupServices.Content = LanguageManager.GetString("BtnBackup");
            if (BtnOpenServicesMsc != null) BtnOpenServicesMsc.Content = LanguageManager.GetString("BtnOpenServicesMsc");

            // Tasks
            if (BtnScanTasks != null) BtnScanTasks.Content = LanguageManager.GetString("BtnScanTasks");
            if (BtnCopyTaskPrompt != null) BtnCopyTaskPrompt.Content = LanguageManager.GetString("BtnCopyPrompt");
            if (BtnPasteTaskAI != null) BtnPasteTaskAI.Content = LanguageManager.GetString("BtnPasteAI");
            if (BtnEditTaskPrompt != null) BtnEditTaskPrompt.Content = LanguageManager.GetString("BtnEditPrompt");
            if (BtnTaskHistory != null) BtnTaskHistory.Content = LanguageManager.GetString("BtnHistory");
            if (BtnBackupTasks != null) BtnBackupTasks.Content = LanguageManager.GetString("BtnBackup");
            if (BtnOpenTaskScheduler != null) BtnOpenTaskScheduler.Content = LanguageManager.GetString("BtnOpenTaskScheduler");

            // Autoruns
            if (BtnScanAutoruns != null) BtnScanAutoruns.Content = LanguageManager.GetString("BtnScanAutoruns");
            if (BtnBrowseAutorunFolder != null) BtnBrowseAutorunFolder.Content = LanguageManager.GetString("BtnBrowseFolder");
            if (BtnCopyAutorunPrompt != null) BtnCopyAutorunPrompt.Content = LanguageManager.GetString("BtnCopyPrompt");
            if (BtnPasteAutorunAI != null) BtnPasteAutorunAI.Content = LanguageManager.GetString("BtnPasteAI");
            if (BtnEditAutorunPrompt != null) BtnEditAutorunPrompt.Content = LanguageManager.GetString("BtnEditPrompt");
            if (BtnAutorunHistory != null) BtnAutorunHistory.Content = LanguageManager.GetString("BtnHistory");
            if (BtnBackupAutoruns != null) BtnBackupAutoruns.Content = LanguageManager.GetString("BtnBackup");
            if (BtnOpenStartupApps != null) BtnOpenStartupApps.Content = LanguageManager.GetString("BtnOpenStartupApps");

            // Backups
            if (BtnCreateBackupFromTab != null) BtnCreateBackupFromTab.Content = LanguageManager.GetString("BtnCreateBackup");
            if (BtnRestoreSelectedBackup != null) BtnRestoreSelectedBackup.Content = LanguageManager.GetString("BtnRestoreSelected");
            if (BtnRenameBackup != null) BtnRenameBackup.Content = LanguageManager.GetString("BtnRename");
            if (BtnDeleteBackup != null) BtnDeleteBackup.Content = LanguageManager.GetString("BtnDelete");
            if (BtnRefreshBackups != null) BtnRefreshBackups.Content = LanguageManager.GetString("BtnRefresh");
            
            // Note: Many settings labels are hardcoded in XAML without x:Name. 
            // In a real-world scenario, we would bind them or name them.
            // For now, I'm updating the critical ones that have names or that I can access easily.
            
            // Global Monitoring Button
            if (BtnToggleMonitoring != null)
            {
                bool isMonitoring = _gpuMonitor != null && _gpuMonitor.IsMonitoring;
                BtnToggleMonitoring.Content = isMonitoring ? LanguageManager.GetString("BtnStopMonitoring") : LanguageManager.GetString("BtnStartMonitoring");
            }
            if (BtnToggleMonitoringSettings != null)
            {
                bool isMonitoring = _gpuMonitor != null && _gpuMonitor.IsMonitoring;
                BtnToggleMonitoringSettings.Content = isMonitoring ? LanguageManager.GetString("BtnStopMonitoring") : LanguageManager.GetString("BtnStartMonitoring");
            }

            // Checkboxes
            if (ChkLowImpactMode != null) ChkLowImpactMode.Content = LanguageManager.GetString("LowImpactMode");
            if (DevModeCheckBoxSettings != null) DevModeCheckBoxSettings.Content = LanguageManager.GetString("DevMode");

            if (ChkMonitorGpu != null) ChkMonitorGpu.Content = LanguageManager.GetString("EnableGpu");
            if (ChkMonitorCpu != null) ChkMonitorCpu.Content = LanguageManager.GetString("EnableCpu");
            if (ChkMonitorRam != null) ChkMonitorRam.Content = LanguageManager.GetString("EnableRam");
            if (ChkMonitorNet != null) ChkMonitorNet.Content = LanguageManager.GetString("EnableNet");

            if (ChkNotifyGpu != null) ChkNotifyGpu.Content = LanguageManager.GetString("NotifyOnly");
            if (ChkNotifyCpu != null) ChkNotifyCpu.Content = LanguageManager.GetString("NotifyOnly");
            if (ChkNotifyRam != null) ChkNotifyRam.Content = LanguageManager.GetString("NotifyOnly");
            if (ChkNotifyNet != null) ChkNotifyNet.Content = LanguageManager.GetString("NotifyOnly");

            if (ChkKillGpuGame != null) ChkKillGpuGame.Content = LanguageManager.GetString("AutoKillGame");
            if (ChkKillCpuGame != null) ChkKillCpuGame.Content = LanguageManager.GetString("AutoKillGame");
            if (ChkKillRamGame != null) ChkKillRamGame.Content = LanguageManager.GetString("AutoKillGame");
            if (ChkKillNetGame != null) ChkKillNetGame.Content = LanguageManager.GetString("AutoKillGame");

            if (ChkKillGpuGlobal != null) ChkKillGpuGlobal.Content = LanguageManager.GetString("AutoKillAlways");
            if (ChkKillCpuGlobal != null) ChkKillCpuGlobal.Content = LanguageManager.GetString("AutoKillAlways");
            if (ChkKillRamGlobal != null) ChkKillRamGlobal.Content = LanguageManager.GetString("AutoKillAlways");
            if (ChkKillNetGlobal != null) ChkKillNetGlobal.Content = LanguageManager.GetString("AutoKillAlways");

            // Other Apps Page - Tool Cards
            // Note: Since these are in XAML without binding, we can't easily update them without traversing the visual tree 
            // or naming every single TextBlock.
            // However, we can update the "Run Tool" and "Website" buttons if we find them, 
            // but they are inside templates/structures.
            // For this iteration, the main navigation and settings are the most critical.
        }



        #endregion

        #region Processes Tab (Real-time GPU Monitoring)
        private void OnMonitoringStateChanged(bool isMonitoring)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateMonitoringButtonsState(isMonitoring);

                // Update Status Panel
                if (StatusPanel != null)
                {
                    StatusPanel.Visibility = Visibility.Visible;
                    
                    if (isMonitoring)
                    {
                        GpuStatusIcon.Text = "🟢";
                        GpuStatusText.Text = _gpuMonitor.DevMode ? LanguageManager.GetString("StatusActiveDev") : LanguageManager.GetString("StatusActive");
                        GpuStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 167, 69)); // Green
                    }
                    else
                    {
                        GpuStatusIcon.Text = "⚪";
                        GpuStatusText.Text = LanguageManager.GetString("StatusStopped");
                        GpuStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(170, 170, 170)); // Gray
                    }
                }
            });
        }

        private void UpdateMonitoringButtonsState(bool isMonitoring)
        {
            var secondaryStyle = (Style)FindResource("SecondaryButtonStyle");
            var modernStyle = (Style)FindResource("ModernButtonStyle");

            // Update Dashboard Button
            if (BtnToggleMonitoring != null)
            {
                if (isMonitoring)
                {
                    BtnToggleMonitoring.Content = LanguageManager.GetString("BtnStopMonitoring");
                    BtnToggleMonitoring.Style = secondaryStyle;
                    BtnToggleMonitoring.Width = 180;
                }
                else
                {
                    BtnToggleMonitoring.Content = LanguageManager.GetString("BtnStartMonitoring");
                    BtnToggleMonitoring.Style = modernStyle;
                    BtnToggleMonitoring.Width = 200;
                }
            }

            // Update Settings Button
            if (BtnToggleMonitoringSettings != null)
            {
                if (isMonitoring)
                {
                    BtnToggleMonitoringSettings.Content = LanguageManager.GetString("BtnStopMonitoring");
                    BtnToggleMonitoringSettings.Style = secondaryStyle;
                }
                else
                {
                    BtnToggleMonitoringSettings.Content = LanguageManager.GetString("BtnStartMonitoring");
                    BtnToggleMonitoringSettings.Style = modernStyle;
                }
            }
        }
        
        private void BtnToggleMonitoring_Click(object sender, RoutedEventArgs e)
        {
            if (_gpuMonitor == null) return;

            if (_gpuMonitor.IsMonitoring)
            {
                // Stop
                _gpuMonitor.StopMonitoring();
            }
            else
            {
                // Start
                _gpuMonitor.DevMode = DevModeCheckBoxSettings.IsChecked == true;
                
                if (int.TryParse(DurationSecondsInput.Text, out int durationSeconds) && durationSeconds > 0)
                    _gpuMonitor.AlertDurationSeconds = durationSeconds;
                else
                    _gpuMonitor.AlertDurationSeconds = 300;

                if (double.TryParse(GpuThresholdInput.Text, out double gpuThreshold))
                    _gpuMonitor.GpuThresholdPercent = gpuThreshold;
                if (double.TryParse(CpuThresholdInput.Text, out double cpuThreshold))
                    _gpuMonitor.CpuThresholdPercent = cpuThreshold;
                if (double.TryParse(RamThresholdInput.Text, out double ramThreshold))
                    _gpuMonitor.RamThresholdMB = ramThreshold;
                
                // Network Threshold with Unit Conversion
                if (double.TryParse(NetThresholdInput.Text, out double netThreshold))
                {
                    if (ComboNetUnit.SelectedIndex == 1) // Mb/s selected
                        _gpuMonitor.NetworkThresholdMB = netThreshold / 8.0;
                    else
                        _gpuMonitor.NetworkThresholdMB = netThreshold;
                }

                _gpuMonitor.MonitorGpu = ChkMonitorGpu.IsChecked == true;
                _gpuMonitor.MonitorCpu = ChkMonitorCpu.IsChecked == true;
                _gpuMonitor.MonitorRam = ChkMonitorRam.IsChecked == true;
                _gpuMonitor.MonitorNetwork = ChkMonitorNet.IsChecked == true;

                // Auto-Kill Global
                _gpuMonitor.AutoKillGpuGlobal = ChkKillGpuGlobal.IsChecked == true;
                _gpuMonitor.AutoKillCpuGlobal = ChkKillCpuGlobal.IsChecked == true;
                _gpuMonitor.AutoKillRamGlobal = ChkKillRamGlobal.IsChecked == true;
                _gpuMonitor.AutoKillNetworkGlobal = ChkKillNetGlobal.IsChecked == true;

                // Auto-Kill Game Mode
                _gpuMonitor.AutoKillGpuGameMode = ChkKillGpuGame.IsChecked == true;
                _gpuMonitor.AutoKillCpuGameMode = ChkKillCpuGame.IsChecked == true;
                _gpuMonitor.AutoKillRamGameMode = ChkKillRamGame.IsChecked == true;
                _gpuMonitor.AutoKillNetworkGameMode = ChkKillNetGame.IsChecked == true;

                // Notifications
                _gpuMonitor.NotifyGpu = ChkNotifyGpu.IsChecked == true;
                _gpuMonitor.NotifyCpu = ChkNotifyCpu.IsChecked == true;
                _gpuMonitor.NotifyRam = ChkNotifyRam.IsChecked == true;
                _gpuMonitor.NotifyNetwork = ChkNotifyNet.IsChecked == true;

                if (int.TryParse(IntervalInputSettings.Text, out int interval))
                    _gpuMonitor.CheckIntervalSeconds = interval;

                // Low Impact Mode
                _gpuMonitor.LowImpactMode = ChkLowImpactMode.IsChecked == true;

                // Save all settings to disk
                AppSettings.Instance.Save();

                _gpuMonitor.StartMonitoring();
            }
        }

        private void BtnProcessSettings_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 7;
            PageTitle.Text = "Settings";
            // Ensure button state is correct when navigating via top bar
            UpdateMonitoringButtonsState(_gpuMonitor != null && _gpuMonitor.IsMonitoring);
        }
        
        private void BtnProcessHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var helpWindow = new HelpWindow();
                helpWindow.Owner = this;
                helpWindow.Show();
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to open help: {ex.Message}");
            }
        }

        private void BtnGoToGameMode_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 2; // Index 2 is Game Mode Tab
            PageTitle.Text = "Game Mode";
            // Tab indices: 0=Dashboard, 1=Processes, 2=GameMode, 3=Services, 4=Tasks, 5=Autoruns, 6=Backups
        }

        private void BtnRestoreClosedApps_Click(object sender, RoutedEventArgs e)
        {
            if (_gameModeManager != null)
            {
                int count = _gameModeManager.RestoreKilledProcesses();
                if (count > 0)
                {
                    LogMessage($"Restored {count} processes.");
                    MessageBox.Show($"Restored {count} processes.", "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No processes to restore from this session.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void BtnStartMonitoring_Click(object sender, RoutedEventArgs e)
        {
             // Legacy method, redirected to toggle
             BtnToggleMonitoring_Click(sender, e);
        }

        private void BtnStopMonitoring_Click(object sender, RoutedEventArgs e)
        {
             // Legacy method, redirected to toggle
             BtnToggleMonitoring_Click(sender, e);
        }

        private void OnProcessesUpdated(List<ProcessData> processes)
        {
            Dispatcher.Invoke(() =>
            {
                // Optimize: Create a new collection for bulk update instead of Clear() + Add() loop
                // This prevents 100+ CollectionChanged events which kills UI performance
                var filteredList = new List<ProcessData>();
                
                foreach (var p in processes)
                {
                    if (!WhitelistManager.IsWhitelisted(p.ProcessName))
                        filteredList.Add(p);
                }

                // Update the binding source directly for a single UI refresh
                // Reusing existing collection instance causes flicker/lag with Clear()
                _processes = new ObservableCollection<ProcessData>(filteredList);
                ProcessDataGrid.ItemsSource = _processes;

                // Update Top 3 Consumers Widget
                UpdateTopConsumers(processes);
            });
        }

        private void UpdateTopConsumers(List<ProcessData> processes)
        {
            if (processes == null || processes.Count == 0) return;

            // Top CPU
            var topCpu = processes.OrderByDescending(p => p.CpuUsage).FirstOrDefault();
            if (topCpu != null)
            {
                if (TopCpuName != null) TopCpuName.Text = topCpu.ProcessName;
                if (TopCpuValue != null) TopCpuValue.Text = $"{topCpu.CpuUsage:F1}%";
            }

            // Top GPU
            var topGpu = processes.OrderByDescending(p => p.GpuUsage).FirstOrDefault();
            if (topGpu != null)
            {
                if (TopGpuName != null) TopGpuName.Text = topGpu.ProcessName;
                if (TopGpuValue != null) TopGpuValue.Text = $"{topGpu.GpuUsage:F1}%";
            }

            // Top RAM
            var topRam = processes.OrderByDescending(p => p.RamUsageMB).FirstOrDefault();
            if (topRam != null)
            {
                if (TopRamName != null) TopRamName.Text = topRam.ProcessName;
                if (TopRamValue != null) TopRamValue.Text = $"{topRam.RamUsageMB:F0} MB";
            }

            // Top Network
            var topNet = processes.OrderByDescending(p => p.NetworkUsageMB).FirstOrDefault();
            if (topNet != null)
            {
                if (TopNetName != null) TopNetName.Text = topNet.ProcessName;
                if (TopNetValue != null) TopNetValue.Text = $"{topNet.NetworkUsageMB:F2} MB/s";
            }
        }

        private void OnGpuAlert(ProcessData process)
        {
            Dispatcher.Invoke(() =>
            {
                if (WhitelistManager.IsWhitelisted(process.ProcessName)) return;

                // Add to history
                _gpuHistory.AddDetection(process);
                GpuHistoryPanel.Visibility = Visibility.Visible;

                // Add to recommendations if not already there
                if (!_processRecommendations.Any(r => r.ProcessName == process.ProcessName))
                {
                    process.AiAction = "Kill";
                    string violation = process.ViolationReason ?? "GPU";
                    process.AiReason = $"High {violation} usage detected";
                    _processRecommendations.Add(process);
                }

                string reason = process.ViolationReason ?? "GPU";
                string details = "";
                
                if (reason == "GPU") details = $"{process.GpuUsage:F1}% GPU = ~{process.EstimatedFpsLoss} FPS lost";
                else if (reason == "CPU") details = $"{process.CpuUsage:F1}% CPU";
                else if (reason == "RAM") details = $"{process.RamUsageMB:F0} MB RAM";
                else if (reason == "Network") details = $"{process.NetworkUsageMB:F2} MB/s Network";
                
                PerformanceBannerText.Text = $"⚠️ {process.ProcessName}: {details}";
                PerformanceBanner.Visibility = Visibility.Visible;
            });
        }

        private void BtnWhitelistProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessData process)
            {
                WhitelistManager.Add("Process", process.ProcessName, process.Path);
                _processes.Remove(process);
                var rec = _processRecommendations.FirstOrDefault(r => r.ProcessName == process.ProcessName);
                if (rec != null) _processRecommendations.Remove(rec);
                LogMessage($"Whitelisted: {process.ProcessName}");
            }
        }

        private void BtnDesignateAsGame_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessData process)
            {
                if (!string.IsNullOrEmpty(process.ProcessName))
                {
                    _gameModeManager.AddUserGame(process.ProcessName);
                    LogMessage($"Designated as Game: {process.ProcessName}");
                }
            }
        }

        private void BtnApplyProcessRec_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessData process)
            {
                try
                {
                    var proc = System.Diagnostics.Process.GetProcessById(process.ProcessId);
                    proc.Kill();
                    _processRecommendations.Remove(process);
                    _gpuHistory.MarkAsKilled(process.ProcessName);
                    
                    // Record to action history
                    ActionHistory.Record(ActionType.ManualKill, "Process", process.ProcessName, 
                        process.Path, process.ProcessId, process.GpuUsage, 
                        "Killed from AI recommendations", "User");
                    
                    LogMessage($"Killed: {process.ProcessName}");
                }
                catch (Exception ex)
                {
                    LogMessage($"Error killing {process.ProcessName}: {ex.Message}");
                }
            }
        }

        private void BtnRecWhitelist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessData process)
            {
                WhitelistManager.Add("Process", process.ProcessName, process.Path);
                _processRecommendations.Remove(process);
                _gpuHistory.MarkAsWhitelisted(process.ProcessName);
                
                // Record to action history
                ActionHistory.Record(ActionType.WhitelistAdd, "Process", process.ProcessName, 
                    process.Path, reason: "Whitelisted from AI recommendations", source: "User");
                
                LogMessage($"Whitelisted from AI recs: {process.ProcessName}");
            }
        }

        private void BtnRecFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessData process)
            {
                if (!string.IsNullOrEmpty(process.Path) && File.Exists(process.Path))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{process.Path}\"");
                    LogMessage($"Opened folder: {process.ProcessName}");
                }
            }
        }

        private void BtnDismissBanner_Click(object sender, RoutedEventArgs e)
        {
            PerformanceBanner.Visibility = Visibility.Collapsed;
        }

        #region Game Mode
        private void InitializeGameModeUI()
        {
            if (_gameModeManager == null) return;
            
            GameModeComboBox.Items.Clear();
            foreach (var config in _gameModeManager.GetConfigs())
            {
                GameModeComboBox.Items.Add(config.Name);
            }
            if (GameModeComboBox.Items.Count > 0)
                GameModeComboBox.SelectedIndex = 0;
            
            GameModePanel.Visibility = Visibility.Visible;
            UpdateGameModeButtons();
        }

        private void BtnToggleGameMode_Click(object sender, RoutedEventArgs e)
        {
            if (_gameModeManager == null) return;

            if (_gameModeManager.IsActive)
            {
                // Deactivate
                _gameModeManager.Deactivate();
                GameModeStatusText.Text = "(Inactive)";
                GameModePanel.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(26, 58, 92));
                LogMessage("[GameMode] Deactivated");
            }
            else
            {
                // Activate
                if (GameModeComboBox.SelectedItem == null) 
                {
                    MessageBox.Show("Please select a game mode configuration first.");
                    return;
                }

                var selectedConfigName = GameModeComboBox.SelectedItem.ToString();
                var config = _gameModeManager.GetConfigs().FirstOrDefault(c => c.Name == selectedConfigName);
                
                if (config != null)
                {
                    config.AutoKillEnabled = AutoKillCheckBox.IsChecked == true;
                    _gameModeManager.Activate(config);
                    
                    GameModeStatusText.Text = $"Active: {config.Name}";
                    GameModePanel.Background = System.Windows.Media.Brushes.DarkGreen;
                    
                    LogMessage($"[GameMode] Activated: {config.Name} (AutoKill: {config.AutoKillEnabled})");
                }
            }
            
            UpdateGameModeButtons();
        }

        private void UpdateGameModeButtons()
        {
            bool isActive = _gameModeManager != null && _gameModeManager.IsActive;
            var brushConverter = new System.Windows.Media.BrushConverter();
            var greenBrush = (System.Windows.Media.Brush)brushConverter.ConvertFrom("#28A745");
            var redBrush = System.Windows.Media.Brushes.Crimson;

            // Tab Button
            if (BtnTabToggleGameMode != null)
            {
                BtnTabToggleGameMode.Content = isActive ? "STOP GAME MODE" : "START GAME MODE";
                BtnTabToggleGameMode.Background = isActive ? redBrush : greenBrush;
            }
        }

        private void BtnAddGameProcess_Click(object sender, RoutedEventArgs e)
        {
            var selector = new ProcessSelectorWindow("Select Game Process", "Add Game");
            selector.Owner = this;
            if (selector.ShowDialog() == true)
            {
                int count = 0;
                foreach (var name in selector.SelectedProcessNames)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _gameModeManager.AddUserGame(name);
                        count++;
                    }
                }
                
                if (count > 0)
                {
                    LogMessage($"[GameMode] Added {count} games manually.");
                    MessageBox.Show($"Added {count} games to Game Mode library.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void OnGameModeIntruderDetected(ProcessData process)
        {
            Dispatcher.Invoke(() =>
            {
                LogMessage($"[GameMode] Intruder: {process.ProcessName} using {process.GpuUsage:F1}% GPU");
            });
        }

        private void OnGameModeIntruderKilled(ProcessData process)
        {
            Dispatcher.Invoke(() =>
            {
                _gpuHistory.MarkAsKilled(process.ProcessName);
                
                // Record to action history
                ActionHistory.Record(ActionType.AutoKill, "Process", process.ProcessName, 
                    process.Path, process.ProcessId, process.GpuUsage, 
                    "Auto-killed by Game Mode", "GameMode");
                
                LogMessage($"[GameMode] Auto-killed: {process.ProcessName}");
            });
        }

        private void OnProcessKilledFromNotification(string processName, int? processId)
        {
            Dispatcher.Invoke(() =>
            {
                // Remove from process list if visible
                var process = _processes.FirstOrDefault(p => p.ProcessId == processId);
                if (process != null)
                {
                    _processes.Remove(process);
                }
                // Remove from recommendations if present
                var rec = _processRecommendations.FirstOrDefault(r => r.ProcessId == processId);
                if (rec != null)
                {
                    _processRecommendations.Remove(rec);
                }
                // Mark in GPU history
                _gpuHistory?.MarkAsKilled(processName);
                LogMessage($"[Notification] Killed: {processName}");
            });
        }

        private void OnProcessWhitelistedFromNotification(string processName, string path)
        {
            Dispatcher.Invoke(() =>
            {
                // Remove from process list
                var process = _processes.FirstOrDefault(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
                if (process != null)
                {
                    _processes.Remove(process);
                }
                // Remove from recommendations
                var rec = _processRecommendations.FirstOrDefault(r => r.Name.Equals(processName, StringComparison.OrdinalIgnoreCase));
                if (rec != null)
                {
                    _processRecommendations.Remove(rec);
                }
                // Mark in GPU history
                _gpuHistory?.MarkAsWhitelisted(processName);
                LogMessage($"[Notification] Whitelisted: {processName}");
            });
        }

        private void OnProcessSnoozedFromNotification(string processName)
        {
            Dispatcher.Invoke(() =>
            {
                LogMessage($"[Notification] Snoozed: {processName}");
            });
        }
        #endregion

        #region GPU History
        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            _gpuHistory.Clear();
            GpuAlertSnoozer.ClearSnooze();
            LogMessage("[GPU History] Cleared");
        }

        private void BtnExportHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(_gpuHistory.Events, Newtonsoft.Json.Formatting.Indented);
                Clipboard.SetText(json);
                LogMessage("[GPU History] Exported to clipboard");
            }
            catch (Exception ex)
            {
                LogMessage($"[GPU History] Export failed: {ex.Message}");
            }
        }

        private void BtnHistoryWhitelist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GpuDetectionEvent evt)
            {
                WhitelistManager.Add("Process", evt.ProcessName, evt.Path);
                _gpuHistory.MarkAsWhitelisted(evt.ProcessName);
                LogMessage($"[GPU History] Whitelisted: {evt.ProcessName}");
            }
        }

        private void BtnHistoryFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GpuDetectionEvent evt)
            {
                if (!string.IsNullOrEmpty(evt.Path) && File.Exists(evt.Path))
                {
                    var folder = Path.GetDirectoryName(evt.Path);
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{evt.Path}\"");
                    LogMessage($"[GPU History] Opened folder: {evt.ProcessName}");
                }
            }
        }

        private void BtnHistoryKill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is GpuDetectionEvent evt)
            {
                try
                {
                    var process = System.Diagnostics.Process.GetProcessById(evt.ProcessId);
                    process.Kill();
                    process.WaitForExit(5000);
                    _gpuHistory.MarkAsKilled(evt.ProcessName);
                    LogMessage($"[GPU History] Killed: {evt.ProcessName}");
                }
                catch (Exception ex)
                {
                    LogMessage($"[GPU History] Kill failed: {ex.Message}");
                }
            }
        }
        #endregion
        #endregion

        #region Services Tab
        private void BtnScanServices_Click(object sender, RoutedEventArgs e)
        {
            _services.Clear();
            var scanner = new Scanner();
            var results = scanner.ScanServices();
            foreach (var s in results)
            {
                // Set default selected values to current values
                s.UserSelectedStartup = s.CurrentStartup;
                s.UserSelectedState = s.CurrentState;
                _services.Add(s);
            }
            // Show all services in panel immediately
            ServiceActionsPanel.ItemsSource = _services;
            LogMessage(LanguageManager.GetString("LogScanComplete", _services.Count));
        }

        private void BtnCopyServicePrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_services.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("MsgNoScanData"));
                return;
            }
            var noXbox = ChkNoXbox?.IsChecked ?? false;
            var noStore = ChkNoStore?.IsChecked ?? false;
            var prompt = AiPromptBuilder.BuildServicePrompt(_services.ToList(), noXbox, noStore);
            Clipboard.SetText(prompt);
            LogMessage(LanguageManager.GetString("StatusCopied"));
        }

        private void BtnEditServicePrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_services.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("MsgNoScanData"));
                return;
            }

            var noXbox = ChkNoXbox?.IsChecked ?? false;
            var noStore = ChkNoStore?.IsChecked ?? false;
            var prompt = AiPromptBuilder.BuildServicePrompt(_services.ToList(), noXbox, noStore);
            OpenPromptEditor(prompt);
        }

        private void BtnPasteServiceAI_Click(object sender, RoutedEventArgs e)
        {
            var clipboard = Clipboard.GetText();
            
            // If we have scanned data, apply AI decisions to existing services
            if (_services.Count > 0)
            {
                var result = AiResponseParser.ParseAnalysis(clipboard);
                
                if (!string.IsNullOrWhiteSpace(result.Summary))
                {
                    ServicesSummaryText.Text = result.Summary;
                    ServicesSummaryBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    ServicesSummaryBanner.Visibility = Visibility.Collapsed;
                }

                ApplyServiceDecisions(result.Decisions);
            }
            else
            {
                // No scan data - parse directly from AI response
                var parsedServices = AiResponseParser.ParseServiceDataFromJson(clipboard);
                if (parsedServices.Count > 0)
                {
                    _services.Clear();
                    foreach (var service in parsedServices)
                    {
                        _services.Add(service);
                    }
                    ServiceActionsPanel.ItemsSource = _services;
                    LogMessage($"AI analysis complete. Found {parsedServices.Count} recommendations from pasted data.");
                }
                else
                {
                    MessageBox.Show("Could not parse any service data from clipboard. Please scan first or paste valid AI response.");
                }
            }
        }

        private void ApplyServiceDecisions(List<AiDecision> decisions)
        {
            _serviceRecommendations.Clear();
            var foundTargets = 0;

            foreach (var service in _services.ToList())
            {
                var decision = decisions.FirstOrDefault(d => string.Equals(d.Target, service.ServiceName, StringComparison.OrdinalIgnoreCase));
                if (decision != null)
                {
                    service.AiAction = decision.Action;
                    service.AiReason = decision.Reason;
                    service.IsRecommended = true;
                    
                    // Use AI recommended values as defaults (capitalized properly)
                    if (!string.IsNullOrEmpty(decision.RecommendedStartup))
                    {
                        // Capitalize first letter
                        service.UserSelectedStartup = char.ToUpper(decision.RecommendedStartup[0]) + decision.RecommendedStartup.Substring(1).ToLower();
                    }
                    else
                    {
                        service.UserSelectedStartup = decision.Action?.ToLower() == "disable" ? "Disabled" : "Manual";
                    }
                    
                    if (!string.IsNullOrEmpty(decision.RecommendedState))
                    {
                        service.UserSelectedState = char.ToUpper(decision.RecommendedState[0]) + decision.RecommendedState.Substring(1).ToLower();
                    }
                    else
                    {
                        service.UserSelectedState = "Stopped";
                    }
                    
                    _serviceRecommendations.Add(service);
                    foundTargets++;
                }
                else
                {
                    _services.Remove(service);
                }
            }
            
            LogMessage($"AI analysis complete. Found {foundTargets} recommendations.");
        }

        private void BtnApplyServiceWithSelection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ServiceData service)
            {
                try
                {
                    var previousState = BuildServiceStateSnapshot(service.CurrentStartup, service.CurrentState);
                    ApplyServiceConfiguration(service);
                    var appliedState = BuildServiceStateSnapshot(service.UserSelectedStartup, service.UserSelectedState);
                    var actionType = service.UserSelectedStartup?.Equals("Disabled", StringComparison.OrdinalIgnoreCase) == true
                        ? ActionType.ServiceDisabled
                        : ActionType.ServiceManual;
                    ActionHistory.Record(actionType, "Service", service.ServiceName, service.Path, reason: "Applied from Services tab", source: "User",
                        previousStateJson: previousState, appliedStateJson: appliedState);
                    LogMessage($"Applied to {service.ServiceName}: Startup={service.UserSelectedStartup}, State={service.UserSelectedState}");
                    _serviceRecommendations.Remove(service);
                    _services.Remove(service);
                }
                catch (Exception ex)
                {
                    LogMessage($"Error applying {service.ServiceName}: {ex.Message}");
                }
            }
        }

        private void BtnWhitelistService_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && (btn.Tag ?? btn.DataContext) is ServiceData service)
            {
                WhitelistManager.Add("Service", service.ServiceName, service.Path);
                _serviceRecommendations.Remove(service);
                _services.Remove(service);
                LogMessage($"Whitelisted service: {service.ServiceName}");
            }
        }

        private void BtnApplyServices_Click(object sender, RoutedEventArgs e)
        {
            var toApply = _serviceRecommendations.ToList();
            var applied = 0;
            foreach (var s in toApply)
            {
                try
                {
                    var previousState = BuildServiceStateSnapshot(s.CurrentStartup, s.CurrentState);
                    ApplyServiceConfiguration(s);
                    var appliedState = BuildServiceStateSnapshot(s.UserSelectedStartup, s.UserSelectedState);
                    var actionType = s.UserSelectedStartup?.Equals("Disabled", StringComparison.OrdinalIgnoreCase) == true
                        ? ActionType.ServiceDisabled
                        : ActionType.ServiceManual;
                    ActionHistory.Record(actionType, "Service", s.ServiceName, s.Path, reason: "Bulk apply from Services tab", source: "User",
                        previousStateJson: previousState, appliedStateJson: appliedState);
                    LogMessage($"Applied to {s.ServiceName}: Startup={s.UserSelectedStartup}, State={s.UserSelectedState}");
                    _serviceRecommendations.Remove(s);
                    _services.Remove(s);
                    applied++;
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] Failed service apply for {s.ServiceName}: {ex.Message}");
                }
            }
            LogMessage(LanguageManager.GetString("LogApplied", applied));
        }
        #endregion

        #region Tasks Tab
        private void BtnScanTasks_Click(object sender, RoutedEventArgs e)
        {
            _tasks.Clear();
            var scanner = new Scanner();
            var results = scanner.ScanTasks();
            foreach (var t in results)
                _tasks.Add(t);
            TaskActionsPanel.ItemsSource = _tasks;
            LogMessage(LanguageManager.GetString("LogScanComplete", _tasks.Count));
        }

        private void BtnCopyTaskPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_tasks.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("MsgNoScanData"));
                return;
            }
            var prompt = AiPromptBuilder.BuildTaskPrompt(_tasks.ToList());
            Clipboard.SetText(prompt);
            LogMessage(LanguageManager.GetString("StatusCopied"));
        }

        private void BtnEditTaskPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_tasks.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("MsgNoScanData"));
                return;
            }

            var prompt = AiPromptBuilder.BuildTaskPrompt(_tasks.ToList());
            OpenPromptEditor(prompt);
        }

        private void BtnPasteTaskAI_Click(object sender, RoutedEventArgs e)
        {
            var clipboard = Clipboard.GetText();
            
            if (_tasks.Count > 0)
            {
                var result = AiResponseParser.ParseAnalysis(clipboard);
                
                if (!string.IsNullOrWhiteSpace(result.Summary))
                {
                    TasksSummaryText.Text = result.Summary;
                    TasksSummaryBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    TasksSummaryBanner.Visibility = Visibility.Collapsed;
                }

                ApplyDecisionsToGrid(_tasks, result.Decisions);
            }
            else
            {
                var parsedTasks = AiResponseParser.ParseTaskDataFromJson(clipboard);
                if (parsedTasks.Count > 0)
                {
                    _tasks.Clear();
                    foreach (var task in parsedTasks)
                        _tasks.Add(task);
                    TaskActionsPanel.ItemsSource = _tasks;
                    LogMessage($"AI analysis complete. Found {parsedTasks.Count} recommendations from pasted data.");
                }
                else
                {
                    MessageBox.Show("Could not parse any task data from clipboard. Please scan first or paste valid AI response.");
                }
            }
        }

        private void BtnTaskProperties_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TaskData task)
            {
                try 
                {
                    // Open Task Scheduler
                    var psi = new System.Diagnostics.ProcessStartInfo("taskschd.msc") { UseShellExecute = true };
                    System.Diagnostics.Process.Start(psi);
                    
                    // Show details in a MessageBox so user can find it
                    string msg = $"Task Name: {task.TaskName}\n" +
                                 $"Path: {task.TaskPath}\n" +
                                 $"State: {task.State}\n" +
                                 $"Last Run: {task.LastRunTime}\n" +
                                 $"Next Run: {task.NextRunTime}\n\n" +
                                 "Task Scheduler has been opened. Please navigate to the task path to view properties.";
                    
                    MessageBox.Show(msg, "Task Properties", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage($"[Tasks] Opened Task Scheduler for: {task.TaskName}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open Task Scheduler: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnApplyTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TaskData task)
            {
                try
                {
                    var previousState = BuildTaskStateSnapshot(task.IsEnabled);
                    if (!DisableScheduledTask(task))
                    {
                        MessageBox.Show($"Unable to disable task '{task.TaskName}'.", "Disable Task", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var appliedState = BuildTaskStateSnapshot(false);
                    ActionHistory.Record(ActionType.TaskDisabled, "Task", task.TaskName, task.TaskPath, reason: "Disabled from Tasks tab", source: "User",
                        previousStateJson: previousState, appliedStateJson: appliedState);
                    LogMessage($"Disabled task: {task.TaskName}");
                    _tasks.Remove(task);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to disable task '{task.TaskName}': {ex.Message}", "Disable Task Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage($"[ERROR] Failed to disable task {task.TaskName}: {ex.Message}");
                }
            }
        }

        private void BtnWhitelistTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is TaskData task)
            {
                WhitelistManager.Add("Task", task.TaskName, task.TaskPath);
                _tasks.Remove(task);
                LogMessage($"Whitelisted task: {task.TaskName}");
            }
        }

        private void BtnApplyTasks_Click(object sender, RoutedEventArgs e)
        {
            var toApply = _tasks.Where(t => !string.IsNullOrEmpty(t.AiAction)).ToList();
            var applied = 0;
            foreach (var t in toApply)
            {
                try
                {
                    var previousState = BuildTaskStateSnapshot(t.IsEnabled);
                    if (!ApplyTaskAiRecommendation(t))
                    {
                        continue;
                    }

                    var appliedState = BuildTaskStateSnapshot(false);
                    ActionHistory.Record(ActionType.TaskDisabled, "Task", t.TaskName, t.TaskPath, reason: "Bulk apply from Tasks tab", source: "User",
                        previousStateJson: previousState, appliedStateJson: appliedState);
                    _tasks.Remove(t);
                    applied++;
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] Failed task apply for {t.TaskName}: {ex.Message}");
                }
            }
            LogMessage(LanguageManager.GetString("LogApplied", applied));
        }
        #endregion

        #region Autoruns Tab
        private void BtnScanAutoruns_Click(object sender, RoutedEventArgs e)
        {
            _autoruns.Clear();
            var scanner = new Scanner();
            var results = scanner.ScanAutoruns();
            foreach (var a in results)
                _autoruns.Add(a);
            AutorunActionsPanel.ItemsSource = _autoruns;
            LogMessage(LanguageManager.GetString("LogScanComplete", _autoruns.Count));
        }

        private void BtnCopyAutorunPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_autoruns.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("MsgNoScanData"));
                return;
            }
            var prompt = AiPromptBuilder.BuildAutorunPrompt(_autoruns.ToList());
            Clipboard.SetText(prompt);
            LogMessage(LanguageManager.GetString("StatusCopied"));
        }

        private void BtnEditAutorunPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (_autoruns.Count == 0)
            {
                MessageBox.Show(LanguageManager.GetString("MsgNoScanData"));
                return;
            }

            var prompt = AiPromptBuilder.BuildAutorunPrompt(_autoruns.ToList());
            OpenPromptEditor(prompt);
        }

        private void BtnPasteAutorunAI_Click(object sender, RoutedEventArgs e)
        {
            var clipboard = Clipboard.GetText();
            
            if (_autoruns.Count > 0)
            {
                var result = AiResponseParser.ParseAnalysis(clipboard);
                
                if (!string.IsNullOrWhiteSpace(result.Summary))
                {
                    AutorunsSummaryText.Text = result.Summary;
                    AutorunsSummaryBanner.Visibility = Visibility.Visible;
                }
                else
                {
                    AutorunsSummaryBanner.Visibility = Visibility.Collapsed;
                }

                ApplyDecisionsToGrid(_autoruns, result.Decisions);
            }
            else
            {
                var parsedAutoruns = AiResponseParser.ParseAutorunDataFromJson(clipboard);
                if (parsedAutoruns.Count > 0)
                {
                    _autoruns.Clear();
                    foreach (var autorun in parsedAutoruns)
                        _autoruns.Add(autorun);
                    AutorunActionsPanel.ItemsSource = _autoruns;
                    LogMessage($"AI analysis complete. Found {parsedAutoruns.Count} recommendations from pasted data.");
                }
                else
                {
                    MessageBox.Show("Could not parse any autorun data from clipboard. Please scan first or paste valid AI response.");
                }
            }
        }

        private void BtnApplyAutorun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutorunData autorun)
            {
                try
                {
                    var previousState = BuildAutorunStateSnapshot(autorun.Location, autorun.EntryName, autorun.Command);
                    if (!DisableAutorunEntry(autorun))
                    {
                        MessageBox.Show($"Unable to disable autorun '{autorun.EntryName}'.", "Disable Autorun", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var appliedState = BuildAutorunStateSnapshot(autorun.Location, autorun.EntryName, null);
                    ActionHistory.Record(ActionType.AutorunDisabled, "Autorun", autorun.EntryName, autorun.Path, reason: "Disabled from Autoruns tab", source: "User",
                        previousStateJson: previousState, appliedStateJson: appliedState);
                    LogMessage($"Disabled autorun: {autorun.EntryName}");
                    _autoruns.Remove(autorun);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to disable autorun '{autorun.EntryName}': {ex.Message}", "Disable Autorun Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    LogMessage($"[ERROR] Failed to disable autorun {autorun.EntryName}: {ex.Message}");
                }
            }
        }

        private void BtnBrowseAutorunFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select startup folder to scan for autoruns",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                var folderPath = dialog.FolderName;
                LogMessage($"Scanning folder for autoruns: {folderPath}");
                
                try
                {
                    var entries = ScanFolderForExecutables(folderPath);
                    foreach (var entry in entries)
                    {
                        entry.Icon = LoadIconForPath(entry.ExecutablePath);
                        _autoruns.Add(entry);
                    }
                    
                    AutorunActionsPanel.ItemsSource = _autoruns;
                    LogMessage($"Found {entries.Count} executables in folder");
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] Failed to scan folder: {ex.Message}");
                }
            }
        }

        private List<AutorunData> ScanFolderForExecutables(string folderPath)
        {
            var entries = new List<AutorunData>();
            
            try
            {
                var exeFiles = System.IO.Directory.GetFiles(folderPath, "*.exe", System.IO.SearchOption.TopDirectoryOnly);
                
                foreach (var exePath in exeFiles)
                {
                    try
                    {
                        var fileName = System.IO.Path.GetFileNameWithoutExtension(exePath);
                        var company = GetFileCompanyName(exePath);
                        
                        entries.Add(new AutorunData
                        {
                            EntryName = fileName,
                            Command = exePath,
                            Path = exePath,
                            ExecutablePath = exePath,
                            Location = "User Folder",
                            IsEnabled = true,
                            Company = company
                        });
                    }
                    catch { /* Skip problematic files */ }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] Error scanning folder: {ex.Message}");
            }
            
            return entries;
        }

        private System.Windows.Media.ImageSource LoadIconForPath(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                    return null;

                using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(filePath))
                {
                    if (icon == null) return null;
                    
                    var bitmap = icon.ToBitmap();
                    var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        bitmap.GetHbitmap(),
                        IntPtr.Zero,
                        System.Windows.Int32Rect.Empty,
                        System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    bitmapSource.Freeze();
                    return bitmapSource;
                }
            }
            catch
            {
                return null;
            }
        }

        private string GetFileCompanyName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                    return "";
                
                var fileVersionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                return fileVersionInfo.CompanyName ?? "";
            }
            catch { return ""; }
        }

        private void BtnWhitelistAutorun_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is AutorunData autorun)
            {
                WhitelistManager.Add("Autorun", autorun.EntryName, autorun.Path);
                _autoruns.Remove(autorun);
                LogMessage($"Whitelisted autorun: {autorun.EntryName}");
            }
        }

        private void BtnApplyAutoruns_Click(object sender, RoutedEventArgs e)
        {
            var toApply = _autoruns.Where(a => !string.IsNullOrEmpty(a.AiAction)).ToList();
            var applied = 0;
            foreach (var a in toApply)
            {
                try
                {
                    var previousState = BuildAutorunStateSnapshot(a.Location, a.EntryName, a.Command);
                    if (!ApplyAutorunAiRecommendation(a))
                    {
                        continue;
                    }

                    var appliedState = BuildAutorunStateSnapshot(a.Location, a.EntryName, null);
                    ActionHistory.Record(ActionType.AutorunDisabled, "Autorun", a.EntryName, a.Path, reason: "Bulk apply from Autoruns tab", source: "User",
                        previousStateJson: previousState, appliedStateJson: appliedState);
                    _autoruns.Remove(a);
                    applied++;
                }
                catch (Exception ex)
                {
                    LogMessage($"[ERROR] Failed autorun apply for {a.EntryName}: {ex.Message}");
                }
            }
            LogMessage(LanguageManager.GetString("LogApplied", applied));
        }

        private void BtnCreateOptimizationBackup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var servicesForBackup = _services.Concat(_serviceRecommendations).DistinctBy(s => s.ServiceName).ToList();
                var tasksForBackup = _tasks.ToList();
                var autorunsForBackup = _autoruns.ToList();

                if (servicesForBackup.Count == 0 && tasksForBackup.Count == 0 && autorunsForBackup.Count == 0)
                {
                    MessageBox.Show("No data available to backup. Run Services/Tasks/Autoruns scans first.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var defaultName = $"Backup {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
                var backupName = AskTextInput("Create Backup", "Backup name:", defaultName);
                if (backupName == null)
                {
                    return;
                }

                var backup = OptimizationBackupManager.CreateNamedBackup(backupName, servicesForBackup, tasksForBackup, autorunsForBackup);
                LogMessage($"Backup created: {backup.BackupName} ({backup.BackupId})");
                RefreshBackupList();
                MessageBox.Show("Backup created successfully.", "Backup", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] Backup creation failed: {ex.Message}");
                MessageBox.Show($"Failed to create backup: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestoreOptimizationBackup_Click(object sender, RoutedEventArgs e)
        {
            BtnOpenBackupsTab_Click(sender, e);
        }

        private void BtnOpenBackupsTab_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 6;
            PageTitle.Text = "Backups";
            RefreshBackupList();
        }

        private void BtnRefreshBackups_Click(object sender, RoutedEventArgs e)
        {
            RefreshBackupList();
        }

        private void BtnRestoreSelectedBackup_Click(object sender, RoutedEventArgs e)
        {
            var selected = BackupsDataGrid.SelectedItem as OptimizationBackupInfo;
            if (selected == null)
            {
                MessageBox.Show("Select a backup first.", "Restore Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Restore backup '{selected.BackupName}'?", "Restore Backup", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                var result = OptimizationBackupManager.RestoreBackupById(selected.BackupId);
                LogMessage($"Backup restore finished. Success={result.Succeeded}, Failed={result.Failed}");
                if (result.Errors.Count > 0)
                {
                    var details = string.Join(Environment.NewLine, result.Errors.Take(8));
                    MessageBox.Show($"Restore completed with errors.\nSuccess: {result.Succeeded}\nFailed: {result.Failed}\n\n{details}", "Restore Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"Restore completed.\nSuccess: {result.Succeeded}", "Restore Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                RefreshOptimizationLists();
            }
            catch (Exception ex)
            {
                LogMessage($"[ERROR] Backup restore failed: {ex.Message}");
                MessageBox.Show($"Restore failed: {ex.Message}", "Restore Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRenameBackup_Click(object sender, RoutedEventArgs e)
        {
            var selected = BackupsDataGrid.SelectedItem as OptimizationBackupInfo;
            if (selected == null)
            {
                MessageBox.Show("Select a backup first.", "Rename Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var name = AskTextInput("Rename Backup", "New backup name:", selected.BackupName);
            if (name == null)
            {
                return;
            }

            if (!OptimizationBackupManager.RenameBackup(selected.BackupId, name))
            {
                MessageBox.Show("Failed to rename backup.", "Rename Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshBackupList();
            LogMessage($"Backup renamed: {selected.BackupName} -> {name}");
        }

        private void BtnDeleteBackup_Click(object sender, RoutedEventArgs e)
        {
            var selected = BackupsDataGrid.SelectedItem as OptimizationBackupInfo;
            if (selected == null)
            {
                MessageBox.Show("Select a backup first.", "Delete Backup", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show($"Delete backup '{selected.BackupName}'?", "Delete Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }

            if (!OptimizationBackupManager.DeleteBackup(selected.BackupId))
            {
                MessageBox.Show("Failed to delete backup.", "Delete Backup", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            RefreshBackupList();
            LogMessage($"Backup deleted: {selected.BackupName}");
        }

        private void RefreshBackupList()
        {
            if (_backupInfos == null)
            {
                return;
            }

            _backupInfos.Clear();
            foreach (var backup in OptimizationBackupManager.ListBackups())
            {
                _backupInfos.Add(backup);
            }
        }

        private string AskTextInput(string title, string label, string defaultValue = "")
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.White
            };

            var root = new Grid { Margin = new Thickness(14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8) };
            Grid.SetRow(text, 0);

            var input = new TextBox { Text = defaultValue ?? string.Empty, MinWidth = 340, Padding = new Thickness(6) };
            Grid.SetRow(input, 1);

            var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 90, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Cancel", Width = 90 };
            panel.Children.Add(ok);
            panel.Children.Add(cancel);
            Grid.SetRow(panel, 2);

            root.Children.Add(text);
            root.Children.Add(input);
            root.Children.Add(panel);
            dialog.Content = root;

            string result = null;
            ok.Click += (_, _) =>
            {
                var value = input.Text?.Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    MessageBox.Show("Value cannot be empty.", title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                result = value;
                dialog.DialogResult = true;
                dialog.Close();
            };
            cancel.Click += (_, _) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            dialog.ShowDialog();
            return result;
        }

        private void OpenPromptEditor(string prompt)
        {
            var editor = new PromptEditorWindow(prompt) { Owner = this };
            editor.ShowDialog();
        }

        private void BtnOpenServicesMsc_Click(object sender, RoutedEventArgs e)
        {
            OpenWindowsTool("services.msc", "Services console");
        }

        private void BtnOpenTaskScheduler_Click(object sender, RoutedEventArgs e)
        {
            OpenWindowsTool("taskschd.msc", "Task Scheduler");
        }

        private void BtnOpenStartupApps_Click(object sender, RoutedEventArgs e)
        {
            OpenWindowsTool("ms-settings:startupapps", "Startup Apps settings");
        }

        private void OpenWindowsTool(string fileName, string displayName)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(fileName) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
                LogMessage($"Opened {displayName}.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open {displayName}: {ex.Message}", "Open Tool Error", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"[ERROR] Failed to open {displayName}: {ex.Message}");
            }
        }

        private bool DisableScheduledTask(TaskData task)
        {
            if (task == null || string.IsNullOrWhiteSpace(task.TaskPath))
            {
                return false;
            }

            using (var taskService = new Microsoft.Win32.TaskScheduler.TaskService())
            {
                var scheduledTask = taskService.GetTask(task.TaskPath);
                if (scheduledTask == null)
                {
                    return false;
                }

                if (scheduledTask.Enabled)
                {
                    scheduledTask.Enabled = false;
                }
            }

            return true;
        }

        private bool ApplyTaskAiRecommendation(TaskData task)
        {
            if (task == null)
            {
                return false;
            }

            var action = task.AiAction?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return false;
            }

            return action.Contains("disable") || action.Contains("stop") || action.Contains("delete")
                ? DisableScheduledTask(task)
                : false;
        }

        private bool DisableAutorunEntry(AutorunData autorun)
        {
            if (autorun == null || string.IsNullOrWhiteSpace(autorun.EntryName))
            {
                return false;
            }

            bool removed = false;
            removed |= TryDeleteAutorunValue(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", autorun.EntryName);
            removed |= TryDeleteAutorunValue(Microsoft.Win32.Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", autorun.EntryName);
            removed |= TryDeleteAutorunValue(Microsoft.Win32.Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", autorun.EntryName);
            return removed;
        }

        private bool TryDeleteAutorunValue(Microsoft.Win32.RegistryKey root, string subKeyPath, string valueName)
        {
            using (var key = root.OpenSubKey(subKeyPath, writable: true))
            {
                if (key?.GetValue(valueName) == null)
                {
                    return false;
                }

                key.DeleteValue(valueName, false);
                return true;
            }
        }

        private bool ApplyAutorunAiRecommendation(AutorunData autorun)
        {
            if (autorun == null)
            {
                return false;
            }

            var action = autorun.AiAction?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return false;
            }

            return action.Contains("disable") || action.Contains("stop") || action.Contains("delete")
                ? DisableAutorunEntry(autorun)
                : false;
        }

        private void ApplyServiceConfiguration(ServiceData service)
        {
            if (service == null || string.IsNullOrWhiteSpace(service.ServiceName))
            {
                throw new InvalidOperationException("Invalid service payload.");
            }

            var startupType = service.UserSelectedStartup?.Trim().ToLowerInvariant() switch
            {
                "automatic" => 2,
                "delayed" => 2,
                "disabled" => 4,
                _ => 3
            };

            using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service.ServiceName}", writable: true))
            {
                if (key == null)
                {
                    throw new InvalidOperationException("Service registry key not found.");
                }

                key.SetValue("Start", startupType, Microsoft.Win32.RegistryValueKind.DWord);
            }

            using var serviceController = new System.ServiceProcess.ServiceController(service.ServiceName);
            var desiredState = service.UserSelectedState?.Trim().ToLowerInvariant();
            if (desiredState == "running" && serviceController.Status != System.ServiceProcess.ServiceControllerStatus.Running && startupType != 4)
            {
                serviceController.Start();
            }
            else if ((desiredState == "stopped" || desiredState == "paused" || startupType == 4) &&
                     serviceController.Status == System.ServiceProcess.ServiceControllerStatus.Running)
            {
                serviceController.Stop();
            }
        }

        private void RefreshOptimizationLists()
        {
            var scanner = new Scanner();

            _services.Clear();
            _serviceRecommendations.Clear();
            foreach (var service in scanner.ScanServices())
            {
                service.UserSelectedStartup = service.CurrentStartup;
                service.UserSelectedState = service.CurrentState;
                _services.Add(service);
            }
            ServiceActionsPanel.ItemsSource = _services;

            _tasks.Clear();
            foreach (var task in scanner.ScanTasks())
            {
                _tasks.Add(task);
            }
            TaskActionsPanel.ItemsSource = _tasks;

            _autoruns.Clear();
            foreach (var autorun in scanner.ScanAutoruns())
            {
                _autoruns.Add(autorun);
            }
            AutorunActionsPanel.ItemsSource = _autoruns;
        }

        private static string BuildServiceStateSnapshot(string startup, string state)
        {
            return JsonConvert.SerializeObject(new
            {
                Startup = startup ?? "Unknown",
                State = state ?? "Unknown"
            });
        }

        private static string BuildTaskStateSnapshot(bool isEnabled)
        {
            return JsonConvert.SerializeObject(new
            {
                Enabled = isEnabled
            });
        }

        private static string BuildAutorunStateSnapshot(string location, string entryName, string command)
        {
            return JsonConvert.SerializeObject(new
            {
                Location = location ?? "Unknown",
                Name = entryName ?? "Unknown",
                Present = !string.IsNullOrWhiteSpace(command),
                Command = command ?? ""
            });
        }
        #endregion

        #region Settings & Help
        private void BtnViewWhitelist_Click(object sender, RoutedEventArgs e)
        {
            var whitelistWindow = new WhitelistWindow();
            whitelistWindow.ShowDialog();
        }

        private void BtnViewBlacklist_Click(object sender, RoutedEventArgs e)
        {
            var blacklistWindow = new BlacklistWindow();
            blacklistWindow.ShowDialog();
        }

        private void BtnBlacklistProcess_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ProcessData process)
            {
                var name = process.ProcessName;
                if (MessageBox.Show($"Add '{name}' to Blacklist?\nIt will be auto-killed in Game Mode.", "Add to Blacklist", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    BlacklistManager.Add(name, true);
                    LogMessage($"Added '{name}' to Blacklist.");
                }
            }
        }

        private void BtnViewHistory_Click(object sender, RoutedEventArgs e)
        {
            var historyWindow = new HistoryWindow();
            historyWindow.ShowDialog();
        }
        #endregion

        #region Helper Methods
        private void ApplyDecisionsToGrid<T>(ObservableCollection<T> collection, List<AiDecision> decisions) where T : SystemDataBase
        {
            var toRemove = new List<T>();
            var foundTargets = 0;

            foreach (var item in collection.ToList())
            {
                var decision = decisions.FirstOrDefault(d => string.Equals(d.Target, item.Name, StringComparison.OrdinalIgnoreCase));
                if (decision != null)
                {
                    item.AiAction = decision.Action;
                    item.AiReason = decision.Reason;
                    item.IsRecommended = true;
                    foundTargets++;
                }
                else
                {
                    // If no recommendation from AI, we remove it from the grid to show ONLY changes
                    toRemove.Add(item);
                }
            }

            foreach (var item in toRemove)
            {
                collection.Remove(item);
            }
            
            LogMessage($"AI analysis complete. Found {foundTargets} recommendations.");
        }

        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var line = $"[{timestamp}] {message}\n";
                if (DashboardLogTextBlock != null) DashboardLogTextBlock.Text += line;
                if (GameModeLogTextBlock != null) GameModeLogTextBlock.Text += line;
            });
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            var logText = DashboardLogTextBlock?.Text;
            if (!string.IsNullOrWhiteSpace(logText))
            {
                Clipboard.SetText(logText);
                LogMessage("[SYSTEM] Activity log copied to clipboard");
            }
            else
            {
                MessageBox.Show("No logs to copy.", "Copy Logs", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        #endregion

        #region Other Apps Tab
        private void RunPowerShellCommand(string command)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    UseShellExecute = true,
                    Verb = "runas" // Request Admin
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to run command: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenWebsite(string url)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open website: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRunChrisTitus_Click(object sender, RoutedEventArgs e)
        {
            RunPowerShellCommand("iwr -useb https://christitus.com/win | iex");
        }

        private void BtnWebChrisTitus_Click(object sender, RoutedEventArgs e)
        {
            OpenWebsite("https://christitus.com");
        }

        private void BtnRunWin11Debloat_Click(object sender, RoutedEventArgs e)
        {
            RunPowerShellCommand("& ([scriptblock]::Create((irm 'https://debloat.raphi.re/')))");
        }

        private void BtnWebWin11Debloat_Click(object sender, RoutedEventArgs e)
        {
            OpenWebsite("https://debloat.raphi.re/");
        }

        private void BtnRunDebloaterTool_Click(object sender, RoutedEventArgs e)
        {
            RunPowerShellCommand("iex (iwr 'https://github.com/megsystem/DebloaterTool/raw/refs/heads/main/External/Scripts/DebloaterTool.ps1')");
        }

        private void BtnWebDebloaterTool_Click(object sender, RoutedEventArgs e)
        {
            OpenWebsite("https://github.com/megsystem/DebloaterTool");
        }
        #endregion
    }
}
