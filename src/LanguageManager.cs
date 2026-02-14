using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WinOptimizer.AI
{
    public static class LanguageManager
    {
        private static JObject _languages;
        private static string _currentLanguage = "English";

        public static void LoadLanguages(string filePath)
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                _languages = JObject.Parse(json);
            }
        }

        public static List<string> GetAvailableLanguages()
        {
            if (_languages == null) return new List<string> { "English" };
            return new List<string>(_languages.Properties().Select(p => p.Name));
        }

        public static void SetLanguage(string language)
        {
            if (_languages?[language] != null)
            {
                _currentLanguage = language;
            }
        }

        public static string GetString(string key, params object[] args)
        {
            if (_languages == null) return key;

            var langObj = _languages[_currentLanguage];
            if (langObj == null) return key;

            var value = langObj[key]?.ToString();
            if (string.IsNullOrEmpty(value)) return key;

            return args.Length > 0 ? string.Format(value, args) : value;
        }

        public static string CurrentLanguage => _currentLanguage;
    }
}
