using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WinOptimizer.AI
{
    public class BlacklistItem
    {
        public string Name { get; set; }
        public bool AutoKill { get; set; } = true;
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    public static class BlacklistManager
    {
        private static readonly string BlacklistPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blacklist.json");
        private static Dictionary<string, BlacklistItem> _blacklistItems = new Dictionary<string, BlacklistItem>(StringComparer.OrdinalIgnoreCase);

        static BlacklistManager()
        {
            Load();
        }

        public static void Load()
        {
            try
            {
                if (File.Exists(BlacklistPath))
                {
                    var json = File.ReadAllText(BlacklistPath);
                    var list = JsonConvert.DeserializeObject<List<BlacklistItem>>(json) ?? new List<BlacklistItem>();
                    _blacklistItems.Clear();
                    foreach (var item in list)
                    {
                        if (!string.IsNullOrEmpty(item.Name) && !_blacklistItems.ContainsKey(item.Name))
                        {
                            _blacklistItems.Add(item.Name, item);
                        }
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var list = _blacklistItems.Values.ToList();
                var json = JsonConvert.SerializeObject(list, Formatting.Indented);
                File.WriteAllText(BlacklistPath, json);
            }
            catch { }
        }

        public static void Add(string name, bool autoKill)
        {
            if (string.IsNullOrEmpty(name)) return;

            if (_blacklistItems.ContainsKey(name))
            {
                _blacklistItems[name].AutoKill = autoKill;
            }
            else
            {
                _blacklistItems.Add(name, new BlacklistItem { Name = name, AutoKill = autoKill });
            }
            Save();
        }

        public static bool Remove(string name)
        {
            if (_blacklistItems.Remove(name))
            {
                Save();
                return true;
            }
            return false;
        }

        public static bool IsBlacklisted(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return _blacklistItems.ContainsKey(name);
        }

        public static bool ShouldAutoKill(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (_blacklistItems.TryGetValue(name, out var item))
            {
                return item.AutoKill;
            }
            return false;
        }

        public static List<BlacklistItem> GetItems() => _blacklistItems.Values.ToList();
    }
}
