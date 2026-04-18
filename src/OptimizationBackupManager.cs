using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using Newtonsoft.Json;
using System.ServiceProcess;

namespace WinOptimizer.AI
{
    public class OptimizationBackup
    {
        public string BackupId { get; set; } = Guid.NewGuid().ToString("N");
        public string BackupName { get; set; } = "Backup";
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public List<ServiceBackupEntry> Services { get; set; } = new();
        public List<TaskBackupEntry> Tasks { get; set; } = new();
        public List<AutorunBackupEntry> Autoruns { get; set; } = new();
    }

    public class OptimizationBackupInfo
    {
        public string BackupId { get; set; }
        public string BackupName { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public int ServicesCount { get; set; }
        public int TasksCount { get; set; }
        public int AutorunsCount { get; set; }
        public string FilePath { get; set; }
    }

    public class ServiceBackupEntry
    {
        public string ServiceName { get; set; }
        public int StartupType { get; set; } // 2=Auto,3=Manual,4=Disabled
        public bool IsRunning { get; set; }
    }

    public class TaskBackupEntry
    {
        public string TaskPath { get; set; }
        public bool IsEnabled { get; set; }
    }

    public class AutorunBackupEntry
    {
        public string EntryName { get; set; }
        public string Command { get; set; }
        public string Location { get; set; } // HKLM, HKLM-WOW64, HKCU
    }

    public class BackupOperationResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public static class OptimizationBackupManager
    {
        private static readonly string BackupDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOptimizer.AI",
            "backups");

        private static readonly string BackupFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinOptimizer.AI",
            "optimization_backup.json");

        public static string BackupPath => BackupFilePath;

        public static bool HasBackup() => File.Exists(BackupFilePath);

        public static List<OptimizationBackupInfo> ListBackups()
        {
            EnsureBackupDirectory();
            var infos = new List<OptimizationBackupInfo>();

            foreach (var file in Directory.GetFiles(BackupDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var backup = JsonConvert.DeserializeObject<OptimizationBackup>(json);
                    if (backup == null)
                    {
                        continue;
                    }

                    infos.Add(new OptimizationBackupInfo
                    {
                        BackupId = backup.BackupId,
                        BackupName = backup.BackupName,
                        CreatedAtUtc = backup.CreatedAtUtc,
                        ServicesCount = backup.Services?.Count ?? 0,
                        TasksCount = backup.Tasks?.Count ?? 0,
                        AutorunsCount = backup.Autoruns?.Count ?? 0,
                        FilePath = file
                    });
                }
                catch
                {
                    // Skip broken backup files.
                }
            }

            return infos
                .OrderByDescending(i => i.CreatedAtUtc)
                .ToList();
        }

        public static void CreateBackup(IEnumerable<ServiceData> services, IEnumerable<TaskData> tasks, IEnumerable<AutorunData> autoruns)
        {
            var backupName = $"Backup {DateTime.Now:yyyy-MM-dd HH-mm-ss}";
            var backup = CreateNamedBackup(backupName, services, tasks, autoruns);
            SaveLegacyLatestBackup(backup);
        }

        public static OptimizationBackup CreateNamedBackup(string backupName, IEnumerable<ServiceData> services, IEnumerable<TaskData> tasks, IEnumerable<AutorunData> autoruns)
        {
            EnsureBackupDirectory();
            var safeName = string.IsNullOrWhiteSpace(backupName) ? $"Backup {DateTime.Now:yyyy-MM-dd HH-mm-ss}" : backupName.Trim();
            var backup = new OptimizationBackup
            {
                BackupName = safeName,
                CreatedAtUtc = DateTime.UtcNow,
                Services = BuildServiceBackup(services),
                Tasks = BuildTaskBackup(tasks),
                Autoruns = BuildAutorunBackup(autoruns)
            };

            var json = JsonConvert.SerializeObject(backup, Formatting.Indented);
            var filePath = GetBackupFilePath(backup.BackupId);
            File.WriteAllText(filePath, json);
            return backup;
        }

