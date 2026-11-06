using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace SimpleLocalization
{
    [Serializable]
    public struct LanguageFontMapping
    {
        public SystemLanguage language;
        public TMP_FontAsset font;
    }

    [CreateAssetMenu(fileName = "LocalizationConfig", menuName = "SimpleLocalization/Config")]
    public sealed class LocalizationConfig : ScriptableObject
    {
        [Header("Data Source")]
        [Tooltip("The TSV/CSV file containing translations. If null, loads Resources/Localization.csv automatically.")]
        [FormerlySerializedAs("tsvFile")]
        [SerializeField] private TextAsset _tsvFile;

        [Header("Language Settings")]
        [FormerlySerializedAs("defaultLanguage")]
        [SerializeField] private SystemLanguage _defaultLanguage = SystemLanguage.English;

        [FormerlySerializedAs("fallbackLanguage")]
        [SerializeField] private SystemLanguage _fallbackLanguage = SystemLanguage.English;

        [Header("Global Fonts")]
        [Tooltip("Primary font used across the project when no language-specific font is defined.")]
        [FormerlySerializedAs("defaultFont")]
        [SerializeField] private TMP_FontAsset _defaultFont;

        [FormerlySerializedAs("languageFonts")]
        [SerializeField] private List<LanguageFontMapping> _languageFonts = new();

        public TextAsset TsvFile => _tsvFile;
        public SystemLanguage DefaultLanguage => _defaultLanguage;
        public SystemLanguage FallbackLanguage => _fallbackLanguage;
        public TMP_FontAsset DefaultFont => _defaultFont;
        public IReadOnlyList<LanguageFontMapping> LanguageFonts => _languageFonts;

        public TMP_FontAsset GetFontForLanguage(SystemLanguage lang)
        {
            if (_languageFonts != null)
            {
                for (int i = 0; i < _languageFonts.Count; i++)
                {
                    if (_languageFonts[i].language == lang && _languageFonts[i].font != null)
                    {
                        return _languageFonts[i].font;
                    }
                }
            }

            return _defaultFont;
        }
    }
}
