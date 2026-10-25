using UnityEngine;
using UnityEngine.InputSystem;
using ThailandHorrorJam.Systems.Needle;
using ThailandHorrorJam.Systems.Gameplay;

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
        [Tooltip("Speed multiplier applied to player horizontal velocity while aiming/pinning needle (ย่องเล็ง).")]
        [SerializeField] private float _aimingSpeedMultiplier = 0.38f;

        [Header("Jump Settings")]
        [SerializeField] private float _jumpForce = 12f;
        [SerializeField] private float _coyoteTime = 0.15f;
        [SerializeField] private float _jumpBufferTime = 0.15f;
        [SerializeField] private float _fallMultiplier = 2.5f;
        [SerializeField] private float _lowJumpMultiplier = 2f;
        [SerializeField] private float _maxFallSpeed = 20f;

        [Header("Ground Check")]
        [SerializeField] private Transform _groundCheckPoint;
        [SerializeField] private Vector2 _groundCheckSize = new Vector2(0.6f, 0.14f);
        [SerializeField] private LayerMask _groundLayer;

        [Header("Ledge & Corner Assistance (Limbo Style)")]
        [Tooltip("เปิดใช้งานระบบช่วยดูด/ดันตัวผู้เล่นขึ้นแพลตฟอร์มเมื่อกระโดดเฉียดมุม 90 องศา")]
        [SerializeField] private bool _enableCornerCorrection = true;
        [Tooltip("ระยะยิง Raycast เหนือศีรษะสำหรับเช็กมุมเพดาน")]
        [SerializeField] private float _headRayDistance = 0.28f;
        [Tooltip("ระยะเยื้องซ้าย-ขวาจากกึ่งกลางตัวสำหรับตรวจจับมุม")]
        [SerializeField] private float _cornerRaySideOffset = 0.28f;
        [Tooltip("ระยะขยับตัวผู้เล่นหลบมุมเพดานในแต่ละฟิสิกส์เฟรม")]
        [SerializeField] private float _headNudgeStep = 0.06f;
        [Tooltip("ระยะเช็กเพื่อดึงตัวขึ้นบนขอบแท่น (Ledge Step-up)")]
        [SerializeField] private float _ledgeRayForwardDistance = 0.42f;
        [Tooltip("ระยะยกตัวขึ้นยืนบนขอบแท่น/กล่องเมื่อเท้าหรือเข่าเฉียดขอบมุม (รองรับ Box สูง ~0.5m)")]
        [SerializeField] private float _ledgeStepUpDistance = 0.45f;

        [Header("Ground Snapping")]
        [Tooltip("เปิดใช้งานระบบดูดเท้าติดพื้นเวลาก้าวลงทางลาดหรือขอบเตี้ย (ป้องกันตัวลอยสะดุด)")]
        [SerializeField] private bool _enableGroundSnapping = true;
        [Tooltip("ระยะดูดเท้าติดพื้นสูงสุด (0.35m เหมาะสำหรับทางลาดชันและขอบเตี้ย)")]
        [SerializeField] private float _groundSnapDistance = 0.35f;

        [Header("Haul Airborne Settings")]
        [Tooltip("ระยะเวลาลอยตัวกลางอากาศสูงสุดขณะลากของ ก่อนที่สายด้ายจะขาด/หลุดออกอัตโนมัติ (เช่น ตกขอบแท่นสูงหรือตกเหว)")]
        [SerializeField] private float _maxHaulAirborneDuration = 0.25f;

        [Header("Crush Hazard")]
        [Tooltip("เปิดใช้งานระบบบดขยี้ตายเมื่อศีรษะติดเพดานหรือถูกวัตถุทับ (Limbo Style)")]
        [SerializeField] private bool _enableCrushDeath = true;
        [Tooltip("ระยะเวลาถูกบีบอัดต่อเนื่องก่อนตาย (วินาที)")]
        [SerializeField] private float _crushDeathDelay = 0.08f;
        [Tooltip("ระยะตรวจสอบสิ่งกีดขวางเหนือศีรษะ")]
        [SerializeField] private float _ceilingCheckDistance = 0.18f;
        [Tooltip("ความเร็วตกขั้นต่ำของวัตถุที่ร่วงกระแทกศีรษะแล้วทำให้ตายทันที (m/s)")]
        [SerializeField] private float _crushImpactVelocityThreshold = 2.5f;

        private Rigidbody2D _rb;
        private Collider2D _col;
        private PlayerVisuals _playerVisuals;
        private NeedleController _needleController;
        private readonly Collider2D[] _groundHitBuffer = new Collider2D[6]; // เผื่อกรอง Collider ตัวเองและรองรับวัตถุซ้อนทับกันหลายชิ้น
        private readonly RaycastHit2D[] _snapHitBuffer = new RaycastHit2D[4];
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
        private float _haulingAirborneTimer;
        private bool _isAiming;
        private bool _isInputLocked;
        private Vector2? _aimLeashAnchor;
        private float _aimLeashDistance;
        private float _crushTimer;

        public bool IsGrounded => _isGrounded;
        public bool IsHauling => _isHauling;
        public bool IsAiming => _isAiming;
        public bool IsInputLocked => _isInputLocked;
        public float HorizontalInput => _horizontalInput;
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;
        public Vector2 PlatformVelocity => _currentStitchable != null ? _currentStitchable.CurrentVelocity : Vector2.zero;
        public Vector2 RelativeVelocity => Velocity - PlatformVelocity;
        public StitchableObject CurrentStitchable => _currentStitchable;

        /// <summary>
        /// Checks whether the given collider belongs to the player (via tag or root/parent component).
        /// </summary>
        public static bool IsPlayer(Collider2D col) =>
            col != null && (col.CompareTag("Player") || col.GetComponentInParent<PlayerController2D>() != null);

        public bool IsStandingOn(Component target)
        {
            if (target == null || !_isGrounded) return false;

            Transform targetTransform = target.transform;
            return (_currentStitchable != null && (_currentStitchable.transform == targetTransform || _currentStitchable.transform.IsChildOf(targetTransform)))
                || (_currentGroundCollider != null && (_currentGroundCollider.transform == targetTransform || _currentGroundCollider.transform.IsChildOf(targetTransform)));
        }

        public event System.Action<float> OnLanded;
        public event System.Action OnJumped;

        public void SetInputLocked(bool locked)
        {
            _isInputLocked = locked;
            _horizontalInput = 0f;
            _isJumpButtonHeld = false;
            _jumpBufferCounter = 0f;
            _jumpRequested = false;

            if (locked && _rb != null)
            {
                _rb.linearVelocity = Vector2.zero;
            }
        }

        public void TeleportTo(Vector2 position, bool faceRight)
        {
            if (_rb != null)
            {
                _rb.position = position;
                _rb.linearVelocity = Vector2.zero;
            }
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            _lastVerticalVelocity = 0f;

            if (_playerVisuals == null)
            {
                _playerVisuals = GetComponentInChildren<PlayerVisuals>();
            }

            if (_playerVisuals != null)
            {
                _playerVisuals.SetFacing(faceRight);
            }
        }

        public void SetHauling(bool isHauling)
        {
            _isHauling = isHauling;
            if (!isHauling)
            {
                _haulingAirborneTimer = 0f;
            }
        }

        public void ReleaseHaulingTether()
        {
            _haulingAirborneTimer = 0f;
            if (_needleController != null && _needleController.IsTetheredToPlayer)
            {
                _needleController.ReleasePlayerTether();
            }
            else if (_currentStitchable != null)
            {
                _currentStitchable.ReleasePlayerTether();
            }
            else
            {
                SetHauling(false);
            }
        }

        public void SetAiming(bool isAiming, Vector2? leashAnchor = null, float leashDistance = 0f)
        {
            _isAiming = isAiming;
            if (isAiming)
            {
                _aimLeashAnchor = leashAnchor;
                _aimLeashDistance = leashDistance;
            }
            else
            {
                _aimLeashAnchor = null;
                _aimLeashDistance = 0f;
            }
        }

        public void UpdateAimLeash(Vector2 leashAnchor, float leashDistance)
        {
            _aimLeashAnchor = leashAnchor;
            _aimLeashDistance = leashDistance;
        }

        public void ClearAimLeash()
        {
            _aimLeashAnchor = null;
            _aimLeashDistance = 0f;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<Collider2D>();
            _playerVisuals = GetComponentInChildren<PlayerVisuals>();
            if (_needleController == null)
            {
                _needleController = GetComponent<NeedleController>();
            }

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
            ApplyPlatformMovement();
            CheckGround();
            CheckCrushHazard();

            // ตรวจสอบกรณีผูกลากของแล้วตัวลอยอยู่ในอากาศ (เช่น เดินตกขอบแท่นสูงหรือตกเหว)
            // ถ้าลอยค้างกลางอากาศเกินกำหนด ให้ปลดสายด้ายหลุดอัตโนมัติ เพื่อให้ตัวละครตกลงสู่พื้นอย่างเป็นธรรมชาติ
            if (_isHauling && !_isGrounded)
            {
                _haulingAirborneTimer += Time.fixedDeltaTime;
                if (_haulingAirborneTimer >= _maxHaulAirborneDuration)
                {
                    _haulingAirborneTimer = 0f;
                    ReleaseHaulingTether();
                }
            }
            else
            {
                _haulingAirborneTimer = 0f;
            }

            if (_jumpRequested)
            {
                ExecuteJump();
                _jumpRequested = false;
            }

            ApplyHorizontalMovement();
            ApplyJumpPhysics();

            if (_enableCornerCorrection && !_isGrounded)
            {
                ApplyCornerCorrection();
            }

            ApplyAimLeashConstraint();

            // เก็บค่าความเร็วแกน Y ไว้ท้ายสุด เพื่อใช้วัดแรงกระแทกก่อนที่ Physics Step รอบถัดไปจะปรับเป็น 0
            _lastVerticalVelocity = _rb.linearVelocity.y;
        }

        private void ApplyPlatformMovement()
        {
            if (_currentStitchable != null)
            {
                Vector2 delta = _currentStitchable.FrameDelta;

                // สำหรับการเคลื่อนที่ในแนวดิ่งลง ช่วยดึงตำแหน่ง Y ตามลงไปด้วยเพื่อไม่ให้เท้าลอยกระตุก
                if (delta.y < -0.0001f && delta.sqrMagnitude < 4.0f)
                {
                    _rb.position = new Vector2(_rb.position.x, _rb.position.y + delta.y);
                }
            }
        }

        private void ReadMovementInput()
        {
            _horizontalInput = 0f;
            if (_isInputLocked)
            {
                _isJumpButtonHeld = false;
                return;
            }

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
            if (_isInputLocked)
            {
                _jumpBufferCounter = 0f;
                _jumpRequested = false;
                return;
            }

            if (_isGrounded)
            {
                _coyoteTimeCounter = _coyoteTime;
            }
            else
            {
                _coyoteTimeCounter -= Time.deltaTime;
            }

            _jumpBufferCounter -= Time.deltaTime;

            if (_jumpBufferCounter > 0f && _coyoteTimeCounter > 0f && !_isAiming)
            {
                // ขณะกำลังลากวัตถุ (Hauling) หากผู้เล่นกดกระโดด จะปลดเชือกหลุดอัตโนมัติแล้วกระโดดได้อย่างคล่องตัว
                if (_isHauling)
                {
                    ReleaseHaulingTether();
                }

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
            _currentStitchable = null;
            _currentGroundCollider = null;
            // ถ้าระหว่างติด Jump Buffer ผู้เล่นปล่อยปุ่มกระโดดไปแล้ว ให้ตัดความสูงกระโดดทันที (Short Hop)
            _isJumpCut = !_isJumpButtonHeld;
            OnJumped?.Invoke();
        }

        private void ApplyHorizontalMovement()
        {
            float speed = _moveSpeed;
            if (_isAiming)
            {
                speed *= _aimingSpeedMultiplier;
            }
            else if (_isHauling)
            {
                // ขณะลากของ หากตัวลอยอยู่ในอากาศ (ตกขอบแท่น) จะไม่สามารถออกแรงดึงของกลางอากาศได้
                if (!_isGrounded)
                {
                    speed = 0f;
                }
                else
                {
                    speed *= _haulingSpeedMultiplier;
                }
            }

            Vector2 platVel = PlatformVelocity;
            float targetRelSpeed = _horizontalInput * speed;

            // หน่วงความเร็วแนวนอนไม่ให้เร่งหนีออกจากระยะด้ายเมื่อด้ายถูกขึงตึงถึงระยะจำกัด
            if (_isAiming && _aimLeashAnchor.HasValue && _aimLeashDistance > 0f)
            {
                Vector2 anchor = _aimLeashAnchor.Value;
                float currentDist = Vector2.Distance(_rb.position, anchor);
                float dx = _rb.position.x - anchor.x;

                if (currentDist >= _aimLeashDistance - 0.05f)
                {
                    if ((dx > 0.01f && targetRelSpeed > 0f) || (dx < -0.01f && targetRelSpeed < 0f))
                    {
                        targetRelSpeed = 0f;
                    }
                }
            }

            float accelRate = Mathf.Abs(targetRelSpeed) > 0.01f ? _acceleration : _deceleration;
            float currentRelVelX = _rb.linearVelocity.x - platVel.x;
            float newRelVelX = Mathf.MoveTowards(currentRelVelX, targetRelSpeed, accelRate * Time.fixedDeltaTime);

            _rb.linearVelocity = new Vector2(platVel.x + newRelVelX, _rb.linearVelocity.y);
        }

        private void ApplyJumpPhysics()
        {
            if (_isGrounded) return;

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

        private void ApplyAimLeashConstraint()
        {
            if (!_isAiming || !_aimLeashAnchor.HasValue || _aimLeashDistance <= 0f) return;

            Vector2 anchor = _aimLeashAnchor.Value;
            Vector2 toPlayer = _rb.position - anchor;
            float dist = toPlayer.magnitude;

            if (dist > _aimLeashDistance)
            {
                Vector2 clampedDir = dist > 0.0001f ? toPlayer / dist : Vector2.right;
                Vector2 clampedPos = anchor + clampedDir * _aimLeashDistance;

                _rb.position = clampedPos;

                float outgoingSpeed = Vector2.Dot(_rb.linearVelocity, clampedDir);
                if (outgoingSpeed > 0f)
                {
                    _rb.linearVelocity -= clampedDir * outgoingSpeed;
                }
            }
        }

        private void CheckGround()
        {
            Vector2 checkPos = _groundCheckPoint != null
                ? _rb.position + (Vector2)_groundCheckPoint.localPosition
                : _rb.position + Vector2.down * _col.bounds.extents.y;

            // จำกัดความกว้างของกล่องเช็กพื้นไม่ให้ล้นเกินความกว้างลำตัวผู้เล่น เพื่อป้องกันการชนด้านข้างของกำแพงหรือกล่อง
            float colWidth = _col != null ? _col.bounds.size.x : 0.5f;
            float checkWidth = Mathf.Min(_groundCheckSize.x, colWidth * 0.85f);
            Vector2 effectiveCheckSize = new Vector2(checkWidth, _groundCheckSize.y);

            bool wasGrounded = _isGrounded;
            int hitCount = Physics2D.OverlapBox(checkPos, effectiveCheckSize, 0f, _groundFilter, _groundHitBuffer);

            Collider2D detectedGround = null;
            float playerFeetY = _col != null ? _col.bounds.min.y : transform.position.y;

            // ให้ความสำคัญกับ StitchableObject เฉพาะเมื่อผู้เล่นยืนอยู่บนหลังของมันจริงๆ
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D groundCol = _groundHitBuffer[i];
                if (groundCol == null || groundCol == _col) continue;

                StitchableObject so = groundCol.GetComponentInParent<StitchableObject>();
                if (so != null)
                {
                    if (playerFeetY >= groundCol.bounds.max.y - 0.2f)
                    {
                        detectedGround = groundCol;
                        break;
                    }
                }
                else if (detectedGround == null)
                {
                    detectedGround = groundCol;
                }
            }

            // Downward Ground Snapping: เมื่อเดินลงทางลาดหรือขอบเตี้ย (velocity Y <= 0 และเฟรมก่อนอยู่บนพื้น)
            // ยิง Raycast ลงไปในระยะสั้นเพื่อดูดเท้าติดพื้นทันที ป้องกันตัวลอยกระตุก และไม่ดูดติด Trigger colliders
            if (_enableGroundSnapping && detectedGround == null && wasGrounded && _rb.linearVelocity.y <= 0.05f)
            {
                int snapCount = Physics2D.Raycast(checkPos, Vector2.down, _groundFilter, _snapHitBuffer, _groundSnapDistance);
                for (int s = 0; s < snapCount; s++)
                {
                    RaycastHit2D snapHit = _snapHitBuffer[s];
                    if (snapHit.collider != null && snapHit.collider != _col && !snapHit.collider.isTrigger)
                    {
                        float snapDeltaY = snapHit.point.y - checkPos.y;
                        if (snapDeltaY < 0f && Mathf.Abs(snapDeltaY) <= _groundSnapDistance)
                        {
                            _rb.position = new Vector2(_rb.position.x, _rb.position.y + snapDeltaY);
                            detectedGround = snapHit.collider;
                            break;
                        }
                    }
                }
            }

            UpdateCachedGround(detectedGround);

            // คำนวณความเร็วแนวดิ่งของวัตถุใต้เท้า (รองรับ StitchableObject และแพลตฟอร์มเคลื่อนที่ในแนวดิ่ง)
            float groundVelY = 0f;
            if (_currentStitchable != null)
            {
                groundVelY = _currentStitchable.CurrentVelocity.y;
            }
            else if (detectedGround != null && detectedGround.attachedRigidbody != null)
            {
                groundVelY = detectedGround.attachedRigidbody.linearVelocity.y;
            }

            float relVertVelocity = _rb.linearVelocity.y - groundVelY;

            // นับว่าอยู่บนพื้นเมื่อสัมผัส Collider พื้น และความเร็วสัมพัทธ์ไม่ได้พุ่งตัวขึ้นจากการกระโดด
            _isGrounded = detectedGround != null && relVertVelocity <= 0.35f;

            if (!wasGrounded && _isGrounded)
            {
                _isJumpCut = false;
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

            // ปลดการลาก (Tether/Harness) ทันทีหากผู้เล่นก้าวขึ้นมายืนบนวัตถุที่ตัวเองกำลังผูกเชือกอยู่จริงๆ
            // เพื่อป้องกันแรงฟิสิกส์ดึงวัตถุใต้เท้าจนเกิดบั๊กเลื่อนไหลตามตัว (Self-propulsion hoverboard exploit)
            if (_isHauling && _currentStitchable != null && _currentStitchable.IsTetheredToPlayer && IsStandingOn(_currentStitchable))
            {
                _currentStitchable.ReleasePlayerTether();
            }
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

        private void CheckCrushHazard()
        {
            if (!_enableCrushDeath || !_isGrounded || _isInputLocked)
            {
                _crushTimer = 0f;
                return;
            }

            // ความเร็วของพื้นใต้เท้า
            float platformUpwardSpeed = _currentStitchable != null
                ? _currentStitchable.CurrentVelocity.y
                : (_currentGroundCollider != null && _currentGroundCollider.attachedRigidbody != null
                    ? _currentGroundCollider.attachedRigidbody.linearVelocity.y
                    : 0f);

            // ตรวจสอบสิ่งกีดขวางเหนือศีรษะ (เพดาน หรือ วัตถุที่อยู่ข้างบน)
            float colWidth = _col != null ? _col.bounds.size.x : 0.5f;
            float checkWidth = colWidth * 0.75f;
            Vector2 headTop = _col != null
                ? new Vector2(_col.bounds.center.x, _col.bounds.max.y - 0.04f)
                : (Vector2)transform.position + Vector2.up * 0.9f;

            RaycastHit2D hit = Physics2D.BoxCast(headTop, new Vector2(checkWidth, 0.06f), 0f, Vector2.up, _ceilingCheckDistance, _groundLayer);

            bool isCrushing = false;
            if (hit.collider != null && hit.collider != _col && !hit.collider.isTrigger)
            {
                // ตรวจสอบว่าไม่ใช่ตัววัตถุที่ยืนอยู่เอง
                Transform currentGroundTransform = _currentGroundCollider != null ? _currentGroundCollider.transform : null;
                Transform stitchableTransform = _currentStitchable != null ? _currentStitchable.transform : null;

                bool isSameAsGround = (currentGroundTransform != null && (hit.collider.transform == currentGroundTransform || hit.collider.transform.IsChildOf(currentGroundTransform)))
                    || (stitchableTransform != null && (hit.collider.transform == stitchableTransform || hit.collider.transform.IsChildOf(stitchableTransform)));

                if (!isSameAsGround)
                {
                    // กรณีที่ 1: พื้นใต้เท้ากำลังยกตัวขึ้นดันตัวผู้เล่นอัดกับเพดาน
                    if (platformUpwardSpeed > 0.15f)
                    {
                        isCrushing = true;
                    }
                    else
                    {
                        // กรณีที่ 2: วัตถุด้านบน (เช่น StitchableObject หรือ Rigidbody) กำลังเคลื่อนที่ลงมาทับผู้เล่นที่ยืนบนพื้น
                        StitchableObject overheadStitchable = hit.collider.GetComponentInParent<StitchableObject>();
                        float overheadDownSpeed = 0f;
                        if (overheadStitchable != null)
                        {
                            overheadDownSpeed = -overheadStitchable.CurrentVelocity.y;
                        }
                        else if (hit.collider.attachedRigidbody != null)
                        {
                            overheadDownSpeed = -hit.collider.attachedRigidbody.linearVelocity.y;
                        }

                        // วัตถุด้านบนกำลังเคลื่อนที่ลงมาทับผู้เล่น
                        if (overheadDownSpeed > 0.15f)
                        {
                            isCrushing = true;
                        }
                    }
                }
            }

            if (isCrushing)
            {
                _crushTimer += Time.fixedDeltaTime;
                if (_crushTimer >= _crushDeathDelay)
                {
                    _crushTimer = 0f;
                    ExecuteCrushDeath();
                }
            }
            else
            {
                _crushTimer = 0f;
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandleOverheadCollisionImpact(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            if (!_isGrounded) return;
            HandleOverheadCollisionImpact(collision);
        }

        private void HandleOverheadCollisionImpact(Collision2D collision)
        {
            if (!_enableCrushDeath || _isInputLocked) return;

            StitchableObject so = collision.collider.GetComponentInParent<StitchableObject>();
            Rigidbody2D otherRb = collision.rigidbody;

            // ตรวจสอบเฉพาะการชนกับ StitchableObject หรือ Rigidbody ที่มีมวล
            if (so == null && otherRb == null) return;

            // คำนวณความเร็วในการร่วงลงมาของวัตถุ
            float objDownSpeed = 0f;
            if (otherRb != null)
            {
                objDownSpeed = -otherRb.linearVelocity.y;
            }
            if (so != null)
            {
                objDownSpeed = Mathf.Max(objDownSpeed, -so.CurrentVelocity.y);
            }

            // คำนวณระดับความสูงของศีรษะผู้เล่น
            float playerHeadY = _col != null ? _col.bounds.max.y - 0.2f : transform.position.y + 0.4f;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);

                // หากจุดสัมผัสเกิดขึ้นที่บริเวณศีรษะ (ด้านบนสุดของผู้เล่น)
                if (contact.point.y >= playerHeadY)
                {
                    // กรณีร่วงตกจากที่สูงกระแทกหัวอย่างรุนแรง (Instant Impact Crush)
                    if (objDownSpeed >= _crushImpactVelocityThreshold)
                    {
                        ExecuteCrushDeath();
                        return;
                    }

                    // กรณีผู้เล่นยืนติดพื้นแล้วมีกล่องเลื่อนลงมาทับหรือวางทับต่อเนื่อง
                    if (_isGrounded && objDownSpeed > 0.12f)
                    {
                        ExecuteCrushDeath();
                        return;
                    }
                }
            }
        }

        private void ExecuteCrushDeath()
        {
            if (CheckpointManager.Instance != null && !CheckpointManager.Instance.IsRespawning)
            {
                CameraController2D.Instance?.Shake(0.35f, 0.45f);
                CheckpointManager.Instance.TriggerPlayerDeath(shakeIntensity: 0.35f, shakeDuration: 0.45f);
            }
        }

        private void ApplyCornerCorrection()
        {
            ApplyHeadCornerCorrection();
            ApplyLedgeStepUpCorrection();
        }

        /// <summary>
        /// เมื่อกระโดดขึ้นแล้วศีรษะเฉียดมุม 90 องศาของเพดาน/กล่อง
        /// จะช่วยเบี่ยงตำแหน่งตัวละครหลบมุมออกด้านข้าง ป้องกันหัวโหม่งมุมแล้วร่วงดิ่ง
        /// </summary>
        private void ApplyHeadCornerCorrection()
        {
            if (_rb.linearVelocity.y <= 0f) return;

            Vector2 center = (Vector2)transform.position + Vector2.up * _col.bounds.extents.y;
            Vector2 leftOrigin = center + Vector2.left * _cornerRaySideOffset;
            Vector2 rightOrigin = center + Vector2.right * _cornerRaySideOffset;

            RaycastHit2D hitLeft = Physics2D.Raycast(leftOrigin, Vector2.up, _headRayDistance, _groundLayer);
            RaycastHit2D hitRight = Physics2D.Raycast(rightOrigin, Vector2.up, _headRayDistance, _groundLayer);

            bool isRightSolid = hitRight.collider != null && hitRight.collider != _col && !hitRight.collider.isTrigger;
            bool isLeftSolid = hitLeft.collider != null && hitLeft.collider != _col && !hitLeft.collider.isTrigger;

            // ขวาชน แต่ซ้ายโล่ง -> ดันหลบไปทางซ้าย
            if (isRightSolid && !isLeftSolid)
            {
                _rb.position = new Vector2(_rb.position.x - _headNudgeStep, _rb.position.y);
            }
            // ซ้ายชน แต่ขวาโล่ง -> ดันหลบไปทางขวา
            else if (isLeftSolid && !isRightSolid)
            {
                _rb.position = new Vector2(_rb.position.x + _headNudgeStep, _rb.position.y);
            }
        }

        /// <summary>
        /// เมื่อกระโดดเข้าหามุม 90 องศาของแท่นแพลตฟอร์มหรือกล่อง (Box) แล้วเท้า/เข่าเฉียดขอบบน
        /// ระบบจะช่วยยกและสไลด์ตัวละครขึ้นไปยืนบนขอบแท่นทันที (สไตล์ Limbo Ledge Assistance)
        /// </summary>
        private void ApplyLedgeStepUpCorrection()
        {
            // ทำงานเฉพาะเมื่อผู้เล่นกำลังลอยอยู่ และมีทิศทางการเคลื่อนที่หรือป้อน Input เข้าหากำแพง/กล่อง
            float moveDir = Mathf.Abs(_horizontalInput) > 0.05f ? Mathf.Sign(_horizontalInput) : Mathf.Sign(_rb.linearVelocity.x);
            if (Mathf.Abs(moveDir) < 0.05f && Mathf.Abs(_rb.linearVelocity.x) < 0.05f) return;

            Vector2 forward = Vector2.right * moveDir;
            float extentsY = _col.bounds.extents.y;
            Vector2 feetPos = (Vector2)transform.position + Vector2.down * (extentsY * 0.75f); // ระดับเท้า/ข้อเท้า
            Vector2 kneePos = (Vector2)transform.position + Vector2.down * (extentsY * 0.25f); // ระดับเข่า
            Vector2 waistPos = transform.position; // กึ่งกลางลำตัว/ระดับเอว
            Vector2 headPos = (Vector2)transform.position + Vector2.up * (extentsY * 0.85f); // ระดับเหนืออก/หัว

            // ตรวจสอบว่าศีรษะด้านหน้าโล่ง (ถ้ามีกำแพงสูงขวางอยู่ข้างหน้าจะไม่ทำ Step-up)
            RaycastHit2D hitHead = Physics2D.Raycast(headPos, forward, _ledgeRayForwardDistance, _groundLayer);
            if (hitHead.collider != null && hitHead.collider != _col && !hitHead.collider.isTrigger) return;

            // ตรวจสอบว่าระดับเท้า, เข่า หรือเอว ชนขอบมุมของวัตถุ (เช่น Box.prefab หรือมุมแพลตฟอร์ม)
            RaycastHit2D hitLower = Physics2D.Raycast(feetPos, forward, _ledgeRayForwardDistance, _groundLayer);
            if (hitLower.collider == null || hitLower.collider == _col || hitLower.collider.isTrigger)
            {
                hitLower = Physics2D.Raycast(kneePos, forward, _ledgeRayForwardDistance, _groundLayer);
            }
            if (hitLower.collider == null || hitLower.collider == _col || hitLower.collider.isTrigger)
            {
                hitLower = Physics2D.Raycast(waistPos, forward, _ledgeRayForwardDistance, _groundLayer);
            }

            if (hitLower.collider != null && hitLower.collider != _col && !hitLower.collider.isTrigger)
            {
                // คำนวณจุด probe ด้านบนของวัตถุโดยตรงจาก Bounds ของ Collider หรือตำแหน่งที่ชน
                float topBoundY = hitLower.collider.bounds.max.y;
                float currentFeetY = transform.position.y - extentsY;
                float heightDifference = topBoundY - currentFeetY;

                // ยิง probe จากเหนือขอบแท่นลงมาเพื่อหาผิวด้านบนที่แม่นยำ
                Vector2 probeTop = new Vector2(hitLower.point.x + (moveDir * 0.12f), topBoundY + 0.15f);
                RaycastHit2D hitLedgeTop = Physics2D.Raycast(probeTop, Vector2.down, Mathf.Max(0.6f, heightDifference + 0.3f), _groundLayer);

                float surfaceY = hitLedgeTop.collider != null && hitLedgeTop.collider != _col && !hitLedgeTop.collider.isTrigger ? hitLedgeTop.point.y : topBoundY;
                heightDifference = surfaceY - currentFeetY;

                // ถ้าระดับขอบบนของกล่อง/แท่นอยู่สูงกว่าเท้าไม่เกิน _ledgeStepUpDistance
                if (heightDifference > -0.05f && heightDifference <= _ledgeStepUpDistance)
                {
                    Vector2 steppedPos = new Vector2(
                        _rb.position.x + (moveDir * 0.08f),
                        surfaceY + extentsY + 0.02f
                    );

                    Vector2 clearanceSize = new Vector2(
                        _col.bounds.size.x * 0.9f,
                        Mathf.Max(0.1f, _col.bounds.size.y - 0.04f)
                    );
                    int overlapCount = Physics2D.OverlapBox(steppedPos, clearanceSize, 0f, _groundFilter, _groundHitBuffer);
                    for (int i = 0; i < overlapCount; i++)
                    {
                        Collider2D hit = _groundHitBuffer[i];
                        if (hit != null && hit != _col)
                        {
                            return; // มีเพดานหรือสิ่งกีดขวางทับตำแหน่งปลายทาง ยกเลิกการยกตัวขึ้นขอบ
                        }
                    }

                    // ยกตัวผู้เล่นขึ้นไปยืนบนขอบแท่น และเลื่อนเข้าหาแท่นเล็กน้อย ป้องกันการสะดุดขอบซ้ำ
                    _rb.position = steppedPos;

                    // ปรับความเร็วในแนวดิ่งให้อยู่นิ่งบนแท่นทันที และช่วยส่งแรงเดินหน้าต่อ
                    _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, Mathf.Max(0f, _rb.linearVelocity.y));
                    _isGrounded = true;
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Collider2D col = _col != null ? _col : GetComponent<Collider2D>();
            float fallbackOffset = col != null ? col.bounds.extents.y : 0.5f;

            // 1. Ground Check Box (เขียวเมื่ออยู่บนพื้น / แดงเมื่อลอย)
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Vector2 checkPos = _groundCheckPoint != null
                ? (Vector2)_groundCheckPoint.position
                : (Vector2)transform.position + Vector2.down * fallbackOffset;

            float colWidth = col != null ? col.bounds.size.x : 0.5f;
            float checkWidth = Mathf.Min(_groundCheckSize.x, colWidth * 0.85f);
            Gizmos.DrawWireCube(checkPos, new Vector2(checkWidth, _groundCheckSize.y));

            // 2. Ground Snapping (สีม่วง Magenta ใต้ฝ่าเท้า)
            if (_enableGroundSnapping && _groundSnapDistance > 0f)
            {
                Gizmos.color = new Color(0.85f, 0.35f, 1f, 0.85f);
                Vector2 snapStart = checkPos;
                Vector2 snapEnd = checkPos + Vector2.down * _groundSnapDistance;
                Gizmos.DrawLine(snapStart, snapEnd);
                Gizmos.DrawLine(snapEnd + Vector2.left * 0.12f, snapEnd + Vector2.right * 0.12f);
            }

            if (_enableCornerCorrection)
            {
                // 3. Head Corner Correction (สีเหลือง เหนือศีรษะ)
                Gizmos.color = Color.yellow;
                Vector2 headCenter = (Vector2)transform.position + Vector2.up * fallbackOffset;
                Gizmos.DrawLine(headCenter + Vector2.left * _cornerRaySideOffset, headCenter + Vector2.left * _cornerRaySideOffset + Vector2.up * _headRayDistance);
                Gizmos.DrawLine(headCenter + Vector2.right * _cornerRaySideOffset, headCenter + Vector2.right * _cornerRaySideOffset + Vector2.up * _headRayDistance);

                // 4. Ledge Step-up Correction (สีฟ้า Cyan แสดงทั้งระยะยื่นแนวนอน และความสูง _ledgeStepUpDistance แนวตั้ง)
                float feetY = transform.position.y - fallbackOffset;

                // ด้านขวา: แสดงเส้นขอบความสูง Step-up และกล่องโซนช่วยยกตัว
                Gizmos.color = Color.cyan;
                Vector2 rightEdgeBottom = new Vector2(transform.position.x + _ledgeRayForwardDistance, feetY);
                Vector2 rightEdgeTop = new Vector2(transform.position.x + _ledgeRayForwardDistance, feetY + _ledgeStepUpDistance);
                Gizmos.DrawLine(new Vector2(transform.position.x, feetY), rightEdgeBottom);
                Gizmos.DrawLine(rightEdgeBottom, rightEdgeTop);
                Gizmos.DrawLine(rightEdgeTop, rightEdgeTop + Vector2.left * 0.1f);

                Vector2 rightBoxCenter = new Vector2(transform.position.x + (_ledgeRayForwardDistance * 0.5f), feetY + (_ledgeStepUpDistance * 0.5f));
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.2f);
                Gizmos.DrawCube(rightBoxCenter, new Vector3(_ledgeRayForwardDistance, _ledgeStepUpDistance, 0.05f));

                // ด้านซ้าย: แสดงเส้นขอบความสูง Step-up และกล่องโซนช่วยยกตัว
                Gizmos.color = Color.cyan;
                Vector2 leftEdgeBottom = new Vector2(transform.position.x - _ledgeRayForwardDistance, feetY);
                Vector2 leftEdgeTop = new Vector2(transform.position.x - _ledgeRayForwardDistance, feetY + _ledgeStepUpDistance);
                Gizmos.DrawLine(new Vector2(transform.position.x, feetY), leftEdgeBottom);
                Gizmos.DrawLine(leftEdgeBottom, leftEdgeTop);
                Gizmos.DrawLine(leftEdgeTop, leftEdgeTop + Vector2.right * 0.1f);

                Vector2 leftBoxCenter = new Vector2(transform.position.x - (_ledgeRayForwardDistance * 0.5f), feetY + (_ledgeStepUpDistance * 0.5f));
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.2f);
                Gizmos.DrawCube(leftBoxCenter, new Vector3(_ledgeRayForwardDistance, _ledgeStepUpDistance, 0.05f));
            }

            // 5. Crush Hazard Ceiling Check (สีส้ม/แดง เหนือศีรษะ)
            if (_enableCrushDeath)
            {
                Gizmos.color = _crushTimer > 0f ? Color.red : new Color(1f, 0.45f, 0.15f, 0.75f);
                Vector2 headTop = col != null
                    ? new Vector2(col.bounds.center.x, col.bounds.max.y - 0.04f)
                    : (Vector2)transform.position + Vector2.up * 0.9f;
                Gizmos.DrawWireCube(headTop + Vector2.up * (_ceilingCheckDistance * 0.5f), new Vector2(colWidth * 0.75f, _ceilingCheckDistance));
            }
        }
    }
}