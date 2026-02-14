using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WinOptimizer.AI
{
    /// <summary>
    /// Represents a custom whitelist with a name and entries
    /// </summary>
    public class CustomWhitelist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "Default Whitelist";
        public string Description { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<WhitelistEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// Entry in a whitelist
    /// </summary>
    public class WhitelistEntry
    {
        public string Type { get; set; } // Process, Service, Task, Autorun
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public string Reason { get; set; } // Why it was whitelisted
    }

    /// <summary>
    /// Manages multiple custom whitelists
    /// </summary>
    public static class CustomWhitelistManager
    {
        private static readonly string WhitelistsDir;
        private static readonly string WhitelistsFile;
        private static List<CustomWhitelist> _whitelists = new();
        private static CustomWhitelist _activeWhitelist;

        static CustomWhitelistManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appData, "WinOptimizer.AI");
            WhitelistsDir = appFolder;
            WhitelistsFile = Path.Combine(appFolder, "whitelists.json");
            
            Directory.CreateDirectory(appFolder);
            LoadWhitelists();
            
            // Create default whitelist if none exist
            if (_whitelists.Count == 0)
            {
                var defaultWhitelist = new CustomWhitelist
                {
                    Name = "Default",
                    Description = "Default whitelist for common exceptions"
                };
                _whitelists.Add(defaultWhitelist);
                _activeWhitelist = defaultWhitelist;
                SaveWhitelists();
            }
            else
            {
                _activeWhitelist = _whitelists.First();
            }
        }

        public static List<CustomWhitelist> GetAllWhitelists() => _whitelists;

        public static CustomWhitelist GetActiveWhitelist() => _activeWhitelist;

        public static void SetActiveWhitelist(string id)
        {
            var whitelist = _whitelists.FirstOrDefault(w => w.Id == id);
            if (whitelist != null)
            {
                _activeWhitelist = whitelist;
            }
        }

        public static CustomWhitelist CreateWhitelist(string name, string description = "")
        {
            var whitelist = new CustomWhitelist
            {
                Name = name,
                Description = description
            };
            _whitelists.Add(whitelist);
            SaveWhitelists();
            return whitelist;
        }

        public static void DeleteWhitelist(string id)
        {
            var whitelist = _whitelists.FirstOrDefault(w => w.Id == id);
            if (whitelist != null && _whitelists.Count > 1)
            {
                _whitelists.Remove(whitelist);
                if (_activeWhitelist?.Id == id)
                {
                    _activeWhitelist = _whitelists.First();
                }
                SaveWhitelists();
            }
        }

        public static void RenameWhitelist(string id, string newName)
        {
            var whitelist = _whitelists.FirstOrDefault(w => w.Id == id);
            if (whitelist != null)
            {
                whitelist.Name = newName;
                SaveWhitelists();
            }
        }

        public static void AddToActiveWhitelist(string type, string name, string path = "", string reason = "")
        {
            if (_activeWhitelist == null) return;

            // Check if already exists
            if (_activeWhitelist.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return;

            _activeWhitelist.Entries.Add(new WhitelistEntry
            {
                Type = type,
                Name = name,
                Path = path,
                Reason = reason
            });
            SaveWhitelists();
        }

        public static void AddToWhitelist(string whitelistId, string type, string name, string path = "", string reason = "")
        {
            var whitelist = _whitelists.FirstOrDefault(w => w.Id == whitelistId);
            if (whitelist == null) return;

            if (whitelist.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return;

            whitelist.Entries.Add(new WhitelistEntry
            {
                Type = type,
                Name = name,
                Path = path,
                Reason = reason
            });
            SaveWhitelists();
        }

        public static void RemoveFromActiveWhitelist(string name)
        {
            if (_activeWhitelist == null) return;
            
            var entry = _activeWhitelist.Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                _activeWhitelist.Entries.Remove(entry);
                SaveWhitelists();
            }
        }

        public static bool IsInActiveWhitelist(string name)
        {
            if (_activeWhitelist == null) return false;
            return _activeWhitelist.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsInAnyWhitelist(string name)
        {
            return _whitelists.Any(w => w.Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        }

        public static List<WhitelistEntry> GetActiveWhitelistEntries()
        {
            return _activeWhitelist?.Entries ?? new List<WhitelistEntry>();
        }

        public static void MoveEntry(string entryName, string fromWhitelistId, string toWhitelistId)
        {
            var fromWhitelist = _whitelists.FirstOrDefault(w => w.Id == fromWhitelistId);
            var toWhitelist = _whitelists.FirstOrDefault(w => w.Id == toWhitelistId);
            
            if (fromWhitelist == null || toWhitelist == null) return;

            var entry = fromWhitelist.Entries.FirstOrDefault(e => e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                fromWhitelist.Entries.Remove(entry);
                if (!toWhitelist.Entries.Any(e => e.Name.Equals(entryName, StringComparison.OrdinalIgnoreCase)))
                {
                    toWhitelist.Entries.Add(entry);
                }
                SaveWhitelists();
            }
        }

        private static void LoadWhitelists()
        {
            try
            {
                if (File.Exists(WhitelistsFile))
                {
                    var json = File.ReadAllText(WhitelistsFile);
                    _whitelists = JsonConvert.DeserializeObject<List<CustomWhitelist>>(json) ?? new List<CustomWhitelist>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load whitelists: {ex.Message}");
                _whitelists = new List<CustomWhitelist>();
            }
        }

        private static void SaveWhitelists()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_whitelists, Formatting.Indented);
                File.WriteAllText(WhitelistsFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save whitelists: {ex.Message}");
            }
        }

        /// <summary>
        /// Export whitelist to JSON string
        /// </summary>
        public static string ExportWhitelist(string whitelistId)
        {
            var whitelist = _whitelists.FirstOrDefault(w => w.Id == whitelistId);
            if (whitelist == null) return null;
            return JsonConvert.SerializeObject(whitelist, Formatting.Indented);
        }

        /// <summary>
        /// Import whitelist from JSON string
        /// </summary>
        public static CustomWhitelist ImportWhitelist(string json)
        {
            try
            {
                var whitelist = JsonConvert.DeserializeObject<CustomWhitelist>(json);
                if (whitelist != null)
                {
                    whitelist.Id = Guid.NewGuid().ToString(); // New ID to avoid conflicts
                    _whitelists.Add(whitelist);
                    SaveWhitelists();
                    return whitelist;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to import whitelist: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Check if an item is whitelisted by either the old WhitelistManager or the new custom whitelists
        /// </summary>
        public static bool IsWhitelisted(string name)
        {
            // Check old whitelist system
            if (WhitelistManager.IsWhitelisted(name))
                return true;
            
            // Check new custom whitelist system
            return IsInAnyWhitelist(name);
        }
    }
}
