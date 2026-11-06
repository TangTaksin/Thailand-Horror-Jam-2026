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
        [SerializeField] private SceneTransition _sceneTransition;
        [SerializeField] private TransitionEventChannelSO _transitionEventChannel;
        [SerializeField] private ThailandHorrorJam.Systems.Gameplay.CutsceneController _introCutscene;
        [SerializeField] private CanvasGroup _titleMenuCanvasGroup;
        [Tooltip("Delay in seconds between transition completion and starting the Intro cutscene.")]
        [SerializeField] private float _introStartDelay = 1.5f;

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
        private readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _raycastResults = new(16);
        private UnityEngine.EventSystems.PointerEventData _cachedPointerData;

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

            var kb = Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame)
            {
                if (SimpleLocalization.LocalizationManager.CurrentLanguage == SystemLanguage.English)
                {
                    SimpleLocalization.LocalizationManager.SetLanguage(SystemLanguage.Thai);
                }
                else
                {
                    SimpleLocalization.LocalizationManager.SetLanguage(SystemLanguage.English);
                }
            }

            // 2. Pulsing alpha on prompt text
            if (_promptCanvasGroup != null)
            {
                float alpha = Mathf.Lerp(0.25f, 1f, (Mathf.Sin(Time.time * _promptPulseSpeed) + 1f) * 0.5f);
                _promptCanvasGroup.alpha = alpha;
            }

            // 3. Pointer / Mouse click anywhere on the screen (New Input System)
            var mouse = Mouse.current;
            bool mouseClicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            var pointer = Pointer.current;
            bool pointerPressed = pointer != null && pointer.press.wasPressedThisFrame;

            if (mouseClicked || pointerPressed)
            {
                if (!IsPointerOverInteractiveUI())
                {
                    StartGame();
                    return;
                }
            }
        }

        private bool IsPointerOverInteractiveUI()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            if (eventSystem == null) return false;

            Vector2 pointerPos = Vector2.zero;
            var mouse = Mouse.current;
            if (mouse != null) pointerPos = mouse.position.ReadValue();
            else if (Pointer.current != null) pointerPos = Pointer.current.position.ReadValue();

            _cachedPointerData ??= new UnityEngine.EventSystems.PointerEventData(eventSystem);

            _cachedPointerData.position = pointerPos;
            _raycastResults.Clear();
            eventSystem.RaycastAll(_cachedPointerData, _raycastResults);

            for (int i = 0; i < _raycastResults.Count; i++)
            {
                var go = _raycastResults[i].gameObject;
                if (go == null || go.name == "FullscreenClickTarget") continue;

                var selectable = go.GetComponentInParent<UnityEngine.UI.Selectable>();
                if (selectable != null && selectable.gameObject.name != "FullscreenClickTarget")
                {
                    return true;
                }
            }

            return false;
        }

        public void StartGame()
        {
            if (_hasStarted) return;
            _hasStarted = true;

            Debug.Log("[MainMenu] Starting game transition into Intro/Gameplay...");

            if (_promptCanvasGroup != null)
            {
                _promptCanvasGroup.alpha = 1f;
            }

            // If we have an Intro cutscene, play Pixel Transition first before starting the cutscene
            if (_introCutscene != null)
            {
                StartCoroutine(PlayTransitionThenStartIntro());
                return;
            }

            // Fallback: direct scene transition to Gameplay
            if (_sceneSwitcher != null)
            {
                _sceneSwitcher.LoadNextScene();
            }
        }

        private System.Collections.IEnumerator PlayTransitionThenStartIntro()
        {
            // 1. Locate SceneTransition if not explicitly assigned
            SceneTransition transition = _sceneTransition;
            if (transition == null)
            {
                transition = FindAnyObjectByType<SceneTransition>();
            }

            if (transition != null)
            {
                bool isDone = false;
                UnityEngine.Events.UnityAction onDone = () => isDone = true;
                transition.OnTransitionDone.AddListener(onDone);

                // Play pixel transition (matures pixel curtain to cover screen)
                transition.PlayTransition();

                while (!isDone)
                {
                    yield return null;
                }

                transition.OnTransitionDone.RemoveListener(onDone);
            }

            // 2. Hide Main Menu title UI while screen is covered by pixels
            if (_titleMenuCanvasGroup != null)
            {
                _titleMenuCanvasGroup.alpha = 0f;
                _titleMenuCanvasGroup.blocksRaycasts = false;
                _titleMenuCanvasGroup.interactable = false;
            }

            // Ominous silence / suspense delay (1.5 seconds) in total blackness before Intro begins
            if (_introStartDelay > 0f)
            {
                yield return new WaitForSeconds(_introStartDelay);
            }

            // 3. Start Intro Cutscene Canvas behind the pixel veil
            if (_introCutscene != null)
            {
                _introCutscene.StartCutscene();
            }

            // 4. Reveal screen back from pixels into the Intro cutscene
            if (transition != null)
            {
                // Play forward to clear the pixel curtain (0 -> 1)
                transition.SendMessage("Start", UnityEngine.SendMessageOptions.DontRequireReceiver);
            }
        }
    }
}
