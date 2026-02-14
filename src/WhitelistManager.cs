using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WinOptimizer.AI
{
    public class WhitelistItem
    {
        public string Type { get; set; } // Process, Service, Task, Autorun
        public string Name { get; set; }
        public string Path { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    public static class WhitelistManager
    {
        private static readonly string WhitelistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "whitelist.json");
        private static HashSet<string> _whitelistedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static List<WhitelistItem> _items = new List<WhitelistItem>();
        public static bool LoadedFromDisk { get; private set; } = false;

        static WhitelistManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(WhitelistPath))
                {
                    var json = File.ReadAllText(WhitelistPath);
                    _items = JsonConvert.DeserializeObject<List<WhitelistItem>>(json) ?? new List<WhitelistItem>();
                    UpdateHash();
                    LoadedFromDisk = true;
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_items, Formatting.Indented);
                File.WriteAllText(WhitelistPath, json);
            }
            catch { }
        }

        public static void Add(string type, string name, string path = "")
        {
            if (IsWhitelisted(name)) return;

            _items.Add(new WhitelistItem { Type = type, Name = name, Path = path, AddedAt = DateTime.Now });
            _whitelistedNames.Add(name);
            Save();
        }

        public static bool Remove(string type, string name)
        {
            var item = _items.FirstOrDefault(i => i.Type == type && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (item != null)
            {
                _items.Remove(item);
                _whitelistedNames.Remove(name);
                Save();
                return true;
            }
            return false;
        }

        public static void ClearAll()
        {
            _items.Clear();
            _whitelistedNames.Clear();
            Save();
        }

        public static List<WhitelistItem> GetAllEntries() => new List<WhitelistItem>(_items);

        public static bool IsWhitelisted(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return _whitelistedNames.Contains(name);
        }

        private static void UpdateHash()
        {
            _whitelistedNames.Clear();
            foreach (var item in _items)
            {
                if (!string.IsNullOrEmpty(item.Name))
                    _whitelistedNames.Add(item.Name);
            }
        }

        public static List<WhitelistItem> GetItems() => _items;
    }
}
