// LocalizationService.cs - Handles loading and accessing localized strings from JSON files
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Provides localized string access from JSON resource files.
    /// Supports runtime language switching.
    /// </summary>
    public class LocalizationService
    {
        private static LocalizationService _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        private Dictionary<string, string> _strings = new Dictionary<string, string>();
        private string _currentLanguage = "ja";
        private string _resourcePath;

        public string CurrentLanguage => _currentLanguage;

        /// <summary>
        /// Available languages (detected from res/lang/ folder).
        /// </summary>
        public string[] AvailableLanguages { get; private set; } = new[] { "ja", "en" };

        private LocalizationService()
        {
            // Find the res/lang folder relative to this script
            _resourcePath = FindResourcePath();
            DetectAvailableLanguages();
        }

        /// <summary>
        /// Loads strings for the specified language.
        /// </summary>
        /// <param name="languageCode">Language code (e.g., "ja", "en")</param>
        public void LoadLanguage(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode)) languageCode = "ja";

            string jsonPath = Path.Combine(_resourcePath, $"{languageCode}.json");
            if (!File.Exists(jsonPath))
            {
                Debug.LogWarning($"[LocalizationService] Language file not found: {jsonPath}, falling back to 'ja'");
                jsonPath = Path.Combine(_resourcePath, "ja.json");
            }

            if (!File.Exists(jsonPath))
            {
                Debug.LogError($"[LocalizationService] Default language file not found: {jsonPath}");
                return;
            }

            try
            {
                string json = File.ReadAllText(jsonPath);
                _strings = ParseJson(json);
                _currentLanguage = languageCode;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LocalizationService] Failed to load language file: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a localized string by key.
        /// </summary>
        /// <param name="key">String key as defined in the JSON file</param>
        /// <returns>Localized string, or the key itself if not found</returns>
        public string Get(string key)
        {
            if (_strings.Count == 0)
            {
                LoadLanguage(_currentLanguage);
            }

            if (_strings.TryGetValue(key, out var value))
            {
                return value;
            }

            // Return the key as fallback
            Debug.LogWarning($"[LocalizationService] Missing key: {key}");
            return key;
        }

        /// <summary>
        /// Gets a localized string by key, with a default value fallback.
        /// </summary>
        public string Get(string key, string defaultValue)
        {
            if (_strings.TryGetValue(key, out var value))
            {
                return value;
            }
            return defaultValue;
        }

        /// <summary>
        /// Gets a localized string with format arguments.
        /// </summary>
        public string Get(string key, params object[] args)
        {
            string format = Get(key);
            try
            {
                return string.Format(format, args);
            }
            catch
            {
                return format;
            }
        }

        /// <summary>
        /// Shorthand accessor.
        /// </summary>
        public string this[string key] => Get(key);

        private string FindResourcePath()
        {
            // 1. Primary fixed path check as requested
            // Expected: Assets/Editor/MaskMaker/res/lang
            string fixedPath = Path.Combine(Application.dataPath, "Editor", "MaskMaker", "res", "lang");
            if (Directory.Exists(fixedPath))
            {
                return fixedPath;
            }

            // 2. Relative fallback (in case folder was renamed but structure is intact)
            string[] guids = AssetDatabase.FindAssets("LocalizationService t:Script");
            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath) && assetPath.EndsWith("Services/LocalizationService.cs"))
                {
                    // e.g. Assets/Something/Services/LocalizationService.cs -> Assets/Something/res/lang
                    string serviceDir = Path.GetDirectoryName(assetPath);
                    string rootDir = Path.GetDirectoryName(serviceDir);
                    string langPathRelative = Path.Combine(rootDir, "res", "lang"); 
                    
                    if (AssetDatabase.IsValidFolder(langPathRelative))
                    {
                        // Convert Asset path to full system path
                        return Path.GetFullPath(langPathRelative);
                    }
                }
            }

            Debug.LogError($"[MaskMaker] Language resources not found. Expected at: {fixedPath}");
            return string.Empty;
        }

        private void DetectAvailableLanguages()
        {
            if (string.IsNullOrEmpty(_resourcePath) || !Directory.Exists(_resourcePath))
            {
                AvailableLanguages = new[] { "ja" };
                return;
            }

            var languages = new List<string>();
            foreach (var file in Directory.GetFiles(_resourcePath, "*.json"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                languages.Add(name);
            }

            if (languages.Count > 0)
            {
                AvailableLanguages = languages.ToArray();
            }
        }

        /// <summary>
        /// Simple JSON parser for flat string dictionaries.
        /// Avoids dependency on Unity's JsonUtility which doesn't support Dictionary.
        /// </summary>
        private Dictionary<string, string> ParseJson(string json)
        {
            var result = new Dictionary<string, string>();

            // Remove whitespace and braces
            json = json.Trim();
            if (json.StartsWith("{")) json = json.Substring(1);
            if (json.EndsWith("}")) json = json.Substring(0, json.Length - 1);

            int pos = 0;
            while (pos < json.Length)
            {
                // Find key
                int keyStart = json.IndexOf('"', pos);
                if (keyStart < 0) break;
                int keyEnd = json.IndexOf('"', keyStart + 1);
                if (keyEnd < 0) break;
                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                // Find colon
                int colonPos = json.IndexOf(':', keyEnd);
                if (colonPos < 0) break;

                // Find value
                int valueStart = json.IndexOf('"', colonPos);
                if (valueStart < 0) break;
                int valueEnd = valueStart + 1;
                while (valueEnd < json.Length)
                {
                    if (json[valueEnd] == '"' && json[valueEnd - 1] != '\\')
                    {
                        break;
                    }
                    valueEnd++;
                }
                if (valueEnd >= json.Length) break;

                string value = json.Substring(valueStart + 1, valueEnd - valueStart - 1);
                // Unescape basic sequences
                value = value.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");

                result[key] = value;
                pos = valueEnd + 1;
            }

            return result;
        }

        /// <summary>
        /// Reloads the current language (useful after editing JSON files).
        /// </summary>
        public void Reload()
        {
            _strings.Clear();
            LoadLanguage(_currentLanguage);
        }
    }
}
