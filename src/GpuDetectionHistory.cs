using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Represents a GPU usage detection event for history tracking
    /// </summary>
    public class GpuDetectionEvent : INotifyPropertyChanged
    {
        private DateTime _timestamp;
        private string _processName;
        private int _processId;
        private string _path;
        private double _gpuUsage;
        private double _fpsLoss;
        private bool _isWhitelisted;
        private string _action;

        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; OnPropertyChanged(nameof(Timestamp)); }
        }

        public string ProcessName
        {
            get => _processName;
            set { _processName = value; OnPropertyChanged(nameof(ProcessName)); }
        }

        public int ProcessId
        {
            get => _processId;
            set { _processId = value; OnPropertyChanged(nameof(ProcessId)); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(nameof(Path)); }
        }

        public double GpuUsage
        {
            get => _gpuUsage;
            set { _gpuUsage = value; OnPropertyChanged(nameof(GpuUsage)); }
        }

        public double FpsLoss
        {
            get => _fpsLoss;
            set { _fpsLoss = value; OnPropertyChanged(nameof(FpsLoss)); }
        }

        public bool IsWhitelisted
        {
            get => _isWhitelisted;
            set { _isWhitelisted = value; OnPropertyChanged(nameof(IsWhitelisted)); }
        }

        public string Action
        {
            get => _action;
            set { _action = value; OnPropertyChanged(nameof(Action)); }
        }

        public string TimeAgo => GetTimeAgo(Timestamp);

        private string GetTimeAgo(DateTime timestamp)
        {
            var diff = DateTime.Now - timestamp;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{diff.TotalMinutes:F0}m ago";
            if (diff.TotalHours < 24) return $"{diff.TotalHours:F0}h ago";
            return $"{diff.TotalDays:F0}d ago";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// Manages GPU detection history
    /// </summary>
    public class GpuDetectionHistory
    {
        public ObservableCollection<GpuDetectionEvent> Events { get; } = new();

        public void AddDetection(ProcessData process, string action = null)
        {
            // Check if we already have a recent detection for this process (within last 5 minutes)
            var recent = Events.FirstOrDefault(e => 
                e.ProcessName == process.ProcessName && 
                (DateTime.Now - e.Timestamp).TotalMinutes < 5);

            if (recent != null)
            {
                // Update existing entry
                recent.GpuUsage = process.GpuUsage;
                recent.FpsLoss = process.EstimatedFpsLoss;
                recent.Timestamp = DateTime.Now;
                recent.Action = action ?? recent.Action;
            }
            else
            {
                // Add new entry
                Events.Insert(0, new GpuDetectionEvent
                {
                    Timestamp = DateTime.Now,
                    ProcessName = process.ProcessName,
                    ProcessId = process.ProcessId,
                    Path = process.Path,
                    GpuUsage = process.GpuUsage,
                    FpsLoss = process.EstimatedFpsLoss,
                    IsWhitelisted = WhitelistManager.IsWhitelisted(process.ProcessName),
                    Action = action
                });

                // Keep only last 100 events
                while (Events.Count > 100)
                    Events.RemoveAt(Events.Count - 1);
            }
        }

        public void MarkAsWhitelisted(string processName)
        {
            foreach (var evt in Events.Where(e => e.ProcessName == processName))
            {
                evt.IsWhitelisted = true;
                evt.Action = "Whitelisted";
            }
        }

        public void MarkAsKilled(string processName)
        {
            foreach (var evt in Events.Where(e => e.ProcessName == processName))
            {
                evt.Action = "Killed";
            }
        }

        public void Clear()
        {
            Events.Clear();
        }
    }
}
