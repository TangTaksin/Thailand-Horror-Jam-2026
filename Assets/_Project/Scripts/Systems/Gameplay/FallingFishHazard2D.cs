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

        [Header("Audio SFX (AudioManager / Fallback Clips)")]
        [Tooltip("SFX name registered in AudioManager for player collision knockback (DieSlug 0-2).")]
        [SerializeField] private string _fishImpactSFXName = "sfx_fish_impact";
        [Tooltip("Direct audio clips for random knockback sounds (DieSlug 0-2). Played if AudioManager sound name is missing or as fallback.")]
        [SerializeField] private AudioClip[] _fishImpactClips;
        [SerializeField] [Range(0f, 1f)] private float _fishImpactVolume = 0.9f;

        [Tooltip("SFX name registered in AudioManager for needle projectile hitting fish (DieSkin_03).")]
        [SerializeField] private string _fishNeedleHitSFXName = "sfx_fish_needle_hit";
        [Tooltip("Fallback clip for needle hitting fish if AudioManager is unavailable.")]
        [SerializeField] private AudioClip _fishNeedleHitClip;
        [SerializeField] [Range(0f, 1f)] private float _fishNeedleHitVolume = 0.95f;

        [Header("Hazard vs Ambient Ground Flop Mode")]
        [Tooltip("If false, fish will not knockback or harm the player on contact (e.g. Sector 06 ambient fish rain).")]
        [SerializeField] private bool _harmPlayer = true;

        [Tooltip("If true, fish detects the ground, stops falling on impact, and flops continuously on the surface.")]
        [SerializeField] private bool _landAndFlopOnGround = false;

        [Tooltip("Ground layer mask for detecting landing surfaces (e.g. Ground layer).")]
        [SerializeField] private LayerMask _groundLayerMask = 1 << 6;

        [Tooltip("Vertical offset from ground hit point to fish center when resting on floor.")]
        [SerializeField] private float _groundRestOffset = 0.18f;

        [Tooltip("Duration in seconds that the fish remains flopping on the ground before recycling (0 = forever until pool recycles).")]
        [SerializeField] private float _groundFlopDuration = 16f;

        private Collider2D _collider;
        private bool _isDeflected;
        private bool _isCaughtOnShield;
        private bool _isFloppingOnGround;
        private float _groundFlopTimer;
        private float _twitchTimer;
        private float _nextTwitchInterval = 0.8f;
        private Vector2 _currentVelocity;
        private float _animTimer;
        private int _currentFrame;
        private Action<FallingFishHazard2D> _returnToPoolAction;

        private Transform _originalPoolParent;
        private Vector3 _groundRestPosition;
        private float _activeFallSpeed;
        private float _activeHorizontalDrift;

        // ระบบจำค่าเริ่มต้นสำหรับ Object Pool (รับประกันค่า 1, 1, 1 เสมอ)
        private bool _isInitialized;
        private Vector3 _baseScale = Vector3.one;

        private Tween _fallRotateTween;
        private Tween _fallScaleTween;

        public bool IsDeflected => _isDeflected;
        public bool IsCaughtOnShield => _isCaughtOnShield;
        public bool IsFloppingOnGround => _isFloppingOnGround;
        public bool HarmPlayer => _harmPlayer;
        public bool LandAndFlopOnGround => _landAndFlopOnGround;
        public Vector2 CurrentVelocity => _currentVelocity;
        public float FallSpeed => _fallSpeed;
        public float DespawnY => _despawnY;
        public Transform PoolParent => _originalPoolParent;
        public int CurrentFrame => _currentFrame;

        private void Awake()
        {
            EnsureInitialized();
        }

        public void SetPoolParent(Transform poolParent)
        {
            _originalPoolParent = poolParent;
        }

        /// <summary>
        /// บังคับจำค่า Scale ดั้งเดิมของ Prefab (รับประกันเป็น Vector3.one เสมอ ป้องกันการจำค่าที่บิดเบี้ยว)
        /// </summary>
        private void EnsureInitialized()
        {
            if (_isInitialized) return;
            _isInitialized = true;

            if (_originalPoolParent == null && transform.parent != null)
            {
                _originalPoolParent = transform.parent;
            }

            _baseScale = Vector3.one;
            transform.localScale = Vector3.one;

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
        /// Configures runtime behavior flags (e.g. ambient fish rain in Sector 06 vs hazard in Sector 05).
        /// </summary>
        public void ConfigureBehavior(bool harmPlayer, bool landAndFlopOnGround, LayerMask groundLayerMask, float groundFlopDuration = 16f)
        {
            _harmPlayer = harmPlayer;
            _landAndFlopOnGround = landAndFlopOnGround;
            _groundLayerMask = groundLayerMask;
            _groundFlopDuration = groundFlopDuration;
        }

        /// <summary>
        /// Initializes and spawns the fish at the specified world position.
        /// </summary>
        public void Spawn(
            Vector3 spawnPosition, 
            Action<FallingFishHazard2D> returnToPoolAction, 
            Sprite[] frames = null,
            float speedMultiplier = 1f,
            float horizontalDrift = 0f)
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
            _isCaughtOnShield = false;
            _isFloppingOnGround = false;

            _activeFallSpeed = _fallSpeed * speedMultiplier;
            _activeHorizontalDrift = horizontalDrift;
            _currentVelocity = new Vector2(_activeHorizontalDrift, -_activeFallSpeed);

            _animTimer = 0f;
            _currentFrame = 0;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.flipX = false;
                _spriteRenderer.flipY = false;
                _spriteRenderer.color = Color.white;
                if (_animationFrames != null && _animationFrames.Length > 0)
                {
                    _spriteRenderer.sprite = _animationFrames[0];
                }
            }

            if (_collider != null)
            {
                _collider.enabled = true;
            }

            gameObject.SetActive(true);
            enabled = true;

            // บังคับสเกลและมุมหมุนตั้งต้นให้เป็นปกติก่อนเริ่มอนิเมชันร่วงหล่น
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;

            // เริ่มเล่น visual juice ส่ายอย่างเป็นธรรมชาติ (สเกลคงที่ 1:1 พิกเซลคมชัดเสมอ)
            StartFallingLoopTween();
        }

        private void Update()
        {
            UpdateAnimation(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (_isFloppingOnGround)
            {
                UpdateFloppingState(dt);
                return;
            }

            if (_isDeflected)
            {
                _currentVelocity.y -= 9.8f * dt;
            }
            else
            {
                // ความเร็วตกในแนวดิ่งและ drift ตามค่าที่สุ่มตอน spawn
                _currentVelocity = new Vector2(_activeHorizontalDrift, -_activeFallSpeed);
            }

            Vector2 prevFishPos = transform.position;
            Vector3 stepMove = (Vector3)(_currentVelocity * dt);
            Vector2 nextFishPos = prevFishPos + (Vector2)stepMove;

            // ตรวจสอบว่าปลาตกตัดผ่านเส้นด้ายที่ผูกกับตัวผู้เล่น (Player Tether) หรือไม่
            if (_harmPlayer && !_isDeflected && !_isCaughtOnShield)
            {
                CheckTetherSlice(prevFishPos, nextFishPos);
            }

            // ตรวจจับการกระทบพื้นเฉพาะเมื่อเปิดโหมด _landAndFlopOnGround
            if (_landAndFlopOnGround && !_isDeflected && !_isCaughtOnShield)
            {
                float checkDist = Mathf.Abs(stepMove.y) + 0.18f;
                RaycastHit2D hit = Physics2D.Raycast(transform.position, Vector2.down, checkDist, _groundLayerMask);
                if (hit.collider != null && !hit.collider.isTrigger)
                {
                    LandOnGround(hit.point, hit.normal);
                    return;
                }
            }

            transform.position += stepMove;

            if (transform.position.y <= _despawnY || Mathf.Abs(transform.position.x) > 200f)
            {
                Despawn();
            }
        }

        /// <summary>
        /// เมื่อปลาตกลงกระทบพื้น จะหยุดร่วง สควอชเด้งตัว และนอนดิ้นส่ายอยู่บนพื้นอย่างต่อเนื่อง
        /// </summary>
        public void LandOnGround(Vector2 groundPoint, Vector2 normal)
        {
            if (_isFloppingOnGround || _isCaughtOnShield) return;

            _isFloppingOnGround = true;
            _currentVelocity = Vector2.zero;
            StopFallingLoopTweens();

            // ปิด Collider2D ทันทีที่กระทบพื้นเพื่อลดภาระการคำนวณ Physics 2D สำหรับปลา 100+ ตัวบนพื้น
            if (_collider != null)
            {
                _collider.enabled = false;
            }

            _groundRestPosition = new Vector3(groundPoint.x, groundPoint.y + _groundRestOffset, 0f);
            transform.position = _groundRestPosition;

            // สุ่มมุมนอนระนาบพื้นเล็กน้อย (-14 ถึง +14 องศา)
            float groundAngle = UnityEngine.Random.Range(-14f, 14f);
            transform.localRotation = Quaternion.Euler(0f, 0f, groundAngle);

            // อนิเมชันกระแทกพื้น Squish & Stretch แบบปลอดภัย สเกลกลับเป็น Vector3.one เสมอ
            transform.DOKill();
            transform.localScale = Vector3.one;
            Vector3 impactPunch = new Vector3(0.35f, -0.28f, 0f);
            transform.DOPunchScale(impactPunch, 0.22f, 8, 0.8f)
                .OnComplete(() => transform.localScale = Vector3.one);

            _twitchTimer = 0f;
            _nextTwitchInterval = UnityEngine.Random.Range(0.45f, 0.9f);
        }

        private void UpdateFloppingState(float dt)
        {
            _groundFlopTimer += dt;
            _twitchTimer += dt;

            // การกระตุกดิ้นและเด้งขึ้นลงบนพื้นอย่างต่อเนื่อง (Spasmodic floor bounce & twitch)
            if (_twitchTimer >= _nextTwitchInterval)
            {
                _twitchTimer = 0f;
                _nextTwitchInterval = UnityEngine.Random.Range(0.45f, 1.0f);

                float bounceHop = UnityEngine.Random.Range(0.06f, 0.16f);
                float bounceTilt = UnityEngine.Random.Range(-18f, 18f);

                // คืนค่าตำแหน่งและสเกลฐานก่อนเริ่มเด้งใหม่เสมอ ป้องกันตำแหน่งเลื่อนหรือสเกลค้าง
                transform.DOKill();
                transform.position = _groundRestPosition;
                transform.localScale = Vector3.one;

                transform.DOPunchPosition(new Vector3(0f, bounceHop, 0f), 0.22f, 4, 0.6f)
                    .OnComplete(() =>
                    {
                        transform.position = _groundRestPosition;
                        transform.localScale = Vector3.one;
                    });
                transform.DOLocalRotate(new Vector3(0f, 0f, bounceTilt), 0.20f);
            }

            // หากเกินเวลา _groundFlopDuration ให้ค่อยๆ จางและคืนสู่ Pool
            if (_groundFlopDuration > 0f && _groundFlopTimer >= _groundFlopDuration)
            {
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.DOFade(0f, 0.5f).OnComplete(Despawn);
                }
                else
                {
                    Despawn();
                }
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
        /// 1. อนิเมชันการดิ้นส่ายขณะร่วงหล่น (สเกลคงที่ 1:1 พิกเซลคมชัด ไม่บิดเบี้ยว)
        /// </summary>
        private void StartFallingLoopTween()
        {
            if (_isDeflected || !gameObject.activeInHierarchy) return;

            // บังคับสเกล 1, 1, 1 ป้องกันพิกเซลย้วย
            transform.localScale = Vector3.one;

            float startDir = UnityEngine.Random.value > 0.5f ? 1f : -1f;
            transform.localRotation = Quaternion.Euler(0f, 0f, -startDir * _fallWiggleAngle);

            _fallRotateTween = transform
                .DOLocalRotate(new Vector3(0f, 0f, startDir * _fallWiggleAngle), _fallWiggleDuration)
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
            transform.localScale = Vector3.one;

            Vector3 scaledPunch = _deflectPunchScale;
            transform.DOPunchScale(scaledPunch, 0.22f, 10, 1f)
                .OnComplete(() => transform.localScale = Vector3.one);

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
        private void CheckTetherSlice(Vector2 prevPos, Vector2 nextPos)
        {
            PlayerController2D player = PlayerController2D.Instance;
            if (player == null) return;

            bool isTethered = player.IsHauling || (player.NeedleController != null && player.NeedleController.IsTetheredToPlayer);
            if (!isTethered) return;

            IStitchable crate = (player.NeedleController != null && player.NeedleController.IsTetheredToPlayer)
                ? player.NeedleController.TetheredStitchable
                : player.CurrentStitchable;

            if (crate == null || crate.ObjectTransform == null) return;

            Vector2 playerPos = player.transform.position;
            Vector2 cratePos = crate.CurrentPosition;

            if (SutureThread.SegmentsIntersect(prevPos, nextPos, playerPos, cratePos) ||
                SqrDistToSegment(prevPos, playerPos, cratePos) <= 0.09f ||
                SqrDistToSegment(nextPos, playerPos, cratePos) <= 0.09f)
            {
                player.ReleaseHaulingTether();
                if (player.NeedleController != null && player.NeedleController.IsTetheredToPlayer)
                {
                    player.NeedleController.ReleasePlayerTether();
                }

                _isDeflected = true;
                float bounceX = UnityEngine.Random.Range(-1.6f, 1.6f);
                if (Mathf.Abs(bounceX) < 0.5f) bounceX = 1.0f;
                _currentVelocity = new Vector2(bounceX, 1.5f);
                if (_collider != null) _collider.enabled = false;
                PlayPlayerHitTween(Mathf.Sign(bounceX));

                CameraController2D.Instance?.Shake(0.04f, 0.1f);
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX("sfx_thread_cut");
                }
            }
        }

        private static float SqrDistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-6f) return (p - a).sqrMagnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            Vector2 proj = a + t * ab;
            return (p - proj).sqrMagnitude;
        }

        private void PlayPlayerHitTween(float bounceDir)
        {
            StopFallingLoopTweens();

            transform.DOKill();
            transform.localScale = Vector3.one;

            Vector3 scaledPunch = _playerHitPunchScale;
            transform.DOPunchScale(scaledPunch, 0.28f, 12, 0.8f)
                .OnComplete(() => transform.localScale = Vector3.one);

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

        private void PlayFishImpactSound()
        {
            if (AudioManager.Instance == null) return;

            if (!string.IsNullOrEmpty(_fishImpactSFXName) && AudioManager.Instance.GetSoundClip(_fishImpactSFXName) != null)
            {
                AudioManager.Instance.PlaySFX(_fishImpactSFXName, _fishImpactVolume);
            }
            else if (_fishImpactClips != null && _fishImpactClips.Length > 0)
            {
                AudioClip clip = _fishImpactClips[UnityEngine.Random.Range(0, _fishImpactClips.Length)];
                if (clip != null)
                {
                    AudioManager.Instance.PlaySFX(clip, _fishImpactVolume);
                }
            }
        }

        private void PlayFishNeedleHitSound()
        {
            if (AudioManager.Instance == null) return;

            if (!string.IsNullOrEmpty(_fishNeedleHitSFXName) && AudioManager.Instance.GetSoundClip(_fishNeedleHitSFXName) != null)
            {
                AudioManager.Instance.PlaySFX(_fishNeedleHitSFXName, _fishNeedleHitVolume);
            }
            else if (_fishNeedleHitClip != null)
            {
                AudioManager.Instance.PlaySFX(_fishNeedleHitClip, _fishNeedleHitVolume);
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
        /// หยุด Tween ทั้งหมดและคืนค่า Transform + Parent + ฟิสิกส์ความเร็ว + สี + สไปรต์ กลับสู่เฟรมเริ่มต้น (1, 1, 1) ทันที 100%
        /// </summary>
        private void ResetPoolState()
        {
            StopFallingLoopTweens();

            transform.DOKill();

            // คืน Parent กลับสู่ Pool ดั้งเดิมเสมอ (กรณีถูกเก็บบนหลังคาลังเคลื่อนที่)
            if (_originalPoolParent != null && transform.parent != _originalPoolParent)
            {
                transform.SetParent(_originalPoolParent, false);
            }

            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = Color.white;
                _spriteRenderer.flipX = false;
                _spriteRenderer.flipY = false;
                if (_animationFrames != null && _animationFrames.Length > 0)
                {
                    _spriteRenderer.sprite = _animationFrames[0];
                }
            }

            _baseScale = Vector3.one;
            transform.localScale = Vector3.one;
            transform.localRotation = Quaternion.identity;

            _isDeflected = false;
            _isCaughtOnShield = false;
            _isFloppingOnGround = false;
            _groundFlopTimer = 0f;
            _twitchTimer = 0f;
            _animTimer = 0f;
            _currentFrame = 0;
            _activeFallSpeed = _fallSpeed;
            _activeHorizontalDrift = 0f;
            _currentVelocity = new Vector2(0f, -_fallSpeed);
            if (_collider != null)
            {
                _collider.enabled = true;
            }
        }

        #endregion

        /// <summary>
        /// Collects and attaches this falling fish onto the crate roof shield,
        /// neutralizing its hazard physics and parenting it as visual gory debris.
        /// </summary>
        public void CollectOnShield(Transform shieldParent, Vector3 localPos)
        {
            _isCaughtOnShield = true;
            StopFallingLoopTweens();
            transform.DOKill();
            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
            }

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            enabled = false;
            transform.SetParent(shieldParent, true);
            transform.localPosition = localPos;
            transform.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-25f, 25f));
        }

        public bool OnHitByNeedle(NeedleProjectile projectile, Vector2 hitPoint, Vector2 hitDirection)
        {
            if (_isDeflected) return false;

            _isDeflected = true;
            _isFloppingOnGround = false;

            float lateralDir = Mathf.Sign(hitDirection.x);
            if (Mathf.Abs(hitDirection.x) < 0.1f)
            {
                lateralDir = transform.position.x >= 109.5f ? 1f : -1f;
            }

            _currentVelocity = new Vector2(lateralDir * _deflectLateralSpeed, _deflectUpwardSpeed);

            PlayNeedleDeflectTween(lateralDir);

            PlayFishNeedleHitSound();

            CameraController2D.Instance?.Shake(0.02f, 0.05f);

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            return true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (_isFloppingOnGround || _isDeflected || other == null) return;

            // หากปิด _harmPlayer ให้เพิกเฉย ไม่ผลักหรือขัดจังหวะผู้เล่น
            if (!_harmPlayer && PlayerController2D.IsPlayer(other))
            {
                return;
            }

            if (PlayerController2D.IsPlayer(other))
            {
                PlayerController2D player = other.GetComponentInParent<PlayerController2D>();
                if (player != null)
                {
                    if (player.CanReceiveHazardKnockback)
                    {
                        // เด้งไปทางซ้ายเสมอ (ค่าแกน X ติดลบเสมอ) ไม่ว่าจะหันหน้าไปทางไหน
                        Vector2 impulse = new Vector2(-Mathf.Abs(_knockbackHorizontalForce), _knockbackVerticalForce);
                        player.ApplyKnockback(impulse, 0.22f);
                        player.SetHazardKnockbackCooldown(0.28f);
                    }
                }

                _isDeflected = true;
                float randomBounceX = UnityEngine.Random.Range(-2f, 2f);
                if (Mathf.Abs(randomBounceX) < 0.5f) randomBounceX = 1.2f;

                _currentVelocity = new Vector2(randomBounceX, 1.5f);
                if (_collider != null) _collider.enabled = false;

                PlayPlayerHitTween(Mathf.Sign(randomBounceX));
                PlayFishImpactSound();
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