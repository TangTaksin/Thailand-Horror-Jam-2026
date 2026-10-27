using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Interaction
{
    // Runs after CameraController2D.LateUpdate so WorldToScreenPoint never lags 1 frame behind camera smoothing.
    [DefaultExecutionOrder(100)]
    public sealed class InteractionPromptUI : MonoBehaviour
    {
        private const float k_HiddenScale = 0.72f;
        private const float k_AwareDotScale = 0.65f;
        private const float k_InteractIconScale = 1.0f;

        [Header("Channel & Icons")]
        [SerializeField] private InteractionEventChannelSO _interactionChannel;
        [SerializeField] private InteractionPromptIconSetSO _iconSet;

        [Header("UI References")]
        [SerializeField] private RectTransform _promptRoot;
        [SerializeField] private CanvasGroup _promptCanvasGroup;
        [SerializeField] private Image _dotImage;
        [SerializeField] private Image _leftClickImage;
        [SerializeField] private Image _rightClickImage;
        [SerializeField] private TextMeshProUGUI _actionHintText;

        [Header("Transition & Offset")]
        [SerializeField] private float _transitionDuration = 0.12f;
        [SerializeField] private float _awareDotAlpha = 0.45f;
        [SerializeField] private float _aboveBoxOffset = 0.4f;
        [SerializeField] private Vector2 _screenOffset = Vector2.zero;

        private Camera _mainCamera;
        private StitchableObject _currentTarget;
        private PromptStage _targetStage = PromptStage.Hidden;

        private float _currentOverallAlpha;
        private float _currentIconBlend;
        private float _currentScale = k_HiddenScale;

        private void Awake()
        {
            _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            ResetDefaultLayout();
            ApplySpritesFromSet();
            ApplyCurrentVisuals();
        }

        private void OnEnable()
        {
            ResetDefaultLayout();
            if (_interactionChannel == null) return;
            _interactionChannel.FocusChanged += HandleFocusChanged;
            HandleFocusChanged(_interactionChannel.LastFocusData);
        }

        public void ResetDefaultLayout()
        {
            if (_dotImage != null)
            {
                _dotImage.gameObject.SetActive(true);
                RectTransform rt = _dotImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(16f, 16f);
            }

            if (_leftClickImage != null)
            {
                _leftClickImage.gameObject.SetActive(true);
                RectTransform rt = _leftClickImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(-28f, 10f);
                rt.sizeDelta = new Vector2(34f, 34f);
            }

            if (_rightClickImage != null)
            {
                _rightClickImage.gameObject.SetActive(true);
                RectTransform rt = _rightClickImage.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(28f, 10f);
                rt.sizeDelta = new Vector2(34f, 34f);
            }

            if (_actionHintText != null)
            {
                _actionHintText.gameObject.SetActive(true);
                RectTransform rt = _actionHintText.rectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, -14f);
                rt.sizeDelta = new Vector2(140f, 20f);
                _actionHintText.alignment = TextAlignmentOptions.Center;
            }
        }

        private void OnDisable()
        {
            if (_interactionChannel == null) return;
            _interactionChannel.FocusChanged -= HandleFocusChanged;
        }

        private void LateUpdate()
        {
            if (_targetStage != PromptStage.Hidden && _targetStage != PromptStage.AimingPrompt && (_currentTarget == null || !_currentTarget.gameObject.activeInHierarchy))
            {
                _targetStage = PromptStage.Hidden;
                _currentTarget = null;
            }

            UpdateTransitionValues();

            if (_currentOverallAlpha > 0.001f || _targetStage != PromptStage.Hidden)
            {
                UpdateScreenPosition();
            }
        }

        private void HandleFocusChanged(InteractionFocusData focusData)
        {
            bool switchedTarget = _currentTarget != null && focusData.Target != null && !ReferenceEquals(_currentTarget, focusData.Target);

            _currentTarget = focusData.Target;
            _targetStage = focusData.Stage;

            UpdateHintLabels();

            if (switchedTarget && _targetStage != PromptStage.Hidden)
            {
                _currentScale = Mathf.Min(_currentScale, 0.84f);
            }

            if (_targetStage != PromptStage.Hidden)
            {
                UpdateScreenPosition();
            }
        }

        private void UpdateHintLabels()
        {
            if (_actionHintText == null) return;

            if (_targetStage == PromptStage.AimingPrompt)
            {
                // เล็งเข็มอยู่: ซ้าย=ยิง, ขวา=ออก
                _actionHintText.text = "Shoot        Cancel";
                _actionHintText.enabled = true;
            }
            else if (_targetStage == PromptStage.InteractIcon)
            {
                // หน้ากล่อง: ซ้าย=เย็บ, ขวา=ลาก
                _actionHintText.text = "Stitch        Drag";
                _actionHintText.enabled = true;
            }
            else
            {
                _actionHintText.enabled = false;
            }
        }

        private void ApplySpritesFromSet()
        {
            if (_iconSet == null) return;
            if (_dotImage != null && _iconSet.DotSprite != null) _dotImage.sprite = _iconSet.DotSprite;
            if (_leftClickImage != null && _iconSet.LeftClickSprite != null) _leftClickImage.sprite = _iconSet.LeftClickSprite;
            if (_rightClickImage != null && _iconSet.RightClickSprite != null) _rightClickImage.sprite = _iconSet.RightClickSprite;
        }

        private void UpdateTransitionValues()
        {
            float step = Time.unscaledDeltaTime / Mathf.Max(0.05f, _transitionDuration);

            bool isFullPrompt = _targetStage == PromptStage.InteractIcon || _targetStage == PromptStage.AimingPrompt;
            float targetAlpha = isFullPrompt ? 1f : (_targetStage == PromptStage.AwareDot ? _awareDotAlpha : 0f);
            float targetBlend = isFullPrompt ? 1f : (_targetStage == PromptStage.AwareDot ? 0f : _currentIconBlend);
            float targetScale = isFullPrompt ? k_InteractIconScale : (_targetStage == PromptStage.AwareDot ? k_AwareDotScale : k_HiddenScale);

            _currentOverallAlpha = Mathf.MoveTowards(_currentOverallAlpha, targetAlpha, step);
            _currentIconBlend = Mathf.MoveTowards(_currentIconBlend, targetBlend, step);
            _currentScale = Mathf.MoveTowards(_currentScale, targetScale, step * 0.6f);

            ApplyCurrentVisuals();
        }

        private void ApplyCurrentVisuals()
        {
            if (_promptCanvasGroup != null) _promptCanvasGroup.alpha = _currentOverallAlpha;
            if (_promptRoot != null) _promptRoot.localScale = new Vector3(_currentScale, _currentScale, 1f);

            if (_dotImage != null)
            {
                if (!_dotImage.gameObject.activeSelf) _dotImage.gameObject.SetActive(true);
                Color c = _dotImage.color;
                c.a = 1f - _currentIconBlend;
                _dotImage.color = c;
            }

            if (_leftClickImage != null)
            {
                if (!_leftClickImage.gameObject.activeSelf) _leftClickImage.gameObject.SetActive(true);
                Color c = _leftClickImage.color;
                c.a = _currentIconBlend;
                _leftClickImage.color = c;
            }

            if (_rightClickImage != null)
            {
                if (!_rightClickImage.gameObject.activeSelf) _rightClickImage.gameObject.SetActive(true);
                Color c = _rightClickImage.color;
                c.a = _currentIconBlend;
                _rightClickImage.color = c;
            }

            if (_actionHintText != null)
            {
                if (!_actionHintText.gameObject.activeSelf) _actionHintText.gameObject.SetActive(true);
                Color c = _actionHintText.color;
                c.a = _currentIconBlend * 0.9f;
                _actionHintText.color = c;
            }
        }

        private void UpdateScreenPosition()
        {
            if (_promptRoot == null) return;
            if (_mainCamera == null)
            {
                _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
                if (_mainCamera == null) return;
            }

            Vector3 worldAnchor;
            if (_targetStage == PromptStage.AimingPrompt)
            {
                // ลอยตาม Mouse Cursor ในโหมดเล็ง
                Vector2 mousePos = UnityEngine.InputSystem.Mouse.current != null
                    ? UnityEngine.InputSystem.Mouse.current.position.ReadValue()
                    : Vector2.zero;
                _promptRoot.position = new Vector3(mousePos.x, mousePos.y + 45f, 0f);
                return;
            }
            else if (_currentTarget != null)
            {
                Collider2D col = _currentTarget.Collider;
                worldAnchor = col != null
                    ? new Vector3(col.bounds.center.x, col.bounds.max.y + _aboveBoxOffset, 0f)
                    : _currentTarget.transform.position + new Vector3(0f, _aboveBoxOffset + 0.5f, 0f);
            }
            else
            {
                return;
            }

            Vector3 screenPos = _mainCamera.WorldToScreenPoint(worldAnchor);
            if (screenPos.z < 0f)
            {
                if (_promptCanvasGroup != null) _promptCanvasGroup.alpha = 0f;
                return;
            }

            _promptRoot.position = new Vector3(screenPos.x + _screenOffset.x, screenPos.y + _screenOffset.y, 0f);
        }
    }
}
