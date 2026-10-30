using System.Collections;
using UnityEngine;
using DG.Tweening;
using TMPro;
using ThailandHorrorJam.Systems.Needle;
using ThailandHorrorJam.Systems.Gameplay;
using ThailandHorrorJam.Systems.UI;

namespace ThailandHorrorJam.Core
{
    public sealed class PlayerVisuals : MonoBehaviour
    {
        public enum VisualState
        {
            Idle,
            Walk,
            HaulWalk,
            Jump,
            Fall,
            FallLoop,
            Aim,
            Throw,
            Dead,
            WakeUp
        }

        [Header("References")]
        [SerializeField] private PlayerController2D _controller;
        [SerializeField] private NeedleController _needleController;
        [SerializeField] private Transform _visualHolder;
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("Sprite Clips")]
        [SerializeField] private Sprite[] _idleSprites;
        [SerializeField] private Sprite[] _walkSprites;
        [Tooltip("Dedicated bent/crouching pulling animation clip when hauling tethered objects.")]
        [SerializeField] private Sprite[] _haulWalkSprites;
        [SerializeField] private Sprite[] _jumpSprites;
        [SerializeField] private Sprite[] _fallSprites;
        [SerializeField] private Sprite[] _fallLoopSprites;
        [SerializeField] private Sprite[] _aimSprites;
        [SerializeField] private Sprite[] _throwSprites;
        [SerializeField] private Sprite[] _deadSprites;
        [SerializeField] private Sprite[] _wakeUpSprites;

        [Header("Throw FX & Ripple")]
        [SerializeField] private GameObject _rippleEffectPrefab;
        [SerializeField] private Vector3 _throwFxOffset = new Vector3(0.22f, 0.05f, 0f);

        [Header("Aim Needle Visual (Hand-held)")]
        [SerializeField] private GameObject _aimNeedleObj;
        [SerializeField] private SpriteRenderer _aimNeedleRenderer;
        [Tooltip("Hand anchor offset in VisualHolder local space where the needle eye is held.")]
        [SerializeField] private Vector3 _aimNeedleHandOffset = new Vector3(-0.328f, 0.02f, 0f);
        [SerializeField] private bool _flipNeedleWhenAimingLeft = true;

        [Header("Frame Rates (FPS)")]
        [SerializeField] private float _idleFps = 6.6f;
        [SerializeField] private float _walkFps = 6.4f;
        [SerializeField] private float _haulWalkFps = 6.4f;
        [SerializeField] private float _haulingWalkFpsMultiplier = 0.8f;
        [SerializeField] private float _jumpFps = 12f;
        [SerializeField] private float _fallFps = 12f;
        [SerializeField] private float _fallLoopFps = 10f;
        [SerializeField] private float _aimFps = 6f;
        [SerializeField] private float _throwFps = 14f;
        [SerializeField] private float _deadFps = 8f;
        [SerializeField] private float _wakeUpFps = 8f;
        [SerializeField] private float _throwDuration = 0.18f;

        [Header("Airborne Thresholds")]
        [Tooltip("Downward vertical velocity required to transition from Jump/Apex into Fall.")]
        [SerializeField] private float _fallThreshold = -2.2f;

        [Header("Facing Settings")]
        [Tooltip("When true, faces towards mouse aim while pinning needle threads.")]
        [SerializeField] private bool _faceAimWhenPinning = true;
        [SerializeField] private bool _startFacingRight = true;

        [Header("Audio (Footsteps on Walk Frames)")]
        [Tooltip("เล่นเสียงฝีเท้าตามจังหวะเฟรมของ Sprite เดิน")]
        [SerializeField] private bool _playFootstepAudio = true;
        [Tooltip("ชื่อ SFX ใน AudioManager")]
        [SerializeField] private string _footstepSFXName = "Footstep";
        [Tooltip("ระดับความดังของเสียงฝีเท้า")]
        [SerializeField] [Range(0f, 1f)] private float _footstepVolume = 0.65f;
        [Tooltip("ดัชนีเฟรมใน Walk Animation ที่เท้าแตะพื้น (สำหรับ 6 เฟรม โดยปกติคือเฟรม 2 และ 5)")]
        [SerializeField] private int[] _walkFootstepFrames = new int[] { 2, 5 };
        [Tooltip("ดัชนีเฟรมใน HaulWalk Animation ที่เท้าแตะพื้น")]
        [SerializeField] private int[] _haulWalkFootstepFrames = new int[] { 1 };

        [Header("Squash & Stretch (Juice)")]
        [SerializeField] private bool _enableSquashAndStretch = true;
        [SerializeField] private Vector3 _jumpStretch = new Vector3(0.86f, 1.18f, 1f);
        [SerializeField] private Vector3 _landSquash = new Vector3(1.22f, 0.80f, 1f);
        [SerializeField] private float _squashRecoverySpeed = 10f;

        [Header("Haul Strain (Juice)")]
        [SerializeField] private bool _enableHaulTilt = true;
        [Tooltip("Forward torso tilt angle while walking with a tethered load.")]
        [SerializeField] private float _haulForwardTiltAngle = -13f;
        [Tooltip("Backward bracing tilt angle while holding tether tension at rest.")]
        [SerializeField] private float _haulBraceTiltAngle = 6f;
        [SerializeField] private float _tiltSpeed = 12f;

