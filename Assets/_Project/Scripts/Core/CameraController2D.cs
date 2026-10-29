using UnityEngine;
using DG.Tweening;

namespace ThailandHorrorJam.Core
{
    /// <summary>
    /// Cinematic 2D smooth follow camera with lookahead, boundary damping,
    /// and micro screen-shake/punch for tactile game feel (Limbo/Inside/Webbed style).
    /// </summary>
    public sealed class CameraController2D : MonoBehaviour
    {
        public static CameraController2D Instance { get; private set; }

        [Header("Target & Tracking")]
        [SerializeField] private Transform _target;
        [SerializeField] private Vector3 _offset = new Vector3(0f, 1.2f, -10f);
        [SerializeField] private float _smoothTime = 0.22f;
        [SerializeField] private float _lookAheadDistance = 1.2f;
        [SerializeField] private float _lookAheadSpeed = 3.5f;

        [Header("Boundary / Confinement")]
        [Tooltip("Collider2D defining the outer limit for the camera view (e.g. CameraBoundary with Polygon/BoxCollider2D).")]
        [SerializeField] private Collider2D _boundaryCollider;
        [SerializeField] private bool _enableBoundary = true;

        [Header("Shake Settings")]
        [SerializeField] private float _maxShakeOffset = 0.35f;

        private Rigidbody2D _targetRb;
        private Vector3 _currentVelocity;
        private float _currentLookAheadX;
        private float _targetLookAheadX;

        private float _shakeTimer;
        private float _shakeDuration;
        private float _shakeIntensity;
        private Vector3 _punchOffset;
        private const float k_PunchDamping = 12f;

        private Camera _cam;
        private float _defaultOrthoSize = 5.5f;
        private Vector3 _defaultOffset;
        private Tween _zoomTween;
        private Tween _offsetTween;

        public float DefaultOrthoSize => _defaultOrthoSize;
        public Vector3 DefaultOffset => _defaultOffset;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            _defaultOffset = _offset;

            if (_target == null)
            {
                var player = GameObject.FindWithTag("Player");
                if (player != null)
                {
                    _target = player.transform;
                }
            }

            if (_target != null)
            {
                _targetRb = _target.GetComponent<Rigidbody2D>();
            }

            _cam = GetComponent<Camera>();
            if (_cam != null)
            {
                _defaultOrthoSize = _cam.orthographicSize;
            }

            if (_boundaryCollider == null)
            {
                var boundaryObj = GameObject.Find("CameraBoundary");
                if (boundaryObj != null)
                {
                    _boundaryCollider = boundaryObj.GetComponent<Collider2D>();
                }
            }
        }

        private void OnDestroy()
        {
            _zoomTween?.Kill();
            _offsetTween?.Kill();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            // 1. Calculate lookahead based on horizontal movement
            float targetDir = 0f;
            if (_targetRb != null && Mathf.Abs(_targetRb.linearVelocity.x) > 0.5f)
            {
                targetDir = Mathf.Sign(_targetRb.linearVelocity.x);
            }

            _targetLookAheadX = targetDir * _lookAheadDistance;
            _currentLookAheadX = Mathf.MoveTowards(_currentLookAheadX, _targetLookAheadX, _lookAheadSpeed * Time.deltaTime);

            // 2. Base follow target position
            Vector3 targetPos = _target.position + _offset;
            targetPos.x += _currentLookAheadX;
            targetPos.z = _offset.z;

            if (_enableBoundary && _boundaryCollider != null)
            {
                targetPos = ClampToBounds(targetPos);
            }

            Vector3 smoothPos = Vector3.SmoothDamp(transform.position, targetPos, ref _currentVelocity, _smoothTime);

            if (_enableBoundary && _boundaryCollider != null)
            {
                smoothPos = ClampToBounds(smoothPos);
            }

            // 3. Apply shake offset
            Vector3 shakeOffset = Vector3.zero;
            if (_shakeTimer > 0f)
            {
                _shakeTimer -= Time.deltaTime;
                float trauma = _shakeDuration > 0.0001f ? Mathf.Clamp01(_shakeTimer / _shakeDuration) : 0f;
                float x = (Mathf.PerlinNoise(Time.time * 35f, 0f) - 0.5f) * 2f * _shakeIntensity * trauma;
                float y = (Mathf.PerlinNoise(0f, Time.time * 35f) - 0.5f) * 2f * _shakeIntensity * trauma;
                shakeOffset = new Vector3(
                    Mathf.Clamp(x, -_maxShakeOffset, _maxShakeOffset),
                    Mathf.Clamp(y, -_maxShakeOffset, _maxShakeOffset),
                    0f
                );
            }

            // 4. Apply punch recoil
            _punchOffset = Vector3.MoveTowards(_punchOffset, Vector3.zero, k_PunchDamping * Time.deltaTime);

            Vector3 finalPos = smoothPos + shakeOffset + _punchOffset;
            if (_enableBoundary && _boundaryCollider != null)
            {
                finalPos = ClampToBounds(finalPos);
            }

            transform.position = finalPos;
        }