        public static OptimizationBackup LoadBackup()
        {
            if (!File.Exists(BackupFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(BackupFilePath);
            return JsonConvert.DeserializeObject<OptimizationBackup>(json);
        }

        public static OptimizationBackup LoadBackupById(string backupId)
        {
            var filePath = GetBackupFilePath(backupId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            var json = File.ReadAllText(filePath);
            return JsonConvert.DeserializeObject<OptimizationBackup>(json);
        }

        public static bool RenameBackup(string backupId, string newName)
        {
            if (string.IsNullOrWhiteSpace(backupId) || string.IsNullOrWhiteSpace(newName))
            {
                return false;
            }

            var backup = LoadBackupById(backupId);
            if (backup == null)
            {
                return false;
            }

            backup.BackupName = newName.Trim();
            var json = JsonConvert.SerializeObject(backup, Formatting.Indented);
            File.WriteAllText(GetBackupFilePath(backupId), json);
            return true;
        }

        public static bool DeleteBackup(string backupId)
        {
            var filePath = GetBackupFilePath(backupId);
            if (!File.Exists(filePath))
            {
                return false;
            }

            File.Delete(filePath);
            return true;
        }

        public static BackupOperationResult RestoreBackup(OptimizationBackup backup)
        {
            var result = new BackupOperationResult();
            if (backup == null)
            {
                result.Failed = 1;
                result.Errors.Add("No backup loaded.");
                return result;
            }

            RestoreServices(backup.Services, result);
            RestoreTasks(backup.Tasks, result);
            RestoreAutoruns(backup.Autoruns, result);
            return result;
        }

        public static BackupOperationResult RestoreBackupById(string backupId)
        {
            var backup = LoadBackupById(backupId);
            return RestoreBackup(backup);
        }

        public static void RestoreService(ServiceBackupEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.ServiceName))
            {
                throw new InvalidOperationException("Invalid service restore entry.");
            }

            SetServiceStartupType(entry.ServiceName, entry.StartupType);
            using var sc = new ServiceController(entry.ServiceName);
            if (entry.IsRunning && sc.Status != ServiceControllerStatus.Running)
            {
                sc.Start();
            }
            else if (!entry.IsRunning && sc.Status == ServiceControllerStatus.Running)
            {
                sc.Stop();
            }
        }

        public static void RestoreTask(TaskBackupEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.TaskPath))
            {
                throw new InvalidOperationException("Invalid task restore entry.");
            }

            using var taskService = new TaskService();
            var task = taskService.GetTask(entry.TaskPath);
            if (task == null)
            {
                throw new InvalidOperationException("Task not found.");
            }

