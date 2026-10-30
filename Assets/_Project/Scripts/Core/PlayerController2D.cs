using UnityEngine;
using UnityEngine.InputSystem;
using ThailandHorrorJam.Systems.Needle;
using ThailandHorrorJam.Systems.Gameplay;

namespace ThailandHorrorJam.Core
{
    /// <summary>
    /// Responsive 2D platformer character controller with jump buffering, coyote time, and variable jump cut.
    /// </summary>
    public sealed class PlayerController2D : MonoBehaviour
    {
        [Header("Component References")]
        [Tooltip("Rigidbody2D หลักของผู้เล่น (หากไม่กำหนดจะค้นหาบน GameObject นี้หรือ Parent อัตโนมัติ)")]
        [SerializeField] private Rigidbody2D _rb;
        [Tooltip("Collider2D หลักของลำตัวผู้เล่น (หากไม่กำหนดจะค้นหาบน GameObject นี้หรือ Children อัตโนมัติ)")]
        [SerializeField] private Collider2D _col;

        [Header("Movement Settings")]
        [SerializeField] private float _moveSpeed = 6.5f;
        [SerializeField] private float _acceleration = 50f;
        [SerializeField] private float _deceleration = 60f;

        [Header("Hauling / Burden Settings")]
        [Tooltip("Speed multiplier applied to player horizontal velocity while tethered and hauling an object.")]
        [SerializeField] private float _haulingSpeedMultiplier = 0.58f;
        [Tooltip("Allow player to walk while aiming needle. If false, player stays stationary in aim stance.")]
        [SerializeField] private bool _canMoveWhileAiming = false;
        [Tooltip("Speed multiplier applied to player horizontal velocity while aiming/pinning needle (ย่องเล็ง) when _canMoveWhileAiming is true.")]
        [SerializeField] private float _aimingSpeedMultiplier = 0f;
        [Tooltip("SFX name for hauling/dragging crate loop (Slide_lp).")]
        [SerializeField] private string _haulingDragSFXName = "sfx_crate_drag_loop";
        [SerializeField] [Range(0f, 1f)] private float _haulingDragVolume = 0.75f;

        [Header("Jump & Gravity Settings")]
        [SerializeField] private float _jumpForce = 12f;
        [Tooltip("สเกลแรงโน้มถ่วงกำหนดเอง (คำนวณในสคริปต์ 100% โดยตัด Rigidbody2D Gravity ออกเพื่อป้องกันการลื่นไถลบนทางลาด)")]
        [SerializeField] private float _gravityScale = 1.0f;
        [SerializeField] private float _coyoteTime = 0.15f;
        [SerializeField] private float _jumpBufferTime = 0.15f;
        [SerializeField] private float _fallMultiplier = 2.5f;
        [SerializeField] private float _lowJumpMultiplier = 2f;
        [SerializeField] private float _maxFallSpeed = 20f;
        [Tooltip("Super Jump upward velocity boost granted when jumping while attached to a StitchableObject over an erupting CorpseGasVent.")]
        [SerializeField] private float _ventSuperJumpBoost = 7.0f;

        [Header("Ground Check")]
        [Tooltip("Collider ของเท้าผู้เล่น (เช่น Feet BoxCollider2D) ที่กำหนดขนาดและตำแหน่งตรวจพื้นโดยตรงจาก Prefab")]
        [SerializeField] private Collider2D _feetCollider;
        [SerializeField] private LayerMask _groundLayer;

        [Header("Slope Settings")]
        [Tooltip("มุมทางลาดสูงสุดที่ผู้เล่นสามารถเดินขึ้นได้ (องศา)")]
        [SerializeField] private float _maxSlopeAngle = 55f;
        [Tooltip("ระยะยิง Raycast ตรวจจับทางลาดใต้ฝ่าเท้า")]
        [SerializeField] private float _slopeCheckDistance = 0.35f;

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
        [SerializeField] private float _maxHaulAirborneDuration = 0.85f;

        [Header("Crush Hazard")]
        [Tooltip("เปิดใช้งานระบบบดขยี้ตายเมื่อศีรษะติดเพดานหรือถูกวัตถุทับ (Limbo Style)")]
        [SerializeField] private bool _enableCrushDeath = true;
        [Tooltip("ระยะเวลาถูกบีบอัดต่อเนื่องก่อนตาย (วินาที)")]
        [SerializeField] private float _crushDeathDelay = 0.08f;
        [Tooltip("ความเร็วตกขั้นต่ำของวัตถุที่ร่วงกระแทกศีรษะแล้วทำให้ตายทันที (m/s)")]
        [SerializeField] private float _crushImpactVelocityThreshold = 2.0f;
        [Tooltip("ระยะตรวจสอบเพดานเหนือศีรษะ (เมตร)")]
        [SerializeField] private float _ceilingCheckDistance = 0.5f;

        [Header("Respawn Ground Snap")]
        [Tooltip("ดูดตำแหน่งเท้าผู้เล่นลงแตะพื้นด้านล่างอัตโนมัติเมื่อเกิดใหม่ (ป้องกันอาการเกิดลอยกลางอากาศแล้วร่วงลงมาจากฟ้า)")]
        [SerializeField] private bool _snapToGroundOnTeleport = true;
        [Tooltip("ระยะค้นหาพื้นด้านล่างสูงสุดเมื่อเกิดใหม่ (เมตร)")]
        [SerializeField] private float _maxRespawnGroundSnapDistance = 15f;

        private PlayerVisuals _playerVisuals;
        private NeedleController _needleController;
        private readonly Collider2D[] _groundHitBuffer = new Collider2D[6]; // เผื่อกรอง Collider ตัวเองและรองรับวัตถุซ้อนทับกันหลายชิ้น
        private readonly RaycastHit2D[] _snapHitBuffer = new RaycastHit2D[8];
        private readonly System.Collections.Generic.List<Collider2D> _ignoredCrushColliders = new System.Collections.Generic.List<Collider2D>(4);
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
        private bool _isDraggingAudioPlaying;
        private bool _isAiming;
        private bool _isInputLocked;
        private Vector2? _aimLeashAnchor;
        private float _aimLeashDistance;
        private float _crushTimer;
        private float _respawnImmunityTimer;
        private float _throwSlowdownTimer;
        private float _throwSlowdownMultiplier = 1f;
        private float _knockbackTimer;
        private float _hazardKnockbackCooldownTimer;
        private Vector2 _knockbackVelocity;
        private float _ventSuperJumpTimer;

        private bool _isOnSlope;
        private Vector2 _slopeNormal = Vector2.up;
        private float _slopeAngle;
        private Vector2 _slopeParallel = Vector2.right;

        public bool IsGrounded => _isGrounded;
        public bool IsHauling => _isHauling;
        public bool IsInputLocked => _isInputLocked;
        public bool IsFacingRight => _playerVisuals != null ? _playerVisuals.IsFacingRight : true;
        public bool CanReceiveHazardKnockback => _hazardKnockbackCooldownTimer <= 0f;
        public float HorizontalInput => _horizontalInput;
        public Collider2D MainCollider => _col;
        public Vector2 Velocity => _rb != null ? _rb.linearVelocity : Vector2.zero;
        public Vector2 PlatformVelocity => _currentStitchable != null ? _currentStitchable.CurrentVelocity : Vector2.zero;
        public Vector2 RelativeVelocity => Velocity - PlatformVelocity;
        public StitchableObject CurrentStitchable => _currentStitchable;
        public bool IsVentSuperJumping => _ventSuperJumpTimer > 0f;
        public NeedleController NeedleController => _needleController;

        public static PlayerController2D Instance { get; private set; }

