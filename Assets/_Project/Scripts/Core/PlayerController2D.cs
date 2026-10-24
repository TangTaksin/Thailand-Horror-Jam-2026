using UnityEngine;
using UnityEngine.InputSystem;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Core
{
    /// <summary>
    /// Responsive 2D platformer character controller with jump buffering, coyote time, and variable jump cut.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class PlayerController2D : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 6.5f;
        [SerializeField] private float _acceleration = 50f;
        [SerializeField] private float _deceleration = 60f;

        [Header("Hauling / Burden Settings")]
        [Tooltip("Speed multiplier applied to player horizontal velocity while tethered and hauling an object.")]
        [SerializeField] private float _haulingSpeedMultiplier = 0.58f;

        [Header("Jump Settings")]
        [SerializeField] private float _jumpForce = 12f;
        [SerializeField] private float _coyoteTime = 0.15f;
        [SerializeField] private float _jumpBufferTime = 0.15f;
        [SerializeField] private float _fallMultiplier = 2.5f;
        [SerializeField] private float _lowJumpMultiplier = 2f;
        [SerializeField] private float _maxFallSpeed = 20f;

        [Header("Ground Check")]
        [SerializeField] private Transform _groundCheckPoint;
        [SerializeField] private Vector2 _groundCheckSize = new Vector2(0.6f, 0.1f);
        [SerializeField] private LayerMask _groundLayer;

        private Rigidbody2D _rb;
        private Collider2D _col;
        private readonly Collider2D[] _groundHitBuffer = new Collider2D[2]; // เผื่อกรอง Collider ตัวเองออก
        private ContactFilter2D _groundFilter;

        private Collider2D _currentGroundCollider;
        private StitchableObject _currentStitchable;

        private float _horizontalInput;
        private bool _isGrounded;
        private float _coyoteTimeCounter;
        private float _jumpBufferCounter;
        private bool _isJumpButtonHeld;
        private bool _isJumpCut;
        private bool _jumpRequested;
        private float _lastVerticalVelocity;
        private float _footstepTimer;
        private bool _isHauling;

        public bool IsGrounded => _isGrounded;
        public bool IsHauling => _isHauling;
        public float HorizontalInput => _horizontalInput;
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;

        public event System.Action<float> OnLanded;
        public event System.Action OnJumped;

        public void SetHauling(bool isHauling)
        {
            _isHauling = isHauling;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<Collider2D>();

            _rb.freezeRotation = true;
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            if (_groundLayer.value == 0)
            {
                _groundLayer = LayerMask.GetMask("Ground", "Stitchable", "Default");
            }

            _groundFilter = new ContactFilter2D();
            _groundFilter.SetLayerMask(_groundLayer);
            _groundFilter.useLayerMask = true;
            _groundFilter.useTriggers = false;
        }

        private void Update()
        {
            ReadMovementInput();
            HandleJumpTimers();
        }

        private void FixedUpdate()
        {
            CheckGround();

            if (_jumpRequested)
            {
                ExecuteJump();
                _jumpRequested = false;
            }

            ApplyHorizontalMovement();
            ApplyJumpPhysics();

            // เก็บค่าความเร็วแกน Y ไว้ท้ายสุด เพื่อใช้วัดแรงกระแทกก่อนที่ Physics Step รอบถัดไปจะปรับเป็น 0
            _lastVerticalVelocity = _rb.linearVelocity.y;
        }

        private void ReadMovementInput()
        {
            _horizontalInput = 0f;
            bool jumpPressed = false;
            bool jumpReleased = false;
            bool jumpHeld = false;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed)
                    _horizontalInput -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed)
                    _horizontalInput += 1f;

                jumpPressed |= Keyboard.current.spaceKey.wasPressedThisFrame
                            || Keyboard.current.wKey.wasPressedThisFrame
                            || Keyboard.current.upArrowKey.wasPressedThisFrame;

                jumpReleased |= Keyboard.current.spaceKey.wasReleasedThisFrame
                             || Keyboard.current.wKey.wasReleasedThisFrame
                             || Keyboard.current.upArrowKey.wasReleasedThisFrame;

                jumpHeld |= Keyboard.current.spaceKey.isPressed
                         || Keyboard.current.wKey.isPressed
                         || Keyboard.current.upArrowKey.isPressed;
            }

            if (Gamepad.current != null)
            {
                float stickX = Gamepad.current.leftStick.x.ReadValue();
                if (Mathf.Abs(stickX) > 0.2f)
                {
                    _horizontalInput = Mathf.Sign(stickX);
                }

                jumpPressed |= Gamepad.current.buttonSouth.wasPressedThisFrame;
                jumpReleased |= Gamepad.current.buttonSouth.wasReleasedThisFrame;
                jumpHeld |= Gamepad.current.buttonSouth.isPressed;
            }

            _isJumpButtonHeld = jumpHeld;

            if (jumpPressed)
            {
                _jumpBufferCounter = _jumpBufferTime;
            }

            if (jumpReleased)
            {
                _isJumpCut = true;
            }
        }

        private void HandleJumpTimers()
        {
            if (_isGrounded)
            {
                _coyoteTimeCounter = _coyoteTime;
            }
            else
            {
                _coyoteTimeCounter -= Time.deltaTime;
            }

            _jumpBufferCounter -= Time.deltaTime;

            if (_jumpBufferCounter > 0f && _coyoteTimeCounter > 0f && !_isHauling)
            {
                _jumpRequested = true;
                _jumpBufferCounter = 0f;
                _coyoteTimeCounter = 0f;
            }
        }

        private void ExecuteJump()
        {
            float extraBounce = 0f;
            if (_currentStitchable != null)
            {
                _currentStitchable.OnPlayerJumpedOff(transform.position);

                if (_currentStitchable.CurrentVelocity.y > 0.25f)
                {
                    extraBounce = Mathf.Min(_currentStitchable.CurrentVelocity.y * 0.85f, 6.0f);
                    CameraController2D.Instance?.Punch(Vector2.up, 0.035f);
                }
            }

            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, _jumpForce + extraBounce);
            _isGrounded = false;
            // ถ้าระหว่างติด Jump Buffer ผู้เล่นปล่อยปุ่มกระโดดไปแล้ว ให้ตัดความสูงกระโดดทันที (Short Hop)
            _isJumpCut = !_isJumpButtonHeld;
            OnJumped?.Invoke();
        }

        private void ApplyHorizontalMovement()
        {
            float speed = _isHauling ? _moveSpeed * _haulingSpeedMultiplier : _moveSpeed;
            float targetSpeed = _horizontalInput * speed;
            float accelRate = Mathf.Abs(targetSpeed) > 0.01f ? _acceleration : _deceleration;
            float newVelX = Mathf.MoveTowards(_rb.linearVelocity.x, targetSpeed, accelRate * Time.fixedDeltaTime);

            _rb.linearVelocity = new Vector2(newVelX, _rb.linearVelocity.y);
        }

        private void ApplyJumpPhysics()
        {
            float velY = _rb.linearVelocity.y;

            if (velY < 0f)
            {
                velY += Physics2D.gravity.y * (_fallMultiplier - 1f) * Time.fixedDeltaTime;
                velY = Mathf.Max(velY, -_maxFallSpeed);
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, velY);
            }
            else if (velY > 0f && _isJumpCut)
            {
                velY += Physics2D.gravity.y * (_lowJumpMultiplier - 1f) * Time.fixedDeltaTime;
                _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, velY);
            }
        }

        private void CheckGround()
        {
            Vector2 checkPos = _groundCheckPoint != null
                ? (Vector2)_groundCheckPoint.position
                : (Vector2)transform.position + Vector2.down * _col.bounds.extents.y;

            bool wasGrounded = _isGrounded;
            int hitCount = Physics2D.OverlapBox(checkPos, _groundCheckSize, 0f, _groundFilter, _groundHitBuffer);

            Collider2D detectedGround = null;
            for (int i = 0; i < hitCount; i++)
            {
                if (_groundHitBuffer[i] != _col) // ป้องกันการตรวจจับ Collider ของตัวเอง
                {
                    detectedGround = _groundHitBuffer[i];
                    break;
                }
            }

            UpdateCachedGround(detectedGround);

            // นับว่าอยู่บนพื้นเฉพาะเมื่อไม่ได้กำลังพุ่งตัวขึ้นจากการกระโดด
            _isGrounded = detectedGround != null && _rb.linearVelocity.y <= 0.1f;

            if (!wasGrounded && _isGrounded)
            {
                HandleLandingImpact();
            }

            if (_isGrounded && Mathf.Abs(_rb.linearVelocity.x) > 0.1f)
            {
                HandleWalkingVibration();
            }
        }

        private void UpdateCachedGround(Collider2D newGround)
        {
            if (_currentGroundCollider == newGround) return;

            _currentGroundCollider = newGround;
            _currentStitchable = newGround != null ? newGround.GetComponentInParent<StitchableObject>() : null;
        }

        private void HandleLandingImpact()
        {
            float impactSpeed = Mathf.Abs(_lastVerticalVelocity);
            OnLanded?.Invoke(impactSpeed);

            if (impactSpeed > 1.8f)
            {
                if (_currentStitchable != null)
                {
                    _currentStitchable.OnPlayerLanded(impactSpeed, transform.position);
                }

                if (impactSpeed > 7.5f)
                {
                    CameraController2D.Instance?.Shake(0.08f, 0.08f);
                }
            }
        }

        private void HandleWalkingVibration()
        {
            _footstepTimer -= Time.fixedDeltaTime;
            if (_footstepTimer <= 0f)
            {
                _footstepTimer = _isHauling ? 0.42f : 0.28f;
                if (_currentStitchable != null)
                {
                    _currentStitchable.OnPlayerWalkingStep(transform.position);
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Collider2D col = _col != null ? _col : GetComponent<Collider2D>();
            float fallbackOffset = col != null ? col.bounds.extents.y : 0.5f;

            Vector2 checkPos = _groundCheckPoint != null
                ? (Vector2)_groundCheckPoint.position
                : (Vector2)transform.position + Vector2.down * fallbackOffset;
            Gizmos.DrawWireCube(checkPos, _groundCheckSize);
        }
    }
}