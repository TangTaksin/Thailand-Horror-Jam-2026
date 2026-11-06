using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace SimpleLocalization
{
    [RequireComponent(typeof(Button))]
    public sealed class LanguageToggleUI : MonoBehaviour
    {
        [Header("Target Languages to Cycle")]
        [FormerlySerializedAs("toggleLanguages")]
        [SerializeField] private SystemLanguage[] _toggleLanguages = new[] { SystemLanguage.English, SystemLanguage.Thai };

        [Header("UI Label (Optional)")]
        [Tooltip("If null, searches in children for TMP_Text.")]
        [FormerlySerializedAs("labelText")]
        [SerializeField] private TMP_Text _labelText;

        [Tooltip("Optional localization key for the button label (e.g. 'ui_language_btn'). If empty or missing, displays short language code (EN/TH).")]
        [FormerlySerializedAs("localizationKey")]
        [SerializeField] private string _localizationKey;

        [Tooltip("If true, shows the language you will switch TO, e.g. 'TH' when current is EN. If false, shows current language.")]
        [FormerlySerializedAs("showNextLanguageOnButton")]
        [SerializeField] private bool _showNextLanguageOnButton = false;

        private Button _button;

        private void Awake()
        {
            _button = GetComponent<Button>();
            if (_labelText == null)
            {
                _labelText = GetComponentInChildren<TMP_Text>();
            }
        }

        private void OnEnable()
        {
            if (_button != null)
            {
                _button.onClick.AddListener(OnToggleClicked);
            }

            LocalizationManager.OnLanguageChanged += UpdateLabel;
            UpdateLabel();
        }

        private void OnDisable()
        {
            if (_button != null)
            {
                _button.onClick.RemoveListener(OnToggleClicked);
            }

            LocalizationManager.OnLanguageChanged -= UpdateLabel;
        }

        public void OnToggleClicked()
        {
            if (_toggleLanguages == null || _toggleLanguages.Length == 0) return;

            int currentIndex = Array.IndexOf(_toggleLanguages, LocalizationManager.CurrentLanguage);
            int nextIndex = (currentIndex + 1) % _toggleLanguages.Length;
            if (currentIndex == -1)
            {
                nextIndex = 0;
            }

            LocalizationManager.SetLanguage(_toggleLanguages[nextIndex]);
        }

        public void UpdateLabel()
        {
            if (_labelText == null) return;

            var font = LocalizationManager.GetFont(LocalizationManager.CurrentLanguage);
            if (font != null && _labelText.font != font)
            {
                _labelText.font = font;
            }

            if (!string.IsNullOrEmpty(_localizationKey) && LocalizationManager.HasKey(_localizationKey))
            {
                _labelText.text = LocalizationManager.Get(_localizationKey);
                return;
            }

            SystemLanguage displayLang = LocalizationManager.CurrentLanguage;
            if (_showNextLanguageOnButton && _toggleLanguages != null && _toggleLanguages.Length > 1)
            {
                int curr = Array.IndexOf(_toggleLanguages, LocalizationManager.CurrentLanguage);
                int next = (curr + 1) % _toggleLanguages.Length;
                displayLang = _toggleLanguages[next];
            }

            _labelText.text = GetShortLanguageName(displayLang);
        }

        private static string GetShortLanguageName(SystemLanguage lang)
        {
            switch (lang)
            {
                case SystemLanguage.English: return "EN";
                case SystemLanguage.Thai: return "TH";
                case SystemLanguage.Japanese: return "JP";
                case SystemLanguage.ChineseSimplified: return "CN";
                default: return lang.ToString().Substring(0, Math.Min(3, lang.ToString().Length)).ToUpper();
            }
        }
    }
}