        /// <summary>
        /// ตรวจสอบว่า Collider นี้เป็นส่วนหนึ่งของตัวผู้เล่นหรือไม่ (ทั้งแบบติดตรง หรือเป็น Compound Collider บนลูก)
        /// </summary>
        public bool IsSelfCollider(Collider2D otherCol)
        {
            if (otherCol == null) return false;
            return otherCol == _col
                || otherCol == _feetCollider
                || (_rb != null && otherCol.attachedRigidbody == _rb)
                || otherCol.transform.IsChildOf(transform);
        }

        /// <summary>
        /// Checks whether the given collider or component belongs to the player (via tag or root/parent component).
        /// </summary>
        public static bool IsPlayer(Collider2D col)
        {
            if (col == null) return false;
            if (col.CompareTag("Player")) return true;
            if (col.attachedRigidbody != null && col.attachedRigidbody.CompareTag("Player")) return true;
            return col.GetComponentInParent<PlayerController2D>() != null;
        }

        public static bool IsPlayer(Component comp)
        {
            if (comp == null) return false;
            if (comp.CompareTag("Player")) return true;
            if (comp is Collider2D col && col.attachedRigidbody != null && col.attachedRigidbody.CompareTag("Player")) return true;
            return comp.GetComponentInParent<PlayerController2D>() != null;
        }

        public bool IsStandingOn(Component target)
        {
            if (target == null || !_isGrounded) return false;

            Transform targetTransform = target.transform;
            return (_currentStitchable != null && (_currentStitchable.transform == targetTransform || _currentStitchable.transform.IsChildOf(targetTransform)))
                || (_currentGroundCollider != null && (_currentGroundCollider.transform == targetTransform || _currentGroundCollider.transform.IsChildOf(targetTransform)));
        }

        public event System.Action<float> OnLanded;
        public event System.Action OnJumped;

        /// <summary>
        /// Applies an external physics knockback impulse to the player (e.g. from falling fish hazards).
        /// </summary>
        public void ApplyKnockback(Vector2 impulse, float duration = 0.22f)
        {
            if (_rb == null) _rb = GetComponent<Rigidbody2D>();
            if (_rb == null) _rb = GetComponentInParent<Rigidbody2D>();
            if (_rb == null) return;
            _knockbackTimer = Mathf.Max(_knockbackTimer, duration);
            _knockbackVelocity = impulse;
            _rb.linearVelocity = impulse;
            CameraController2D.Instance?.Shake(0.035f, 0.1f);

            // เมื่อโดนกระแทกจาก Hazard (เช่น โดนปลาตกใส่) ด้ายที่ผูกติดอยู่กับตัวผู้เล่น (Tether) ต้องขาดหลุดทันที
            if (_isHauling)
            {
                ReleaseHaulingTether();
            }

            if (_needleController != null)
            {
                if (_needleController.IsTetheredToPlayer)
                {
                    _needleController.ReleasePlayerTether();
                }
                if (_needleController.IsAiming)
                {
                    _needleController.CancelStitchingOrTether();
                }
            }
        }

        public void SetHazardKnockbackCooldown(float duration = 0.28f)
        {
            _hazardKnockbackCooldownTimer = Mathf.Max(_hazardKnockbackCooldownTimer, duration);
        }

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

        public void ApplyThrowSlowdown(float duration = 0.15f, float multiplier = 0.25f)
        {
            _throwSlowdownTimer = duration;
            _throwSlowdownMultiplier = multiplier;
        }

        public void StopHorizontalMovement()
        {
            if (_rb != null)
            {
                _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            }
        }

        public void TeleportTo(Vector2 position, bool faceRight, bool snapToGround = true)
        {
            RestoreIgnoredCrushColliders();

            Vector2 finalPosition = position;
            Collider2D snappedGround = null;
            Vector2 snappedNormal = Vector2.up;

            if (snapToGround && _snapToGroundOnTeleport && TryResolveGroundSnapPosition(position, out Vector2 groundPos, out Collider2D hitCol, out Vector2 hitNormal))
            {
                finalPosition = groundPos;
                snappedGround = hitCol;
                snappedNormal = hitNormal;
            }

            if (_rb != null)
            {
                _rb.position = finalPosition;
                _rb.linearVelocity = Vector2.zero;
            }
            transform.position = new Vector3(finalPosition.x, finalPosition.y, transform.position.z);
            Physics2D.SyncTransforms();

            _lastVerticalVelocity = 0f;
            _respawnImmunityTimer = 2.0f;
            _crushTimer = 0f;
            _jumpRequested = false;
            _jumpBufferCounter = 0f;

            if (snappedGround != null)
            {
                _isGrounded = true;
                _coyoteTimeCounter = _coyoteTime;
                _currentGroundCollider = snappedGround;
                _currentStitchable = snappedGround.GetComponentInParent<StitchableObject>();
                float slopeAngle = Vector2.Angle(snappedNormal, Vector2.up);
                _isOnSlope = slopeAngle > 0.5f && slopeAngle <= _maxSlopeAngle;
                _slopeNormal = snappedNormal;
                _slopeAngle = slopeAngle;
                _slopeParallel = new Vector2(snappedNormal.y, -snappedNormal.x).normalized;
            }

            if (_playerVisuals == null)
            {
                _playerVisuals = GetComponentInChildren<PlayerVisuals>();
            }

            if (_playerVisuals != null)
            {
                _playerVisuals.SetFacing(faceRight);
            }
        }

        private bool TryResolveGroundSnapPosition(Vector2 spawnPos, out Vector2 snappedPos, out Collider2D groundCollider, out Vector2 groundNormal)
        {
            snappedPos = spawnPos;
            groundCollider = null;
            groundNormal = Vector2.up;

            Physics2D.SyncTransforms();
            float feetOffsetY = _col != null ? (transform.position.y - _col.bounds.min.y) : 0.44f;
            float minSlopeNormalY = Mathf.Cos(_maxSlopeAngle * Mathf.Deg2Rad);

            const float startLift = 0.35f;
            Vector2 rayOrigin = new Vector2(spawnPos.x, spawnPos.y + startLift);
            float maxRayDist = Mathf.Max(1f, _maxRespawnGroundSnapDistance) + startLift;

            int hitCount = Physics2D.Raycast(rayOrigin, Vector2.down, _groundFilter, _snapHitBuffer, maxRayDist);
            float bestDist = float.MaxValue;
            RaycastHit2D bestHit = default;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _snapHitBuffer[i];
                if (hit.collider == null || IsSelfCollider(hit.collider) || hit.collider.isTrigger) continue;
                if (hit.distance <= 0.0001f) continue;
                if (hit.normal.y < minSlopeNormalY) continue;

                if (hit.distance < bestDist)
                {
                    bestDist = hit.distance;
                    bestHit = hit;
                }
            }

            if (bestHit.collider == null) return false;

            snappedPos = new Vector2(spawnPos.x, bestHit.point.y + feetOffsetY);
            groundCollider = bestHit.collider;
            groundNormal = bestHit.normal;
            return true;
        }

