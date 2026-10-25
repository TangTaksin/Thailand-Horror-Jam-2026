using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Coordinates Limbo-style death and respawn loop:
    /// - Locks player input
    /// - Triggers slow blackout fade (0.6s)
    /// - Repositions player and resets section puzzle objects
    /// - Smoothly fades back in (0.6s) and unlocks input
    /// </summary>
    public sealed class CheckpointManager : MonoBehaviour
    {
        public static CheckpointManager Instance { get; private set; }

        [Header("Limbo Transition Timings")]
        [Tooltip("Duration of the slow fade to black when dying (~0.6s).")]
        [SerializeField] private float _fadeOutDuration = 0.6f;
        [Tooltip("Duration to hold in total darkness while resetting world states (~0.3s).")]
        [SerializeField] private float _blackoutPauseDuration = 0.3f;
        [Tooltip("Duration of the slow wake-up fade in (~0.6s).")]
        [SerializeField] private float _fadeInDuration = 0.6f;

        [Header("Fade Overlay Settings")]
        [Tooltip("Optional CanvasGroup overlay. If unassigned, one will be created dynamically at runtime.")]
        [SerializeField] private CanvasGroup _fadeCanvasGroup;
        [SerializeField] private Color _fadeColor = Color.black;

        [Header("Default Spawn (Fallback)")]
        [Tooltip("Optional initial spawn point if no Checkpoint has been triggered yet.")]
        [SerializeField] private Transform _defaultSpawnPoint;
        [SerializeField] private bool _defaultFacingRight = true;

        private Checkpoint _currentCheckpoint;
        private PlayerController2D _playerController;
        private NeedleController _needleController;
        private Vector2 _initialPlayerPosition;
        private bool _isRespawning;
        private Coroutine _activeRespawnRoutine;
        private readonly List<StitchableObject> _allLevelStitchables = new List<StitchableObject>();

        public bool IsRespawning => _isRespawning;
        public Checkpoint CurrentCheckpoint => _currentCheckpoint;

        public event System.Action OnPlayerDied;
        public event System.Action OnPlayerRespawned;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsurePlayerReferences();
            EnsureAllStitchables();
            EnsureFadeOverlay();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void EnsurePlayerReferences()
        {
            if (_playerController == null)
            {
                _playerController = FindAnyObjectByType<PlayerController2D>();
            }

            if (_needleController == null)
            {
                _needleController = FindAnyObjectByType<NeedleController>();
            }

            if (_playerController != null)
            {
                _initialPlayerPosition = _playerController.transform.position;
            }
            else if (_defaultSpawnPoint != null)
            {
                _initialPlayerPosition = _defaultSpawnPoint.position;
            }
            else
            {
                _initialPlayerPosition = transform.position;
            }
        }

        private void EnsureAllStitchables()
        {
            _allLevelStitchables.Clear();
            StitchableObject[] found = FindObjectsByType<StitchableObject>(FindObjectsInactive.Exclude);
            if (found != null && found.Length > 0)
            {
                _allLevelStitchables.AddRange(found);
            }
        }

        private void EnsureFadeOverlay()
        {
            if (_fadeCanvasGroup != null)
            {
                _fadeCanvasGroup.alpha = 0f;
                _fadeCanvasGroup.blocksRaycasts = false;
                return;
            }

            // Create zero-setup UI overlay dynamically so any scene works instantly
            GameObject canvasGo = new GameObject("[LimboFadeCanvas]");
            canvasGo.transform.SetParent(transform);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            _fadeCanvasGroup = canvasGo.AddComponent<CanvasGroup>();
            _fadeCanvasGroup.alpha = 0f;
            _fadeCanvasGroup.blocksRaycasts = false;

            GameObject imageGo = new GameObject("BlackOverlay");
            imageGo.transform.SetParent(canvasGo.transform, false);

            Image img = imageGo.AddComponent<Image>();
            img.color = _fadeColor;
            img.raycastTarget = false;

            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
        }

        /// <summary>
        /// Registers a new active checkpoint passed by the player.
        /// </summary>
        public void SetActiveCheckpoint(Checkpoint checkpoint)
        {
            if (checkpoint == null) return;
            _currentCheckpoint = checkpoint;
        }

        /// <summary>
        /// Initiates the Limbo death sequence when the player hits a KillZone or hazard.
        /// </summary>
        public void TriggerPlayerDeath(KillZone killZone)
        {
            if (_isRespawning) return;
            _isRespawning = true;

            float shakeIntensity = killZone != null ? killZone.CameraShakeIntensity : 0.15f;
            float shakeDuration = killZone != null ? killZone.CameraShakeDuration : 0.3f;

            if (_activeRespawnRoutine != null)
            {
                StopCoroutine(_activeRespawnRoutine);
            }
            _activeRespawnRoutine = StartCoroutine(LimboRespawnSequence(shakeIntensity, shakeDuration));
        }

        /// <summary>
        /// Manual trigger to kill / respawn player (e.g. from code or script tests).
        /// </summary>
        public void TriggerPlayerDeath(float shakeIntensity = 0.15f, float shakeDuration = 0.3f)
        {
            if (_isRespawning) return;
            _isRespawning = true;

            if (_activeRespawnRoutine != null)
            {
                StopCoroutine(_activeRespawnRoutine);
            }
            _activeRespawnRoutine = StartCoroutine(LimboRespawnSequence(shakeIntensity, shakeDuration));
        }

        private IEnumerator LimboRespawnSequence(float shakeIntensity, float shakeDuration)
        {
            _isRespawning = true;
            OnPlayerDied?.Invoke();

            EnsurePlayerReferences();

            // 1. Visceral Impact: Camera shake, lock input, cancel needle
            CameraController2D.Instance?.Shake(shakeIntensity, shakeDuration);

            if (_playerController != null)
            {
                _playerController.SetInputLocked(true);
            }

            if (_needleController != null)
            {
                _needleController.ResetAllNeedleStates();
            }

            // 2. Slow fade to black (atmospheric fainting feel)
            yield return FadeRoutine(0f, 1f, _fadeOutDuration);

            // 3. Pause in darkness to reposition & reset puzzle state
            if (_blackoutPauseDuration > 0f)
            {
                yield return new WaitForSecondsRealtime(_blackoutPauseDuration);
            }

            // Reposition player
            Vector2 targetSpawn;
            bool targetFacing;

            if (_currentCheckpoint != null)
            {
                targetSpawn = _currentCheckpoint.SpawnPosition;
                targetFacing = _currentCheckpoint.RespawnFacingRight;
            }
            else if (_defaultSpawnPoint != null)
            {
                targetSpawn = _defaultSpawnPoint.position;
                targetFacing = _defaultFacingRight;
            }
            else
            {
                targetSpawn = _initialPlayerPosition;
                targetFacing = _defaultFacingRight;
            }

            if (_playerController != null)
            {
                _playerController.TeleportTo(targetSpawn, targetFacing);
            }

            // Snap camera immediately to prevent awkward camera pan when screen fades back in
            CameraController2D.Instance?.SnapToTarget();

            // Reset puzzle elements
            ResetAssociatedOrAllStitchables();

            // 4. Slow wake-up fade in
            yield return FadeRoutine(1f, 0f, _fadeInDuration);

            // 5. Unlock player control
            if (_playerController != null)
            {
                _playerController.SetInputLocked(false);
            }

            _isRespawning = false;
            _activeRespawnRoutine = null;
            OnPlayerRespawned?.Invoke();
        }

        private void ResetAssociatedOrAllStitchables()
        {
            if (_currentCheckpoint != null && _currentCheckpoint.AssociatedStitchables != null && _currentCheckpoint.AssociatedStitchables.Count > 0)
            {
                IReadOnlyList<StitchableObject> list = _currentCheckpoint.AssociatedStitchables;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null)
                    {
                        list[i].ResetToInitialState();
                    }
                }
            }
            else
            {
                for (int i = 0; i < _allLevelStitchables.Count; i++)
                {
                    if (_allLevelStitchables[i] != null)
                    {
                        _allLevelStitchables[i].ResetToInitialState();
                    }
                }
            }
        }

        private IEnumerator FadeRoutine(float fromAlpha, float toAlpha, float duration)
        {
            if (_fadeCanvasGroup == null) yield break;

            if (duration <= 0.001f)
            {
                _fadeCanvasGroup.alpha = toAlpha;
                _fadeCanvasGroup.blocksRaycasts = toAlpha > 0.01f;
                yield break;
            }

            float elapsed = 0f;
            _fadeCanvasGroup.blocksRaycasts = true;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _fadeCanvasGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, t);
                yield return null;
            }

            _fadeCanvasGroup.alpha = toAlpha;
            _fadeCanvasGroup.blocksRaycasts = toAlpha > 0.01f;
        }
    }
}
