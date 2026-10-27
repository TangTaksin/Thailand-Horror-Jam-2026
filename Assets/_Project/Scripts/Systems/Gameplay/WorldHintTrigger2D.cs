using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Prerequisite condition required before a world hint can be shown to the player.
    /// </summary>
    public enum HintPrerequisite
    {
        [Tooltip("Always show when entering the trigger zone (default for movement/jump/slope tutorials).")]
        None = 0,

        [Tooltip("Only show if the player has NeedleController enabled.")]
        RequireNeedle = 1,

        [Tooltip("Only show if the player does NOT have NeedleController.")]
        RequireNoNeedle = 2
    }

    /// <summary>
    /// Dismissal behavior for a world hint.
    /// </summary>
    public enum HintDismissMode
    {
        [Tooltip("Fades in on enter, fades out on exit. Re-triggers every time player approaches.")]
        RepeatableOnProximity = 0,

        [Tooltip("Fades in on enter, fades out on exit, and is permanently dismissed after the player leaves.")]
        OneShotOnExit = 1,

        [Tooltip("Monitors an external IHintDismissCondition. Permanently fades out once condition is met (e.g. bridge severed).")]
        OnConditionMet = 2
    }

    /// <summary>
    /// Universal world-space tutorial hint trigger that smoothly fades in visual hints
    /// (SpriteRenderer icons + TextMeshPro text) when the player enters its trigger zone,
    /// and fades out when leaving, on action completion, or when an external puzzle condition is met.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class WorldHintTrigger2D : MonoBehaviour
    {
        [Header("Visual Root")]
        [Tooltip("Root GameObject of the hint containing SpriteRenderer(s) and TextMeshPro component(s). If null, defaults to this GameObject.")]
        [SerializeField] private GameObject _hintRoot;

        [Header("Behavior & Rules")]
        [Tooltip("Condition required for the hint to appear.")]
        [SerializeField] private HintPrerequisite _prerequisite = HintPrerequisite.None;

        [Tooltip("Dismissal behavior when player enters/exits or fulfills an objective.")]
        [SerializeField] private HintDismissMode _dismissMode = HintDismissMode.RepeatableOnProximity;

        [Tooltip("Optional MonoBehaviour implementing IHintDismissCondition (e.g. HangingObject2D). If resolved, hint is permanently dismissed.")]
        [SerializeField] private MonoBehaviour _dismissConditionTarget;

        [Header("Fade Settings")]
        [SerializeField] private float _fadeDuration = 0.35f;

        private const float k_AlphaEpsilon = 0.001f;
        private const float k_MinFadeDuration = 0.01f;

        private static readonly List<SpriteRenderer> s_TempSprites = new();
        private static readonly List<TextMeshPro> s_TempTexts = new();

        private readonly List<(SpriteRenderer sr, Color baseColor)> _sprites = new();
        private readonly List<(TextMeshPro tmp, Color baseColor)> _texts = new();

        private IHintDismissCondition _dismissCondition;
        private NeedleController _playerNeedle;
        private Coroutine _fadeCoroutine;
        private float _currentAlphaRatio = 0f;
        private bool _isPlayerInside = false;
        private bool _isPermanentlyDismissed = false;

        public GameObject HintRoot
        {
            get => _hintRoot;
            set
            {
                _hintRoot = value;
                CacheVisualComponents();
            }
        }

        public HintPrerequisite Prerequisite
        {
            get => _prerequisite;
            set => _prerequisite = value;
        }

        public HintDismissMode DismissMode
        {
            get => _dismissMode;
            set => _dismissMode = value;
        }

        public MonoBehaviour DismissConditionTarget
        {
            get => _dismissConditionTarget;
            set => SetDismissCondition(value);
        }

        public float FadeDuration
        {
            get => _fadeDuration;
            set => _fadeDuration = Mathf.Max(0.01f, value);
        }

        public bool IsPermanentlyDismissed => _isPermanentlyDismissed;

        private void Awake()
        {
            if (_hintRoot == null)
            {
                Transform visualTransform = transform.Find("Visual");
                if (visualTransform != null)
                {
                    _hintRoot = visualTransform.gameObject;
                }
            }

            _playerNeedle = FindAnyObjectByType<NeedleController>();
            ResolveDismissCondition();
            CacheVisualComponents();
            ApplyAlphaRatio(0f);
        }

        private void SetDismissCondition(MonoBehaviour target)
        {
            _dismissConditionTarget = target;
            ResolveDismissCondition();
        }

        private void ResolveDismissCondition()
        {
            if (_dismissConditionTarget != null)
            {
                _dismissCondition = _dismissConditionTarget as IHintDismissCondition;
            }
            else if (_dismissMode == HintDismissMode.OnConditionMet)
            {
                _dismissCondition = GetComponentInChildren<IHintDismissCondition>(true)
                                 ?? GetComponentInParent<IHintDismissCondition>();
            }
        }

        public void CacheVisualComponents()
        {
            _sprites.Clear();
            _texts.Clear();

            if (_hintRoot == null) return;

            s_TempSprites.Clear();
            _hintRoot.GetComponentsInChildren(true, s_TempSprites);
            for (int i = 0; i < s_TempSprites.Count; i++)
            {
                var sr = s_TempSprites[i];
                if (sr != null)
                {
                    _sprites.Add((sr, sr.color));
                }
            }
            s_TempSprites.Clear();

            s_TempTexts.Clear();
            _hintRoot.GetComponentsInChildren(true, s_TempTexts);
            for (int i = 0; i < s_TempTexts.Count; i++)
            {
                var tmp = s_TempTexts[i];
                if (tmp != null)
                {
                    _texts.Add((tmp, tmp.color));
                }
            }
            s_TempTexts.Clear();
        }

        private void Update()
        {
            if (_isPermanentlyDismissed) return;

            // Check if external condition has been met to dismiss hint permanently
            if (_dismissMode == HintDismissMode.OnConditionMet && _dismissCondition != null)
            {
                if (_dismissCondition.IsHintDismissed)
                {
                    DismissPermanently();
                    return;
                }
            }

            // If player state changed while inside trigger zone (e.g. picked up needle)
            if (_isPlayerInside && _currentAlphaRatio <= k_AlphaEpsilon)
            {
                if (MeetsPrerequisite())
                {
                    StartFade(1f);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isPermanentlyDismissed) return;

            if (PlayerController2D.IsPlayer(other))
            {
                CachePlayerNeedle(other);
                _isPlayerInside = true;

                if (MeetsPrerequisite())
                {
                    StartFade(1f);
                }
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (_isPermanentlyDismissed || !gameObject.activeInHierarchy || !enabled) return;

            if (PlayerController2D.IsPlayer(other))
            {
                _isPlayerInside = false;

                if (_dismissMode == HintDismissMode.OneShotOnExit)
                {
                    DismissPermanently();
                }
                else
                {
                    StartFade(0f);
                }
            }
        }

        /// <summary>
        /// Permanently dismisses this hint trigger so it will never show again.
        /// </summary>
        public void DismissPermanently()
        {
            _isPermanentlyDismissed = true;
            _isPlayerInside = false;
            StartFade(0f);
        }

        /// <summary>
        /// Manually triggers the hint to fade in.
        /// </summary>
        public void ShowHint()
        {
            if (_isPermanentlyDismissed) return;
            StartFade(1f);
        }

        /// <summary>
        /// Manually triggers the hint to fade out.
        /// </summary>
        public void HideHint()
        {
            if (_isPermanentlyDismissed) return;
            StartFade(0f);
        }

        private void CachePlayerNeedle(Collider2D col)
        {
            if (_playerNeedle == null && col != null)
            {
                _playerNeedle = col.GetComponentInParent<NeedleController>();
            }

            if (_playerNeedle == null)
            {
                _playerNeedle = FindAnyObjectByType<NeedleController>();
            }
        }

        private bool MeetsPrerequisite()
        {
            switch (_prerequisite)
            {
                case HintPrerequisite.None:
                    return true;

                case HintPrerequisite.RequireNeedle:
                    return HasNeedle();

                case HintPrerequisite.RequireNoNeedle:
                    return !HasNeedle();

                default:
                    return true;
            }
        }

        private bool HasNeedle()
        {
            if (_playerNeedle == null)
            {
                _playerNeedle = FindAnyObjectByType<NeedleController>();
            }

            return _playerNeedle != null && _playerNeedle.enabled;
        }

        private void OnDisable()
        {
            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
                _fadeCoroutine = null;
            }
            _isPlayerInside = false;
        }

        private void StartFade(float targetRatio)
        {
            if (!gameObject.activeInHierarchy || !enabled)
            {
                if (_fadeCoroutine != null)
                {
                    StopCoroutine(_fadeCoroutine);
                    _fadeCoroutine = null;
                }
                _currentAlphaRatio = targetRatio;
                ApplyAlphaRatio(targetRatio);
                return;
            }

            if (_fadeCoroutine != null)
            {
                StopCoroutine(_fadeCoroutine);
            }
            _fadeCoroutine = StartCoroutine(FadeRoutine(targetRatio));
        }

        private IEnumerator FadeRoutine(float targetRatio)
        {
            if (_hintRoot != null && _hintRoot != gameObject && !_hintRoot.activeSelf && targetRatio > 0f)
            {
                _hintRoot.SetActive(true);
            }

            float startRatio = _currentAlphaRatio;
            float elapsed = 0f;
            float duration = Mathf.Max(k_MinFadeDuration, _fadeDuration);

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                _currentAlphaRatio = Mathf.Lerp(startRatio, targetRatio, t);
                ApplyAlphaRatio(_currentAlphaRatio);
                yield return null;
            }

            _currentAlphaRatio = targetRatio;
            ApplyAlphaRatio(_currentAlphaRatio);

            if (_hintRoot != null && _hintRoot != gameObject && _currentAlphaRatio <= k_AlphaEpsilon)
            {
                _hintRoot.SetActive(false);
            }

            _fadeCoroutine = null;
        }

        private void ApplyAlphaRatio(float ratio)
        {
            for (int i = 0; i < _sprites.Count; i++)
            {
                var entry = _sprites[i];
                if (entry.sr != null)
                {
                    Color c = entry.baseColor;
                    c.a = entry.baseColor.a * ratio;
                    entry.sr.color = c;
                }
            }

            for (int i = 0; i < _texts.Count; i++)
            {
                var entry = _texts[i];
                if (entry.tmp != null)
                {
                    Color c = entry.baseColor;
                    c.a = entry.baseColor.a * ratio;
                    entry.tmp.color = c;
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                Gizmos.color = new Color(0.2f, 0.85f, 0.95f, 0.3f);
                Bounds b = col.bounds;
                Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = new Color(0.2f, 0.85f, 0.95f, 0.9f);
                Gizmos.DrawWireCube(b.center, b.size);
            }

            GameObject target = _hintRoot != null ? _hintRoot : gameObject;
            if (target != null && target != gameObject)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.8f);
                Gizmos.DrawLine(transform.position, target.transform.position);
            }
        }
#endif
    }
}
