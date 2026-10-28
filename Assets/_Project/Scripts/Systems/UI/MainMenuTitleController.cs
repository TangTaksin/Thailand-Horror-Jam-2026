using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace ThailandHorrorJam.Systems.UI
{
    /// <summary>
    /// Minimalist Main Menu title screen controller.
    /// Handles subtle floating animation on the game logo, breathing pulse on the
    /// "CLICK TO START" prompt, and full-screen mouse click detection to trigger
    /// the pixel scene transition into gameplay.
    /// </summary>
    public sealed class MainMenuTitleController : MonoBehaviour
    {
        [Header("Scene Transition")]
        [SerializeField] private SceneSwitcher _sceneSwitcher;

        [Header("Visual Elements")]
        [SerializeField] private RectTransform _logoRectTransform;
        [SerializeField] private CanvasGroup _promptCanvasGroup;

        [Header("Motion Settings")]
        [Tooltip("Speed of the subtle vertical floating motion of the logo.")]
        [SerializeField] private float _logoFloatSpeed = 1.6f;

        [Tooltip("Amplitude in pixels for logo floating.")]
        [SerializeField] private float _logoFloatAmplitude = 8f;

        [Tooltip("Speed of the prompt alpha pulse.")]
        [SerializeField] private float _promptPulseSpeed = 2.4f;

        private Vector2 _logoInitialAnchoredPos;
        private bool _hasStarted;

        private void Awake()
        {
            if (_logoRectTransform != null)
            {
                _logoInitialAnchoredPos = _logoRectTransform.anchoredPosition;
            }
        }

        private void Update()
        {
            // 1. Subtle floating on logo
            if (_logoRectTransform != null)
            {
                float yOffset = Mathf.Sin(Time.time * _logoFloatSpeed) * _logoFloatAmplitude;
                _logoRectTransform.anchoredPosition = _logoInitialAnchoredPos + new Vector2(0f, yOffset);
            }

            if (_hasStarted) return;

            // 2. Pulsing alpha on prompt text
            if (_promptCanvasGroup != null)
            {
                float alpha = Mathf.Lerp(0.25f, 1f, (Mathf.Sin(Time.time * _promptPulseSpeed) + 1f) * 0.5f);
                _promptCanvasGroup.alpha = alpha;
            }

            // 3. Pointer / Mouse click anywhere on the screen (New Input System)
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                StartGame();
                return;
            }

            var pointer = Pointer.current;
            if (pointer != null && pointer.press.wasPressedThisFrame)
            {
                StartGame();
                return;
            }
        }

        public void StartGame()
        {
            if (_hasStarted) return;
            _hasStarted = true;

            Debug.Log("[MainMenu] Starting game transition into Gameplay scene...");

            if (_promptCanvasGroup != null)
            {
                _promptCanvasGroup.alpha = 1f;
            }

            if (_sceneSwitcher != null)
            {
                _sceneSwitcher.LoadNextScene();
            }
        }
    }
}