        [Header("Needle Throw Recoil (Juice)")]
        [SerializeField] private bool _enableThrowRecoil = true;
        [Tooltip("Backward physical kick distance along opposite aim trajectory.")]
        [SerializeField] private float _throwRecoilDistance = 0.06f;
        [SerializeField] private Vector3 _throwRecoilSquash = new Vector3(1.16f, 0.88f, 1f);
        [SerializeField] private float _recoilRecoverySpeed = 16f;

        [Header("Aim Pitch Lean (Juice)")]
        [SerializeField] private bool _enableAimLean = true;
        [Tooltip("Maximum torso tilt angle up or down towards aim cursor.")]
        [SerializeField] private float _maxAimTiltAngle = 7f;

        [Header("Airborne Fall Stretch (Juice)")]
        [SerializeField] private bool _enableAirStretch = true;
        [Tooltip("Downward vertical velocity threshold where body starts stretching.")]
        [SerializeField] private float _fallStretchThreshold = -3.5f;
        [SerializeField] private float _maxFallStretchY = 1.15f;

        [Header("DOTween Impact FX")]
        [SerializeField] private bool _enableSnapFx = true;
        [SerializeField] private Color _snapFlashColor = new Color(1.5f, 1.5f, 1.5f, 1f);

        [Header("Game Start Intro")]
        [Tooltip("If true, player starts lying on the floor, wakes up, then unlocks input and reveals the movement hint.")]
        [SerializeField] private bool _enableGameStartWakeUp = true;
        [Tooltip("How long to stay lying still on the floor before starting to stand up (seconds).")]
        [SerializeField] private float _startLieDownDuration = 4.0f;
        [Tooltip("Optional reference to the Hint_Movement object in scene. If null, auto-finds in scene.")]
        [SerializeField] private GameObject _hintMovement;
        [Tooltip("Duration to smoothly fade in the movement hint text once standing up.")]
        [SerializeField] private float _hintFadeInDuration = 0.6f;

        private VisualState _currentState = VisualState.Idle;
        private Sprite[] _currentClip;
        private int _currentFrame;
        private float _frameTimer;
        private bool _isFacingRight = true;
        private Vector3 _scaleModifier = Vector3.one;
        private Vector3 _baseScale = Vector3.one;
        private float _currentTiltAngle;
        private Vector3 _recoilOffset = Vector3.zero;
        private float _throwTimer;
        private bool _isDead;
        private bool _isWakingUp;
        private float _wakeUpHoldTimer;
        private System.Action _onWakeUpCompletedCallback;
        private Color _hintMovementTargetColor = Color.white;
        private bool _hasCachedHintColor;
        private bool _isSubscribedToCheckpointManager;

        public VisualState CurrentState => _currentState;
        public bool IsFacingRight => _isFacingRight;
        public bool IsWakingUp => _isWakingUp;
        public SpriteRenderer MainRenderer => _spriteRenderer;
        public event System.Action OnWakeUpCompleted;

