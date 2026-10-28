using System;
using UnityEngine;
using DG.Tweening;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Falling dead fish hazard spawned from the ceiling waterfall in Sector 05.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class FallingFishHazard2D : MonoBehaviour, INeedleHittable
    {
        [Header("Movement & Physics")]
        [Tooltip("Downward vertical falling speed in meters per second.")]
        [SerializeField] private float _fallSpeed = 4.4f;

        [Tooltip("Lateral gliding speed when deflected by needle.")]
        [SerializeField] private float _deflectLateralSpeed = 5.2f;

        [Tooltip("Upward bounce velocity applied when deflected.")]
        [SerializeField] private float _deflectUpwardSpeed = 2.0f;

        [Tooltip("Y position threshold below which the fish despawns back to the pool.")]
        [SerializeField] private float _despawnY = -3.2f;

        [Header("Knockback on Player")]
        [Tooltip("Horizontal impulse applied to the player when struck by an undeflected fish.")]
        [SerializeField] private float _knockbackHorizontalForce = 4.8f;

        [Tooltip("Upward / vertical impulse applied to the player on strike.")]
        [SerializeField] private float _knockbackVerticalForce = 2.0f;

        [Header("Visuals & Animation")]
        [Tooltip("Frames of the 24-frame Dead_Fish_SpriteSheet.")]
        [SerializeField] private Sprite[] _animationFrames;

        [Tooltip("Playback speed for the frame animation.")]
        [SerializeField] private float _fps = 16f;

        [Tooltip("Custom SpriteRenderer for the fish (auto-detected if null).")]
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("DOTween: 1. Falling Motion (ขณะร่วงหล่น)")]
        [SerializeField] private float _fallWiggleAngle = 9f;
        [SerializeField] private float _fallWiggleDuration = 0.28f;
        [SerializeField] private Vector2 _fallStretchScale = new Vector2(0.92f, 1.08f);

        [Header("DOTween: 2. Needle Deflection (เมื่อถูกเข็มยิงปัด)")]
        [SerializeField] private Vector3 _deflectPunchScale = new Vector3(0.35f, -0.25f, 0f);
        [SerializeField] private float _deflectSpinAngle = 140f;
        [SerializeField] private float _deflectTweenDuration = 0.45f;

        [Header("DOTween: 3. Player Impact (เมื่อหล่นใส่ตัวผู้เล่น)")]
        [SerializeField] private Vector3 _playerHitPunchScale = new Vector3(0.5f, -0.42f, 0f);
        [SerializeField] private float _playerHitSpinAngle = 320f;
        [SerializeField] private float _playerHitTweenDuration = 0.5f;

        private Collider2D _collider;
        private bool _isDeflected;
        private Vector2 _currentVelocity;
        private float _animTimer;
        private int _currentFrame;
        private Action<FallingFishHazard2D> _returnToPoolAction;

        // ระบบจำค่าเริ่มต้นสำหรับ Object Pool (รับประกันค่า 1, 1, 1 เสมอ)
        private bool _isInitialized;
        private Vector3 _baseScale = Vector3.one;

        private Tween _fallRotateTween;
        private Tween _fallScaleTween;

        public bool IsDeflected => _isDeflected;
        public Vector2 CurrentVelocity => _currentVelocity;
        public float FallSpeed => _fallSpeed;

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>
        /// บังคับจำค่า Scale ดั้งเดิมของ Prefab (รับประกันเป็น Vector3.one เสมอ ป้องกันการจำค่าที่บิดเบี้ยว)
        /// </summary>
        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            // ตรวจสอบค่า localScale: หากเป็น Vector3.zero หรือถูกบิดเบี้ยว ให้ใช้ Vector3.one เสมอ
            if (transform.localScale != Vector3.zero && Mathf.Abs(transform.localScale.x - 1f) <= 0.05f)
            {
                _baseScale = transform.localScale;
            }
            else
            {
                _baseScale = Vector3.one;
                transform.localScale = Vector3.one;
            }

            _collider = GetComponent<Collider2D>();
            if (_collider != null)
            {
                _collider.isTrigger = true;
            }

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
                if (_spriteRenderer == null)
                {
                    _spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
                }
            }
        }

        /// <summary>
        /// Initializes and spawns the fish at the specified world position.
        /// </summary>
        public void Spawn(Vector3 spawnPosition, Action<FallingFishHazard2D> returnToPoolAction, Sprite[] frames = null)
        {
            EnsureInitialized();

            // ล้าง Tween เก่าทั้งหมดและรีเซ็ตสเกลกลับเป็น (1, 1, 1) พร้อมความเร็วตกปกติ 100%
            ResetPoolState();

            if (frames != null && frames.Length > 0)
            {
                _animationFrames = frames;
            }

            transform.position = spawnPosition;
            _returnToPoolAction = returnToPoolAction;
            _isDeflected = false;
            _currentVelocity = new Vector2(0f, -_fallSpeed);
            _animTimer = UnityEngine.Random.Range(0f, 0.2f);
            _currentFrame = UnityEngine.Random.Range(0, _animationFrames != null && _animationFrames.Length > 0 ? _animationFrames.Length : 1);

            if (_spriteRenderer != null && _animationFrames != null && _animationFrames.Length > 0)
            {
                _spriteRenderer.sprite = _animationFrames[_currentFrame];
                _spriteRenderer.color = Color.white;
            }

            if (_collider != null)
            {
                _collider.enabled = true;
            }

            gameObject.SetActive(true);

            // บังคับสเกลและมุมหมุนตั้งต้นให้เป็นปกติก่อนเริ่มอนิเมชันร่วงหล่น
            transform.localScale = _baseScale;
            transform.localRotation = Quaternion.identity;

            // เริ่มเล่น visual juice ส่ายและยืดหดตัวขณะร่วงหล่น (ความเร็วตกคงที่สม่ำเสมอ)
            StartFallingLoopTween();
        }

        private void Update()
        {
            UpdateAnimation(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (_isDeflected)
            {
                _currentVelocity.y -= 9.8f * dt;
            }
            else
            {
                // ความเร็วตกในแนวดิ่งคงที่สม่ำเสมอเสมอเมื่อยังไม่ถูกเบี่ยงวิถี (-_fallSpeed)
                _currentVelocity = new Vector2(0f, -_fallSpeed);
            }

            transform.position += (Vector3)(_currentVelocity * dt);

            if (transform.position.y <= _despawnY || Mathf.Abs(transform.position.x) > 200f)
            {
                Despawn();
            }
        }

        private void UpdateAnimation(float dt)
        {
            if (_animationFrames == null || _animationFrames.Length == 0 || _spriteRenderer == null)
            {
                return;
            }

            _animTimer += dt;
            float frameInterval = _fps > 0.1f ? (1f / _fps) : 0.06f;

            if (_animTimer >= frameInterval)
            {
                _animTimer -= frameInterval;
                _currentFrame = (_currentFrame + 1) % _animationFrames.Length;
                _spriteRenderer.sprite = _animationFrames[_currentFrame];
            }
        }

        #region DOTween Animations

        /// <summary>
        /// 1. อนิเมชันการดิ้นส่ายและยืดหดตัวขณะร่วงหล่น (ไม่กระทบความเร็วตกในฟิสิกส์)
        /// </summary>
        private void StartFallingLoopTween()
        {
            if (_isDeflected || !gameObject.activeInHierarchy) return;

            // บังคับตั้งต้นที่ _baseScale ก่อนเข้าลูป Yoyo
            transform.localScale = _baseScale;

            float startDir = UnityEngine.Random.value > 0.5f ? 1f : -1f;
            transform.localRotation = Quaternion.Euler(0f, 0f, -startDir * _fallWiggleAngle);

            _fallRotateTween = transform
                .DOLocalRotate(new Vector3(0f, 0f, startDir * _fallWiggleAngle), _fallWiggleDuration)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);

            Vector3 targetStretch = new Vector3(
                _baseScale.x * _fallStretchScale.x,
                _baseScale.y * _fallStretchScale.y,
                _baseScale.z
            );

            _fallScaleTween = transform
                .DOScale(targetStretch, _fallWiggleDuration * 0.85f)
                .SetEase(Ease.InOutSine)
                .SetLoops(-1, LoopType.Yoyo);
        }

        /// <summary>
        /// 2. เมื่อถูกเข็มยิงปัด (Deflection)
        /// </summary>
        private void PlayNeedleDeflectTween(float lateralDir)
        {
            StopFallingLoopTweens();

            transform.DOKill();
            transform.localScale = _baseScale;

            // คูณสัดส่วน Punch กับ _baseScale เพื่อให้ขนาดเด้งสัมพันธ์กับขนาดจริงของ Prefab
            Vector3 scaledPunch = Vector3.Scale(_baseScale, _deflectPunchScale);
            transform.DOPunchScale(scaledPunch, 0.22f, 10, 1f)
                .OnComplete(() => transform.localScale = _baseScale);

            transform.DOLocalRotate(
                new Vector3(0f, 0f, lateralDir * -_deflectSpinAngle),
                _deflectTweenDuration,
                RotateMode.FastBeyond360
            ).SetEase(Ease.OutCubic);

            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = new Color(0.8f, 0.95f, 1f, 1f);
                _spriteRenderer.DOFade(0.55f, _deflectTweenDuration).SetEase(Ease.InQuad);
            }
        }

        /// <summary>
        /// 3. เมื่อหล่นใส่ตัวผู้เล่น
        /// </summary>
        private void PlayPlayerHitTween(float bounceDir)
        {
            StopFallingLoopTweens();

            transform.DOKill();
            transform.localScale = _baseScale;

            Vector3 scaledPunch = Vector3.Scale(_baseScale, _playerHitPunchScale);
            transform.DOPunchScale(scaledPunch, 0.28f, 12, 0.8f)
                .OnComplete(() => transform.localScale = _baseScale);

            transform.DOLocalRotate(
                new Vector3(0f, 0f, -bounceDir * _playerHitSpinAngle),
                _playerHitTweenDuration,
                RotateMode.FastBeyond360
            ).SetEase(Ease.OutQuad);

            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = new Color(1f, 0.65f, 0.65f, 1f);
                _spriteRenderer.DOFade(0.4f, _playerHitTweenDuration).SetEase(Ease.InSine);
            }
        }

        private void StopFallingLoopTweens()
        {
            _fallRotateTween?.Kill();
            _fallRotateTween = null;

            _fallScaleTween?.Kill();
            _fallScaleTween = null;
        }

        /// <summary>
        /// หยุด Tween ทั้งหมดและคืนค่า Transform + ฟิสิกส์ความเร็ว + สี กลับสู่ค่าเริ่มต้น (1, 1, 1) ทันที 100%
        /// </summary>
        private void ResetPoolState()
        {
            StopFallingLoopTweens();

            transform.DOKill();
            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = Color.white;
            }

            if (_baseScale == Vector3.zero) _baseScale = Vector3.one;
            transform.localScale = _baseScale;
            transform.localRotation = Quaternion.identity;

            _isDeflected = false;
            _currentVelocity = new Vector2(0f, -_fallSpeed);
            if (_collider != null)
            {
                _collider.enabled = true;
            }
        }

        #endregion

        public bool OnHitByNeedle(NeedleProjectile projectile, Vector2 hitPoint, Vector2 hitDirection)
        {
            if (_isDeflected) return false;

            _isDeflected = true;

            float lateralDir = Mathf.Sign(hitDirection.x);
            if (Mathf.Abs(hitDirection.x) < 0.1f)
            {
                lateralDir = transform.position.x >= 109.5f ? 1f : -1f;
            }

            _currentVelocity = new Vector2(lateralDir * _deflectLateralSpeed, _deflectUpwardSpeed);

            PlayNeedleDeflectTween(lateralDir);

            CameraController2D.Instance?.Shake(0.02f, 0.05f);

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            return true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isDeflected || other == null) return;

            if (PlayerController2D.IsPlayer(other))
            {
                PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
                if (player != null)
                {
                    float pushDir = -1f;
                    Vector2 impulse = new Vector2(pushDir * _knockbackHorizontalForce, _knockbackVerticalForce);
                    player.ApplyKnockback(impulse, 0.22f);
                }

                _isDeflected = true;
                float randomBounceX = UnityEngine.Random.Range(-2f, 2f);
                if (Mathf.Abs(randomBounceX) < 0.5f) randomBounceX = 1.2f;

                _currentVelocity = new Vector2(randomBounceX, 1.5f);
                if (_collider != null) _collider.enabled = false;

                PlayPlayerHitTween(Mathf.Sign(randomBounceX));
            }
        }

        public void Despawn()
        {
            ResetPoolState();
            gameObject.SetActive(false);
            _returnToPoolAction?.Invoke(this);
        }

        private void OnDisable()
        {
            ResetPoolState();
        }
    }
}