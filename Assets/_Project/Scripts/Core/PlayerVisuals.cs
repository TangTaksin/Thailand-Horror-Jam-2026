using UnityEngine;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Core
{
    public sealed class PlayerVisuals : MonoBehaviour
    {
        public enum VisualState
        {
            Idle,
            Walk,
            Jump,
            Fall,
            FallLoop
        }

        [Header("References")]
        [SerializeField] private PlayerController2D _controller;
        [SerializeField] private NeedleController _needleController;
        [SerializeField] private Transform _visualHolder;
        [SerializeField] private SpriteRenderer _spriteRenderer;

        [Header("Sprite Clips")]
        [SerializeField] private Sprite[] _idleSprites;
        [SerializeField] private Sprite[] _walkSprites;
        [SerializeField] private Sprite[] _jumpSprites;
        [SerializeField] private Sprite[] _fallSprites;
        [SerializeField] private Sprite[] _fallLoopSprites;

        [Header("Frame Rates (FPS)")]
        [SerializeField] private float _idleFps = 8f;
        [SerializeField] private float _walkFps = 10f;
        [SerializeField] private float _haulingWalkFpsMultiplier = 0.6f;
        [SerializeField] private float _jumpFps = 16f;
        [SerializeField] private float _fallFps = 12f;
        [SerializeField] private float _fallLoopFps = 10f;

        [Header("Airborne Thresholds")]
        [Tooltip("Downward vertical velocity required to transition from Jump/Apex into Fall.")]
        [SerializeField] private float _fallThreshold = -2.2f;

        [Header("Facing Settings")]
        [Tooltip("When true, faces towards mouse aim while pinning needle threads.")]
        [SerializeField] private bool _faceAimWhenPinning = true;
        [SerializeField] private bool _startFacingRight = true;

        [Header("Squash & Stretch (Juice)")]
        [SerializeField] private bool _enableSquashAndStretch = true;
        [SerializeField] private Vector3 _jumpStretch = new Vector3(0.86f, 1.18f, 1f);
        [SerializeField] private Vector3 _landSquash = new Vector3(1.22f, 0.80f, 1f);
        [SerializeField] private float _squashRecoverySpeed = 10f;

        private VisualState _currentState = VisualState.Idle;
        private Sprite[] _currentClip;
        private int _currentFrame;
        private float _frameTimer;
        private bool _isFacingRight = true;
        private Vector3 _scaleModifier = Vector3.one;
        private Vector3 _baseScale = Vector3.one;

        public VisualState CurrentState => _currentState;
        public bool IsFacingRight => _isFacingRight;

        private void Awake()
        {
            if (_controller == null)
            {
                _controller = GetComponentInParent<PlayerController2D>();
            }

            if (_needleController == null)
            {
                _needleController = GetComponentInParent<NeedleController>();
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
        }

        private void OnEnable()
        {
            if (_controller != null)
            {
                _controller.OnJumped += HandlePlayerJumped;
                _controller.OnLanded += HandlePlayerLanded;
            }
        }

        private void OnDisable()
        {
            if (_controller != null)
            {
                _controller.OnJumped -= HandlePlayerJumped;
                _controller.OnLanded -= HandlePlayerLanded;
            }
        }

        private void Start()
        {
            if (_visualHolder != null)
            {
                float dirX = _isFacingRight ? 1f : -1f;
                _visualHolder.localScale = new Vector3(dirX * _baseScale.x, _baseScale.y, 1f);
            }
            SetState(VisualState.Idle, forceRestart: true);
        }

        private void LateUpdate()
        {
            if (_controller == null) return;

            UpdateFacingDirection();
            UpdateVisualState();
            AnimateCurrentFrame();
            UpdateSquashAndStretch();
        }

        #region Facing Direction

        private void UpdateFacingDirection()
        {
            // 1. If aiming needle, face towards aim cursor
            if (_faceAimWhenPinning && _needleController != null && _needleController.IsPinning)
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
            if (!_controller.IsGrounded)
            {
                float velY = _controller.Velocity.y;

                // เข้าสู่ท่า Jump เมื่อพุ่งตัวขึ้น หรือค้างท่า Jump ไว้จนกว่าความเร็วตกจะเลยค่า _fallThreshold (Apex)
                bool isRisingOrAtJumpApex = velY > 0.1f || (_currentState == VisualState.Jump && velY > _fallThreshold);

                if (isRisingOrAtJumpApex)
                {
                    SetState(VisualState.Jump);
                }
                else
                {
                    // กรณีเดินตกขอบพื้น (ไม่ได้มาจากท่า Jump) หรือร่วงผ่านจุด Apex มาแล้ว ให้เข้าสู่ท่า Fall ทันที
                    if (_currentState != VisualState.Fall && _currentState != VisualState.FallLoop)
                    {
                        bool hasFallEntry = _fallSprites != null && _fallSprites.Length > 0;
                        SetState(hasFallEntry ? VisualState.Fall : VisualState.FallLoop);
                    }
                }
            }
            else
            {
                // เช็คจากความเร็วจริงเพื่อป้องกันอาการซอยเท้าอยู่กับที่เวลาเดินชนกำแพง
                bool isMoving = Mathf.Abs(_controller.Velocity.x) > 0.1f;
                SetState(isMoving ? VisualState.Walk : VisualState.Idle);
            }
        }

        private void SetState(VisualState newState, bool forceRestart = false)
        {
            if (_currentState == newState && !forceRestart) return;

            _currentState = newState;
            _currentFrame = 0;
            _frameTimer = 0f;

            _currentClip = GetSpritesForState(newState);

            if (_currentClip != null && _currentClip.Length > 0 && _spriteRenderer != null)
            {
                _spriteRenderer.sprite = _currentClip[0];
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
                case VisualState.Jump:
                    return _jumpSprites != null && _jumpSprites.Length > 0 ? _jumpSprites : _idleSprites;
                case VisualState.Fall:
                    return _fallSprites != null && _fallSprites.Length > 0 ? _fallSprites : _idleSprites;
                case VisualState.FallLoop:
                    return _fallLoopSprites != null && _fallLoopSprites.Length > 0
                        ? _fallLoopSprites
                        : (_fallSprites != null && _fallSprites.Length > 0 ? _fallSprites : _idleSprites);
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
                else if (_currentState == VisualState.Walk && IsWalkingBackwards())
                {
                    // เล่นอนิเมชันเดินย้อนกลับเมื่อผู้เล่นก้าวถอยหลังขณะเล็งเข็ม
                    _currentFrame = (_currentFrame - 1 + _currentClip.Length) % _currentClip.Length;
                }
                else
                {
                    _currentFrame = (_currentFrame + 1) % _currentClip.Length;
                }

                if (_currentFrame != previousFrame)
                {
                    _spriteRenderer.sprite = _currentClip[_currentFrame];
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
                case VisualState.Jump:
                    return _jumpFps;
                case VisualState.Fall:
                    return _fallFps;
                case VisualState.FallLoop:
                    return _fallLoopFps;
                default:
                    return 10f;
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
                _scaleModifier = Vector3.MoveTowards(_scaleModifier, Vector3.one, _squashRecoverySpeed * Time.deltaTime);
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
    }
}