        public void ResetRespawnImmunity(float duration = 2.0f)
        {
            _respawnImmunityTimer = Mathf.Max(_respawnImmunityTimer, duration);
            _crushTimer = 0f;
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
                if (!_canMoveWhileAiming)
                {
                    StopHorizontalMovement();
                }
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

        private void Awake()
        {
            Instance = this;

            if (_rb == null)
            {
                _rb = GetComponent<Rigidbody2D>();
                if (_rb == null) _rb = GetComponentInParent<Rigidbody2D>();
            }

            if (_col == null)
            {
                _col = GetComponent<Collider2D>();
                if (_col == null) _col = GetComponentInChildren<Collider2D>();
            }

            if (_feetCollider == null)
            {
                Transform feetChild = transform.Find("Colliders/Feet");
                if (feetChild != null) _feetCollider = feetChild.GetComponent<Collider2D>();
            }

            _playerVisuals = GetComponentInChildren<PlayerVisuals>();
            if (_needleController == null)
            {
                _needleController = GetComponent<NeedleController>();
                if (_needleController == null) _needleController = GetComponentInParent<NeedleController>();
            }

            if (_rb != null)
            {
                _rb.freezeRotation = true;
                _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                _rb.gravityScale = 0f; // ตัด Engine Gravity ออกทั้งหมด เพื่อให้สคริปต์คำนวณอย่างแม่นยำ ไม่ลื่นไหลบนทางลาด
            }

            if (_groundLayer.value == 0)
            {
                _groundLayer = LayerMask.GetMask("Ground", "Stitchable", "Default");
            }

            _groundFilter = new ContactFilter2D();
            _groundFilter.SetLayerMask(_groundLayer);
            _groundFilter.useLayerMask = true;
            _groundFilter.useTriggers = false;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (_hazardKnockbackCooldownTimer > 0f)
            {
                _hazardKnockbackCooldownTimer -= Time.deltaTime;
            }
            ReadMovementInput();
            HandleJumpTimers();
        }

        private void FixedUpdate()
        {
            if (_respawnImmunityTimer > 0f)
            {
                _respawnImmunityTimer -= Time.fixedDeltaTime;
            }

            if (_ventSuperJumpTimer > 0f)
            {
                _ventSuperJumpTimer -= Time.fixedDeltaTime;
            }

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
            UpdateHaulingAudio();

            // แคชความเร็วแนวดิ่งขณะลอยตัว ถ้าความเร็วเป็นลบ (กำลังตก) ให้เก็บค่าตกสูงสุดไว้ก่อนที่จะถูก Physics Solver ปรับเป็น 0
            if (!_isGrounded && _rb.linearVelocity.y < -0.1f)
            {
                _lastVerticalVelocity = _rb.linearVelocity.y;
            }
            else if (_isGrounded)
            {
                _lastVerticalVelocity = Mathf.Min(_lastVerticalVelocity, _rb.linearVelocity.y);
            }
        }

        private void ApplyPlatformMovement()
        {
            if (_isGrounded && _currentStitchable != null)
            {
                Vector2 delta = _currentStitchable.FrameDelta;

                // สำหรับการเคลื่อนที่ในแนวดิ่งลง ช่วยดึงตำแหน่ง Y ตามลงไปด้วยเพื่อไม่ให้เท้าลอยกระตุก
                // ส่วนการเคลื่อนที่ขึ้นและแนวนอน ความเร็ว linearVelocity (groundVelY / PlatformVelocity.x)
                // ถูกจำลองผ่าน Physics simulation อยู่แล้ว จึงห้ามบวก delta ซ้ำซ้อน (ป้องกันอาการกระตุกดีดลอยตัวขึ้นฟ้า 2 เท่า)
                if (delta.y < -0.0001f && delta.sqrMagnitude < 4.0f)
                {
                    _rb.position = new Vector2(_rb.position.x, _rb.position.y + delta.y);
                }

                // หากยืนอยู่บนแท่นและไม่ได้กดกระโดด หากแท่นชะลอตัว หยุด หรือเริ่มเลื่อนลง (เช่น เมื่อถึงจุดยอด Apex)
                // ต้องตัดความเร็วแนวดิ่งของผู้เล่นไม่ให้ลอยทะลักสูงกว่าแท่น ป้องกันแรงเฉื่อยดีดตัวลอยขึ้นฟ้า
                if (!_jumpRequested)
                {
                    float platformVelY = _currentStitchable.CurrentVelocity.y;
                    if (platformVelY <= 0.05f || _rb.linearVelocity.y > platformVelY)
                    {
                        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, platformVelY);
                    }
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
                // การปลดสายจูงถูกย้ายไปทำใน ExecuteJump() เพื่อให้คำนวณโบนัส Super Jump จากท่อก๊าซให้เรียบร้อยก่อนปลด

                _jumpRequested = true;
                _jumpBufferCounter = 0f;
                _coyoteTimeCounter = 0f;
            }
        }

        private void ExecuteJump()
        {
            float extraBounce = 0f;
            bool ventErupting = CorpseGasVent2D.Instance != null && CorpseGasVent2D.Instance.IsErupting;

            // เงื่อนไข Super Jump Boost: ต้อง Tethered และ ยืนอยู่บนกล่อง (StitchableObject) เหนือท่อที่กำลังพ่นก๊าซ เท่านั้น
            bool isStandingOnCrate = _currentStitchable != null && (_isGrounded || _coyoteTimeCounter > 0f);
            if (!isStandingOnCrate && _needleController != null && _needleController.TetheredStitchable != null)
            {
                if (_currentGroundCollider != null && _currentGroundCollider.transform.IsChildOf(_needleController.TetheredStitchable.ObjectTransform))
                {
                    isStandingOnCrate = true;
                }
            }

            bool isCrateTethered = false;
            if (_currentStitchable != null)
            {
                isCrateTethered = _currentStitchable.IsTetheredToPlayer || _isHauling
                    || (_needleController != null && _needleController.IsTetheredToPlayer && (_needleController.TetheredStitchable == null || _needleController.TetheredStitchable.ObjectTransform == _currentStitchable.transform));
            }
            else if (_needleController != null && _needleController.IsTetheredToPlayer && _needleController.TetheredStitchable != null)
            {
                isCrateTethered = true;
            }

            GameObject crateObj = _currentStitchable != null ? _currentStitchable.gameObject : (_needleController?.TetheredStitchable?.ObjectTransform?.gameObject);
            bool isCrateInVent = ventErupting && crateObj != null && CorpseGasVent2D.Instance != null && CorpseGasVent2D.Instance.IsObjectInVent(crateObj);

            bool isVentBoostEligible = ventErupting && isStandingOnCrate && isCrateTethered && isCrateInVent;

            if (isVentBoostEligible)
            {
                _ventSuperJumpTimer = 0.8f;
                extraBounce = _ventSuperJumpBoost;
                CameraController2D.Instance?.Punch(Vector2.up, 0.08f);
                CameraController2D.Instance?.Shake(0.12f, 0.15f);

                if (_currentStitchable != null)
                {
                    _currentStitchable.OnPlayerJumpedOff(transform.position);
                }
            }
            else if (_currentStitchable != null)
            {
                _currentStitchable.OnPlayerJumpedOff(transform.position);

                if (_currentStitchable.CurrentVelocity.y > 0.25f)
                {
                    extraBounce = Mathf.Min(_currentStitchable.CurrentVelocity.y * 0.85f, 6.0f);
                    CameraController2D.Instance?.Punch(Vector2.up, 0.035f);
                }
            }

            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, _jumpForce + extraBounce);

            // ปลดสายจูงที่ผูกกับตัวผู้เล่นทันทีเมื่อกระโดด (ทั้งกระโดดจากพื้นหรือกระโดดออกจากกล่อง) เพื่อไม่ให้ Joint ดึงรั้งตัวผู้เล่นกลับลงมา
            if (_isHauling || (_needleController != null && _needleController.IsTetheredToPlayer))
            {
                ReleaseHaulingTether();
            }

            _isGrounded = false;
            _isOnSlope = false;
            _slopeNormal = Vector2.up;
            _slopeAngle = 0f;
            _slopeParallel = Vector2.right;
            _currentStitchable = null;
            _currentGroundCollider = null;
            // ถ้าระหว่างติด Jump Buffer ผู้เล่นปล่อยปุ่มกระโดดไปแล้ว ให้ตัดความสูงกระโดดทันที (Short Hop)
            // ยกเว้นตอนได้ Super Jump Boost จากท่อก๊าซ จะให้วิถีส่งพุ่งเต็มแรงเสมอ
            _isJumpCut = !isVentBoostEligible && !_isJumpButtonHeld;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_player_jump", 0.75f);
            }
            OnJumped?.Invoke();
        }

        private void ApplyHorizontalMovement()
        {
            if (_knockbackTimer > 0f)
            {
                _knockbackTimer -= Time.fixedDeltaTime;
                _knockbackVelocity.x = Mathf.MoveTowards(_knockbackVelocity.x, 0f, _deceleration * 0.75f * Time.fixedDeltaTime);
                _rb.linearVelocity = new Vector2(_knockbackVelocity.x, _rb.linearVelocity.y);
                return;
            }

            float speed = _moveSpeed;

            if (_throwSlowdownTimer > 0f)
            {
                _throwSlowdownTimer -= Time.fixedDeltaTime;
                speed *= _throwSlowdownMultiplier;
            }

            if (_isAiming)
            {
                if (!_canMoveWhileAiming)
                {
                    speed = 0f;
                }
                else
                {
                    speed *= _aimingSpeedMultiplier;
                }
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

            if (_isGrounded && _isOnSlope && !_jumpRequested)
            {
                // เคลื่อนที่ไปตามแนวความชันของทางลาด (Slope Parallel)
                Vector2 currentRelVel = _rb.linearVelocity - platVel;
                float currentSlopeSpeed = Vector2.Dot(currentRelVel, _slopeParallel);
                float newSlopeSpeed = Mathf.MoveTowards(currentSlopeSpeed, targetRelSpeed, accelRate * Time.fixedDeltaTime);

                Vector2 targetSlopeVel = _slopeParallel * newSlopeSpeed;
                _rb.linearVelocity = platVel + targetSlopeVel;
            }
            else
            {
                float currentRelVelX = _rb.linearVelocity.x - platVel.x;
                float newRelVelX = Mathf.MoveTowards(currentRelVelX, targetRelSpeed, accelRate * Time.fixedDeltaTime);

                _rb.linearVelocity = new Vector2(platVel.x + newRelVelX, _rb.linearVelocity.y);
            }
        }

        private void ApplyJumpPhysics()
        {
            if (_isGrounded) return;

            float velY = _rb.linearVelocity.y;
            float gravityMultiplier = _gravityScale; // 1.0f

            if (velY < 0f)
            {
                gravityMultiplier = _fallMultiplier; // 2.5f คืนค่าวิถีตกเดิม 100%
            }
            else if (velY > 0f && _isJumpCut)
            {
                gravityMultiplier = _lowJumpMultiplier; // 2.0f คืนค่า Short Hop เดิม 100%
            }

            velY += Physics2D.gravity.y * gravityMultiplier * Time.fixedDeltaTime;
            velY = Mathf.Max(velY, -_maxFallSpeed);
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, velY);
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
            Vector2 checkPos;
            Vector2 effectiveCheckSize;
            float feetBottomY;
            float colWidth;

            float colBottomY = _col != null ? _col.bounds.min.y : transform.position.y - 0.5f;
            float bodyWidth = _col != null ? _col.bounds.size.x : 0.5f;

            // Predictive landing probe: เมื่อตัวละครกำลังลอยตกมาจากอากาศ (velY <= 0)
            // ยื่นกล่องตรวจจับลงต่ำกว่าระดับฝ่าเท้าเล็กน้อย (0.12m) เพื่อให้ตรวจจับการแลนดิ้งได้ทันทีในเฟรมแรก
            // ป้องกัน Physics step delay ที่ทำให้เกิดอาการ Player land delay
            float probeDown = (!_isGrounded && _rb.linearVelocity.y <= 0.1f) ? 0.12f : 0.04f;

            if (_feetCollider != null)
            {
                colWidth = Mathf.Max(_feetCollider.bounds.size.x, bodyWidth * 0.85f);
                feetBottomY = Mathf.Min(_feetCollider.bounds.min.y, colBottomY);
                float checkHeight = Mathf.Max(_feetCollider.bounds.size.y, 0.16f) + probeDown;
                checkPos = new Vector2(transform.position.x, feetBottomY + checkHeight * 0.5f - probeDown);
                effectiveCheckSize = new Vector2(colWidth, checkHeight);
            }
            else
            {
                colWidth = bodyWidth * 0.85f;
                feetBottomY = colBottomY;
                float checkHeight = 0.16f + probeDown;
                checkPos = new Vector2(transform.position.x, feetBottomY + 0.08f - (probeDown * 0.5f));
                effectiveCheckSize = new Vector2(colWidth, checkHeight);
            }

            bool wasGrounded = _isGrounded;
            int hitCount = Physics2D.OverlapBox(checkPos, effectiveCheckSize, 0f, _groundFilter, _groundHitBuffer);

            Collider2D detectedGround = null;
            float playerFeetY = _col != null ? _col.bounds.min.y : transform.position.y;

            // คัดกรองเฉพาะ Collider ที่มีพื้นผิวรองรับฝ่าเท้าผู้เล่นจริงๆ (ปฏิเสธกำแพงแนวดิ่ง ป้องกันบั๊กลอยตัวไถติดข้างกำแพง)
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D groundCol = _groundHitBuffer[i];
                if (groundCol == null || IsSelfCollider(groundCol) || groundCol.isTrigger) continue;

                if (!IsValidGroundCollider(groundCol, checkPos, feetBottomY, colWidth, playerFeetY))
                {
                    continue;
                }

                StitchableObject so = groundCol.GetComponentInParent<StitchableObject>();
                if (so != null)
                {
                    detectedGround = groundCol;
                    break;
                }
                else if (detectedGround == null)
                {
                    detectedGround = groundCol;
                }
            }

            // Downward Ground Snapping: ตรวจสอบทั้งกรณีเดินลงทางลาดเตี้ย และกรณีแลนดิ้งลงพื้นใกล้ถึง
            // ยิง Raycast ลงไปในระยะสั้นเพื่อดูดเท้าติดพื้นทันที ป้องกันตัวลอยกระตุก/ดีเลย์
            float lastGroundVelY = _currentStitchable != null ? _currentStitchable.CurrentVelocity.y : 0f;
            float relSnapVelY = _rb.linearVelocity.y - lastGroundVelY;
            float minSlopeNormalY = Mathf.Cos(_maxSlopeAngle * Mathf.Deg2Rad);
            bool canSnapLanding = !wasGrounded && relSnapVelY <= -0.5f;
            if (_enableGroundSnapping && detectedGround == null && !_jumpRequested && ((wasGrounded && (relSnapVelY <= 0.2f || _currentStitchable != null)) || canSnapLanding))
            {
                Vector2 rayStart = new Vector2(checkPos.x, feetBottomY + 0.05f);
                float snapDist = _groundSnapDistance + 0.05f;
                int snapCount = Physics2D.Raycast(rayStart, Vector2.down, _groundFilter, _snapHitBuffer, snapDist);
                for (int s = 0; s < snapCount; s++)
                {
                    RaycastHit2D snapHit = _snapHitBuffer[s];
                    if (snapHit.collider != null && !IsSelfCollider(snapHit.collider) && !snapHit.collider.isTrigger && snapHit.normal.y >= minSlopeNormalY)
                    {
                        float snapGap = snapHit.point.y - feetBottomY;
                        if (snapGap <= 0.001f && Mathf.Abs(snapGap) <= _groundSnapDistance)
                        {
                            _rb.position = new Vector2(_rb.position.x, _rb.position.y + snapGap);
                            detectedGround = snapHit.collider;
                            break;
                        }
                    }
                }
            }

            if (detectedGround != null)
            {
                UpdateSlopeState(checkPos, feetBottomY, colWidth);
            }
            else
            {
                _isOnSlope = false;
                _slopeNormal = Vector2.up;
                _slopeAngle = 0f;
                _slopeParallel = Vector2.right;
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

            if (_isOnSlope && detectedGround != null)
            {
                // บนทางลาด การเดินขึ้นจะเกิดความเร็วแนวดิ่งตามความชันตามธรรมชาติ
                // จึงยอมรับความเร็วแนวดิ่งที่สอดคล้องกับการไต่ทางลาดได้ โดยไม่หลุดสถานะ Grounded
                float maxAllowedUpwardVel = Mathf.Abs(_moveSpeed * Mathf.Sin(_slopeAngle * Mathf.Deg2Rad)) + 0.75f;
                _isGrounded = !_jumpRequested && relVertVelocity <= maxAllowedUpwardVel;
            }
            else
            {
                // นับว่าอยู่บนพื้นเมื่อสัมผัส Collider พื้น และไม่ได้กำลังกระโดดพุ่งขึ้นหนีออกจากพื้นผิว
                // ป้องกันกรณีแท่นกำลังเลื่อนลง (groundVelY < 0) หรือกำลังยกตัวขึ้น (groundVelY > 0) จนถูกปฏิเสธ Grounded
                bool isAscendingAway = _jumpRequested || (relVertVelocity > 0.8f && _rb.linearVelocity.y > 0.8f);
                _isGrounded = detectedGround != null && !isAscendingAway;
            }

            if (!_isGrounded)
            {
                _isOnSlope = false;
                _slopeNormal = Vector2.up;
                _slopeAngle = 0f;
                _slopeParallel = Vector2.right;
            }

            if (_isGrounded && !_jumpRequested)
            {
                if (_isOnSlope)
                {
                    // หากยืนอยู่นิ่งๆ บนทางลาด ล็อคความเร็วให้หยุดนิ่งสนิทเพื่อไม่ให้ลื่นไหลลงทางลาด
                    if (Mathf.Abs(_horizontalInput) < 0.01f)
                    {
                        _rb.linearVelocity = new Vector2(PlatformVelocity.x, groundVelY);
                    }
                }
                else
                {
                    // เมื่อยืนอยู่บนพื้นราบหรือแท่นเคลื่อนที่ ล็อคความเร็วแนวดิ่งให้ซิงค์ตรงกับแท่น เพื่อไม่ให้ลื่นไถลหรือลอยตัว
                    if (_currentStitchable != null)
                    {
                        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, groundVelY);
                    }
                    else if (_rb.linearVelocity.y < groundVelY)
                    {
                        _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, groundVelY);
                    }
                }
            }

            if (!wasGrounded && _isGrounded)
            {
                _isJumpCut = false;
                HandleLandingImpact();
            }

            if (_isGrounded && Mathf.Abs(_horizontalInput) > 0.05f && Mathf.Abs(RelativeVelocity.x) > 0.1f)
            {
                HandleWalkingVibration();
            }
        }

        private void UpdateCachedGround(Collider2D newGround)
        {
            if (_currentGroundCollider == newGround) return;

            _currentGroundCollider = newGround;
            _currentStitchable = newGround != null ? newGround.GetComponentInParent<StitchableObject>() : null;

            // การป้องกัน hoverboard exploit เมื่อยืนบนกล่องที่ Tether ไว้ ถูกจัดการผ่านการ suspend Joint ฟิสิกส์
            // ใน StitchableObject.UpdatePlayerHarness แล้ว จึงคงสถานะ Tether ต่อไปได้โดยไม่ต้องตัดสายด้ายทิ้ง
        }

        private bool IsValidGroundCollider(Collider2D groundCol, Vector2 checkPos, float feetBottomY, float colWidth, float playerFeetY)
        {
            if (groundCol == null || IsSelfCollider(groundCol) || groundCol.isTrigger) return false;

            // ตรวจสอบการทับซ้อนในแนวนอน (Horizontal Overlap) ระหว่างตัวผู้เล่นกับวัตถุ
            // หากตัวผู้เล่นอยู่นอกขอบซ้าย/ขวาของวัตถุ (เช่น เบียดกำแพงหรือผนังกล่องด้านข้าง) ต้องไม่นับเป็นพื้นผิวใต้เท้าเด็ดขาด
            float playerHalfWidth = _col != null ? _col.bounds.extents.x : (colWidth * 0.5f);
            float playerMinX = transform.position.x - playerHalfWidth;
            float playerMaxX = transform.position.x + playerHalfWidth;
            bool hasHorizontalOverlap = playerMaxX > (groundCol.bounds.min.x + 0.04f) && playerMinX < (groundCol.bounds.max.x - 0.04f);

            if (!hasHorizontalOverlap)
            {
                return false;
            }

            StitchableObject so = groundCol.GetComponentInParent<StitchableObject>();
            if (so != null)
            {
                // สำหรับกล่อง/แท่นเคลื่อนที่ เมื่อมี Horizontal Overlap และเท้าอยู่ที่ระดับหลังกล่องขึ้นไป (รองรับการแลนดิ้งด้วยความเร็วสูง)
                if (playerFeetY >= groundCol.bounds.max.y - 0.35f)
                {
                    return true;
                }
            }

            // สำหรับ BoxCollider2D ทั่วไปที่ไม่ได้เอียง (เช่น กำแพง เสา บล็อก) ถ้าเท้าอยู่ต่ำกว่าขอบบนเกิน 0.25m แปลว่ากำลังเบียดกำแพงด้านข้างในอากาศ
            bool isComposite = groundCol.transform.parent != null && groundCol.transform.parent.GetComponent<CompositeCollider2D>() != null;
            bool isRotated = Mathf.Abs(Mathf.DeltaAngle(groundCol.transform.eulerAngles.z, 0f)) > 1f;
            if (groundCol is BoxCollider2D && !isComposite && !isRotated)
            {
                if (playerFeetY < groundCol.bounds.max.y - 0.25f)
                {
                    return false;
                }
            }

            // ตรวจสอบผิวด้านล่างด้วย Raycast แนวดิ่ง เพื่อยืนยันว่ามีพื้นผิวระนาบหรือทางลาดที่เดินได้รองรับใต้เท้าจริง (Normal ชี้ขึ้น)
            float rayDistance = 0.25f + _slopeCheckDistance;
            float sideOffset = colWidth * 0.35f;
            Vector2 rayOrigin = new Vector2(checkPos.x, feetBottomY + 0.1f);

            RaycastHit2D hitCenter = Physics2D.Raycast(rayOrigin, Vector2.down, rayDistance, _groundLayer);
            if (IsValidGroundHit(hitCenter, groundCol)) return true;

            RaycastHit2D hitLeft = Physics2D.Raycast(rayOrigin + Vector2.left * sideOffset, Vector2.down, rayDistance, _groundLayer);
            if (IsValidGroundHit(hitLeft, groundCol)) return true;

            RaycastHit2D hitRight = Physics2D.Raycast(rayOrigin + Vector2.right * sideOffset, Vector2.down, rayDistance, _groundLayer);
            if (IsValidGroundHit(hitRight, groundCol)) return true;

            // หากเท้าอยู่เหนือขอบบนของ Collider อิสระ และมี Horizontal Overlap
            if (playerFeetY >= groundCol.bounds.max.y - 0.25f)
            {
                return true;
            }

            return false;
        }

        private bool IsValidGroundHit(RaycastHit2D hit, Collider2D targetCol)
        {
            if (hit.collider == null || IsSelfCollider(hit.collider) || hit.collider.isTrigger) return false;
            if (targetCol != null && hit.collider != targetCol && hit.collider.attachedRigidbody != targetCol.attachedRigidbody) return false;

            // พื้นผิวต้องชี้ขึ้นด้านบนในมุมที่ไม่ชันเกินกำหนด (ไม่เกิน _maxSlopeAngle เช่น 55 องศา)
            float minNormalY = Mathf.Cos(_maxSlopeAngle * Mathf.Deg2Rad);
            return hit.normal.y >= minNormalY;
        }

        private void UpdateSlopeState(Vector2 feetCenter, float feetBottomY, float colWidth)
        {
            float rayDistance = 0.1f + _slopeCheckDistance;
            Vector2 rayOrigin = new Vector2(feetCenter.x, feetBottomY + 0.1f);

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, Vector2.down, rayDistance, _groundLayer);
            if (IsValidSlopeHit(hit))
            {
                SetSlopeFromHit(hit);
                return;
            }

            // Probe ด้านซ้ายและขวาของฝ่าเท้าเผื่อกรณีเดินอยู่ตรงรอยต่อของทางลาด
            float sideOffset = colWidth * 0.35f;
            RaycastHit2D hitLeft = Physics2D.Raycast(rayOrigin + Vector2.left * sideOffset, Vector2.down, rayDistance, _groundLayer);
            if (IsValidSlopeHit(hitLeft))
            {
                SetSlopeFromHit(hitLeft);
                return;
            }

            RaycastHit2D hitRight = Physics2D.Raycast(rayOrigin + Vector2.right * sideOffset, Vector2.down, rayDistance, _groundLayer);
            if (IsValidSlopeHit(hitRight))
            {
                SetSlopeFromHit(hitRight);
                return;
            }

            _isOnSlope = false;
            _slopeNormal = Vector2.up;
            _slopeAngle = 0f;
            _slopeParallel = Vector2.right;
        }

        private bool IsValidSlopeHit(RaycastHit2D hit)
        {
            if (hit.collider == null || IsSelfCollider(hit.collider) || hit.collider.isTrigger) return false;
            float angle = Vector2.Angle(hit.normal, Vector2.up);
            return angle > 0.01f && angle <= _maxSlopeAngle;
        }

        private void SetSlopeFromHit(RaycastHit2D hit)
        {
            _isOnSlope = true;
            _slopeNormal = hit.normal;
            _slopeAngle = Vector2.Angle(hit.normal, Vector2.up);
            Vector2 parallel = new Vector2(hit.normal.y, -hit.normal.x);
            _slopeParallel = parallel.x < 0f ? -parallel : parallel;
        }

        private void HandleLandingImpact()
        {
            float impactSpeed = Mathf.Abs(_lastVerticalVelocity);
            OnLanded?.Invoke(impactSpeed);

            if (impactSpeed > 1.0f)
            {
                int stitchableLayer = LayerMask.NameToLayer("Stitchable");
                bool isStitchableLand = _currentStitchable != null
                    || (_currentGroundCollider != null && (stitchableLayer != -1 && _currentGroundCollider.gameObject.layer == stitchableLayer))
                    || (_currentGroundCollider != null && _currentGroundCollider.GetComponentInParent<StitchableObject>() != null);

                if (AudioManager.Instance != null)
                {
                    float vol = Mathf.Clamp(impactSpeed / 8f, 0.4f, 1.0f);
                    string sfx = isStitchableLand ? "sfx_player_land_wood" : "sfx_player_jump_land";
                    AudioManager.Instance.PlaySFX(sfx, vol);
                }

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
            if (!_enableCrushDeath || !_isGrounded || _isInputLocked || _respawnImmunityTimer > 0f || (CheckpointManager.Instance != null && CheckpointManager.Instance.IsRespawning))
            {
                _crushTimer = 0f;
                return;
            }

            // 1. ตรวจสอบความสูงและตำแหน่งของผู้เล่น
            float feetBottomY = _col != null ? _col.bounds.min.y : transform.position.y - 0.5f;
            float playerHeight = _col != null ? _col.bounds.size.y : 1.0f;
            Vector2 headCenter = _col != null
                ? new Vector2(_col.bounds.center.x, _col.bounds.center.y)
                : (Vector2)transform.position;

            // ยิง Raycast ตรงขึ้นด้านบนจากกึ่งกลางลำตัวผู้เล่น เพื่อวัดระยะเพดานเหนือศีรษะ
            // (ใช้แกน X กึ่งกลางลำตัว เพื่อไม่ให้โดนขอบกำแพงหรือกล่องที่ยืนชิดด้านข้างหลอกเป็น False Positive)
            float maxCheckDistance = playerHeight + 0.5f;
            RaycastHit2D ceilingHit = Physics2D.Raycast(headCenter, Vector2.up, maxCheckDistance, _groundLayer);

            if (ceilingHit.collider != null && !IsSelfCollider(ceilingHit.collider) && !ceilingHit.collider.isTrigger)
            {
                // ตรวจสอบว่าพื้นผิวเหนือศีรษะหันหน้าลงล่างจริง (เป็นเพดาน ไม่ใช่กำแพงแนวตั้ง)
                if (ceilingHit.normal.y <= -0.7f)
                {
                    // คำนวณระยะช่องว่างจริงระหว่างพื้นใต้เท้ากับเพดานเหนือหัว (Clearance)
                    float clearance = ceilingHit.point.y - feetBottomY;

                    // คำนวณความเร็วในการบีบอัดเข้าหากัน (Closing Speed)
                    float floorUpSpeed = _currentStitchable != null
                        ? _currentStitchable.CurrentVelocity.y
                        : (_currentGroundCollider != null && _currentGroundCollider.attachedRigidbody != null
                            ? _currentGroundCollider.attachedRigidbody.linearVelocity.y
                            : 0f);

                    float ceilingDownSpeed = 0f;
                    if (ceilingHit.rigidbody != null)
                    {
                        ceilingDownSpeed = Mathf.Max(ceilingDownSpeed, -ceilingHit.rigidbody.linearVelocity.y);
                    }
                    StitchableObject ceilingSo = ceilingHit.collider.GetComponentInParent<StitchableObject>();
                    if (ceilingSo != null)
                    {
                        ceilingDownSpeed = Mathf.Max(ceilingDownSpeed, -ceilingSo.CurrentVelocity.y);
                    }

                    float closingSpeed = floorUpSpeed + ceilingDownSpeed;

                    // ตรวจสอบสิทธิ์การบดอัด (CrushHazard Component หรือ Tag)
                    bool isCeilingHazard = CrushHazard.IsCrushHazard(ceilingHit.collider.gameObject, out CrushHazard ceilingComp);
                    bool canCeilingSqueeze = !isCeilingHazard || (ceilingComp == null || ceilingComp.CanCrushSqueeze);

                    // ถ้าระยะ Clearance แคบกว่าความสูงตัวละคร (< 85% ของความสูง) และมีแรงบดอัดบีบเข้าหากัน (closingSpeed > 0.05 m/s)
                    // หรือถูกบดอัดจนแบนราบ (< 60% ของความสูง)
                    if (canCeilingSqueeze && clearance > 0f && clearance < playerHeight * 0.85f && (closingSpeed > 0.05f || clearance < playerHeight * 0.6f))
                    {
                        _crushTimer += Time.fixedDeltaTime;
                        if (_crushTimer >= _crushDeathDelay)
                        {
                            _crushTimer = 0f;
                            ExecuteCrushDeath(
                                $"Vertical Squeeze: clearance={clearance:F2}m < {playerHeight * 0.85f:F2}m (closingSpeed={closingSpeed:F2}m/s) between '{(_currentGroundCollider != null ? _currentGroundCollider.name : "Ground")}' and '{ceilingHit.collider.name}'",
                                ceilingHit.collider);
                        }
                        return;
                    }
                }
            }

            _crushTimer = 0f;
        }

        private void OnDisable()
        {
            StopHaulingAudio();
            RestoreIgnoredCrushColliders();
        }

        private void UpdateHaulingAudio()
        {
            if (AudioManager.Instance == null || string.IsNullOrEmpty(_haulingDragSFXName)) return;

            // เช็ค Layer ของพื้นใต้เท้าโดยตรง: ถ้าเหยียบวัตถุ Layer "Stitchable" (layer 7) หรือ _currentStitchable ไม่เป็น null จะไม่เล่นเสียงลาก
            int stitchableLayer = LayerMask.NameToLayer("Stitchable");
            bool isStandingOnStitchable = _currentStitchable != null
                || (_currentGroundCollider != null && (stitchableLayer != -1 && _currentGroundCollider.gameObject.layer == stitchableLayer))
                || (_currentGroundCollider != null && _currentGroundCollider.GetComponentInParent<StitchableObject>() != null);

            // เช็คว่ากล่องที่ Tether อยู่มีการเคลื่อนที่ในแกน X จริงหรือไม่
            bool isCrateMoving = false;
            IStitchable tethered = (_needleController != null && _needleController.TetheredStitchable != null)
                ? _needleController.TetheredStitchable
                : _currentStitchable;
            if (tethered != null && tethered.ObjectTransform != null)
            {
                Rigidbody2D crateRb = tethered.ObjectTransform.GetComponent<Rigidbody2D>();
                if (crateRb != null)
                {
                    isCrateMoving = Mathf.Abs(crateRb.linearVelocity.x) > 0.15f;
                }
            }

            // กำลังลากของอยู่บนพื้น (ไม่ได้ยืนอยู่บน Stitchable), ผู้เล่นกำลังเดิน และกล่องขยับตามจริง
            bool isDragging = _isHauling && _isGrounded && !isStandingOnStitchable
                && Mathf.Abs(_horizontalInput) > 0.1f && Mathf.Abs(_rb.linearVelocity.x) > 0.2f
                && isCrateMoving;

            if (isDragging)
            {
                if (!_isDraggingAudioPlaying)
                {
                    _isDraggingAudioPlaying = true;
                    AudioManager.Instance.PlayLoopingSFX(_haulingDragSFXName, 1.0f);
                }
            }
            else if (_isDraggingAudioPlaying)
            {
                StopHaulingAudio();
            }
        }

        private void StopHaulingAudio()
        {
            if (_isDraggingAudioPlaying)
            {
                _isDraggingAudioPlaying = false;
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.StopLoopingSFX();
                }
            }
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HandleOverheadCollisionImpact(collision, isEnter: true);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            HandleOverheadCollisionImpact(collision, isEnter: false);
        }

        private void HandleOverheadCollisionImpact(Collision2D collision, bool isEnter)
        {
            if (!_enableCrushDeath || _isInputLocked || _respawnImmunityTimer > 0f || (CheckpointManager.Instance != null && CheckpointManager.Instance.IsRespawning)) return;

            // ตรวจสอบว่าวัตถุที่ชนเป็น CrushHazard (มี Component CrushHazard หรือติด Tag 'Hazard' / 'CrushHazard')
            bool isHazard = CrushHazard.IsCrushHazard(collision.gameObject, out CrushHazard hazardComp);
            if (!isHazard) return;
            if (hazardComp != null && !hazardComp.CanCrushFalling) return;

            // คำนวณความเร็วตกของวัตถุอันตราย
            float lethalSpeed = hazardComp != null ? hazardComp.LethalImpactSpeed : _crushImpactVelocityThreshold;
            float objDownSpeed = 0f;
            if (collision.rigidbody != null)
            {
                objDownSpeed = Mathf.Max(objDownSpeed, -collision.rigidbody.linearVelocity.y);
            }
            StitchableObject so = collision.collider.GetComponentInParent<StitchableObject>();
            if (so != null)
            {
                objDownSpeed = Mathf.Max(objDownSpeed, -so.CurrentVelocity.y);
            }

            // คำนวณขอบเขตศีรษะของผู้เล่น
            float playerHeadY = _col != null ? _col.bounds.max.y - 0.08f : transform.position.y + 0.5f;

            for (int i = 0; i < collision.contactCount; i++)
            {
                ContactPoint2D contact = collision.GetContact(i);

                // ต้องเป็นจุดสัมผัสที่อยู่บริเวณศีรษะ และ Normal ต้องกดลงล่างชัดเจน (normal.y <= -0.7f)
                // เพื่อคัดกรองการเดินชนด้านข้างของกล่องออก 100%
                if (contact.point.y >= playerHeadY && contact.normal.y <= -0.7f)
                {
                    // กรณีร่วงตกจากที่สูงกระแทกหัว (Falling Impact)
                    if (isEnter && objDownSpeed >= lethalSpeed && _isGrounded)
                    {
                        ExecuteCrushDeath(
                            $"Crushed by falling hazard '{collision.gameObject.name}' (downSpeed={objDownSpeed:F2} >= {lethalSpeed:F2})",
                            collision.collider);
                        return;
                    }

                    // กรณีวัตถุหนักกดทับแช่บนศีรษะขณะยืนติดพื้น
                    if (_isGrounded && objDownSpeed > 0.1f)
                    {
                        _crushTimer += Time.fixedDeltaTime;
                        if (_crushTimer >= _crushDeathDelay)
                        {
                            _crushTimer = 0f;
                            ExecuteCrushDeath(
                                $"Crushed under descending hazard '{collision.gameObject.name}'",
                                collision.collider);
                            return;
                        }
                    }
                }
            }
        }

        private void ExecuteCrushDeath(string reason = "Unknown crush", Collider2D crushingCollider = null)
        {
            Debug.Log($"[Crush Death] Player died from Crush hazard! Reason: {reason} | Pos: {transform.position} | Ground: {(_currentGroundCollider != null ? _currentGroundCollider.name : "null")}");

            IgnoreCrushingCollider(crushingCollider);
            StopHorizontalMovement();

            if (CheckpointManager.Instance != null && !CheckpointManager.Instance.IsRespawning)
            {
                CameraController2D.Instance?.Shake(0.35f, 0.45f);
                CheckpointManager.Instance.TriggerPlayerDeath(shakeIntensity: 0.35f, shakeDuration: 0.45f);
            }
            else if (CheckpointManager.Instance == null)
            {
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
            }
        }

        private void IgnoreCrushingCollider(Collider2D crushingCollider)
        {
            if (crushingCollider == null || crushingCollider == _currentGroundCollider) return;

            if (_col != null)
            {
                Physics2D.IgnoreCollision(_col, crushingCollider, true);
            }
            if (_feetCollider != null)
            {
                Physics2D.IgnoreCollision(_feetCollider, crushingCollider, true);
            }

            if (!_ignoredCrushColliders.Contains(crushingCollider))
            {
                _ignoredCrushColliders.Add(crushingCollider);
            }
        }

        private void RestoreIgnoredCrushColliders()
        {
            for (int i = 0; i < _ignoredCrushColliders.Count; i++)
            {
                Collider2D ignored = _ignoredCrushColliders[i];
                if (ignored == null) continue;

                if (_col != null)
                {
                    Physics2D.IgnoreCollision(_col, ignored, false);
                }
                if (_feetCollider != null)
                {
                    Physics2D.IgnoreCollision(_feetCollider, ignored, false);
                }
            }
            _ignoredCrushColliders.Clear();
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
            float safeSideOffset = _col != null ? Mathf.Min(_cornerRaySideOffset, _col.bounds.extents.x * 0.85f) : _cornerRaySideOffset;
            Vector2 leftOrigin = center + Vector2.left * safeSideOffset;
            Vector2 rightOrigin = center + Vector2.right * safeSideOffset;

            RaycastHit2D hitLeft = Physics2D.Raycast(leftOrigin, Vector2.up, _headRayDistance, _groundLayer);
            RaycastHit2D hitRight = Physics2D.Raycast(rightOrigin, Vector2.up, _headRayDistance, _groundLayer);

            // เฉพาะกรณีที่ชนเพดานเหนือศีรษะ (Normal หันลงด้านล่าง normal.y < -0.3f) เท่านั้น ไม่เบี่ยงตำแหน่งเมื่อเบียดกำแพงแนวตั้ง
            bool isRightSolidCeiling = hitRight.collider != null && !IsSelfCollider(hitRight.collider) && !hitRight.collider.isTrigger && hitRight.normal.y < -0.3f;
            bool isLeftSolidCeiling = hitLeft.collider != null && !IsSelfCollider(hitLeft.collider) && !hitLeft.collider.isTrigger && hitLeft.normal.y < -0.3f;

            // ขวาชนเพดาน แต่ซ้ายโล่ง -> ดันหลบไปทางซ้ายให้พ้นมุมเพดาน
            if (isRightSolidCeiling && !isLeftSolidCeiling)
            {
                _rb.position = new Vector2(_rb.position.x - _headNudgeStep, _rb.position.y);
            }
            // ซ้ายชนเพดาน แต่ขวาโล่ง -> ดันหลบไปทางขวาให้พ้นมุมเพดาน
            else if (isLeftSolidCeiling && !isRightSolidCeiling)
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

            // ต้องไม่ทำงานขณะที่ผู้เล่นกำลังพุ่งขึ้นในแนวดิ่งด้วยแรงกระโดด เพื่อให้ผู้เล่นกระโดดข้ามกล่อง/สิ่งกีดขวางได้อย่างลื่นไหล
            // (Ledge Step-up จะช่วยยกตัวเฉพาะเมื่ออยู่ที่จุดสูงสุด Apex หรือกำลังร่วงลงมาเฉียดขอบแท่นเท่านั้น)
            if (_rb.linearVelocity.y > 1.0f) return;

            Vector2 forward = Vector2.right * moveDir;
            float extentsY = _col.bounds.extents.y;
            Vector2 feetPos = (Vector2)transform.position + Vector2.down * (extentsY * 0.75f); // ระดับเท้า/ข้อเท้า
            Vector2 kneePos = (Vector2)transform.position + Vector2.down * (extentsY * 0.25f); // ระดับเข่า
            Vector2 waistPos = transform.position; // กึ่งกลางลำตัว/ระดับเอว
            Vector2 headPos = (Vector2)transform.position + Vector2.up * (extentsY * 0.85f); // ระดับเหนืออก/หัว

            float fwdDist = Mathf.Max(0.15f, Mathf.Abs(_ledgeRayForwardDistance));
            float maxStepUp = Mathf.Max(0.2f, Mathf.Abs(_ledgeStepUpDistance));

            // ตรวจสอบว่าศีรษะด้านหน้าโล่ง (ถ้ามีกำแพงสูงขวางอยู่ข้างหน้าจะไม่ทำ Step-up)
            RaycastHit2D hitHead = Physics2D.Raycast(headPos, forward, fwdDist, _groundLayer);
            if (hitHead.collider != null && !IsSelfCollider(hitHead.collider) && !hitHead.collider.isTrigger) return;

            // ตรวจสอบว่าระดับเท้า, เข่า หรือเอว ชนขอบมุมของวัตถุ (เช่น Box.prefab หรือมุมแพลตฟอร์ม)
            RaycastHit2D hitLower = Physics2D.Raycast(feetPos, forward, fwdDist, _groundLayer);
            if (hitLower.collider == null || IsSelfCollider(hitLower.collider) || hitLower.collider.isTrigger)
            {
                hitLower = Physics2D.Raycast(kneePos, forward, fwdDist, _groundLayer);
            }
            if (hitLower.collider == null || IsSelfCollider(hitLower.collider) || hitLower.collider.isTrigger)
            {
                hitLower = Physics2D.Raycast(waistPos, forward, fwdDist, _groundLayer);
            }

            if (hitLower.collider != null && !IsSelfCollider(hitLower.collider) && !hitLower.collider.isTrigger)
            {
                // คำนวณจุด probe ด้านบนของวัตถุโดยตรงจาก Bounds ของ Collider หรือตำแหน่งที่ชน
                float topBoundY = hitLower.collider.bounds.max.y;
                float currentFeetY = transform.position.y - extentsY;
                float heightDifference = topBoundY - currentFeetY;

                // ยิง probe จากเหนือขอบแท่นลงมาเพื่อหาผิวด้านบนที่แม่นยำ
                Vector2 probeTop = new Vector2(hitLower.point.x + (moveDir * 0.12f), topBoundY + 0.15f);
                RaycastHit2D hitLedgeTop = Physics2D.Raycast(probeTop, Vector2.down, Mathf.Max(0.6f, heightDifference + 0.3f), _groundLayer);

                float surfaceY = hitLedgeTop.collider != null && !IsSelfCollider(hitLedgeTop.collider) && !hitLedgeTop.collider.isTrigger ? hitLedgeTop.point.y : topBoundY;
                heightDifference = surfaceY - currentFeetY;

                // ถ้าระดับขอบบนของกล่อง/แท่นอยู่สูงกว่าเท้าไม่เกิน maxStepUp
                if (heightDifference > -0.05f && heightDifference <= maxStepUp)
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
                        if (hit != null && !IsSelfCollider(hit))
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
            float colWidth = col != null ? col.bounds.size.x : 0.5f;

            Vector2 checkPos;
            Vector2 effectiveCheckSize;

            if (_feetCollider != null)
            {
                checkPos = _feetCollider.bounds.center;
                effectiveCheckSize = _feetCollider.bounds.size;
            }
            else
            {
                float fallbackFeetY = col != null ? col.bounds.min.y : transform.position.y - 0.5f;
                checkPos = new Vector2(transform.position.x, fallbackFeetY);
                effectiveCheckSize = new Vector2(colWidth * 0.85f, 0.14f);
            }

            // 1. Ground Check Box (เขียวเมื่ออยู่บนพื้น / แดงเมื่อลอย)
            Gizmos.color = _isGrounded ? Color.green : Color.red;
            Gizmos.DrawWireCube(checkPos, effectiveCheckSize);

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

            // 6. Slope Normal & Direction (เหลือง = Normal, ฟ้า/เขียว = Slope Tangent)
            if (_isOnSlope)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawRay(checkPos, _slopeNormal * 0.6f);
                Gizmos.color = new Color(0f, 1f, 0.8f, 0.9f);
                Gizmos.DrawRay(checkPos - _slopeParallel * 0.4f, _slopeParallel * 0.8f);
            }
        }
    }
}