using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ThailandHorrorJam.Systems.Gameplay
{
    public enum CutsceneEndAction
    {
        ShowEndCard,
        LoadScene,
        TriggerEventOnly
    }

    public enum CutsceneDisplayMode
    {
        [Tooltip("Centered illustration with bottom typewriter subtitle box (Undertale style, ideal for Intro).")]
        WindowedWithSubtitles,
        [Tooltip("Full-screen borderless movie presentation with subtitles completely hidden (ideal for Ending finale).")]
        FullScreenCinematicMovie
    }

    [Serializable]
    public struct CutsceneSlide
    {
        [Tooltip("Illustration sprite for this slide.")]
        public Sprite image;

        [TextArea(2, 5)]
        [Tooltip("Narration subtitle text (supports Undertale-style typewriter reveal).")]
        public string subtitle;

        [Tooltip("Duration in seconds for this slide. If <= 0, falls back to default slide duration.")]
        public float duration;
    }

    /// <summary>
    /// Generalized cinematic slide cutscene controller suitable for both Intro and Ending sequences.
    /// Supports centered illustrations, bottom subtitle typewriter text with SFX, and automated playback.
    /// </summary>
    public class CutsceneController : MonoBehaviour
    {
        [Header("Display Mode")]
        [Tooltip("Choose between Windowed with Subtitles (Intro) or Full-Screen Borderless Movie without text (Ending).")]
        [SerializeField] protected CutsceneDisplayMode _displayMode = CutsceneDisplayMode.WindowedWithSubtitles;

        [Header("Slides")]
        [Tooltip("List of slides with illustration and subtitle.")]
        [SerializeField] protected CutsceneSlide[] _slides;

        [Header("Timing")]
        [Tooltip("Default display duration per slide in seconds.")]
        [SerializeField] protected float _slideDuration = 3.5f;

        [Tooltip("Delay between each typed character in seconds.")]
        [SerializeField] protected float _typewriterSpeed = 0.04f;

        [Header("Cinematic Polish")]
        [Tooltip("Fade in/out duration in seconds when transitioning slides.")]
        [SerializeField] protected float _fadeDuration = 0.45f;
        [Tooltip("Enable subtle handheld camera drift/breathing motion (Static Film with organic micro-movement).")]
        [SerializeField] protected bool _enableCameraDrift = true;
        [Tooltip("Pixel amplitude of the slow organic drift motion.")]
        [SerializeField] protected float _driftAmplitude = 6.0f;
        [Tooltip("Speed frequency of the organic drift motion.")]
        [SerializeField] protected float _driftSpeed = 0.8f;

        [Header("Pacing & Readability Padding")]
        [Tooltip("Delay in seconds after image appears before typewriter text starts typing.")]
        [SerializeField] protected float _typingStartPadding = 0.4f;
        [Tooltip("Hold duration in seconds after typing completes to allow comfortable reading before moving to next slide.")]
        [SerializeField] protected float _readingHoldPadding = 1.0f;

        [Header("End Behavior")]
        [SerializeField] protected CutsceneEndAction _endAction = CutsceneEndAction.ShowEndCard;
        [Tooltip("Target scene name to transition to when EndAction is LoadScene.")]
        [SerializeField] protected string _targetSceneName = "Gameplay";
        [Tooltip("Optional SceneSwitcher component for pixel fade transition.")]
        [SerializeField] protected SceneSwitcher _sceneSwitcher;

        [Header("UI References")]
        [SerializeField] protected CanvasGroup _canvasGroup;
        [SerializeField] protected Image _slideImage;
        [SerializeField] protected TextMeshProUGUI _subtitleText;
        [SerializeField] protected GameObject _endCardPanel;
        [SerializeField] protected Button _reenterLoopButton;
        [Tooltip("Optional debug button to immediately skip the entire cutscene.")]
        [SerializeField] protected Button _skipButton;
        [Tooltip("If true, pressing Space, Escape, or Tab on keyboard will also skip (for fast debugging).")]
        [SerializeField] protected bool _allowKeyboardSkip = true;

        [Header("Ending Optimization")]
        [Tooltip("Deactivate all gameplay sectors and actors when this cutscene begins (recommended for ending cutscene).")]
        [SerializeField] protected bool _deactivateGameplaySectors = false;

        [Header("Audio SFX (Typewriter)")]
        [SerializeField] protected bool _playTypewriterSFX = true;
        [SerializeField] protected string[] _typewriterSFXNames = new[] { "Typewriter_1", "Typewriter_2" };
        [Tooltip("Play SFX every N characters to avoid audio overload.")]
        [SerializeField] protected int _sfxCharInterval = 2;

        [Header("Music (Ending / Custom)")]
        [Tooltip("Optional music clip to play when this cutscene starts. If unassigned on an Ending cutscene, AudioManager.endingMusicClip is used.")]
        [SerializeField] protected AudioClip _cutsceneMusicClip;

        protected Coroutine _cutsceneRoutine;
        protected Coroutine _typewriterRoutine;
        protected int _currentSlideIndex = -1;
        protected bool _isPlaying;
        protected bool _isCompleted;
        protected bool _isReenteringLoop;
        protected Vector2 _slideBaseAnchoredPos;

        public bool IsPlaying => _isPlaying;
        public bool IsCompleted => _isCompleted;
        public int CurrentSlideIndex => _currentSlideIndex;
        public int TotalSlides => _slides != null ? _slides.Length : 0;
        protected static bool IsDevBuildAllowed => Application.isEditor || Debug.isDebugBuild;

        public event Action OnCutsceneStarted;
        public event Action<int> OnSlideChanged;
        public event Action OnCutsceneCompleted;

        protected virtual void Awake()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }

            if (_reenterLoopButton != null)
            {
                _reenterLoopButton.onClick.AddListener(OnReenterLoopClicked);
            }

            if (_skipButton != null)
            {
                if (IsDevBuildAllowed)
                {
                    _skipButton.onClick.AddListener(SkipCutscene);
                }
                else
                {
                    _skipButton.gameObject.SetActive(false);
                }
            }

            // Initially hide the cutscene canvas if not active
            if (_canvasGroup != null && !_isPlaying)
            {
                _canvasGroup.alpha = 0f;
                _canvasGroup.blocksRaycasts = false;
            }

            if (_endCardPanel != null)
            {
                _endCardPanel.SetActive(false);
            }

            if (_subtitleText != null)
            {
                _subtitleText.text = string.Empty;
            }

            ApplyDisplayModeLayout();
            if (_slideImage != null)
            {
                _slideBaseAnchoredPos = _slideImage.rectTransform.anchoredPosition;
            }
        }

        protected virtual void OnDestroy()
        {
            if (_cutsceneRoutine != null)
            {
                StopCoroutine(_cutsceneRoutine);
                _cutsceneRoutine = null;
            }

            if (_typewriterRoutine != null)
            {
                StopCoroutine(_typewriterRoutine);
                _typewriterRoutine = null;
            }

            if (_reenterLoopButton != null)
            {
                _reenterLoopButton.onClick.RemoveListener(OnReenterLoopClicked);
            }

            if (_skipButton != null)
            {
                _skipButton.onClick.RemoveListener(SkipCutscene);
            }
        }

        /// <summary>
        /// Starts automated continuous playback of the cutscene.
        /// </summary>
        /// <summary>
        /// Configures UI layout based on selected display mode: Full-screen stretch (Movie) vs centered with subtitles.
        /// </summary>
        protected virtual void ApplyDisplayModeLayout()
        {
            if (_displayMode == CutsceneDisplayMode.FullScreenCinematicMovie)
            {
                // Full Screen Movie: hide subtitles completely & stretch slide image to fill entire screen
                if (_subtitleText != null)
                {
                    _subtitleText.gameObject.SetActive(false);
                }

                if (_slideImage != null)
                {
                    var rt = _slideImage.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.sizeDelta = Vector2.zero;
                    rt.anchoredPosition = Vector2.zero;
                    _slideImage.preserveAspect = true;
                }
            }
            else
            {
                // Windowed with subtitles (Undertale style)
                if (_subtitleText != null)
                {
                    _subtitleText.gameObject.SetActive(true);
                }
            }
        }

        public void StartCutscene()
        {
            if (_isPlaying) return;

            _isPlaying = true;
            _isCompleted = false;

            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
                _canvasGroup.blocksRaycasts = true;
            }

            if (_endCardPanel != null)
            {
                _endCardPanel.SetActive(false);
            }

            // Lock player movement and needle interactions during cutscenes
            LockPlayerAndNeedle(true);

            // Deactivate all gameplay sectors if requested or in full-screen ending movie mode
            if (_deactivateGameplaySectors || _displayMode == CutsceneDisplayMode.FullScreenCinematicMovie)
            {
                DeactivateGameplaySectors();
            }

            PlayCutsceneMusic();

            OnCutsceneStarted?.Invoke();

            if (_cutsceneRoutine != null)
            {
                StopCoroutine(_cutsceneRoutine);
            }
            _cutsceneRoutine = StartCoroutine(PlayCutsceneRoutine());
        }

        protected virtual void Update()
        {
            if (!_isPlaying || !_allowKeyboardSkip || !IsDevBuildAllowed) return;

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame))
            {
                SkipCutscene();
            }
        }

        /// <summary>
        /// Immediately skips remaining slides and executes the configured end action (for debugging).
        /// </summary>
        public virtual void SkipCutscene()
        {
            if (!_isPlaying) return;

            if (_cutsceneRoutine != null)
            {
                StopCoroutine(_cutsceneRoutine);
                _cutsceneRoutine = null;
            }

            if (_typewriterRoutine != null)
            {
                StopCoroutine(_typewriterRoutine);
                _typewriterRoutine = null;
            }

            _isCompleted = true;
            _isPlaying = false;

            if (_deactivateGameplaySectors || _displayMode == CutsceneDisplayMode.FullScreenCinematicMovie)
            {
                DeactivateGameplaySectors();
            }

            if (_subtitleText != null)
            {
                _subtitleText.text = string.Empty;
            }

            ApplyDisplayModeLayout();
            if (_slideImage != null)
            {
                _slideBaseAnchoredPos = _slideImage.rectTransform.anchoredPosition;
            }

            OnCutsceneCompleted?.Invoke();

            switch (_endAction)
            {
                case CutsceneEndAction.ShowEndCard:
                    if (_endCardPanel != null)
                    {
                        _endCardPanel.SetActive(true);
                    }
                    LockPlayerAndNeedle(true);
                    break;

                case CutsceneEndAction.LoadScene:
                    ExecuteSceneTransition();
                    break;
            }
        }

        protected virtual IEnumerator PlayCutsceneRoutine()
        {
            int total = TotalSlides;

            if (total > 0)
            {
                for (int i = 0; i < total; i++)
                {
                    _currentSlideIndex = i;

                    var slide = _slides[i];
                    Sprite slideSprite = slide.image;
                    string subtitle = slide.subtitle;
                    float duration = slide.duration > 0f ? slide.duration : _slideDuration;

                    float estimatedTypingTime = !string.IsNullOrEmpty(subtitle) ? subtitle.Length * _typewriterSpeed : 0f;
                    float actualSlideDuration = Mathf.Max(duration, estimatedTypingTime + _readingHoldPadding);

                    // 1. Assign image & lock scale to exact 1.0 while completely black
                    if (_slideImage != null)
                    {
                        _slideImage.sprite = slideSprite;
                        _slideImage.rectTransform.localScale = Vector3.one;
                        _slideImage.rectTransform.anchoredPosition = _slideBaseAnchoredPos;
                        _slideImage.color = new Color(1f, 1f, 1f, 0f);
                    }

                    OnSlideChanged?.Invoke(i);

                    // 2. Play slide with Static Film Dissolve + organic subtle camera drift
                    bool isLast = (i == total - 1);
                    bool shouldHoldFinalImage = isLast && (_endAction == CutsceneEndAction.ShowEndCard);
                    yield return StartCoroutine(PlaySingleSlideLifecycle(slideSprite, subtitle, actualSlideDuration, i, shouldHoldFinalImage));
                }

                // Only clear to blackness if we are transitioning away (LoadScene); otherwise keep last slide visible behind End Card
                if (_slideImage != null && _endAction != CutsceneEndAction.ShowEndCard)
                {
                    _slideImage.color = Color.clear;
                }
            }

            // Cutscene completed
            _isCompleted = true;
            _isPlaying = false;
            _cutsceneRoutine = null;

            OnCutsceneCompleted?.Invoke();

            // Handle configured EndAction
            switch (_endAction)
            {
                case CutsceneEndAction.ShowEndCard:
                    if (_endCardPanel != null)
                    {
                        _endCardPanel.SetActive(true);
                    }
                    break;

                case CutsceneEndAction.LoadScene:
                    ExecuteSceneTransition();
                    break;

                case CutsceneEndAction.TriggerEventOnly:
                    break;
            }
        }

        /// <summary>
        /// Plays a single slide with perfectly smooth continuous Ken Burns zoom from start to finish.
        /// Scales continuously across FadeIn -> HeadPadding -> Typing -> Hold -> FadeOut.
        /// </summary>
        protected virtual IEnumerator PlaySingleSlideLifecycle(Sprite sprite, string subtitle, float contentDuration, int slideIndex, bool isLastSlide)
        {
            float fadeInTime = _fadeDuration * 0.5f;
            float fadeOutTime = _fadeDuration * 0.5f;
            float totalLifeTime = fadeInTime + _typingStartPadding + contentDuration + fadeOutTime;

            float elapsed = 0f;
            bool typewriterStarted = false;
            float seed = slideIndex * 13.37f;

            while (elapsed < totalLifeTime)
            {
                elapsed += Time.deltaTime;

                // A. Static Scale (1.0) with subtle organic horror camera drift (Smooth Lissajous / Perlin float)
                if (_slideImage != null)
                {
                    _slideImage.rectTransform.localScale = Vector3.one;

                    if (_enableCameraDrift)
                    {
                        float driftTime = (elapsed + seed) * _driftSpeed;
                        float offsetX = Mathf.Sin(driftTime) * _driftAmplitude;
                        float offsetY = Mathf.Cos(driftTime * 0.73f) * (_driftAmplitude * 0.65f);
                        _slideImage.rectTransform.anchoredPosition = _slideBaseAnchoredPos + new Vector2(offsetX, offsetY);
                    }
                }

                // B. Fade In phase
                if (elapsed <= fadeInTime)
                {
                    float alphaProgress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeInTime));
                    if (_slideImage != null)
                    {
                        Color c = _slideImage.color;
                        c.a = Mathf.SmoothStep(0f, 1f, alphaProgress);
                        _slideImage.color = c;
                    }
                }
                // C. Active display phase
                else if (elapsed <= totalLifeTime - fadeOutTime)
                {
                    if (_slideImage != null && _slideImage.color.a < 1f)
                    {
                        Color c = _slideImage.color;
                        c.a = 1f;
                        _slideImage.color = c;
                    }

                    // Start typewriter after HeadPadding has elapsed
                    if (!typewriterStarted && elapsed >= (fadeInTime + _typingStartPadding))
                    {
                        typewriterStarted = true;
                        if (_displayMode == CutsceneDisplayMode.WindowedWithSubtitles && _subtitleText != null)
                        {
                            if (_typewriterRoutine != null) StopCoroutine(_typewriterRoutine);
                            if (!string.IsNullOrEmpty(subtitle))
                            {
                                _typewriterRoutine = StartCoroutine(TypewriterRoutine(subtitle));
                            }
                            else
                            {
                                _subtitleText.text = string.Empty;
                            }
                        }
                    }
                }
                // D. Fade Out phase (at the end of slide life)
                else
                {
                    // Clear subtitle text as fade out begins for clean transition
                    if (_subtitleText != null && !string.IsNullOrEmpty(_subtitleText.text))
                    {
                        _subtitleText.text = string.Empty;
                    }

                    if (!isLastSlide)
                    {
                        float fadeOutElapsed = elapsed - (totalLifeTime - fadeOutTime);
                        float alphaProgress = Mathf.Clamp01(fadeOutElapsed / Mathf.Max(0.01f, fadeOutTime));
                        if (_slideImage != null)
                        {
                            Color c = _slideImage.color;
                            c.a = Mathf.SmoothStep(1f, 0f, alphaProgress);
                            _slideImage.color = c;
                        }
                    }
                    else if (_slideImage != null)
                    {
                        // Hold final image at 100% opacity for the End Card presentation
                        Color c = _slideImage.color;
                        c.a = 1f;
                        _slideImage.color = c;
                    }
                }

                yield return null;
            }

            // Slide finished: only zero-out alpha if not holding the final image
            if (!isLastSlide && _slideImage != null)
            {
                Color c = _slideImage.color;
                c.a = 0f;
                _slideImage.color = c;
            }

            if (_subtitleText != null)
            {
                _subtitleText.text = string.Empty;
            }
        }

        protected virtual IEnumerator FadeSlideImage(float fromAlpha, float toAlpha, float duration)
        {
            if (_slideImage == null || duration <= 0f) yield break;

            float elapsed = 0f;
            Color c = _slideImage.color;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                c.a = Mathf.Lerp(fromAlpha, toAlpha, Mathf.SmoothStep(0f, 1f, t));
                _slideImage.color = c;
                yield return null;
            }

            c.a = toAlpha;
            _slideImage.color = c;
        }

        protected virtual IEnumerator TypewriterRoutine(string fullText)
        {
            _subtitleText.text = string.Empty;
            int soundCharCounter = 0;

            for (int i = 0; i < fullText.Length; i++)
            {
                _subtitleText.text += fullText[i];

                char c = fullText[i];
                if (_playTypewriterSFX && !char.IsWhiteSpace(c))
                {
                    soundCharCounter++;
                    if (soundCharCounter % Mathf.Max(1, _sfxCharInterval) == 0)
                    {
                        PlayTypewriterSound();
                    }
                }

                yield return new WaitForSeconds(_typewriterSpeed);
            }

            _typewriterRoutine = null;
        }

        protected virtual void PlayTypewriterSound()
        {
            if (_typewriterSFXNames == null || _typewriterSFXNames.Length == 0) return;
            string sfxName = _typewriterSFXNames[UnityEngine.Random.Range(0, _typewriterSFXNames.Length)];

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(sfxName);
            }
        }

        protected virtual void ExecuteSceneTransition()
        {
            StartCoroutine(FadeOutAndLoadSceneRoutine());
        }

        private IEnumerator FadeOutAndLoadSceneRoutine()
        {
            // 1. Ensure screen transitions to pitch black
            if (ThailandHorrorJam.Systems.UI.ScreenFader.Instance != null)
            {
                bool fadeDone = false;
                ThailandHorrorJam.Systems.UI.ScreenFader.Instance.FadeOut(0.6f, () => fadeDone = true);
                while (!fadeDone) yield return null;
            }

            // 2. Load next scene
            if (_sceneSwitcher != null)
            {
                _sceneSwitcher.LoadNextScene();
            }
            else if (!string.IsNullOrEmpty(_targetSceneName))
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(_targetSceneName);
            }
        }

        protected virtual void PlayCutsceneMusic()
        {
            if (AudioManager.Instance == null) return;

            if (_cutsceneMusicClip != null)
            {
                AudioManager.Instance.PlayMusic(_cutsceneMusicClip);
            }
            else if (_displayMode == CutsceneDisplayMode.FullScreenCinematicMovie || _endAction == CutsceneEndAction.ShowEndCard)
            {
                AudioManager.Instance.PlayEndingMusic();
            }
        }

        protected virtual void OnReenterLoopClicked()
        {
            if (_isReenteringLoop) return;
            _isReenteringLoop = true;

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayDefaultMusic();
            }

            if (_reenterLoopButton != null)
            {
                _reenterLoopButton.interactable = false;
            }

            if (UnityEngine.EventSystems.EventSystem.current != null)
            {
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
            }

            StartCoroutine(ReenterLoopRoutine());
        }

        protected virtual IEnumerator ReenterLoopRoutine()
        {
            yield return null;
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        public void SetSlides(CutsceneSlide[] slides)
        {
            _slides = slides;
        }

        public void SetSlideDuration(float duration)
        {
            _slideDuration = duration;
        }

        public void SetEndAction(CutsceneEndAction endAction)
        {
            _endAction = endAction;
        }

        private void LockPlayerAndNeedle(bool locked)
        {
            PlayerController2D player = FindAnyObjectByType<PlayerController2D>();
            if (player != null)
            {
                player.SetInputLocked(locked);
                if (locked)
                {
                    player.StopHorizontalMovement();
                }
            }

            NeedleController needle = FindAnyObjectByType<NeedleController>();
            if (needle != null && locked)
            {
                needle.CancelStitching();
                needle.ResetCutAimState();
            }
        }

        /// <summary>
        /// Deactivates all gameplay sectors and actors so the ending cutscene plays smoothly in isolation.
        /// </summary>
        public virtual void DeactivateGameplaySectors()
        {
            // 1. Deactivate all sector roots via LevelSeamlessManager
            if (LevelSeamlessManager.Instance != null)
            {
                LevelSeamlessManager.Instance.DeactivateAllSectors();
            }
            else
            {
                // Fallback: search any sector roots in the scene
                var rootGos = gameObject.scene.GetRootGameObjects();
                for (int i = 0; i < rootGos.Length; i++)
                {
                    if (rootGos[i] != null && (rootGos[i].name.StartsWith("Sector_") || rootGos[i].name.Contains("Sector")))
                    {
                        rootGos[i].SetActive(false);
                    }
                }
            }

            // 2. Hide / deactivate Player and clean up tether
            PlayerController2D player = FindAnyObjectByType<PlayerController2D>();
            if (player != null)
            {
                player.ReleaseHaulingTether();
                player.SetInputLocked(true);
                player.StopHorizontalMovement();
                player.gameObject.SetActive(false);
            }

            // 3. Release and deactivate NeedleController
            NeedleController needle = FindAnyObjectByType<NeedleController>();
            if (needle != null)
            {
                needle.ReleasePlayerTether();
                needle.CancelStitching();
                needle.ResetCutAimState();
                needle.gameObject.SetActive(false);
            }

            // 4. Deactivate any active falling fish hazards
            var fish = FindObjectsByType<FallingFishHazard2D>(FindObjectsInactive.Exclude);
            for (int i = 0; i < fish.Length; i++)
            {
                if (fish[i] != null) fish[i].gameObject.SetActive(false);
            }

            // 5. Hide any active world hints or prompts
            var hints = FindObjectsByType<WorldHintTrigger2D>(FindObjectsInactive.Exclude);
            for (int i = 0; i < hints.Length; i++)
            {
                if (hints[i] != null) hints[i].HideHint();
            }
        }
    }
}
