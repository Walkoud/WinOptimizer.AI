namespace WinOptimizer.AI
{
    /// <summary>
    /// Process data for real-time GPU/CPU monitoring
    /// </summary>
    public class ProcessData : SystemDataBase
    {
        public override string Type => "Process";
        public override string Name => ProcessName;
        
        public string ProcessName { get; set; }
        public int ProcessId { get; set; }
        public double CpuUsage { get; set; }
        public double GpuUsage { get; set; }
        public double RamUsageMB { get; set; }
        public double NetworkUsageMB { get; set; } // MB/s (IO Proxy)
        public string WindowTitle { get; set; }
        public string Status { get; set; }
        public bool IsResponding { get; set; }
        
        // For GPU monitoring
        public double GpuUsageDuration { get; set; } // in minutes
        public int EstimatedFpsLoss { get; set; }
        public bool IsGame { get; set; }
        public string ViolationReason { get; set; }
        
        public override string GetAiPromptData()
        {
            return $"{{\"name\":\"{EscapeForJson(ProcessName)}\",\"path\":\"{EscapeForJson(Path)}\",\"company\":\"{EscapeForJson(Company)}\",\"cpu\":{CpuUsage:F1},\"gpu\":{GpuUsage:F1},\"ram_mb\":{RamUsageMB:F0},\"window_title\":\"{EscapeForJson(WindowTitle)}\",\"status\":\"{(IsResponding ? "responding" : "not_responding")}\",\"is_game\":{(IsGame ? "true" : "false")},\"fps_loss_estimate\":{EstimatedFpsLoss}}}";
        }

        /// <summary>
        /// Calculate estimated FPS loss based on GPU usage
        /// 1% GPU ≈ 1-2 FPS loss
        /// </summary>
        public void CalculatePerformanceLoss()
        {
            // Formula: 1% GPU ≈ 1.5 FPS loss on average
            EstimatedFpsLoss = (int)(GpuUsage * 1.5);
        }

        public bool ShouldIgnoreGpuAlert()
        {
            // Ignore Microsoft, NVIDIA, AMD, Intel processes
            if (string.IsNullOrEmpty(Company)) return false;
            
            var trustedCompanies = new[] { 
                "Microsoft", "NVIDIA", "AMD", "Intel", 
                "Microsoft Corporation", "NVIDIA Corporation", 
                "Advanced Micro Devices", "Intel Corporation" 
            };
            
            foreach (var trusted in trustedCompanies)
            {
                if (Company.IndexOf(trusted, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            
            return false;
        }
    }

    /// <summary>
    /// Service data for AI analysis
    /// </summary>
    public class ServiceData : SystemDataBase
    {
        public override string Type => "Service";
        public override string Name => ServiceName;
        
        public string ServiceName { get; set; }
        public string DisplayName { get; set; }
        public string CurrentStartup { get; set; } // Automatic, Manual, Disabled
        public string CurrentState { get; set; } // Running, Stopped
        public string StartType { get; set; }
        public bool IsMicrosoft { get; set; }
        
        // User selected values for manual override
        private string _userSelectedStartup = "Disabled";
        public string UserSelectedStartup
        {
            get => _userSelectedStartup;
            set { _userSelectedStartup = value; OnPropertyChanged(); }
        }

        private string _userSelectedState = "Stopped";
        public string UserSelectedState
        {
            get => _userSelectedState;
            set { _userSelectedState = value; OnPropertyChanged(); }
        }
        
        public override string GetAiPromptData()
        {
            // Convert StartType to numeric: Automatic=2, Manual=3, Disabled=4
            int startTypeNum = CurrentStartup?.ToLower() switch
            {
                "automatic" => 2,
                "disabled" => 4,
                _ => 3 // Manual or default
            };
            
            // Convert State to numeric: Stopped=1, Running=4
            int statusNum = CurrentState?.ToLower() switch
            {
                "running" => 4,
                _ => 1 // Stopped or default
            };
            
            return $"{{\"Name\":\"{EscapeForJson(ServiceName)}\",\"DisplayName\":\"{EscapeForJson(DisplayName)}\",\"StartType\":{startTypeNum},\"Status\":{statusNum}}}";
        }
    }

    /// <summary>
    /// Scheduled Task data for AI analysis
    /// </summary>
    public class TaskData : SystemDataBase
    {
        public override string Type => "ScheduledTask";
        public override string Name => TaskName;
        
        public string TaskName { get; set; }
        public string TaskPath { get; set; }
        public string State { get; set; }
        public string LastRunTime { get; set; }
        public string NextRunTime { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsMicrosoft { get; set; }
        public string ActionCommand { get; set; }
        
        public override string GetAiPromptData()
        {
            return $"{{\"name\":\"{EscapeForJson(TaskName)}\",\"path\":\"{EscapeForJson(TaskPath)}\",\"state\":\"{State}\",\"enabled\":{(IsEnabled ? "true" : "false")},\"is_microsoft\":{(IsMicrosoft ? "true" : "false")},\"action\":\"{EscapeForJson(ActionCommand)}\",\"last_run\":\"{LastRunTime}\",\"next_run\":\"{NextRunTime}\"}}";
        }
    }

    /// <summary>
    /// Autorun/Startup data for AI analysis
    /// </summary>
    public class AutorunData : SystemDataBase
    {
        public override string Type => "Autorun";
        public override string Name => EntryName;
        
        public string EntryName { get; set; }
        public string Command { get; set; }
        public string Location { get; set; } // HKLM, HKCU, etc.
        public bool IsEnabled { get; set; }
        
        // For icon display
        public string ExecutablePath { get; set; }
        public System.Windows.Media.ImageSource Icon { get; set; }
        
        public override string GetAiPromptData()
        {
            return $"{{\"name\":\"{EscapeForJson(EntryName)}\",\"command\":\"{EscapeForJson(Command)}\",\"path\":\"{EscapeForJson(Path)}\",\"company\":\"{EscapeForJson(Company)}\",\"location\":\"{Location}\",\"enabled\":{(IsEnabled ? "true" : "false")}}}";
        }
    }
}
