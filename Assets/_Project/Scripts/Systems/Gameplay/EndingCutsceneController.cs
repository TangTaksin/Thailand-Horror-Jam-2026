using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Automatic Movie Cutscene controller for the game's finale.
    /// Plays through the 18 ending illustration slides (End_SC1 to End_SC18) sequentially
    /// with zero input required, culminating in the final Jam credits and loop resolution.
    /// </summary>
    public sealed class EndingCutsceneController : MonoBehaviour
    {
        [Header("Slides")]
        [Tooltip("18 ending illustrations loaded from Assets/_Project/Art/Sprites/Ending (End_SC1 to End_SC18).")]
        [SerializeField] private Sprite[] _slides;

        [Header("Timing")]
        [Tooltip("Display duration per slide in seconds.")]
        [SerializeField] private float _slideDuration = 2.5f;

        [Header("UI References")]
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private Image _slideImage;
        [SerializeField] private GameObject _endCardPanel;
        [SerializeField] private TextMeshProUGUI _endCardTitleText;
        [SerializeField] private TextMeshProUGUI _endCardCreditsText;
        [SerializeField] private Button _reenterLoopButton;

        private Coroutine _cutsceneRoutine;
        private int _currentSlideIndex = -1;
        private bool _isPlaying;
        private bool _isCompleted;
        private bool _isReenteringLoop;

        public bool IsPlaying => _isPlaying;
        public bool IsCompleted => _isCompleted;
        public int CurrentSlideIndex => _currentSlideIndex;
        public int TotalSlides => _slides != null ? _slides.Length : 0;

        public event Action OnCutsceneStarted;
        public event Action<int> OnSlideChanged;
        public event Action OnCutsceneCompleted;

        private void Awake()
        {
            if (_canvasGroup == null)
            {
                _canvasGroup = GetComponent<CanvasGroup>();
            }

            if (_reenterLoopButton != null)
            {
                _reenterLoopButton.onClick.AddListener(OnReenterLoopClicked);
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
        }

        private void OnDestroy()
        {
            if (_cutsceneRoutine != null)
            {
                StopCoroutine(_cutsceneRoutine);
                _cutsceneRoutine = null;
            }

            if (_reenterLoopButton != null)
            {
                _reenterLoopButton.onClick.RemoveListener(OnReenterLoopClicked);
            }
        }

        /// <summary>
        /// Starts the automatic continuous playback of the ending slides.
        /// </summary>
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

            OnCutsceneStarted?.Invoke();

            if (_cutsceneRoutine != null)
            {
                StopCoroutine(_cutsceneRoutine);
            }
            _cutsceneRoutine = StartCoroutine(PlayCutsceneRoutine());
        }

        private IEnumerator PlayCutsceneRoutine()
        {
            if (_slides != null && _slides.Length > 0)
            {
                for (int i = 0; i < _slides.Length; i++)
                {
                    _currentSlideIndex = i;
                    if (_slideImage != null)
                    {
                        _slideImage.sprite = _slides[i];
                        _slideImage.color = Color.white;
                    }

                    OnSlideChanged?.Invoke(i);

                    // Wait for slide duration
                    yield return new WaitForSeconds(_slideDuration);
                }
            }

            // Completed all slides: hold on the final slide and reveal The End card
            _isCompleted = true;
            _isPlaying = false;
            _cutsceneRoutine = null;

            if (_endCardPanel != null)
            {
                _endCardPanel.SetActive(true);
            }

            OnCutsceneCompleted?.Invoke();
        }

        private void OnReenterLoopClicked()
        {
            if (_isReenteringLoop) return;
            _isReenteringLoop = true;

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

        private IEnumerator ReenterLoopRoutine()
        {
            // Allow UI click event propagation to conclude before destroying scene objects
            yield return null;

            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            UnityEngine.SceneManagement.SceneManager.LoadScene(sceneName);
        }

        public void SetSlides(Sprite[] slides)
        {
            _slides = slides;
        }

        public void SetSlideDuration(float duration)
        {
            _slideDuration = duration;
        }
    }
}
