using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Interaction
{
    /// <summary>
    /// Scans every ~0.1s for nearby <see cref="StitchableObject"/> targets using a non-allocating 2D overlap buffer
    /// and a collider cache, selects the best single target, and notifies the mouse prompt UI via event channel.
    /// Also handles AimingPrompt state when player is aiming needle.
    /// </summary>
    public sealed class PlayerInteractor : MonoBehaviour
    {
        private const int k_MaxOverlapHits = 16;
        private const float k_InnerStagePriorityBoost = 100f;

        [Header("Channel & Auto-Spawned UI")]
        [SerializeField] private InteractionEventChannelSO _interactionChannel;
        [SerializeField] private InteractionPromptUI _promptCanvasPrefab;

        [Header("Detection Settings")]
        [SerializeField] private LayerMask _stitchableLayer;
        [SerializeField] private float _awareRadius = 5.5f;
        [SerializeField] private float _interactRadius = 3.5f;
        [SerializeField] private float _scanInterval = 0.1f;

        [Header("Target Scoring")]
        [SerializeField] private float _facingWeight = 0.35f;
        [SerializeField] private float _focusStickinessBonus = 0.12f;

        [Header("Player References")]
        [SerializeField] private PlayerController2D _playerController;
        [SerializeField] private PlayerVisuals _playerVisuals;
        [SerializeField] private NeedleController _needleController;

        private readonly Collider2D[] _overlapBuffer = new Collider2D[k_MaxOverlapHits];
        private readonly Dictionary<Collider2D, StitchableObject> _stitchableCache = new Dictionary<Collider2D, StitchableObject>(32);
        private ContactFilter2D _contactFilter;

        private StitchableObject _currentTarget;
        private InteractableHighlight2D _currentHighlight;
        private PromptStage _currentStage = PromptStage.Hidden;
        private float _scanTimer;

        private void Awake()
        {
            if (_playerController == null) _playerController = GetComponentInParent<PlayerController2D>();
            if (_playerVisuals == null) _playerVisuals = GetComponentInChildren<PlayerVisuals>();
            if (_needleController == null) _needleController = GetComponentInParent<NeedleController>();

            if (_stitchableLayer.value == 0)
            {
                int stitchLayer = LayerMask.NameToLayer("Stitchable");
                _stitchableLayer = stitchLayer != -1 ? (1 << stitchLayer) : ~0;
            }

            _interactRadius = Mathf.Max(0.1f, _interactRadius);
            _awareRadius = Mathf.Max(_interactRadius + 0.1f, _awareRadius);
            _scanInterval = Mathf.Max(0.02f, _scanInterval);

            _contactFilter = new ContactFilter2D();
            _contactFilter.SetLayerMask(_stitchableLayer);
            _contactFilter.useLayerMask = true;
            _contactFilter.useTriggers = true;
        }

        private void Start()
        {
            EnsurePromptCanvasInstance();
        }

        private void EnsurePromptCanvasInstance()
        {
            if (_promptCanvasPrefab == null) return;

            if (InteractionPromptUI.Instance == null)
            {
                InteractionPromptUI spawned = Instantiate(_promptCanvasPrefab);
                spawned.name = "InteractionPromptCanvas";
            }
            else
            {
                if (!InteractionPromptUI.Instance.gameObject.activeInHierarchy)
                {
                    InteractionPromptUI.Instance.gameObject.SetActive(true);
                }
                if (!InteractionPromptUI.Instance.enabled)
                {
                    InteractionPromptUI.Instance.enabled = true;
                }
            }
        }

        private void OnDisable() => ClearFocus();

        private void Update()
        {
            if (_playerController != null && _playerController.IsInputLocked)
            {
                if (_currentStage != PromptStage.Hidden) ClearFocus();
                return;
            }

            // ถ้ากำลังเล็งเข็ม (IsAiming): แสดง AimingPrompt (คลิกซ้ายยิง / คลิกขวายกเลิก)
            if (_needleController != null && _needleController.IsAiming)
            {
                if (_currentStage != PromptStage.AimingPrompt)
                {
                    ApplyFocusState(_currentTarget, PromptStage.AimingPrompt);
                }
                return;
            }

            if (_currentStage == PromptStage.AimingPrompt)
            {
                _scanTimer = 0f;
            }

            if (_needleController != null && _needleController.IsTetheredToPlayer)
            {
                if (_currentStage != PromptStage.Hidden) ClearFocus();
                return;
            }

            _scanTimer -= Time.deltaTime;
            if (_scanTimer <= 0f)
            {
                _scanTimer = _scanInterval;
                PerformProximityScan();
            }
        }

        public static float EvaluateCandidateScore(
            Vector2 playerOrigin,
            Vector2 facingDirection,
            Vector2 targetPosition,
            float distanceToTarget,
            float interactRadius,
            float awareRadius,
            float facingWeight,
            float stickinessBonus,
            bool isCurrentTarget)
        {
            if (distanceToTarget > awareRadius) return float.NegativeInfinity;

            float normalizedCloseness = 1f - Mathf.Clamp01(distanceToTarget / Mathf.Max(0.01f, awareRadius));
            Vector2 toTarget = targetPosition - playerOrigin;
            float facingDot = (toTarget.sqrMagnitude > 0.0001f && facingDirection.sqrMagnitude > 0.0001f)
                ? Vector2.Dot(toTarget.normalized, facingDirection.normalized)
                : 0f;

            float stageBoost = distanceToTarget <= interactRadius ? k_InnerStagePriorityBoost : 0f;
            float hysteresis = isCurrentTarget ? stickinessBonus : 0f;

            return stageBoost + normalizedCloseness + (facingDot * facingWeight) + hysteresis;
        }

        public static PromptStage DeterminePromptStage(float distanceToTarget, float interactRadius, float awareRadius)
        {
            if (distanceToTarget <= interactRadius) return PromptStage.InteractIcon;
            if (distanceToTarget <= awareRadius) return PromptStage.AwareDot;
            return PromptStage.Hidden;
        }

        private void PerformProximityScan()
        {
            Vector2 playerPos = transform.position;
            Vector2 facingDir = _playerVisuals != null && !_playerVisuals.IsFacingRight ? Vector2.left : Vector2.right;

            int hitCount = Physics2D.OverlapCircle(playerPos, _awareRadius, _contactFilter, _overlapBuffer);

            StitchableObject bestTarget = null;
            float bestDistance = float.MaxValue;
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D col = _overlapBuffer[i];
                if (col == null) continue;

                StitchableObject candidate = ResolveStitchableCached(col);
                if (!IsCandidateValid(candidate)) continue;

                Vector2 closestPoint = col.ClosestPoint(playerPos);
                float dist = Vector2.Distance(playerPos, closestPoint);
                if (dist > _awareRadius) continue;

                bool isCurrent = ReferenceEquals(candidate, _currentTarget);
                float score = EvaluateCandidateScore(
                    playerPos,
                    facingDir,
                    candidate.CurrentPosition,
                    dist,
                    _interactRadius,
                    _awareRadius,
                    _facingWeight,
                    _focusStickinessBonus,
                    isCurrent);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = candidate;
                    bestDistance = dist;
                }
            }

            PromptStage nextStage = bestTarget != null
                ? DeterminePromptStage(bestDistance, _interactRadius, _awareRadius)
                : PromptStage.Hidden;

            if (nextStage == PromptStage.Hidden) bestTarget = null;

            ApplyFocusState(bestTarget, nextStage);
        }

        private StitchableObject ResolveStitchableCached(Collider2D col)
        {
            if (_stitchableCache.TryGetValue(col, out StitchableObject cached)) return cached;

            if (!col.TryGetComponent(out StitchableObject resolved))
            {
                resolved = col.GetComponentInParent<StitchableObject>();
            }

            _stitchableCache[col] = resolved;
            return resolved;
        }

        private bool IsCandidateValid(StitchableObject candidate)
        {
            if (candidate == null || !candidate.gameObject.activeInHierarchy) return false;
            if (candidate.CurrentState != StitchState.Idle) return false;
            return true;
        }

        private void ApplyFocusState(StitchableObject nextTarget, PromptStage nextStage)
        {
            if (ReferenceEquals(_currentTarget, nextTarget) && _currentStage == nextStage) return;

            if (_currentHighlight != null && (!ReferenceEquals(_currentTarget, nextTarget) || nextStage != PromptStage.InteractIcon))
            {
                _currentHighlight.SetHighlighted(false);
                _currentHighlight = null;
            }

            _currentTarget = nextTarget;
            _currentStage = nextStage;

            if (_currentTarget != null && _currentStage == PromptStage.InteractIcon)
            {
                if (!_currentTarget.TryGetComponent(out _currentHighlight))
                {
                    _currentHighlight = _currentTarget.gameObject.AddComponent<InteractableHighlight2D>();
                }
                _currentHighlight.SetHighlighted(true);
            }

            InteractionFocusData payload = _currentStage != PromptStage.Hidden
                ? new InteractionFocusData(_currentTarget, _currentStage)
                : InteractionFocusData.Hidden;

            _interactionChannel?.RaiseFocusChanged(payload);
        }

        private void ClearFocus() => ApplyFocusState(null, PromptStage.Hidden);
    }
}
