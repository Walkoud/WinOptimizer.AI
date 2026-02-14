# WinOptimizer.AI

AI-Powered Windows System Optimizer with advanced system analysis and intelligent optimization recommendations.

## Features

- **Comprehensive System Scanning**: Analyzes processes, services, autoruns, and scheduled tasks
- **GPU Usage Monitoring**: Advanced GPU performance counter integration
- **AI-Powered Analysis**: Supports OpenAI, Anthropic, and local Ollama models
- **Modern UI**: Windows 11-style interface with dark/light theme support
- **Safety First**: Built-in whitelist protection for critical system processes
- **Administrator Privileges**: Automatic UAC elevation for system-level operations

## Requirements

- Windows 10/11
- .NET 8.0 Runtime
- Administrator privileges (automatically requested)
- API key for AI services (OpenAI/Anthropic) or local Ollama installation

## Installation

1. Clone or download the project
2. Open PowerShell as Administrator in the project directory
3. Build the project:
   ```powershell
   dotnet build
   ```
4. Run the application:
   ```powershell
   dotnet run
   ```

## NuGet Dependencies

The project automatically installs these packages:
- `Newtonsoft.Json` (13.0.3) - JSON serialization
- `Microsoft.Win32.TaskScheduler` (2.10.1) - Task scheduler integration
- `System.Management` (8.0.0) - WMI and system management

## Usage

### 1. Configure AI Provider
- Select your preferred AI provider (OpenAI, Anthropic, or Local Ollama)
- Enter your API key (not required for Ollama)
- Specify the model name (e.g., "gpt-4", "claude-3-sonnet-20240229", "phi3.5")

### 2. Configure Scan Options
- Choose what to scan: Processes, Services, Autoruns, Scheduled Tasks
- Set filtering thresholds for CPU and GPU usage
- Toggle between dark and light themes

### 3. Perform System Scan
- Click "🔍 Scan System" to analyze your system
- Review the results in the data grid
- Suspicious items are automatically flagged

### 4. AI Analysis
- Click "🤖 Send to AI" to get optimization recommendations
- The AI will analyze the data and suggest safe actions
- Actions are automatically applied with safety checks

## Safety Features

### Critical Process Protection
The application includes a hardcoded whitelist of critical Windows processes that will never be terminated:
- System processes (System, csrss.exe, winlogon.exe, etc.)
- Core services (services.exe, lsass.exe, svchost.exe)
- Essential components (explorer.exe, dwm.exe, etc.)

### Conservative AI Recommendations
The AI system prompt is designed to be extremely conservative:
- Only recommends actions for clearly problematic items
- Prioritizes suspicious items and high resource usage
- Always provides detailed reasoning for recommendations
- Includes risk assessment for each action

### Error Handling
- Robust exception handling for all system operations
- Graceful degradation when permissions are insufficient
- Detailed logging of all operations and errors

## Supported AI Actions

### Kill Process
- Terminates running processes (with safety checks)
- Only applied to non-critical processes
- Includes error handling for access denied scenarios

### Disable Service
- Stops and disables Windows services
- Modifies registry to prevent automatic startup
- Reversible through Windows Services manager

### Remove Autorun
- Removes startup entries from registry
- Supports both HKLM and HKCU locations
- Handles both 32-bit and 64-bit registry views

### Disable Scheduled Task
- Disables scheduled tasks
- Uses official Task Scheduler API
- Preserves task definition for easy re-enabling

## Architecture

### SystemScanner.cs
Core scanning engine that:
- Enumerates processes with CPU/RAM/GPU usage
- Analyzes Windows services and their configurations
- Scans registry autorun locations
- Examines scheduled tasks using official APIs
- Applies intelligent filtering to reduce noise

### MainWindow.xaml/.cs
Modern WPF interface featuring:
- Responsive data grid with system information
- Real-time activity logging
- Theme switching (dark/light mode)
- AI provider configuration
- Asynchronous operations with progress feedback

### AI Integration
Supports multiple AI providers:
- **OpenAI**: GPT-4 and other models via REST API
- **Anthropic**: Claude models via REST API  
- **Ollama**: Local models (Phi-3.5, etc.) via local API

## Security Considerations

- **Administrator Privileges**: Required for system-level operations
- **API Key Storage**: Keys are stored in memory only, not persisted
- **Process Whitelist**: Hardcoded protection against critical process termination
- **Conservative Actions**: AI is instructed to be extremely cautious
- **Reversible Operations**: Most actions can be undone manually

## Troubleshooting

### GPU Counters Not Available
If GPU usage shows 0% for all processes:
- Ensure you have a dedicated GPU
- Update GPU drivers
- Run as Administrator
- Some systems may not expose GPU performance counters

### Access Denied Errors
- Ensure the application is running as Administrator
- Some system processes cannot be accessed even with admin rights
- This is normal and expected behavior

### AI API Errors
- Verify your API key is correct
- Check your internet connection
- Ensure you have sufficient API credits/quota
- For Ollama, ensure the service is running on localhost:11434

## Development Notes

### GPU Engine Monitoring
The application uses Windows Performance Counters to monitor GPU usage:
- Aggregates multiple GPU engine instances per process
- Handles cases where counters are not initialized
- Gracefully degrades when GPU monitoring is unavailable

### Task Scheduler Integration
Uses the official Microsoft.Win32.TaskScheduler library:
- Provides full access to Windows Task Scheduler
- Filters out Microsoft-signed tasks by default
- Handles COM interop complexities automatically

### Asynchronous Operations
All system operations are performed asynchronously:
- UI remains responsive during scans
- Background threads for system analysis
- Proper exception handling and user feedback

## License

This project is provided as-is for educational and personal use. Use at your own risk and always create system backups before making system modifications.

## Contributing

This is a demonstration project showcasing AI-powered system optimization. Feel free to extend and modify for your specific needs.

## Disclaimer

⚠️ **Important**: This tool makes system-level changes that could affect system stability. Always:
- Create a system restore point before use
- Review AI recommendations carefully
- Test in a virtual machine first
- Keep backups of important data
- Use at your own risk
