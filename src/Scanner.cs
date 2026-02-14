using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Scanner for system components (Services, Tasks, Autoruns)
    /// </summary>
    public class Scanner
    {
        public List<ServiceData> ScanServices()
        {
            var services = new List<ServiceData>();
            
            try
            {
                var allServices = ServiceController.GetServices();
                
                foreach (var sc in allServices)
                {
                    try
                    {
                        if (WhitelistManager.IsWhitelisted(sc.ServiceName))
                            continue;

                        var path = GetServicePath(sc.ServiceName);
                        var company = GetFileCompanyName(path);
                        
                        services.Add(new ServiceData
                        {
                            ServiceName = sc.ServiceName,
                            DisplayName = sc.DisplayName,
                            CurrentState = sc.Status.ToString(),
                            CurrentStartup = sc.StartType.ToString(),
                            Path = path,
                            Company = company,
                            IsMicrosoft = IsMicrosoftCompany(company)
                        });
                    }
                    catch { /* Skip on error */ }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Service scan error: {ex.Message}");
            }
            
            return services;
        }

        public List<TaskData> ScanTasks()
        {
            var tasks = new List<TaskData>();
            
            try
            {
                using (var ts = new TaskService())
                {
                    var allTasks = ts.AllTasks;
                    
                    foreach (var task in allTasks)
                    {
                        try
                        {
                            if (WhitelistManager.IsWhitelisted(task.Name))
                                continue;

                            var isMicrosoft = task.Path.StartsWith(@"\Microsoft", StringComparison.OrdinalIgnoreCase);
                            
                            tasks.Add(new TaskData
                            {
                                TaskName = task.Name,
                                TaskPath = task.Path,
                                State = task.State.ToString(),
                                IsEnabled = task.Enabled,
                                IsMicrosoft = isMicrosoft,
                                LastRunTime = task.LastRunTime.ToString("yyyy-MM-dd HH:mm"),
                                NextRunTime = task.NextRunTime.ToString("yyyy-MM-dd HH:mm")
                            });
                        }
                        catch { /* Skip on error */ }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Task scan error: {ex.Message}");
            }
            
            return tasks;
        }

        public List<AutorunData> ScanAutoruns()
        {
            var autoruns = new List<AutorunData>();
            
            // HKLM Run
            autoruns.AddRange(ScanRegistryRun(Registry.LocalMachine, 
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM"));
            autoruns.AddRange(ScanRegistryRun(Registry.LocalMachine, 
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM-WOW64"));
            
            // HKCU Run
            autoruns.AddRange(ScanRegistryRun(Registry.CurrentUser, 
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU"));
            
            return autoruns;
        }

        private List<AutorunData> ScanRegistryRun(RegistryKey rootKey, string subKeyPath, string location)
        {
            var autoruns = new List<AutorunData>();
            
            try
            {
                using (var key = rootKey.OpenSubKey(subKeyPath))
                {
                    if (key != null)
                    {
                        foreach (var valueName in key.GetValueNames())
                        {
                            if (WhitelistManager.IsWhitelisted(valueName))
                                continue;

                            var command = key.GetValue(valueName)?.ToString();
                            if (!string.IsNullOrEmpty(command))
                            {
                                var path = ExtractPathFromCommand(command);
                                var company = GetFileCompanyName(path);
                                
                                var autorun = new AutorunData
                                {
                                    EntryName = valueName,
                                    Command = command,
                                    Path = path,
                                    ExecutablePath = path,
                                    Company = company,
                                    Location = location,
                                    IsEnabled = true
                                };
                                
                                // Load icon if executable path exists
                                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                                {
                                    autorun.Icon = LoadIconForPath(path);
                                }
                                
                                autoruns.Add(autorun);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Registry scan error: {ex.Message}");
            }
            
            return autoruns;
        }

        private System.Windows.Media.ImageSource LoadIconForPath(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
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

        private string GetServicePath(string serviceName)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}"))
                {
                    var imagePath = key?.GetValue("ImagePath")?.ToString();
                    if (!string.IsNullOrEmpty(imagePath))
                    {
                        // Remove quotes and arguments
                        imagePath = imagePath.Trim('"');
                        var spaceIndex = imagePath.IndexOf(' ');
                        if (spaceIndex > 0 && !imagePath.StartsWith("\""))
                        {
                            var possiblePath = imagePath.Substring(0, spaceIndex);
                            if (File.Exists(possiblePath))
                                return possiblePath;
                        }
                        return imagePath;
                    }
                }
            }
            catch { }
            return "";
        }

        private string ExtractPathFromCommand(string command)
        {
            if (string.IsNullOrEmpty(command)) return "";
            
            command = command.Trim();
            
            // Handle quoted paths
            if (command.StartsWith("\""))
            {
                var endQuote = command.IndexOf('\"', 1);
                if (endQuote > 0)
                    return command.Substring(1, endQuote - 1);
            }
            
            // Handle unquoted paths
            var spaceIndex = command.IndexOf(' ');
            if (spaceIndex > 0)
            {
                var possiblePath = command.Substring(0, spaceIndex);
                if (File.Exists(possiblePath))
                    return possiblePath;
            }
            
            return command;
        }

        private string GetFileCompanyName(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return "";
                
                var info = FileVersionInfo.GetVersionInfo(filePath);
                return info.CompanyName ?? "";
            }
            catch { return ""; }
        }

        private bool IsMicrosoftCompany(string company)
        {
            if (string.IsNullOrEmpty(company)) return false;
            return company.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
