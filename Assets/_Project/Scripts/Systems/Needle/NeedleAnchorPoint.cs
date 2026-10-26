using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace ThailandHorrorJam.Systems.Needle
{
    public enum AnchorSnapMode
    {
        [Tooltip("Snaps the needle and suture thread directly to the anchor's configured snap point (ideal for hooks, rings, and pegs).")]
        SnapToPoint = 0,

        [Tooltip("Pins the needle at the exact surface impact point on this object's collider (ideal for wooden beams or anchor strips).")]
        SurfaceHitPoint = 1
    }

    /// <summary>
    /// Marks a GameObject as a valid target for pinning the needle projectile (Waypoint 2).
    /// Supports exact point snapping or surface-hit pinning, visual range/aiming feedback,
    /// puzzle callbacks, and Scene View gizmos for level design.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeedleAnchorPoint : MonoBehaviour
    {
        private static readonly List<NeedleAnchorPoint> s_ActiveAnchors = new List<NeedleAnchorPoint>(16);
        public static IReadOnlyList<NeedleAnchorPoint> ActiveAnchors => s_ActiveAnchors;

        [Header("Anchor Configuration")]
        [Tooltip("Whether this anchor point can currently receive a needle.")]
        [SerializeField] private bool _isAnchorEnabled = true;

        [Tooltip("Determines whether the needle snaps to a fixed center point or pins at the exact surface hit location.")]
        [SerializeField] private AnchorSnapMode _snapMode = AnchorSnapMode.SnapToPoint;

        [Tooltip("Optional child transform used as the exact snap location. If null, uses transform.position + _snapOffset.")]
        [SerializeField] private Transform _customSnapTransform;

        [Tooltip("Local offset from this object's position when SnapToPoint mode is used without a custom transform.")]
        [SerializeField] private Vector2 _snapOffset = Vector2.zero;

        [Tooltip("Small clearance offset along the surface normal when using SurfaceHitPoint mode.")]
        [SerializeField] private float _surfaceOffset = 0.02f;

        [Header("Visual Feedback")]
        [Tooltip("Optional SpriteRenderer to tint and scale for aiming feedback. Auto-detected if left empty.")]
        [SerializeField] private SpriteRenderer _visualRenderer;

        [Tooltip("Whether to tint and scale the visual renderer based on aiming state and thread range.")]
        [SerializeField] private bool _enableVisualFeedback = true;

        [SerializeField] private Color _idleColor = Color.white;
        [SerializeField] private Color _inRangeColor = new Color(0.75f, 0.95f, 1f, 1f);
        [SerializeField] private Color _outOfRangeColor = new Color(1f, 0.4f, 0.4f, 0.65f);
        [SerializeField] private Color _hoveredColor = new Color(0.45f, 1f, 0.65f, 1f);
        [SerializeField] private Color _pinnedColor = new Color(1f, 0.9f, 0.45f, 1f);

        [Tooltip("Scale multiplier applied when the player's aim trajectory is targeting this anchor.")]
        [SerializeField] private float _hoverScaleMultiplier = 1.18f;

        [Tooltip("Speed of procedural scale and color transitions.")]
        [SerializeField] private float _feedbackTransitionSpeed = 12f;

        [Header("Events")]
        [Tooltip("Invoked when a needle embeds into this anchor and completes a suture rail.")]
        [SerializeField] private UnityEvent _onNeedlePinned;

        [Tooltip("Invoked when the suture thread connected to this anchor is severed or released.")]
        [SerializeField] private UnityEvent _onNeedleUnpinned;

        private Transform _visualTransform;
        private Collider2D _col;
        private Vector3 _baseScale = Vector3.one;
        private Vector3 _targetScale = Vector3.one;
        private Color _targetColor = Color.white;
        private bool _isPinned;

        public bool IsAnchorEnabled
        {
            get => _isAnchorEnabled;
            set => _isAnchorEnabled = value;
        }

        public bool IsPinned => _isPinned;

        public Vector2 SnapWorldPosition => _customSnapTransform != null
            ? (Vector2)_customSnapTransform.position
            : (Vector2)transform.position + _snapOffset;

        private void Awake()
        {
            _col = GetComponent<Collider2D>();

            if (_visualRenderer == null)
            {
                _visualRenderer = GetComponent<SpriteRenderer>();
                if (_visualRenderer == null)
                {
                    _visualRenderer = GetComponentInChildren<SpriteRenderer>();
                }
            }

            if (_visualRenderer != null)
            {
                _visualTransform = _visualRenderer.transform;
                _baseScale = _visualTransform.localScale;
                _targetScale = _baseScale;
                _idleColor = _visualRenderer.color;
                _targetColor = _idleColor;
            }
            else
            {
                _visualTransform = transform;
                _baseScale = transform.localScale;
                _targetScale = _baseScale;
            }
        }

        private void OnEnable()
        {
            if (!s_ActiveAnchors.Contains(this))
            {
                s_ActiveAnchors.Add(this);
            }
        }

        private void OnDisable()
        {
            s_ActiveAnchors.Remove(this);
            ResetVisualImmediate();
        }

        private void Update()
        {
            if (!_enableVisualFeedback) return;

            float dt = Time.deltaTime * _feedbackTransitionSpeed;

            if (_visualTransform != null && (_visualTransform.localScale - _targetScale).sqrMagnitude > 0.00001f)
            {
                _visualTransform.localScale = Vector3.MoveTowards(_visualTransform.localScale, _targetScale, dt);
            }

            if (_visualRenderer != null && _visualRenderer.color != _targetColor)
            {
                _visualRenderer.color = Color.Lerp(_visualRenderer.color, _targetColor, Mathf.Clamp01(dt));
            }
        }

        /// <summary>
        /// Calculates the exact world-space waypoint position where the needle and thread should pin.
        /// </summary>
        public Vector2 GetAnchorPosition(Vector2 hitPoint, Vector2 hitNormal)
        {
            if (_snapMode == AnchorSnapMode.SnapToPoint)
            {
                return SnapWorldPosition;
            }

            Vector2 normal = hitNormal.sqrMagnitude > 0.001f ? hitNormal.normalized : Vector2.up;
            return hitPoint + normal * _surfaceOffset;
        }

        /// <summary>
        /// Called by NeedleController when a needle embeds into this anchor.
        /// </summary>
        public void NotifyPinned()
        {
            _isPinned = true;
            _targetColor = _pinnedColor;
            _targetScale = _baseScale;
            _onNeedlePinned?.Invoke();
        }

        /// <summary>
        /// Called by NeedleController when the thread is severed or unpinned from this anchor.
        /// </summary>
        public void NotifyUnpinned()
        {
            if (!_isPinned) return;

            _isPinned = false;
            _targetColor = _idleColor;
            _targetScale = _baseScale;
            _onNeedleUnpinned?.Invoke();
        }

        /// <summary>
        /// Updates visual range and hover state while the player is aiming the needle.
        /// </summary>
        public void UpdateAimFeedback(
            bool isPlayerAiming,
            Vector2 launchOrigin,
            float minDistance,
            float maxDistance,
            bool isTrajectoryTargetingThis)
        {
            if (!_enableVisualFeedback) return;

            if (_isPinned)
            {
                _targetColor = _pinnedColor;
                _targetScale = _baseScale;
                return;
            }

            if (!isPlayerAiming || !_isAnchorEnabled)
            {
                _targetColor = _idleColor;
                _targetScale = _baseScale;
                return;
            }

            Vector2 checkPoint = _snapMode == AnchorSnapMode.SnapToPoint && _col == null
                ? SnapWorldPosition
                : (_col != null ? _col.ClosestPoint(launchOrigin) : SnapWorldPosition);

            float dist = Vector2.Distance(launchOrigin, checkPoint);
            bool inRange = dist >= minDistance && dist <= maxDistance;

            if (isTrajectoryTargetingThis && inRange)
            {
                _targetColor = _hoveredColor;
                _targetScale = _baseScale * _hoverScaleMultiplier;
            }
            else if (inRange)
            {
                _targetColor = _inRangeColor;
                _targetScale = _baseScale;
            }
            else
            {
                _targetColor = _outOfRangeColor;
                _targetScale = _baseScale;
            }
        }

        private void ResetVisualImmediate()
        {
            if (_visualTransform != null)
            {
                _visualTransform.localScale = _baseScale;
            }
            if (_visualRenderer != null)
            {
                _visualRenderer.color = _idleColor;
            }
        }

        private void OnDrawGizmos()
        {
            Color gizmoColor = !_isAnchorEnabled
                ? new Color(0.5f, 0.5f, 0.5f, 0.6f)
                : (_isPinned ? new Color(1f, 0.85f, 0.2f, 0.95f) : new Color(0.3f, 0.95f, 1f, 0.9f));

            Gizmos.color = gizmoColor;
            Vector3 snapPos = SnapWorldPosition;

            if (_snapMode == AnchorSnapMode.SnapToPoint)
            {
                Gizmos.DrawWireSphere(snapPos, 0.16f);
                const float crossSize = 0.1f;
                Gizmos.DrawLine(snapPos + Vector3.left * crossSize, snapPos + Vector3.right * crossSize);
                Gizmos.DrawLine(snapPos + Vector3.down * crossSize, snapPos + Vector3.up * crossSize);
            }
            else
            {
                Collider2D col = _col != null ? _col : GetComponent<Collider2D>();
                if (col != null)
                {
                    Bounds b = col.bounds;
                    Gizmos.DrawWireCube(b.center, b.size);
                }
                else
                {
                    Gizmos.DrawWireCube(transform.position, new Vector3(0.4f, 0.4f, 0f));
                }
            }
        }
    }
}
