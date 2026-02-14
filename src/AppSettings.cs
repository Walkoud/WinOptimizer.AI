using System;
using System.IO;
using Newtonsoft.Json;

namespace WinOptimizer.AI
{
    public class AppSettings
    {
        private static AppSettings _instance;
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Instance
        {
            get
            {
                if (_instance == null)
                {
                    Load();
                }
                return _instance;
            }
        }

        // General Settings
        public string Language { get; set; } = "English";

        // Monitoring Settings
        public double GpuThresholdPercent { get; set; } = 5.0;
        public double CpuThresholdPercent { get; set; } = 50.0;
        public double RamThresholdMB { get; set; } = 2048.0;
        public double NetworkThresholdMB { get; set; } = 10.0;

        public bool MonitorGpu { get; set; } = true;
        public bool MonitorCpu { get; set; } = true;
        public bool MonitorRam { get; set; } = true;
        public bool MonitorNetwork { get; set; } = true;

        public int AlertDurationSeconds { get; set; } = 300;
        public int CheckIntervalSeconds { get; set; } = 10;
        public double GpuDisplayThresholdPercent { get; set; } = 0.1;
        public bool LowImpactMode { get; set; } = true;

        // Notification/Alert Settings
        public bool EnableWindowsNotifications { get; set; } = true;
        public bool ShowKillButtonInNotification { get; set; } = true;
        public bool ShowWhitelistButtonInNotification { get; set; } = true;

        // Specific Alert Toggles (Default: Only GPU is true as requested)
        public bool AlertGpu { get; set; } = true;
        public bool AlertCpu { get; set; } = false;
        public bool AlertRam { get; set; } = false;
        public bool AlertNetwork { get; set; } = false;

        // Auto-Kill Global Settings
        public bool AutoKillGpuGlobal { get; set; } = false;
        public bool AutoKillCpuGlobal { get; set; } = false;
        public bool AutoKillRamGlobal { get; set; } = false;
        public bool AutoKillNetworkGlobal { get; set; } = false;

        // Auto-Kill Game Mode Settings
        public bool AutoKillGpuGameMode { get; set; } = false;
        public bool AutoKillCpuGameMode { get; set; } = false;
        public bool AutoKillRamGameMode { get; set; } = false;
        public bool AutoKillNetworkGameMode { get; set; } = false;

        public static void Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    _instance = JsonConvert.DeserializeObject<AppSettings>(json);
                }
            }
            catch { }

            if (_instance == null)
            {
                _instance = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
    }
}
