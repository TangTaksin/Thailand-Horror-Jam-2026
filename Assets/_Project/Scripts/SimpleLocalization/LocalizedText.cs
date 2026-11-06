using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

namespace SimpleLocalization
{
    [RequireComponent(typeof(TMP_Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        [FormerlySerializedAs("key")]
        [SerializeField] private string _key;

        private TMP_Text _tmpText;

        public string Key => _key;

        private void Awake()
        {
            _tmpText = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            if (_tmpText == null) _tmpText = GetComponent<TMP_Text>();

            LocalizationManager.OnLanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            LocalizationManager.OnLanguageChanged -= Refresh;
        }

        public void SetKey(string newKey)
        {
            _key = newKey;
            Refresh();
        }

        public void Refresh()
        {
            if (_tmpText == null) _tmpText = GetComponent<TMP_Text>();
            if (_tmpText == null || string.IsNullOrEmpty(_key)) return;

            string localizedString = LocalizationManager.Get(_key);
            if (_tmpText.text != localizedString)
            {
                _tmpText.text = localizedString;
            }

            TMP_FontAsset font = LocalizationManager.GetFont(LocalizationManager.CurrentLanguage);
            if (font != null && _tmpText.font != font)
            {
                _tmpText.font = font;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_tmpText == null) _tmpText = GetComponent<TMP_Text>();
            if (!string.IsNullOrEmpty(_key))
            {
                Refresh();
            }
        }
#endif
    }
}
