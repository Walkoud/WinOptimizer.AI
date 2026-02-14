using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Interface for all items that can be analyzed by AI
    /// </summary>
    public interface IAiAnalyzable
    {
        string Type { get; }
        string Name { get; }
        string GetAiPromptData();
    }

    /// <summary>
    /// Interface for AI analysis results
    /// </summary>
    public interface IAiDecision
    {
        string Target { get; }
        string Action { get; } // kill, disable, whitelist
        string Reason { get; }
        string RiskLevel { get; }
    }

    /// <summary>
    /// Base class for all system data types
    /// </summary>
    public abstract class SystemDataBase : IAiAnalyzable, INotifyPropertyChanged
    {
        public abstract string Type { get; }
        public abstract string Name { get; }
        public string Path { get; set; }
        public string Company { get; set; }
        public bool IsSuspicious { get; set; }
        
        private string _aiAction;
        public string AiAction 
        { 
            get => _aiAction;
            set { _aiAction = value; OnPropertyChanged(); }
        }

        private string _aiReason;
        public string AiReason 
        { 
            get => _aiReason;
            set { _aiReason = value; OnPropertyChanged(); }
        }

        private bool _isRecommended;
        public bool IsRecommended 
        { 
            get => _isRecommended;
            set { _isRecommended = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public abstract string GetAiPromptData();

        protected string EscapeForJson(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
        }
    }
}
