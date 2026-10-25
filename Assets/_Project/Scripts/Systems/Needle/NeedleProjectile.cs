using System;
using UnityEngine;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Fast linear projectile for the needle tool.
    /// Launched from the player towards the cursor for Waypoint 2.
    /// Trailing suture thread is connected from the stitchable object to this projectile during flight.
    /// If it hits a valid anchor surface, it completes the rail; if it misses or exceeds thread budget,
    /// it automatically rewinds rapidly back to the player.
    /// </summary>
    public sealed class NeedleProjectile : MonoBehaviour
    {
        private enum ProjectileState
        {
            Idle,
            Flying,
            Rewinding,
            Embedded
        }

        [Header("Projectile Settings")]
        [Tooltip("Linear flight speed in meters per second.")]
        [SerializeField] private float _defaultSpeed = 16.0f;

        [Tooltip("Speed when rapidly rewinding back to player on miss.")]
        [SerializeField] private float _rewindSpeed = 34.0f;

        [Tooltip("Radius of the collision sweep.")]
        [SerializeField] private float _sweepRadius = 0.08f;

        [Header("Visual Orientation")]
        [Tooltip("Optional custom needle sprite. If unassigned, uses SpriteRenderer's assigned sprite.")]
        [SerializeField] private Sprite _customNeedleSprite;

        [Tooltip("Angular offset in degrees to align the needle tip with flight direction. Set to 0 if the sprite points right (--> like Needle_Sheet_0), or -90 if the sprite points up (^).")]
        [SerializeField] private float _spriteRotationOffset = 0f;

        private ProjectileState _state = ProjectileState.Idle;
        private Vector2 _velocity;
        private float _maxDistance;
        private float _traveledDistance;
        private Transform _returnTransform;
        private Transform _playerTransform;
        private ContactFilter2D _collisionFilter;
        private readonly RaycastHit2D[] _hitBuffer = new RaycastHit2D[8];

        private Action<Vector2, Vector2, Collider2D> _onHitCallback;
        private Action _onRewindCompleteCallback;

        private SpriteRenderer _spriteRenderer;
        private Transform _targetToIgnore;

        public bool IsInFlight => _state == ProjectileState.Flying || _state == ProjectileState.Rewinding;
        public bool IsFlying => _state == ProjectileState.Flying;
        public bool IsRewinding => _state == ProjectileState.Rewinding;
        public bool IsEmbedded => _state == ProjectileState.Embedded;
        public float SweepRadius => _sweepRadius;
        public Vector2 CurrentPosition => transform.position;

        private void Awake()
        {
            EnsureSpriteRenderer();
        }

        private void EnsureSpriteRenderer()
        {
            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponent<SpriteRenderer>();
                if (_spriteRenderer == null)
                {
                    _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }

            if (_spriteRenderer.sprite == null && _customNeedleSprite != null)
            {
                _spriteRenderer.sprite = _customNeedleSprite;
            }

            if (_spriteRenderer.sortingOrder < 10)
            {
                _spriteRenderer.sortingOrder = 10;
            }
        }

        /// <summary>
        /// Launches the needle projectile from origin towards target direction.
        /// </summary>
        public void Launch(
            Vector2 origin,
            Vector2 direction,
            float speed,
            float maxDistance,
            Transform returnTransform,
            Transform targetToIgnore,
            ContactFilter2D collisionFilter,
            Action<Vector2, Vector2, Collider2D> onHit,
            Action onRewindComplete,
            Transform playerTransform = null)
        {
            EnsureSpriteRenderer();

            if (playerTransform != null)
            {
                transform.SetParent(null);
            }

            transform.position = origin;
            _velocity = direction.normalized * (speed > 0f ? speed : _defaultSpeed);
            _maxDistance = maxDistance;
            _traveledDistance = 0f;
            _returnTransform = returnTransform;
            _playerTransform = playerTransform;
            _targetToIgnore = targetToIgnore;
            _collisionFilter = collisionFilter;
            _onHitCallback = onHit;
            _onRewindCompleteCallback = onRewindComplete;

            _state = ProjectileState.Flying;
            gameObject.SetActive(true);

            UpdateRotation(direction);
        }

        private void FixedUpdate()
        {
            if (_state == ProjectileState.Flying)
            {
                StepFlight(Time.fixedDeltaTime);
            }
            else if (_state == ProjectileState.Rewinding)
            {
                StepRewind(Time.fixedDeltaTime);
            }
        }

        private void StepFlight(float dt)
        {
            float stepDistance = _velocity.magnitude * dt;
            Vector2 currentPos = transform.position;
            Vector2 stepDir = _velocity.normalized;

            // Swept circle cast to ensure thin colliders are never tunneled through
            int hitCount = Physics2D.CircleCast(currentPos, _sweepRadius, stepDir, _collisionFilter, _hitBuffer, stepDistance);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _hitBuffer[i];
                Collider2D hitCol = hit.collider;
                if (hitCol == null) continue;

                // Ignore return target (e.g. stitchable box), player, and explicit ignore targets
                if (_returnTransform != null && (hitCol.transform == _returnTransform || hitCol.transform.IsChildOf(_returnTransform)))
                {
                    continue;
                }
                if (_playerTransform != null && (hitCol.transform == _playerTransform || hitCol.transform.IsChildOf(_playerTransform)))
                {
                    continue;
                }
                if (_targetToIgnore != null && (hitCol.transform == _targetToIgnore || hitCol.transform.IsChildOf(_targetToIgnore)))
                {
                    continue;
                }

                // Trigger colliders are ignored unless they are an enabled NeedleAnchorPoint
                if (hitCol.isTrigger)
                {
                    NeedleAnchorPoint triggerAnchor = hitCol.GetComponentInParent<NeedleAnchorPoint>();
                    if (triggerAnchor == null || !triggerAnchor.IsAnchorEnabled)
                    {
                        continue;
                    }
                }

                // Hit a solid surface or valid anchor trigger!
                transform.position = hit.point;
                _state = ProjectileState.Embedded;
                _onHitCallback?.Invoke(hit.point, hit.normal, hitCol);
                return;
            }

            // No obstacle hit in this step — move forward
            Vector2 newPos = currentPos + _velocity * dt;
            transform.position = newPos;
            _traveledDistance += stepDistance;

            if (_traveledDistance >= _maxDistance)
            {
                // Reached max allowed thread distance without hitting — auto-rewind!
                StartRewind();
            }
        }

        private void StepRewind(float dt)
        {
            if (_returnTransform == null)
            {
                Cancel();
                return;
            }

            Vector2 currentPos = transform.position;
            Vector2 returnPos = _returnTransform.position;
            Vector2 toReturn = returnPos - currentPos;
            float dist = toReturn.magnitude;
            float step = _rewindSpeed * dt;

            if (dist <= step || dist < 0.15f)
            {
                // Reached origin target — rewind finished
                Cancel();
                _onRewindCompleteCallback?.Invoke();
                return;
            }

            Vector2 dir = toReturn / dist;
            transform.position = currentPos + dir * step;
            UpdateRotation(dir);
        }

        public void StartRewind()
        {
            if (_state == ProjectileState.Rewinding || _state == ProjectileState.Idle) return;
            _state = ProjectileState.Rewinding;
        }

        public void Embed(Vector2 position, Vector2 direction)
        {
            EnsureSpriteRenderer();
            _state = ProjectileState.Embedded;
            transform.position = position;
            UpdateRotation(direction);
            gameObject.SetActive(true);
        }

        public void Cancel()
        {
            _state = ProjectileState.Idle;
            if (_playerTransform != null)
            {
                transform.SetParent(_playerTransform);
                transform.localPosition = Vector3.zero;
            }
            gameObject.SetActive(false);
        }

        private void UpdateRotation(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Euler(0f, 0f, angle + _spriteRotationOffset);
            }
        }
    }
}
