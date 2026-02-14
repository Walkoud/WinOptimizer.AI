using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.ServiceProcess;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Newtonsoft.Json;

namespace WinOptimizer.AI
{
    public class SystemItem
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public string Path { get; set; }
        public string Company { get; set; }
        public double CpuUsage { get; set; }
        public double RamUsage { get; set; }
        public double GpuUsage { get; set; }
        public string Status { get; set; }
        public int ProcessId { get; set; }
        public string Description { get; set; }
        public bool IsSuspicious { get; set; }
    }

    public class SystemScanResult
    {
        public List<SystemItem> Items { get; set; } = new List<SystemItem>();
        public DateTime ScanTime { get; set; }
        public string MachineName { get; set; }
        public string OSVersion { get; set; }
        public int TotalProcesses { get; set; }
        public int SuspiciousItems { get; set; }
    }

    public class SystemScanner
    {
        private readonly HashSet<string> _criticalProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "winlogon.exe",
            "services.exe", "lsass.exe", "svchost.exe", "explorer.exe", "dwm.exe",
            "ntoskrnl.exe", "kernel32.dll", "ntdll.dll", "user32.dll", "gdi32.dll"
        };

        private readonly HashSet<string> _trustedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\Windows\System32",
            @"C:\Windows\SysWOW64",
            @"C:\Program Files",
            @"C:\Program Files (x86)"
        };

        private Dictionary<int, PerformanceCounter> _cpuCounters = new Dictionary<int, PerformanceCounter>();
        private Dictionary<int, double> _previousCpuTimes = new Dictionary<int, double>();

        public SystemScanResult ScanSystem(double cpuThreshold = 0.5, double gpuThreshold = 0.0, 
            bool scanProcesses = true, bool scanServices = true, bool scanAutoruns = true, bool scanTasks = true)
        {
            var result = new SystemScanResult
            {
                ScanTime = DateTime.Now,
                MachineName = Environment.MachineName,
                OSVersion = Environment.OSVersion.ToString()
            };

            try
            {
                if (scanProcesses)
                {
                    var processes = ScanProcesses(cpuThreshold, gpuThreshold);
                    result.Items.AddRange(processes);
                    result.TotalProcesses = Process.GetProcesses().Length;
                }

                if (scanServices)
                {
                    var services = ScanServices();
                    result.Items.AddRange(services);
                }

                if (scanAutoruns)
                {
                    var autoruns = ScanAutoruns();
                    result.Items.AddRange(autoruns);
                }

                if (scanTasks)
                {
                    var tasks = ScanScheduledTasks();
                    result.Items.AddRange(tasks);
                }

                result.SuspiciousItems = result.Items.Count(i => i.IsSuspicious);
            }
            catch (Exception ex)
            {
                // Add error item to results
                result.Items.Add(new SystemItem
                {
                    Type = "Error",
                    Name = "Scan Error",
                    Description = ex.Message,
                    Status = "Failed"
                });
            }

            return result;
        }

        private List<SystemItem> ScanProcesses(double cpuThreshold, double gpuThreshold)
        {
            var items = new List<SystemItem>();
            var processes = Process.GetProcesses();
            var gpuCounters = GetGpuCounters();

            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;

                    var cpuUsage = GetProcessCpuUsage(process);
                    var ramUsage = process.WorkingSet64 / (1024.0 * 1024.0); // Convert to MB
                    var gpuUsage = GetProcessGpuUsage(process.Id, gpuCounters);

                    // Apply filtering logic
                    var isSuspicious = IsSuspiciousProcess(process, cpuUsage, gpuUsage, cpuThreshold, gpuThreshold);
                    
                    if (isSuspicious || cpuUsage > cpuThreshold || gpuUsage > gpuThreshold)
                    {
                        var processPath = GetProcessPath(process);
                        items.Add(new SystemItem
                        {
                            Type = "Process",
                            Name = process.ProcessName,
                            Path = processPath,
                            Company = GetFileCompanyName(processPath),
                            CpuUsage = Math.Round(cpuUsage, 2),
                            RamUsage = Math.Round(ramUsage, 2),
                            GpuUsage = Math.Round(gpuUsage, 2),
                            ProcessId = process.Id,
                            Status = process.Responding ? "Running" : "Not Responding",
                            IsSuspicious = isSuspicious
                        });
                    }
                }
                catch (Exception ex)
                {
                    // Skip processes we can't access
                    Debug.WriteLine($"Error scanning process {process.ProcessName}: {ex.Message}");
                }
            }

            return items;
        }

        private List<SystemItem> ScanServices()
        {
            var items = new List<SystemItem>();
            var services = ServiceController.GetServices();

            foreach (var service in services)
            {
                try
                {
                    // Only include non-Microsoft running services
                    if (service.Status == ServiceControllerStatus.Running && !IsSystemService(service))
                    {
                        var servicePath = GetServicePath(service.ServiceName);
                        var isSuspicious = IsSuspiciousService(service, servicePath);

                        items.Add(new SystemItem
                        {
                            Type = "Service",
                            Name = service.ServiceName,
                            Path = servicePath,
                            Status = service.Status.ToString(),
                            Description = service.DisplayName,
                            IsSuspicious = isSuspicious
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error scanning service {service.ServiceName}: {ex.Message}");
                }
            }

            return items;
        }

        private List<SystemItem> ScanAutoruns()
        {
            var items = new List<SystemItem>();
            
            // Scan HKLM Run keys
            items.AddRange(ScanRegistryRun(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"));
            items.AddRange(ScanRegistryRun(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM"));
            
            // Scan HKCU Run keys
            items.AddRange(ScanRegistryRun(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU"));

            return items;
        }

        private List<SystemItem> ScanRegistryRun(RegistryKey rootKey, string subKeyPath, string hive)
        {
            var items = new List<SystemItem>();

            try
            {
                using (var runKey = rootKey.OpenSubKey(subKeyPath))
                {
                    if (runKey != null)
                    {
                        foreach (var valueName in runKey.GetValueNames())
                        {
                            var value = runKey.GetValue(valueName)?.ToString();
                            if (!string.IsNullOrEmpty(value))
                            {
                                var isSuspicious = IsSuspiciousAutorun(valueName, value);

                                items.Add(new SystemItem
                                {
                                    Type = "Autorun",
                                    Name = valueName,
                                    Path = value,
                                    Status = hive,
                                    IsSuspicious = isSuspicious
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning registry {hive}\\{subKeyPath}: {ex.Message}");
            }

            return items;
        }

        private List<SystemItem> ScanScheduledTasks()
        {
            var items = new List<SystemItem>();

            try
            {
                using (var ts = new TaskService())
                {
                    var tasks = ts.AllTasks.Where(t => t.Enabled && !IsSystemTask(t));

                    foreach (var task in tasks)
                    {
                        try
                        {
                            var isSuspicious = IsSuspiciousTask(task);

                            items.Add(new SystemItem
                            {
                                Type = "Task",
                                Name = task.Name,
                                Path = task.Path,
                                Status = task.State.ToString(),
                                Description = task.Definition.RegistrationInfo.Description,
                                IsSuspicious = isSuspicious
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error processing task {task.Name}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning scheduled tasks: {ex.Message}");
            }

            return items;
        }

        private double GetProcessCpuUsage(Process process)
        {
            try
            {
                if (!_cpuCounters.ContainsKey(process.Id))
                {
                    var counter = new PerformanceCounter("Process", "% Processor Time", process.ProcessName, true);
                    _cpuCounters[process.Id] = counter;
                }

                var existingCounter = _cpuCounters[process.Id];
                var currentValue = existingCounter.NextValue();
                
                // CPU usage calculation requires two samples
                if (_previousCpuTimes.ContainsKey(process.Id))
                {
                    return currentValue / Environment.ProcessorCount;
                }
                else
                {
                    _previousCpuTimes[process.Id] = currentValue;
                    return 0;
                }
            }
            catch
            {
                return 0;
            }
        }

        private Dictionary<int, double> GetGpuCounters()
        {
            var gpuUsage = new Dictionary<int, double>();

            try
            {
                var category = new PerformanceCounterCategory("GPU Engine");
                var instanceNames = category.GetInstanceNames();

                foreach (var instanceName in instanceNames)
                {
                    try
                    {
                        // Parse PID from instance name (format: pid_XXXX_luid_0x00000000_0x0000XXXX_phys_0)
                        var parts = instanceName.Split('_');
                        if (parts.Length >= 2 && parts[0] == "pid" && int.TryParse(parts[1], out int pid))
                        {
                            var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, true);
                            var usage = counter.NextValue();
                            
                            if (gpuUsage.ContainsKey(pid))
                                gpuUsage[pid] += usage;
                            else
                                gpuUsage[pid] = usage;
                        }
                    }
                    catch
                    {
                        // Skip invalid counters
                    }
                }
            }
            catch
            {
                // GPU counters not available
            }

            return gpuUsage;
        }

        private double GetProcessGpuUsage(int processId, Dictionary<int, double> gpuCounters)
        {
            return gpuCounters.ContainsKey(processId) ? gpuCounters[processId] : 0;
        }

        private string GetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? "";
            }
            catch
            {
                return "";
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
            catch
            {
                return "";
            }
        }

        private string GetServicePath(string serviceName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}"))
                {
                    return key?.GetValue("ImagePath")?.ToString() ?? "";
                }
            }
            catch
            {
                return "";
            }
        }

        private bool IsSuspiciousProcess(Process process, double cpuUsage, double gpuUsage, double cpuThreshold, double gpuThreshold)
        {
            if (_criticalProcesses.Contains(process.ProcessName))
                return false;

            var path = GetProcessPath(process);
            
            // Check if process is outside trusted paths
            if (!string.IsNullOrEmpty(path) && !_trustedPaths.Any(tp => path.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
                return true;

            // High resource usage
            if (cpuUsage > cpuThreshold * 10 || gpuUsage > gpuThreshold * 10)
                return true;

            return false;
        }

        private bool IsSuspiciousService(ServiceController service, string path)
        {
            // Check if service path is outside trusted locations
            if (!string.IsNullOrEmpty(path) && !_trustedPaths.Any(tp => path.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private bool IsSuspiciousAutorun(string name, string path)
        {
            // Check if autorun path is outside trusted locations
            if (!string.IsNullOrEmpty(path) && !_trustedPaths.Any(tp => path.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
                return true;

            return false;
        }

        private bool IsSuspiciousTask(Microsoft.Win32.TaskScheduler.Task task)
        {
            try
            {
                // Check task actions for suspicious paths
                foreach (var action in task.Definition.Actions)
                {
                    if (action is ExecAction execAction)
                    {
                        var path = execAction.Path;
                        if (!string.IsNullOrEmpty(path) && !_trustedPaths.Any(tp => path.StartsWith(tp, StringComparison.OrdinalIgnoreCase)))
                            return true;
                    }
                }
            }
            catch
            {
                // If we can't analyze the task, consider it suspicious
                return true;
            }

            return false;
        }

        private bool IsSystemService(ServiceController service)
        {
            // Simple heuristic: services starting with common Windows prefixes
            var systemPrefixes = new[] { "Windows", "Microsoft", "WinHTTP", "Themes", "BITS", "Cryptographic", "DNS", "DHCP", "Event Log", "Plug and Play", "Remote Procedure Call", "Security Accounts Manager", "Server", "Workstation", "Windows Audio", "Windows Defender", "Windows Firewall", "Windows Search", "Windows Time", "Windows Update" };
            
            return systemPrefixes.Any(prefix => service.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                   service.ServiceName.StartsWith("Win", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsSystemTask(Microsoft.Win32.TaskScheduler.Task task)
        {
            // Tasks in Microsoft folder are typically system tasks
            return task.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase);
        }

        public string ExportSystemStateToJson(SystemScanResult scanResult)
        {
            var exportData = new
            {
                scan_metadata = new
                {
                    timestamp = scanResult.ScanTime,
                    machine_name = scanResult.MachineName,
                    os_version = scanResult.OSVersion,
                    total_processes = scanResult.TotalProcesses,
                    suspicious_items = scanResult.SuspiciousItems
                },
                system_items = scanResult.Items.Select(item => new
                {
                    type = item.Type,
                    name = item.Name,
                    path = item.Path,
                    company = item.Company,
                    cpu_usage = item.CpuUsage,
                    ram_usage_mb = item.RamUsage,
                    gpu_usage = item.GpuUsage,
                    status = item.Status,
                    process_id = item.ProcessId,
                    description = item.Description,
                    is_suspicious = item.IsSuspicious
                }).ToList()
            };

            return JsonConvert.SerializeObject(exportData, Formatting.Indented);
        }

        public async System.Threading.Tasks.Task<bool> ApplyAiDecisions(string jsonResponse)
        {
            try
            {
                // Extract JSON from the AI response (handles markdown code blocks and extra text)
                var extractedJson = ExtractJsonFromText(jsonResponse);
                if (string.IsNullOrEmpty(extractedJson))
                {
                    Debug.WriteLine("Could not extract valid JSON from AI response");
                    return false;
                }

                dynamic decisions = JsonConvert.DeserializeObject(extractedJson);
                
                if (decisions?.actions != null)
                {
                    foreach (var action in decisions.actions)
                    {
                        try
                        {
                            string actionType = action.action?.ToString();
                            string targetName = action.target?.ToString();
                            string targetType = action.type?.ToString();

                            switch (actionType?.ToLower())
                            {
                                case "kill":
                                    await KillProcess(targetName);
                                    break;
                                case "disable":
                                    await DisableItem(targetName, targetType);
                                    break;
                                case "whitelist":
                                    // Add to whitelist (implementation depends on requirements)
                                    break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error applying action: {ex.Message}");
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing AI response: {ex.Message}");
                return false;
            }
        }

        private async System.Threading.Tasks.Task KillProcess(string processName)
        {
            try
            {
                // Safety check against critical processes
                if (_criticalProcesses.Contains(processName))
                {
                    throw new InvalidOperationException($"Refusing to kill critical process: {processName}");
                }

                var processes = Process.GetProcessesByName(processName);
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        await System.Threading.Tasks.Task.Delay(100); // Small delay between kills
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to kill process {processName} (PID: {process.Id}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error killing process {processName}: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task DisableItem(string itemName, string itemType)
        {
            try
            {
                switch (itemType?.ToLower())
                {
                    case "service":
                        await DisableService(itemName);
                        break;
                    case "autorun":
                        await DisableAutorun(itemName);
                        break;
                    case "task":
                        await DisableTask(itemName);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling {itemType} {itemName}: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task DisableService(string serviceName)
        {
            try
            {
                using (var service = new ServiceController(serviceName))
                {
                    if (service.Status == ServiceControllerStatus.Running)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                    }
                }

                // Set service to disabled in registry
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", true))
                {
                    key?.SetValue("Start", 4); // 4 = Disabled
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling service {serviceName}: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task DisableAutorun(string autorunName)
        {
            try
            {
                // Try to remove from both HKLM and HKCU
                var keys = new[]
                {
                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true),
                    Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", true),
                    Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true)
                };

                foreach (var key in keys)
                {
                    try
                    {
                        if (key != null && key.GetValue(autorunName) != null)
                        {
                            key.DeleteValue(autorunName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error removing autorun from registry: {ex.Message}");
                    }
                    finally
                    {
                        key?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling autorun {autorunName}: {ex.Message}");
                throw;
            }
        }

        private async System.Threading.Tasks.Task DisableTask(string taskPath)
        {
            try
            {
                using (var ts = new TaskService())
                {
                    var task = ts.GetTask(taskPath);
                    if (task != null)
                    {
                        task.Enabled = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disabling task {taskPath}: {ex.Message}");
                throw;
            }
        }

        private string ExtractJsonFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            // Try to find JSON in markdown code blocks first
            var codeBlockPattern = @"```(?:json)?\s*([\s\S]*?)\s*```";
            var codeBlockMatches = System.Text.RegularExpressions.Regex.Matches(text, codeBlockPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            foreach (System.Text.RegularExpressions.Match match in codeBlockMatches)
            {
                var content = match.Groups[1].Value.Trim();
                if (IsValidJson(content))
                    return content;
            }

            // Try to find JSON between first { and last }
            var startIndex = text.IndexOf('{');
            var endIndex = text.LastIndexOf('}');
            
            if (startIndex >= 0 && endIndex > startIndex)
            {
                var possibleJson = text.Substring(startIndex, endIndex - startIndex + 1);
                if (IsValidJson(possibleJson))
                    return possibleJson;
            }

            // Try the whole text if it's valid JSON
            if (IsValidJson(text.Trim()))
                return text.Trim();

            return null;
        }

        private bool IsValidJson(string text)
        {
            try
            {
                JsonConvert.DeserializeObject(text);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            foreach (var counter in _cpuCounters.Values)
            {
                counter?.Dispose();
            }
            _cpuCounters.Clear();
        }
    }
}
