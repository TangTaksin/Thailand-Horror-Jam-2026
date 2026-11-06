using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SimpleLocalization
{
    public static class LocalizationManager
    {
        public const string k_PrefsKey = "SelectedLanguage";
        public const string k_DefaultResourcePath = "Localization";
        public const string k_ConfigResourcePath = "LocalizationConfig";

        public const string PREFS_KEY = k_PrefsKey;
        public const string DEFAULT_RESOURCE_PATH = k_DefaultResourcePath;
        public const string CONFIG_RESOURCE_PATH = k_ConfigResourcePath;

        private static readonly Dictionary<string, Dictionary<SystemLanguage, string>> s_Table = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<SystemLanguage> s_SupportedLanguages = new();
        private static LocalizationConfig s_Config;
        private static bool s_IsInitialized;

        public static SystemLanguage CurrentLanguage { get; private set; } = SystemLanguage.English;
        public static SystemLanguage FallbackLanguage { get; private set; } = SystemLanguage.English;
        public static IReadOnlyList<SystemLanguage> SupportedLanguages => s_SupportedLanguages;

        public static event Action OnLanguageChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Initialize()
        {
            if (s_IsInitialized) return;

            s_Config = Resources.Load<LocalizationConfig>(CONFIG_RESOURCE_PATH);
            FallbackLanguage = s_Config != null ? s_Config.FallbackLanguage : SystemLanguage.English;
            SystemLanguage defaultLang = s_Config != null ? s_Config.DefaultLanguage : SystemLanguage.English;

            TextAsset tsvAsset = s_Config != null && s_Config.TsvFile != null 
                ? s_Config.TsvFile 
                : Resources.Load<TextAsset>(DEFAULT_RESOURCE_PATH);

            if (tsvAsset != null)
            {
                LoadFromRawText(tsvAsset.text);
            }
            else
            {
                Debug.LogWarning($"[Localization] No localization TSV found at Resources/{DEFAULT_RESOURCE_PATH} or via LocalizationConfig.");
            }

            // Determine initial language
            int savedLangIndex = PlayerPrefs.GetInt(PREFS_KEY, -1);
            if (savedLangIndex != -1 && Enum.IsDefined(typeof(SystemLanguage), savedLangIndex))
            {
                CurrentLanguage = (SystemLanguage)savedLangIndex;
            }
            else if (s_SupportedLanguages.Contains(Application.systemLanguage))
            {
                CurrentLanguage = Application.systemLanguage;
            }
            else
            {
                CurrentLanguage = s_SupportedLanguages.Contains(defaultLang) 
                    ? defaultLang 
                    : (s_SupportedLanguages.Count > 0 ? s_SupportedLanguages[0] : defaultLang);
            }

            s_IsInitialized = true;
        }

        public static void LoadFromRawText(string rawTsv)
        {
            s_Table.Clear();
            s_SupportedLanguages.Clear();

            if (string.IsNullOrWhiteSpace(rawTsv)) return;

            string[] lines = rawTsv.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return;

            // Find header row (skip comments/empty lines before header)
            int headerIndex = -1;
            List<string> headers = null;
            char delimiter = '\t';

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                // Auto-detect delimiter: tab, comma, or semicolon
                delimiter = DetectDelimiter(lines[i]);
                headers = ParseCsvLine(lines[i], delimiter);
                headerIndex = i;
                break;
            }

            if (headers == null || headers.Count < 2) return;

            var columnLanguages = new List<SystemLanguage?>();
            // First column is the key
            columnLanguages.Add(null);

            for (int c = 1; c < headers.Count; c++)
            {
                string headerName = headers[c].Trim();
                if (TryParseLanguage(headerName, out SystemLanguage parsedLang))
                {
                    columnLanguages.Add(parsedLang);
                    if (!s_SupportedLanguages.Contains(parsedLang))
                    {
                        s_SupportedLanguages.Add(parsedLang);
                    }
                }
                else
                {
                    columnLanguages.Add(null);
                }
            }

            // Parse data rows
            for (int i = headerIndex + 1; i < lines.Length; i++)
            {
                string rawLine = lines[i];
                string trimmed = rawLine.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                List<string> cols = ParseCsvLine(rawLine, delimiter);
                if (cols.Count < 1) continue;

                string key = cols[0].Trim();
                if (string.IsNullOrEmpty(key)) continue;

                if (!s_Table.TryGetValue(key, out var langDict))
                {
                    langDict = new Dictionary<SystemLanguage, string>();
                    s_Table[key] = langDict;
                }

                for (int c = 1; c < cols.Count && c < columnLanguages.Count; c++)
                {
                    var lang = columnLanguages[c];
                    if (!lang.HasValue) continue;

                    string text = cols[c].Replace("\\n", "\n").Trim();
                    langDict[lang.Value] = text;
                }
            }
        }

        private static char DetectDelimiter(string headerLine)
        {
            if (headerLine.Contains('\t')) return '\t';
            if (headerLine.Contains(',')) return ',';
            if (headerLine.Contains(';')) return ';';
            return '\t';
        }

        private static List<string> ParseCsvLine(string line, char delimiter)
        {
            var result = new List<string>();
            var sb = new System.Text.StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++; // Skip escaped quote
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    result.Add(sb.ToString().Trim());
                    sb.Clear();
                }
                else
                {
                    sb.Append(c);
                }
            }

            result.Add(sb.ToString().Trim());
            return result;
        }

        public static void SetLanguage(SystemLanguage language)
        {
            if (CurrentLanguage == language && s_IsInitialized) return;

            CurrentLanguage = language;
            PlayerPrefs.SetInt(PREFS_KEY, (int)language);
            PlayerPrefs.Save();

            OnLanguageChanged?.Invoke();
        }

        public static void Reload()
        {
            s_IsInitialized = false;
            Initialize();
            OnLanguageChanged?.Invoke();
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (!s_IsInitialized)
            {
                Initialize();
            }

            if (!s_Table.TryGetValue(key, out var langDict))
            {
#if UNITY_EDITOR
                // In Editor, tests or external edits may have cleared or modified the in-memory table.
                // Re-initialize from Resources to guarantee freshness before reporting missing key.
                if (!Application.isPlaying)
                {
                    s_IsInitialized = false;
                    Initialize();
                    s_Table.TryGetValue(key, out langDict);
                }
#endif
            }

            if (langDict != null)
            {
                if (langDict.TryGetValue(CurrentLanguage, out var text) && !string.IsNullOrEmpty(text))
                {
                    return text;
                }

                // Fallback to designated fallback language
                if (langDict.TryGetValue(FallbackLanguage, out var fallbackText) && !string.IsNullOrEmpty(fallbackText))
                {
                    return fallbackText;
                }

                // Fallback to English
                if (FallbackLanguage != SystemLanguage.English && langDict.TryGetValue(SystemLanguage.English, out var engText) && !string.IsNullOrEmpty(engText))
                {
                    return engText;
                }
            }

            Debug.LogWarning($"[Localization] Missing key: '{key}' for language '{CurrentLanguage}'");
            return $"MISSING:[{key}]";
        }

        public static string Get(string key, params object[] args)
        {
            if (args == null || args.Length == 0) return Get(key);
            if (string.IsNullOrEmpty(key)) return string.Empty;

            if (!s_IsInitialized)
            {
                Initialize();
            }

            if (s_Table.TryGetValue(key, out var langDict))
            {
                if (langDict.TryGetValue(CurrentLanguage, out var text) && !string.IsNullOrEmpty(text))
                {
                    return FormatString(text, args);
                }

                // Fallback to designated fallback language
                if (langDict.TryGetValue(FallbackLanguage, out var fallbackText) && !string.IsNullOrEmpty(fallbackText))
                {
                    return FormatString(fallbackText, args);
                }

                // Fallback to English
                if (FallbackLanguage != SystemLanguage.English && langDict.TryGetValue(SystemLanguage.English, out var engText) && !string.IsNullOrEmpty(engText))
                {
                    return FormatString(engText, args);
                }
            }

            Debug.LogWarning($"[Localization] Missing key: '{key}' for language '{CurrentLanguage}'");
            return $"MISSING:[{key}]";
        }

        public static TMP_FontAsset GetFont(SystemLanguage language)
        {
            if (s_Config != null)
            {
                return s_Config.GetFontForLanguage(language);
            }
            return null;
        }

        public static void SetConfig(LocalizationConfig config)
        {
            s_Config = config;
            if (config != null)
            {
                FallbackLanguage = config.FallbackLanguage;
            }
        }

        public static bool HasKey(string key)
        {
            return !string.IsNullOrEmpty(key) && s_Table.ContainsKey(key);
        }

        private static string FormatString(string text, object[] args)
        {
            if (args == null || args.Length == 0) return text;
            try
            {
                return string.Format(text, args);
            }
            catch (FormatException ex)
            {
                Debug.LogError($"[Localization] Format exception on text: '{text}'. Error: {ex.Message}");
                return text;
            }
        }

        private static bool TryParseLanguage(string input, out SystemLanguage language)
        {
            if (Enum.TryParse(input, true, out language))
            {
                return true;
            }

            // Common abbreviations
            string lower = input.ToLowerInvariant();
            switch (lower)
            {
                case "th":
                case "tha":
                case "thai":
                    language = SystemLanguage.Thai;
                    return true;
                case "en":
                case "eng":
                case "english":
                    language = SystemLanguage.English;
                    return true;
                case "ja":
                case "jp":
                case "japanese":
                    language = SystemLanguage.Japanese;
                    return true;
                case "zh":
                case "cn":
                case "chinese":
                    language = SystemLanguage.ChineseSimplified;
                    return true;
                default:
                    language = SystemLanguage.Unknown;
                    return false;
            }
        }
    }

    public static class LocalizationExtensions
    {
        public static string Localize(this string key)
        {
            return LocalizationManager.Get(key);
        }

        public static string Localize(this string key, params object[] args)
        {
            return LocalizationManager.Get(key, args);
        }
    }
}
