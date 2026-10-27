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

            Vector3 smoothPos = Vector3.SmoothDamp(transform.position, targetPos, ref _currentVelocity, _smoothTime);

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

            transform.position = smoothPos + shakeOffset + _punchOffset;
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
            transform.position = targetPos;
            _currentVelocity = Vector3.zero;
            _currentLookAheadX = 0f;
            _shakeTimer = 0f;
            _punchOffset = Vector3.zero;
        }
    }
}