        public void SetFacing(bool faceRight)
        {
            _isFacingRight = faceRight;
            if (_visualHolder != null)
            {
                float dirX = _isFacingRight ? 1f : -1f;
                _visualHolder.localScale = new Vector3(
                    dirX * _baseScale.x * _scaleModifier.x,
                    _baseScale.y * _scaleModifier.y,
                    1f
                );
            }
        }

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponentInParent<PlayerController2D>();
                if (_controller == null)
                {
                    _controller = transform.root.GetComponentInChildren<PlayerController2D>();
                }
            }

            if (_needleController == null)
            {
                _needleController = GetComponentInParent<NeedleController>();
                if (_needleController == null)
                {
                    _needleController = transform.root.GetComponentInChildren<NeedleController>();
                }
            }

            if (_spriteRenderer == null)
            {
                _spriteRenderer = GetComponentInChildren<SpriteRenderer>();
            }

            if (_visualHolder == null)
            {
                Transform visualChild = transform.Find("Visual");
                if (visualChild != null)
                {
                    _visualHolder = visualChild;
                }
                else if (_spriteRenderer != null && _spriteRenderer.transform != transform)
                {
                    _visualHolder = _spriteRenderer.transform;
                }
                else
                {
                    _visualHolder = transform;
                }
            }

            if (_visualHolder != null)
            {
                _baseScale = new Vector3(
                    Mathf.Abs(_visualHolder.localScale.x) > 0.001f ? Mathf.Abs(_visualHolder.localScale.x) : 1f,
                    Mathf.Abs(_visualHolder.localScale.y) > 0.001f ? Mathf.Abs(_visualHolder.localScale.y) : 1f,
                    1f
                );
            }

            _isFacingRight = _startFacingRight;
            EnsureAimNeedleVisual();

            if (_enableGameStartWakeUp)
            {
                CacheAndHideHintMovement();
            }
        }

        private void OnEnable()
        {
            if (_controller != null)
            {
                _controller.OnJumped += HandlePlayerJumped;
                _controller.OnLanded += HandlePlayerLanded;
            }

            if (_needleController != null)
            {
                _needleController.OnNeedleLaunched += HandleNeedleLaunched;
                _needleController.OnThreadSevered += HandleThreadSevered;
                _needleController.OnPlayerTetherAttached += HandlePlayerTetherAttached;
            }

            SubscribeToCheckpointManager();
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.OnJumped -= HandlePlayerJumped;
                _controller.OnLanded -= HandlePlayerLanded;
            }

            if (_needleController != null)
            {
                _needleController.OnNeedleLaunched -= HandleNeedleLaunched;
                _needleController.OnThreadSevered -= HandleThreadSevered;
                _needleController.OnPlayerTetherAttached -= HandlePlayerTetherAttached;
            }

            UnsubscribeFromCheckpointManager();

            if (_aimNeedleObj != null) _aimNeedleObj.SetActive(false);
            if (_spriteRenderer != null) _spriteRenderer.DOKill();
            if (_visualHolder != null) _visualHolder.DOKill();
        }

        private void Start()
        {
            SubscribeToCheckpointManager();

            if (_visualHolder != null)
            {
                float dirX = _isFacingRight ? 1f : -1f;
                _visualHolder.localScale = new Vector3(dirX * _baseScale.x, _baseScale.y, 1f);
                _visualHolder.localRotation = Quaternion.identity;
                _visualHolder.localPosition = Vector3.zero;
            }

            if (_enableGameStartWakeUp && GetSpritesForState(VisualState.WakeUp)?.Length > 0)
            {
                if (_controller != null)
                {
                    _controller.TeleportTo(transform.position, _isFacingRight, snapToGround: true);
                    _controller.SetInputLocked(true);
                }

                CacheAndHideHintMovement();
                StartCoroutine(LimboWakeUpRoutine());
            }
            else
            {
                SetState(VisualState.Idle, forceRestart: true);
            }
        }

        private void SubscribeToCheckpointManager()
        {
            if (_isSubscribedToCheckpointManager) return;
            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.OnPlayerDied += HandlePlayerDied;
                CheckpointManager.Instance.OnPlayerRespawned += HandlePlayerRespawned;
                _isSubscribedToCheckpointManager = true;
            }
        }

        private void UnsubscribeFromCheckpointManager()
        {
            if (!_isSubscribedToCheckpointManager) return;
            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.OnPlayerDied -= HandlePlayerDied;
                CheckpointManager.Instance.OnPlayerRespawned -= HandlePlayerRespawned;
            }
            _isSubscribedToCheckpointManager = false;
        }

        private void HandlePlayerDied()
        {
            _isDead = true;
            _isWakingUp = false;
            _wakeUpHoldTimer = 0f;
            _onWakeUpCompletedCallback = null;
            _throwTimer = 0f;
            if (_aimNeedleObj != null) _aimNeedleObj.SetActive(false);
            if (_controller != null)
            {
                _controller.StopHorizontalMovement();
            }
            SetState(VisualState.Dead, forceRestart: true);
        }

        private void HandlePlayerRespawned()
        {
            StartWakeUpSequence(0f, null);
        }

        private void LateUpdate()
        {
            if (_controller == null) return;

            UpdateFacingDirection();
            UpdateVisualState();
            AnimateCurrentFrame();
            UpdateProceduralTilt();
            UpdateProceduralRecoil();
            UpdateSquashAndStretch();
            UpdateAimNeedleVisual();
        }

        #region Facing Direction

        private void UpdateFacingDirection()
        {
            if (_isDead) return;
            if (_controller != null && _controller.IsInputLocked) return;

            // 1. If aiming needle, face towards aim cursor
            if (_faceAimWhenPinning && _needleController != null && _needleController.IsAiming)
            {
                Vector3 aimPos = _needleController.GetAimWorldPosition();
                float diffX = aimPos.x - transform.position.x;
                if (Mathf.Abs(diffX) > 0.15f)
                {
                    _isFacingRight = diffX > 0f;
                }
                return;
            }

            // 2. Otherwise face towards player movement input directly
            float inputX = _controller.HorizontalInput;
            if (inputX > 0.05f)
            {
                _isFacingRight = true;
            }
            else if (inputX < -0.05f)
            {
                _isFacingRight = false;
            }
        }

        #endregion

        #region State Selection

        private void UpdateVisualState()
        {
            if (_isDead)
            {
                SetState(VisualState.Dead);
                return;
            }

            if (_throwTimer > 0f)
            {
                _isWakingUp = false;
                _throwTimer -= Time.deltaTime;
                SetState(VisualState.Throw);
                return;
            }

            if (_isWakingUp)
            {
                // When holding lying down (timer > 0 or in limbo routine), NEVER allow cancellation into Idle/Fall.
                // Only allow early cancellation once the stand-up animation is actively playing AND player gives explicit input.
                bool hasMoveInput = _controller != null && Mathf.Abs(_controller.HorizontalInput) > 0.2f;
                bool isAimingNow = _needleController != null && _needleController.IsAiming;
                bool canCancel = _wakeUpHoldTimer <= 0f && (hasMoveInput || isAimingNow);

                if (canCancel)
                {
                    _isWakingUp = false;
                    TriggerWakeUpCompleted();
                }
                else
                {
                    SetState(VisualState.WakeUp);
                    return;
                }
            }

            if (!_controller.IsGrounded)
            {
                // ท่า Jump จะคงอยู่เฉพาะเมื่อผู้เล่นกดกระโดดขึ้นมาจริงๆ (ถูกเริ่มจาก HandlePlayerJumped)
                // และจะค้างท่า Jump ไว้จนกว่าความเร็วสัมพัทธ์ในแนวดิ่งจะตกลงมาถึงจุดยอด Apex (_fallThreshold)
                if (_currentState == VisualState.Jump)
                {
                    float relVelY = _controller.RelativeVelocity.y;
                    if (relVelY <= _fallThreshold)
                    {
                        bool hasFallEntry = _fallSprites != null && _fallSprites.Length > 0;
                        SetState(hasFallEntry ? VisualState.Fall : VisualState.FallLoop);
                    }
                }
                else
                {
                    // กรณีเดินตกขอบพื้น ร่วงจากแท่น หรือลอยตัวบนแท่นที่กำลังยกขึ้น ให้เข้าท่า Fall/FallLoop (ห้ามเข้าท่า Jump เด็ดขาด)
                    if (_currentState != VisualState.Fall && _currentState != VisualState.FallLoop)
                    {
                        bool hasFallEntry = _fallSprites != null && _fallSprites.Length > 0;
                        SetState(hasFallEntry ? VisualState.Fall : VisualState.FallLoop);
                    }
                }
            }
            else
            {
                // เช็คจากทั้ง Input ของผู้เล่นและความเร็วสัมพัทธ์กับพื้นผิว
                // เพื่อไม่ให้เล่นอนิเมชันเดินถ้าไม่ได้กดปุ่มเดิน (เช่น ยืนอยู่บนแท่นเคลื่อนที่/กำลังถูกส่งแรงเฉื่อย)
                // และป้องกันการซอยเท้าอยู่กับที่เวลาเดินชนกำแพง
                bool hasInput = _controller != null && Mathf.Abs(_controller.HorizontalInput) > 0.05f;
                bool isMoving = hasInput && Mathf.Abs(_controller.RelativeVelocity.x) > 0.05f;
                bool isAiming = _needleController != null && _needleController.IsAiming;

                if (isAiming && _aimSprites != null && _aimSprites.Length > 0)
                {
                    SetState(VisualState.Aim);
                }
                else if (isMoving)
                {
                    bool shouldUseHaulWalk = _controller.IsHauling && _haulWalkSprites != null && _haulWalkSprites.Length > 0;
                    SetState(shouldUseHaulWalk ? VisualState.HaulWalk : VisualState.Walk);
                }
                else
                {
                    SetState(VisualState.Idle);
                }
            }
        }

        private void SetState(VisualState newState, bool forceRestart = false)
        {
            if (_currentState == newState && !forceRestart) return;

            _currentState = newState;
            _frameTimer = 0f;
            _currentFrame = 0;

            _currentClip = GetSpritesForState(newState);

            if (_currentClip != null && _currentClip.Length > 0 && _spriteRenderer != null)
            {
                _spriteRenderer.sprite = _currentClip[_currentFrame];
            }
        }

        private Sprite[] GetSpritesForState(VisualState state)
        {
            switch (state)
            {
                case VisualState.Idle:
                    return _idleSprites;
                case VisualState.Walk:
                    return _walkSprites != null && _walkSprites.Length > 0 ? _walkSprites : _idleSprites;
                case VisualState.HaulWalk:
                    return _haulWalkSprites != null && _haulWalkSprites.Length > 0
                        ? _haulWalkSprites
                        : (_walkSprites != null && _walkSprites.Length > 0 ? _walkSprites : _idleSprites);
                case VisualState.Jump:
                    return _jumpSprites != null && _jumpSprites.Length > 0 ? _jumpSprites : _idleSprites;
                case VisualState.Fall:
                    return _fallSprites != null && _fallSprites.Length > 0 ? _fallSprites : _idleSprites;
                case VisualState.FallLoop:
                    return _fallLoopSprites != null && _fallLoopSprites.Length > 0
                        ? _fallLoopSprites
                        : (_fallSprites != null && _fallSprites.Length > 0 ? _fallSprites : _idleSprites);
                case VisualState.Aim:
                    return _aimSprites != null && _aimSprites.Length > 0 ? _aimSprites : _idleSprites;
                case VisualState.Throw:
                    return _throwSprites != null && _throwSprites.Length > 0 ? _throwSprites : _idleSprites;
                case VisualState.Dead:
                    return _deadSprites != null && _deadSprites.Length > 0 ? _deadSprites : _idleSprites;
                case VisualState.WakeUp:
                    return _wakeUpSprites != null && _wakeUpSprites.Length > 0
                        ? _wakeUpSprites
                        : (_deadSprites != null && _deadSprites.Length > 0 ? _deadSprites : _idleSprites);
                default:
                    return _idleSprites;
            }
        }

        #endregion

        #region Frame Playback

        private void AnimateCurrentFrame()
        {
            if (_currentClip == null || _currentClip.Length == 0 || _spriteRenderer == null) return;

            float fps = GetFpsForState(_currentState);
            if (fps <= 0.01f) return;

            // Hold lying down on the ground during intro sequence before starting the stand-up animation
            if (_currentState == VisualState.WakeUp && _wakeUpHoldTimer > 0f)
            {
                _wakeUpHoldTimer -= Time.deltaTime;
                _currentFrame = 0;
                if (_spriteRenderer != null && _currentClip.Length > 0)
                {
                    _spriteRenderer.sprite = _currentClip[0];
                }
                return;
            }

            _frameTimer += Time.deltaTime;
            float frameDuration = 1f / fps;

            if (_frameTimer >= frameDuration)
            {
                _frameTimer -= frameDuration;
                int previousFrame = _currentFrame;

                if (_currentState == VisualState.Jump)
                {
                    if (_currentFrame < _currentClip.Length - 1)
                    {
                        _currentFrame++;
                    }
                }
                else if (_currentState == VisualState.Dead || _currentState == VisualState.Throw || _currentState == VisualState.Aim)
                {
                    if (_currentFrame < _currentClip.Length - 1)
                    {
                        _currentFrame++;
                    }
                }
                else if (_currentState == VisualState.WakeUp)
                {
                    if (_currentFrame < _currentClip.Length - 1)
                    {
                        _currentFrame++;
                    }
                    else
                    {
                        _isWakingUp = false;
                        SetState(VisualState.Idle, forceRestart: true);
                        TriggerWakeUpCompleted();
                        return;
                    }
                }
                else if (_currentState == VisualState.Fall)
                {
                    if (_currentFrame < _currentClip.Length - 1)
                    {
                        _currentFrame++;
                    }
                    else if (_fallLoopSprites != null && _fallLoopSprites.Length > 0)
                    {
                        SetState(VisualState.FallLoop);
                        return;
                    }
                }
                else if ((_currentState == VisualState.Walk || _currentState == VisualState.HaulWalk) && IsWalkingBackwards())
                {
                    // เล่นอนิเมชันเดินย้อนกลับเมื่อผู้เล่นก้าวถอยหลังขณะเล็งเข็มหรือลากของ
                    _currentFrame = (_currentFrame - 1 + _currentClip.Length) % _currentClip.Length;
                }
                else
                {
                    _currentFrame = (_currentFrame + 1) % _currentClip.Length;
                }

                if (_currentFrame != previousFrame)
                {
                    _spriteRenderer.sprite = _currentClip[_currentFrame];
                    CheckFootstepFrame(_currentFrame);
                }
            }
        }

        private bool IsWalkingBackwards()
        {
            float velX = _controller.Velocity.x;
            if (Mathf.Abs(velX) <= 0.1f) return false;

            bool movingRight = velX > 0f;
            return movingRight != _isFacingRight;
        }

        private void CheckFootstepFrame(int frame)
        {
            if (!_playFootstepAudio || string.IsNullOrEmpty(_footstepSFXName)) return;
            if (_controller != null && !_controller.IsGrounded) return;
            if (AudioManager.Instance == null) return;

            bool isStepFrame = false;

            if (_currentState == VisualState.Walk && _walkFootstepFrames != null)
            {
                for (int i = 0; i < _walkFootstepFrames.Length; i++)
                {
                    if (_walkFootstepFrames[i] == frame)
                    {
                        isStepFrame = true;
                        break;
                    }
                }
            }
            else if (_currentState == VisualState.HaulWalk && _haulWalkFootstepFrames != null)
            {
                for (int i = 0; i < _haulWalkFootstepFrames.Length; i++)
                {
                    if (_haulWalkFootstepFrames[i] == frame)
                    {
                        isStepFrame = true;
                        break;
                    }
                }
            }

            if (isStepFrame)
            {
                AudioManager.Instance.PlaySFX(_footstepSFXName, _footstepVolume);
            }
        }

        private float GetFpsForState(VisualState state)
        {
            switch (state)
            {
                case VisualState.Idle:
                    return _idleFps;
                case VisualState.Walk:
                    return _controller != null && _controller.IsHauling
                        ? _walkFps * _haulingWalkFpsMultiplier
                        : _walkFps;
                case VisualState.HaulWalk:
                    return _haulWalkFps;
                case VisualState.Jump:
                    return _jumpFps;
                case VisualState.Fall:
                    return _fallFps;
                case VisualState.FallLoop:
                    return _fallLoopFps;
                case VisualState.Aim:
                    return _aimFps;
                case VisualState.Throw:
                    return _throwFps;
                case VisualState.Dead:
                    return _deadFps;
                case VisualState.WakeUp:
                    return _wakeUpFps;
                default:
                    return 10f;
            }
        }

        #endregion

        #region Procedural Juice (Tilt, Recoil, Stretch)

        private void UpdateProceduralTilt()
        {
            if (_visualHolder == null) return;

            if (_isDead)
            {
                _currentTiltAngle = Mathf.MoveTowards(_currentTiltAngle, 0f, _tiltSpeed * Time.deltaTime);
                _visualHolder.localRotation = Quaternion.Euler(0f, 0f, _currentTiltAngle);
                return;
            }

            float targetAngle = 0f;
            float facingSign = _isFacingRight ? 1f : -1f;

            // 1. Hauling state takes highest priority
            if (_enableHaulTilt && _controller != null && _controller.IsHauling)
            {
                float velX = _controller.Velocity.x;
                bool isMovingHorizontally = Mathf.Abs(velX) > 0.1f;

                if (isMovingHorizontally)
                {
                    // In Unity Transform hierarchy, localRotation is evaluated in parent space (T * R * S).
                    // Facing right: negative Z (-13°) tilts forward (right).
                    // Facing left: positive Z (+13°) tilts forward (left).
                    bool movingForward = (velX > 0f) == _isFacingRight;
                    targetAngle = movingForward
                        ? facingSign * _haulForwardTiltAngle
                        : facingSign * _haulBraceTiltAngle;
                }
                else
                {
                    // When stopped, check if the tethered object is behind the player
                    bool isTetherBehind = false;
                    if (_needleController != null && _needleController.TetheredStitchable != null)
                    {
                        float objectDirX = _needleController.TetheredStitchable.CurrentPosition.x - transform.position.x;
                        isTetherBehind = _isFacingRight ? (objectDirX < -0.1f) : (objectDirX > 0.1f);
                    }

                    if (isTetherBehind)
                    {
                        targetAngle = facingSign * _haulBraceTiltAngle;
                    }
                }
            }
            // 2. Aim pitch lean when aiming needle (if not hauling)
            else if (_enableAimLean && _needleController != null && _needleController.IsAiming)
            {
                Vector3 aimPos = _needleController.GetAimWorldPosition();
                Vector2 aimDir = (aimPos - transform.position);
                if (aimDir.sqrMagnitude > 0.01f)
                {
                    aimDir.Normalize();
                    // Facing right: aiming up tilts chest back (+Z).
                    // Facing left: aiming up tilts chest back (-Z).
                    targetAngle = facingSign * (aimDir.y * _maxAimTiltAngle);
                }
            }

            _currentTiltAngle = Mathf.MoveTowards(_currentTiltAngle, targetAngle, _tiltSpeed * Time.deltaTime);
            _visualHolder.localRotation = Quaternion.Euler(0f, 0f, _currentTiltAngle);
        }

        private void HandleNeedleLaunched(Vector2 aimDir)
        {
            if (_isDead) return;

            _throwTimer = _throwDuration;
            SetState(VisualState.Throw, forceRestart: true);

            if (_controller != null)
            {
                _controller.ApplyThrowSlowdown(0.15f, 0.25f);
            }

            SpawnThrowRipple(aimDir);

            if (!_enableThrowRecoil) return;

            if (aimDir.sqrMagnitude > 0.001f)
            {
                aimDir.Normalize();
                // Kick backward along opposite aim trajectory
                _recoilOffset = -(Vector3)(aimDir * _throwRecoilDistance);
            }

            if (_enableSquashAndStretch)
            {
                _scaleModifier = _throwRecoilSquash;
            }
        }

        private void SpawnThrowRipple(Vector2 aimDir)
        {
            if (_rippleEffectPrefab == null) return;

            Vector3 spawnPos = transform.position;
            float facingSign = _isFacingRight ? 1f : -1f;
            Vector3 offset = new Vector3(_throwFxOffset.x * facingSign, _throwFxOffset.y, 0f);
            spawnPos += offset;

            float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
            Quaternion rotation = Quaternion.Euler(0f, 0f, angle);

            GameObject fxObj = Instantiate(_rippleEffectPrefab, spawnPos, rotation);
            if (fxObj.TryGetComponent(out SpriteRenderer fxSr) && _spriteRenderer != null)
            {
                fxSr.sortingLayerID = _spriteRenderer.sortingLayerID;
                fxSr.sortingOrder = _spriteRenderer.sortingOrder + 1;
            }
        }

        private void EnsureAimNeedleVisual()
        {
            if (_aimNeedleObj == null)
            {
                Transform parentRoot = transform.parent != null ? transform.parent : transform;
                Transform existing = parentRoot.Find("AimNeedle");
                if (existing == null)
                {
                    existing = transform.Find("AimNeedle");
                }

                if (existing != null)
                {
                    _aimNeedleObj = existing.gameObject;
                    _aimNeedleRenderer = _aimNeedleObj.GetComponent<SpriteRenderer>();
                }
            }

            if (_aimNeedleObj != null)
            {
                _aimNeedleObj.SetActive(false);
            }
        }

        private void UpdateAimNeedleVisual()
        {
            if (_aimNeedleObj == null) return;

            bool shouldShow = !_isDead
                              && _currentState == VisualState.Aim
                              && _throwTimer <= 0f
                              && _needleController != null
                              && _needleController.IsAiming
                              && !_needleController.IsAnyNeedleInFlight;

            if (_aimNeedleObj.activeSelf != shouldShow)
            {
                _aimNeedleObj.SetActive(shouldShow);
            }

            if (!shouldShow) return;

            if (_aimNeedleRenderer != null && _spriteRenderer != null)
            {
                _aimNeedleRenderer.sortingLayerID = _spriteRenderer.sortingLayerID;
                _aimNeedleRenderer.sortingOrder = _spriteRenderer.sortingOrder + 1;
                if (_flipNeedleWhenAimingLeft)
                {
                    _aimNeedleRenderer.flipY = !_isFacingRight;
                }
            }

            Vector3 handWorldPos = _visualHolder != null
                ? _visualHolder.TransformPoint(_aimNeedleHandOffset)
                : transform.position + _aimNeedleHandOffset;

            _aimNeedleObj.transform.position = handWorldPos;

            Vector3 aimWorldPos = _needleController.GetAimWorldPosition();
            Vector2 aimDir = (Vector2)aimWorldPos - (Vector2)handWorldPos;

            if (aimDir.sqrMagnitude > 0.0001f)
            {
                aimDir.Normalize();
                float angle = Mathf.Atan2(aimDir.y, aimDir.x) * Mathf.Rad2Deg;
                _aimNeedleObj.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            }
        }

        public bool TryGetNeedleTipPosition(out Vector2 tipPosition)
        {
            if (_aimNeedleObj != null && (_aimNeedleObj.activeSelf || (_needleController != null && _needleController.IsAiming && !_needleController.IsAnyNeedleInFlight)))
            {
                Vector3 handWorldPos = _visualHolder != null
                    ? _visualHolder.TransformPoint(_aimNeedleHandOffset)
                    : transform.position + _aimNeedleHandOffset;

                Vector3 aimWorldPos = _needleController != null ? _needleController.GetAimWorldPosition() : handWorldPos + Vector3.right;
                Vector2 aimDir = (Vector2)aimWorldPos - (Vector2)handWorldPos;
                if (aimDir.sqrMagnitude > 0.0001f)
                {
                    aimDir.Normalize();
                }
                else
                {
                    aimDir = _isFacingRight ? Vector2.right : Vector2.left;
                }

                // 25.3 pixels from pivot at 32 PPU = ~0.79m along needle axis
                tipPosition = (Vector2)handWorldPos + aimDir * 0.79f;
                return true;
            }

            tipPosition = Vector2.zero;
            return false;
        }

        public bool TryGetNeedleHandPosition(out Vector2 handPosition)
        {
            if (_aimNeedleObj != null)
            {
                handPosition = _visualHolder != null
                    ? _visualHolder.TransformPoint(_aimNeedleHandOffset)
                    : transform.position + _aimNeedleHandOffset;
                return true;
            }

            handPosition = transform.position;
            return false;
        }

        private void UpdateProceduralRecoil()
        {
            if (_visualHolder == null) return;

            if (_recoilOffset.sqrMagnitude > 0.00001f)
            {
                _recoilOffset = Vector3.MoveTowards(_recoilOffset, Vector3.zero, _recoilRecoverySpeed * Time.deltaTime);
            }
            else
            {
                _recoilOffset = Vector3.zero;
            }

            _visualHolder.localPosition = _recoilOffset;
        }

        private void HandleThreadSevered()
        {
            if (!_enableSnapFx || _spriteRenderer == null) return;

            _spriteRenderer.DOKill();
            _spriteRenderer.color = _snapFlashColor;
            _spriteRenderer.DOColor(Color.white, 0.12f).SetEase(Ease.OutQuad);

            if (_visualHolder != null)
            {
                _visualHolder.DOPunchScale(new Vector3(0.08f, -0.08f, 0f), 0.15f, 1, 0.5f);
            }
        }

        private void HandlePlayerTetherAttached()
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.DOKill();
                _spriteRenderer.color = Color.white;
                _spriteRenderer.DOColor(new Color(0.6f, 1f, 1f), 0.09f).SetLoops(2, LoopType.Yoyo);
            }

            if (_visualHolder != null)
            {
                _visualHolder.DOPunchScale(new Vector3(-0.06f, 0.06f, 0f), 0.18f, 2, 0.5f);
            }
        }

        #endregion

        #region Squash & Stretch

        private void HandlePlayerJumped()
        {
            SetState(VisualState.Jump, forceRestart: true);

            if (!_enableSquashAndStretch) return;
            _scaleModifier = _jumpStretch;
        }

        private void HandlePlayerLanded(float impactSpeed)
        {
            if (!_enableSquashAndStretch) return;

            float trauma = Mathf.Clamp01(impactSpeed / 12f);
            float squashY = Mathf.Lerp(1.0f, _landSquash.y, trauma);
            float squashX = Mathf.Lerp(1.0f, _landSquash.x, trauma);

            _scaleModifier = new Vector3(squashX, squashY, 1f);
        }

        private void UpdateSquashAndStretch()
        {
            if (_visualHolder == null) return;

            if (_enableSquashAndStretch)
            {
                // Dynamic vertical stretch while falling fast
                if (_enableAirStretch && _controller != null && !_controller.IsGrounded && _controller.Velocity.y < _fallStretchThreshold)
                {
                    float t = Mathf.Clamp01((-_controller.Velocity.y - Mathf.Abs(_fallStretchThreshold)) / 12f);
                    float targetStretchY = Mathf.Lerp(1f, _maxFallStretchY, t);
                    float targetSquashX = Mathf.Lerp(1f, 1f - (t * 0.10f), t);

                    Vector3 airTarget = new Vector3(targetSquashX, targetStretchY, 1f);
                    _scaleModifier = Vector3.Lerp(_scaleModifier, airTarget, Time.deltaTime * 10f);
                }
                else
                {
                    _scaleModifier = Vector3.MoveTowards(_scaleModifier, Vector3.one, _squashRecoverySpeed * Time.deltaTime);
                }
            }
            else
            {
                _scaleModifier = Vector3.one;
            }

            float dirX = _isFacingRight ? 1f : -1f;
            _visualHolder.localScale = new Vector3(
                dirX * _baseScale.x * _scaleModifier.x,
                _baseScale.y * _scaleModifier.y,
                1f
            );
        }

        #endregion

        #region Game Start & Wake-Up Sequence

        public void StartWakeUpSequence(float holdLyingDuration = 0f, System.Action onComplete = null)
        {
            _onWakeUpCompletedCallback = onComplete;
            _wakeUpHoldTimer = holdLyingDuration;
            _isWakingUp = true;
            _isDead = false;
            _throwTimer = 0f;
            _scaleModifier = Vector3.one;
            _recoilOffset = Vector3.zero;
            _currentTiltAngle = 0f;

            if (_visualHolder != null)
            {
                float dirX = _isFacingRight ? 1f : -1f;
                _visualHolder.localScale = new Vector3(dirX * _baseScale.x, _baseScale.y, 1f);
                _visualHolder.localRotation = Quaternion.identity;
                _visualHolder.localPosition = Vector3.zero;
            }
            if (_aimNeedleObj != null) _aimNeedleObj.SetActive(false);

            Sprite[] wakeClip = GetSpritesForState(VisualState.WakeUp);
            if (wakeClip != null && wakeClip.Length > 0)
            {
                SetState(VisualState.WakeUp, forceRestart: true);
                if (_spriteRenderer != null)
                {
                    _spriteRenderer.sprite = wakeClip[0];
                }
            }
            else
            {
                _isWakingUp = false;
                SetState(VisualState.Idle, forceRestart: true);
                TriggerWakeUpCompleted();
            }
        }

        private void TriggerWakeUpCompleted()
        {
            var callback = _onWakeUpCompletedCallback;
            _onWakeUpCompletedCallback = null;
            callback?.Invoke();
            OnWakeUpCompleted?.Invoke();
        }

        private void EnsureHintMovementReference()
        {
            if (_hintMovement != null) return;

            GameObject tut = GameObject.Find("Tutorials Text");
            if (tut != null)
            {
                Transform child = tut.transform.Find("Hint_Movement");
                if (child != null)
                {
                    _hintMovement = child.gameObject;
                    return;
                }
            }

            var allTmps = Resources.FindObjectsOfTypeAll<TextMeshPro>();
            for (int i = 0; i < allTmps.Length; i++)
            {
                if (allTmps[i] != null && allTmps[i].gameObject != null && allTmps[i].gameObject.scene == gameObject.scene)
                {
                    if (allTmps[i].gameObject.name == "Hint_Movement")
                    {
                        _hintMovement = allTmps[i].gameObject;
                        return;
                    }
                }
            }
        }

        private void CacheAndHideHintMovement()
        {
            EnsureHintMovementReference();
            if (_hintMovement != null)
            {
                if (!_hasCachedHintColor && _hintMovement.TryGetComponent<TextMeshPro>(out var tmp))
                {
                    _hintMovementTargetColor = tmp.color;
                    _hasCachedHintColor = true;
                    Color hidden = _hintMovementTargetColor;
                    hidden.a = 0f;
                    tmp.color = hidden;
                }
                _hintMovement.SetActive(false);
            }
        }

        private IEnumerator LimboWakeUpRoutine()
        {
            // 1. Immediately flag waking up and lock hold timer to float.PositiveInfinity
            // This prevents UpdateVisualState / AnimateCurrentFrame from touching or swapping state!
            _isWakingUp = true;
            _wakeUpHoldTimer = float.PositiveInfinity;
            _scaleModifier = Vector3.one;
            _recoilOffset = Vector3.zero;
            _currentTiltAngle = 0f;

            if (_controller != null)
            {
                _controller.SetInputLocked(true);
            }
            if (_aimNeedleObj != null) _aimNeedleObj.SetActive(false);

            Sprite[] wakeClip = GetSpritesForState(VisualState.WakeUp);
            SetState(VisualState.WakeUp, forceRestart: true);
            _currentFrame = 0;
            if (_spriteRenderer != null && wakeClip != null && wakeClip.Length > 0)
            {
                _spriteRenderer.sprite = wakeClip[0];
            }

            // 2. Fade in screen smoothly from pitch black (Limbo style)
            if (ScreenFader.Instance != null)
            {
                ScreenFader.Instance.SetAlpha(1f);

                bool fadeFinished = false;
                ScreenFader.Instance.FadeIn(2.2f, () => fadeFinished = true);

                while (!fadeFinished)
                {
                    yield return null;
                }
            }

            // 3. Pause while lying peacefully in the clear world
            float pauseAfterFade = _startLieDownDuration > 0f ? _startLieDownDuration : 1.2f;
            yield return new WaitForSeconds(pauseAfterFade);

            // 4. Release hold timer to 0 so WakeUp animation smoothly plays to standing position
            _wakeUpHoldTimer = 0f;
            _onWakeUpCompletedCallback = OnGameStartWakeUpCompleted;
        }

        private void OnGameStartWakeUpCompleted()
        {
            if (_controller != null)
            {
                _controller.SetInputLocked(false);
            }

            EnsureHintMovementReference();
            if (_hintMovement != null)
            {
                _hintMovement.SetActive(true);
                if (_hintMovement.TryGetComponent<TextMeshPro>(out var tmp))
                {
                    float targetAlpha = _hasCachedHintColor ? _hintMovementTargetColor.a : tmp.color.a;
                    if (targetAlpha <= 0.01f) targetAlpha = 0.65f;

                    if (_hintFadeInDuration > 0f)
                    {
                        Color c = tmp.color;
                        c.a = 0f;
                        tmp.color = c;
                        tmp.DOKill();
                        tmp.DOFade(targetAlpha, _hintFadeInDuration).SetEase(Ease.OutQuad);
                    }
                    else
                    {
                        Color c = tmp.color;
                        c.a = targetAlpha;
                        tmp.color = c;
                    }
                }
            }
        }

        #endregion
    }
}