            task.Enabled = entry.IsEnabled;
        }

        public static void RestoreAutorun(AutorunBackupEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.EntryName))
            {
                throw new InvalidOperationException("Invalid autorun restore entry.");
            }

            if (!TryWriteAutorunValue(entry.Location, entry.EntryName, entry.Command))
            {
                throw new InvalidOperationException("Unsupported autorun location.");
            }
        }

        private static List<ServiceBackupEntry> BuildServiceBackup(IEnumerable<ServiceData> services)
        {
            var entries = new List<ServiceBackupEntry>();
            var serviceNames = services?
                .Select(s => s.ServiceName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            foreach (var name in serviceNames)
            {
                try
                {
                    using var sc = new ServiceController(name);
                    int startupType = ReadServiceStartupType(name);
                    entries.Add(new ServiceBackupEntry
                    {
                        ServiceName = name,
                        StartupType = startupType,
                        IsRunning = sc.Status == ServiceControllerStatus.Running
                    });
                }
                catch
                {
                    // Best effort backup.
                }
            }

            return entries;
        }

        private static List<TaskBackupEntry> BuildTaskBackup(IEnumerable<TaskData> tasks)
        {
            var entries = new List<TaskBackupEntry>();
            var taskPaths = tasks?
                .Select(t => t.TaskPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<string>();

            using var taskService = new TaskService();
            foreach (var path in taskPaths)
            {
                try
                {
                    var task = taskService.GetTask(path);
                    if (task != null)
                    {
                        entries.Add(new TaskBackupEntry { TaskPath = path, IsEnabled = task.Enabled });
                    }
                }
                catch
                {
                    // Best effort backup.
                }
            }

            return entries;
        }

        private static List<AutorunBackupEntry> BuildAutorunBackup(IEnumerable<AutorunData> autoruns)
        {
            var entries = new List<AutorunBackupEntry>();
            var candidateEntries = autoruns?
                .Where(a => !string.IsNullOrWhiteSpace(a.EntryName))
                .GroupBy(a => $"{a.Location}|{a.EntryName}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList() ?? new List<AutorunData>();

            foreach (var autorun in candidateEntries)
            {
                var command = ReadAutorunValue(autorun.Location, autorun.EntryName);
                if (!string.IsNullOrWhiteSpace(command))
                {
                    entries.Add(new AutorunBackupEntry
                    {
                        EntryName = autorun.EntryName,
                        Command = command,
                        Location = autorun.Location
                    });
                }
            }

            return entries;
        }

        private static void RestoreServices(IEnumerable<ServiceBackupEntry> services, BackupOperationResult result)
        {
            foreach (var entry in services ?? Enumerable.Empty<ServiceBackupEntry>())
            {
                try
                {
                    RestoreService(entry);
                    result.Succeeded++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"Service '{entry.ServiceName}': {ex.Message}");
                }
            }
        }

        private static void RestoreTasks(IEnumerable<TaskBackupEntry> tasks, BackupOperationResult result)
        {
            foreach (var entry in tasks ?? Enumerable.Empty<TaskBackupEntry>())
            {
                try
                {
                    RestoreTask(entry);
                    result.Succeeded++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"Task '{entry.TaskPath}': {ex.Message}");
                }
            }
        }

        private static void RestoreAutoruns(IEnumerable<AutorunBackupEntry> autoruns, BackupOperationResult result)
        {
            foreach (var entry in autoruns ?? Enumerable.Empty<AutorunBackupEntry>())
            {
                try
                {
                    RestoreAutorun(entry);
                    result.Succeeded++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"Autorun '{entry.EntryName}': {ex.Message}");
                }
            }
        }

        private static int ReadServiceStartupType(string serviceName)
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: false);
            var raw = key?.GetValue("Start");
            return raw is int value ? value : 3;
        }

        private static void SetServiceStartupType(string serviceName, int startupType)
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}", writable: true);
            if (key == null)
            {
                throw new InvalidOperationException("Service registry key not found.");
            }

            key.SetValue("Start", startupType, RegistryValueKind.DWord);
        }

        private static string ReadAutorunValue(string location, string entryName)
        {
            var (root, path) = GetAutorunRegistryPath(location);
            if (root == null || string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            using var key = root.OpenSubKey(path, writable: false);
            return key?.GetValue(entryName)?.ToString();
        }

        private static bool TryWriteAutorunValue(string location, string entryName, string command)
        {
            var (root, path) = GetAutorunRegistryPath(location);
            if (root == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            using var key = root.CreateSubKey(path, writable: true);
            key?.SetValue(entryName, command, RegistryValueKind.String);
            return true;
        }

        private static (RegistryKey root, string path) GetAutorunRegistryPath(string location)
        {
            return location?.ToUpperInvariant() switch
            {
                "HKLM" => (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                "HKLM-WOW64" => (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"),
                "HKCU" => (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
                _ => (null, null)
            };
        }

        private static void EnsureBackupDirectory()
        {
            Directory.CreateDirectory(BackupDirectory);
        }

        private static string GetBackupFilePath(string backupId)
        {
            EnsureBackupDirectory();
            var safeId = string.IsNullOrWhiteSpace(backupId) ? Guid.NewGuid().ToString("N") : backupId;
            return Path.Combine(BackupDirectory, $"{safeId}.json");
        }

        private static void SaveLegacyLatestBackup(OptimizationBackup backup)
        {
            var dir = Path.GetDirectoryName(BackupFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonConvert.SerializeObject(backup, Formatting.Indented);
            File.WriteAllText(BackupFilePath, json);
        }
    }
}