        /// <summary>
        /// Trigger a screen shake with given intensity and duration.
        /// </summary>
        public void Shake(float intensity, float duration)
        {
            _shakeIntensity = intensity;
            _shakeDuration = Mathf.Max(0.001f, duration);
            _shakeTimer = duration;
        }

        /// <summary>
        /// Trigger a directional punch recoil (e.g. opposite needle pin aim direction).
        /// </summary>
        public void Punch(Vector2 direction, float distance)
        {
            Vector2 norm = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector2.up;
            _punchOffset = new Vector3(norm.x, norm.y, 0f) * distance;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
            _targetRb = target != null ? target.GetComponent<Rigidbody2D>() : null;
        }

        /// <summary>
        /// Smoothly animates the camera orthographic size to a target value (cinematic zoom).
        /// </summary>
        public void ZoomTo(float targetOrthoSize, float duration, Ease ease = Ease.InOutCubic)
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;

            _zoomTween?.Kill();
            if (duration <= 0.001f)
            {
                _cam.orthographicSize = targetOrthoSize;
                return;
            }

            _zoomTween = DOTween.To(() => _cam.orthographicSize, x => _cam.orthographicSize = x, targetOrthoSize, duration).SetEase(ease);
        }

        /// <summary>
        /// Returns camera orthographic size smoothly back to its default scene value.
        /// </summary>
        public void ResetZoom(float duration, Ease ease = Ease.InOutQuad)
        {
            ZoomTo(_defaultOrthoSize, duration, ease);
        }

        /// <summary>
        /// Smoothly shifts the camera tracking offset (e.g. lifting the camera up in Y during cinematic reveals).
        /// </summary>
        public void ShiftOffset(Vector3 targetOffset, float duration, Ease ease = Ease.InOutCubic)
        {
            _offsetTween?.Kill();
            if (duration <= 0.001f)
            {
                _offset = targetOffset;
                return;
            }

            _offsetTween = DOTween.To(() => _offset, x => _offset = x, targetOffset, duration).SetEase(ease);
        }

        /// <summary>
        /// Smoothly shifts only the camera's Y offset.
        /// </summary>
        public void ShiftOffsetY(float targetOffsetY, float duration, Ease ease = Ease.InOutCubic)
        {
            Vector3 target = new Vector3(_offset.x, targetOffsetY, _offset.z);
            ShiftOffset(target, duration, ease);
        }

        /// <summary>
        /// Smoothly resets camera offset back to its default scene value.
        /// </summary>
        public void ResetOffset(float duration, Ease ease = Ease.InOutQuad)
        {
            ShiftOffset(_defaultOffset, duration, ease);
        }

        /// <summary>
        /// Snaps the camera immediately to the target position, zeroing damp velocity and shake offsets.
        /// Useful during checkpoint respawns and room transitions.
        /// </summary>
        public void SnapToTarget()
        {
            if (_target == null) return;
            Vector3 targetPos = _target.position + _offset;
            targetPos.z = _offset.z;
            if (_enableBoundary && _boundaryCollider != null)
            {
                targetPos = ClampToBounds(targetPos);
            }
            transform.position = targetPos;
            _currentVelocity = Vector3.zero;
            _currentLookAheadX = 0f;
            _shakeTimer = 0f;
            _punchOffset = Vector3.zero;
        }

        public Collider2D BoundaryCollider => _boundaryCollider;

        /// <summary>
        /// Dynamically assign or switch the active boundary collider (e.g. when entering a new sector).
        /// </summary>
        public void SetBoundary(Collider2D boundary)
        {
            _boundaryCollider = boundary;
        }

        /// <summary>
        /// Clamps a camera position vector within _boundaryCollider.
        /// Supports PolygonCollider2D (multi-pass shape confinement) and generic 2D colliders.
        /// </summary>
        public Vector3 ClampToBounds(Vector3 position)
        {
            if (_boundaryCollider == null || _cam == null || !_cam.orthographic) return position;

            if (_boundaryCollider is PolygonCollider2D poly)
            {
                return ClampToPolygon(position, poly);
            }

            Bounds bounds = _boundaryCollider.bounds;
            float vertExtent = _cam.orthographicSize;
            float horzExtent = vertExtent * _cam.aspect;

            float minX = bounds.min.x + horzExtent;
            float maxX = bounds.max.x - horzExtent;
            float minY = bounds.min.y + vertExtent;
            float maxY = bounds.max.y - vertExtent;

            position.x = (minX > maxX) ? bounds.center.x : Mathf.Clamp(position.x, minX, maxX);
            position.y = (minY > maxY) ? bounds.center.y : Mathf.Clamp(position.y, minY, maxY);

            return position;
        }

        private Vector3 ClampToPolygon(Vector3 pos, PolygonCollider2D poly)
        {
            float vertExtent = _cam.orthographicSize;
            float horzExtent = vertExtent * _cam.aspect;

            // 1. Initial outer bounding box clamp
            Bounds b = poly.bounds;
            float minX = b.min.x + horzExtent;
            float maxX = b.max.x - horzExtent;
            float minY = b.min.y + vertExtent;
            float maxY = b.max.y - vertExtent;

            pos.x = (minX > maxX) ? b.center.x : Mathf.Clamp(pos.x, minX, maxX);
            pos.y = (minY > maxY) ? b.center.y : Mathf.Clamp(pos.y, minY, maxY);

            // 2. Multi-pass viewport projection to conform to irregular ceilings, floors, and walls
            for (int iter = 0; iter < 4; iter++)
            {
                Vector2 topMid = new Vector2(pos.x, pos.y + vertExtent);
                Vector2 botMid = new Vector2(pos.x, pos.y - vertExtent);
                Vector2 leftMid = new Vector2(pos.x - horzExtent, pos.y);
                Vector2 rightMid = new Vector2(pos.x + horzExtent, pos.y);

                float pushDown = 0f;
                float pushUp = 0f;
                float pushRight = 0f;
                float pushLeft = 0f;

                Vector2 closestTop = topMid;
                Vector2 closestBot = botMid;
                Vector2 closestLeft = leftMid;
                Vector2 closestRight = rightMid;

                if (!poly.OverlapPoint(topMid))
                {
                    closestTop = poly.ClosestPoint(topMid);
                    float diff = closestTop.y - topMid.y;
                    if (diff < 0f) pushDown = diff;
                }

                if (!poly.OverlapPoint(botMid))
                {
                    closestBot = poly.ClosestPoint(botMid);
                    float diff = closestBot.y - botMid.y;
                    if (diff > 0f) pushUp = diff;
                }

                if (!poly.OverlapPoint(leftMid))
                {
                    closestLeft = poly.ClosestPoint(leftMid);
                    float diff = closestLeft.x - leftMid.x;
                    if (diff > 0f) pushRight = diff;
                }

                if (!poly.OverlapPoint(rightMid))
                {
                    closestRight = poly.ClosestPoint(rightMid);
                    float diff = closestRight.x - rightMid.x;
                    if (diff < 0f) pushLeft = diff;
                }

                // If room height < camera height, center vertically between floor and ceiling
                if (pushDown < -0.001f && pushUp > 0.001f)
                {
                    pos.y = (closestTop.y + closestBot.y) * 0.5f;
                }
                else
                {
                    pos.y += pushDown + pushUp;
                }

                // If room width < camera width, center horizontally between left and right walls
                if (pushLeft < -0.001f && pushRight > 0.001f)
                {
                    pos.x = (closestLeft.x + closestRight.x) * 0.5f;
                }
                else
                {
                    pos.x += pushLeft + pushRight;
                }

                // 3. Corner checks for angled walls / sloped ceilings
                Vector2 tl = new Vector2(pos.x - horzExtent, pos.y + vertExtent);
                Vector2 tr = new Vector2(pos.x + horzExtent, pos.y + vertExtent);
                Vector2 bl = new Vector2(pos.x - horzExtent, pos.y - vertExtent);
                Vector2 br = new Vector2(pos.x + horzExtent, pos.y - vertExtent);

                if (!poly.OverlapPoint(tl))
                {
                    Vector2 c = poly.ClosestPoint(tl);
                    Vector2 diff = c - tl;
                    if (diff.y < 0f) pos.y += diff.y;
                    if (diff.x > 0f) pos.x += diff.x;
                }
                if (!poly.OverlapPoint(tr))
                {
                    Vector2 c = poly.ClosestPoint(tr);
                    Vector2 diff = c - tr;
                    if (diff.y < 0f) pos.y += diff.y;
                    if (diff.x < 0f) pos.x += diff.x;
                }
                if (!poly.OverlapPoint(bl))
                {
                    Vector2 c = poly.ClosestPoint(bl);
                    Vector2 diff = c - bl;
                    if (diff.y > 0f) pos.y += diff.y;
                    if (diff.x > 0f) pos.x += diff.x;
                }
                if (!poly.OverlapPoint(br))
                {
                    Vector2 c = poly.ClosestPoint(br);
                    Vector2 diff = c - br;
                    if (diff.y > 0f) pos.y += diff.y;
                    if (diff.x < 0f) pos.x += diff.x;
                }
            }

            return pos;
        }

        private void OnDrawGizmosSelected()
        {
            Camera cam = _cam != null ? _cam : GetComponent<Camera>();
            if (cam == null || !cam.orthographic) return;

            float vertExtent = cam.orthographicSize;
            float horzExtent = vertExtent * cam.aspect;

            // Viewport rect wireframe in Cyan
            Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.85f);
            Gizmos.DrawWireCube(new Vector3(transform.position.x, transform.position.y, 0f), new Vector3(horzExtent * 2f, vertExtent * 2f, 0.1f));

            if (_boundaryCollider == null) return;

            Bounds bounds = _boundaryCollider.bounds;
            float minX = bounds.min.x + horzExtent;
            float maxX = bounds.max.x - horzExtent;
            float minY = bounds.min.y + vertExtent;
            float maxY = bounds.max.y - vertExtent;

            if (minX <= maxX && minY <= maxY)
            {
                Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.5f);
                Vector3 center = new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
                Vector3 size = new Vector3(maxX - minX, maxY - minY, 0.1f);
                Gizmos.DrawWireCube(center, size);
            }
        }
    }
}
