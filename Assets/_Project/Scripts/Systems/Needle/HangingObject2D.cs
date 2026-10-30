using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Gameplay;

namespace ThailandHorrorJam.Systems.Needle
{
    public enum HangingMode
    {
        [Tooltip("Drawbridge: Rotates down around a hinge/pivot until resting flat as a solid walkway.")]
        Drawbridge = 0,

        [Tooltip("FreeFall: Drops straight down with gravity when cut.")]
        FreeFall = 1
    }

    /// <summary>
    /// Universal hanging object system that suspends any object (such as a drawbridge plank,
    /// a hanging stitchable crate, or heavy prop) with a cuttable red suture thread from the ceiling.
    ///
    /// Modes:
    /// - Drawbridge: Rotates down smoothly to a flat rest angle upon being cut.
    /// - FreeFall: Falls straight down with gravity upon being cut. If a StitchableObject component
    ///   is present, it seamlessly delegates all IStitchable interactions (tethering, hauling, pinning)
    ///   to the StitchableObject once dropped!
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class HangingObject2D : MonoBehaviour, IStitchable, IHintDismissCondition
    {
        [Header("Mode")]
        [Tooltip("Drawbridge: Rotates around a pivot down to flat angle. FreeFall: Drops straight down with gravity.")]
        [SerializeField] private HangingMode _mode = HangingMode.Drawbridge;

        [Header("Ceiling & Attachment Anchors")]
        [Tooltip("Transform representing the anchor point on the ceiling/wall where the thread is tied.")]
        [SerializeField] private Transform _ceilingAnchor;

        [Tooltip("Attachment point on this object where the thread is tied. If null, uses object transform.")]
        [SerializeField] private Transform _objectAnchor;

        [Header("Drawbridge Settings (If Mode = Drawbridge)")]
        [Tooltip("Pivot point around which the bridge rotates. If null, rotates around this object's transform position.")]
        [SerializeField] private Transform _hingePivot;

        [Tooltip("Target Z rotation (in degrees) when fully lowered/flat (usually 0 for horizontal bridge).")]
        [SerializeField] private float _flatRestAngle = 0f;

        [Tooltip("Speed (degrees/sec) at which the bridge rotates down upon being cut.")]
        [SerializeField] private float _fallRotationSpeed = 160f;

        [Header("Designer Dimensions (Drawbridge Only)")]
        [Tooltip("หากเปิด จะปรับขนาด BoxCollider2D และตำแหน่ง TipAnchor ตามความยาวและความหนา (ปิดหากต้องการตั้งค่า Collider ด้วยตัวเอง)")]
        [SerializeField] private bool _manageBridgeGeometry = true;

        [Tooltip("หากเปิด จะปรับขนาด Transform.localScale และตำแหน่งของ child 'Visual' ตามความยาวและความหนา (ปิดไว้เป็นค่าเริ่มต้น เพื่อคงขนาดและตำแหน่ง Visual เดิมของ Sprite/Prefab ไว้ ไม่ให้ถูกดัดแปลง)")]
        [SerializeField] private bool _manageVisualTransform = false;

        [Tooltip("Bridge plank length. Changing this automatically resizes BoxCollider2D, TipAnchor (and Visual if enabled)!")]
        [Min(0.5f)]
        [SerializeField] private float _bridgeLength = 2.4f;

        [Tooltip("Bridge plank thickness/height.")]
        [Min(0.05f)]
        [SerializeField] private float _bridgeThickness = 0.35f;

        [Header("Free Fall Settings (If Mode = FreeFall)")]
        [Tooltip("Gravity scale applied to the Rigidbody2D when falling after being cut.")]
        [SerializeField] private float _freeFallGravityScale = 1.5f;

        [Tooltip("Initial downward speed (m/s) applied immediately when FreeFall starts (0 = fall from rest).")]
        [Min(0f)]
        [SerializeField] private float _freeFallInitialDownwardSpeed = 0f;

        [Tooltip("Lock the object as Static ground once it lands on the floor (useful for static platforms/steps). Ignored if a StitchableObject is attached.")]
        [SerializeField] private bool _lockAsStaticOnLanding = false;

        [Header("Player Trigger Drop (Ambush Trap)")]
        [Tooltip("Automatically sever the hanging thread when the player enters the trigger zone below.")]
        [SerializeField] private bool _dropOnPlayerTrigger = false;

        [Tooltip("Size of the player detection box in world units (relative to initial hanging position).")]
        [SerializeField] private Vector2 _triggerBoxSize = new Vector2(1.6f, 4.2f);

        [Tooltip("Offset of the player detection box from initial hanging position.")]
        [SerializeField] private Vector2 _triggerBoxOffset = new Vector2(-0.5f, -2.1f);

        [Tooltip("Optional delay (seconds) between player entering the trigger zone and the thread snapping.")]
        [Min(0f)]
        [SerializeField] private float _dropDelay = 0f;

        [Tooltip("SFX played via AudioManager when the thread snaps from the player trigger.")]
        [SerializeField] private string _snapSFXName = "sfx_thread_cut";

        [Header("Landing Impact FX")]
        [Tooltip("Trigger camera screen shake when bridge reaches flat resting position or freefall object lands.")]
        [SerializeField] private bool _enableLandingShake = true;
        [SerializeField] private float _landingShakeIntensity = 0.22f;
        [SerializeField] private float _landingShakeDuration = 0.25f;

        [Header("Thread Component")]
        [Tooltip("SutureThread component rendering the hanging thread. If unassigned, auto-finds child or instantiates prefab.")]
        [SerializeField] private SutureThread _sutureThread;

        [Tooltip("Prefab to instantiate if _sutureThread is missing.")]
        [SerializeField] private SutureThread _sutureThreadPrefab;

        private Rigidbody2D _rb;
        private RigidbodyType2D _originalBodyType = RigidbodyType2D.Dynamic;
        private StitchableObject _stitchableTarget;
        private bool _isSuspended = true;
        private bool _isRotatingDown;
        private bool _isFreeFalling;
        private bool _isLockedAtRest;
        private bool _isTriggerDropPending;
        private float _triggerDropTimer;

        private Vector3 _initialPosition;
        private Quaternion _initialRotation;

        public HangingMode Mode => _mode;
        public bool IsSuspended => _isSuspended;
        public bool IsFreeFalling => _isFreeFalling;
        public bool IsLockedAtRest => _isLockedAtRest;
        public bool DropOnPlayerTrigger => _dropOnPlayerTrigger;
        public bool IsHintDismissed => !_isSuspended;
        public Transform CeilingAnchor => _ceilingAnchor;
        public Transform ObjectAnchor => _objectAnchor;
        public Transform HingePivot => _hingePivot;
        public SutureThread SutureThread => _sutureThread;
        public float BridgeLength => _bridgeLength;
        public float BridgeThickness => _bridgeThickness;
        public float FlatRestAngle => _flatRestAngle;
        public bool ManageBridgeGeometry => _manageBridgeGeometry;
        public bool ManageVisualTransform => _manageVisualTransform;
        public StitchableObject StitchableTarget => _stitchableTarget;

        private void OnValidate()
        {
            if (_mode == HangingMode.Drawbridge && _manageBridgeGeometry && !Application.isPlaying)
            {
                ApplyDimensions(_bridgeLength, _bridgeThickness);
            }
        }

        /// <summary>
        /// Resizes the BoxCollider2D, TipAnchor, and child Visual sprite together to ensure perfect alignment for bridges.
        /// </summary>
        public void ApplyDimensions(float length, float thickness)
        {
            _bridgeLength = Mathf.Max(0.5f, length);
            _bridgeThickness = Mathf.Max(0.05f, thickness);

            Vector2 centerOffset = new Vector2(-_bridgeLength * 0.5f, _bridgeThickness * 0.5f);

            // 1. BoxCollider2D
            BoxCollider2D box = GetComponent<BoxCollider2D>();
            if (box != null)
            {
                box.size = new Vector2(_bridgeLength, _bridgeThickness);
                box.offset = centerOffset;
            }

            // 2. TipAnchor
            if (_objectAnchor != null)
            {
                _objectAnchor.localPosition = new Vector3(-_bridgeLength, _bridgeThickness * 0.5f, 0f);
            }

            // 3. Child Visual sprite (Only modify if _manageVisualTransform is explicitly enabled)
            if (_manageVisualTransform)
            {
                Transform visual = transform.Find("Visual");
                if (visual != null)
                {
                    visual.localPosition = (Vector3)centerOffset;
                    visual.localScale = new Vector3(_bridgeLength, _bridgeThickness, 1f);
                }
            }
        }

        /// <summary>
        /// Raycasts upward from the tip anchor to automatically snap the ceiling anchor onto a ceiling surface.
        /// </summary>
        public bool SnapCeilingAnchorUpward(float maxDistance = 15f)
        {
            if (_ceilingAnchor == null) return false;

            Vector2 tipPos = GetObjectAnchorPosition();
            int mask = LayerMask.GetMask("Ground", "Default");
            if (mask == 0) mask = ~LayerMask.GetMask("Player", "Needle");

            RaycastHit2D hit = Physics2D.Raycast(tipPos, Vector2.up, maxDistance, mask);
            if (hit.collider != null)
            {
                _ceilingAnchor.position = hit.point;
                return true;
            }

            return false;
        }

        #region IStitchable Implementation
        public StitchState CurrentState
        {
            get
            {
                if (_stitchableTarget != null && _stitchableTarget.IsTetheredToPlayer)
                {
                    return _stitchableTarget.CurrentState;
                }
                if (_isSuspended) return StitchState.Tethered;
                if (_stitchableTarget != null) return _stitchableTarget.CurrentState;
                return StitchState.Idle;
            }
        }

        public Transform ObjectTransform => transform;

        public Vector2 CurrentPosition
        {
            get
            {
                if (!_isSuspended && _stitchableTarget != null) return _stitchableTarget.CurrentPosition;
                return _rb != null ? _rb.position : (Vector2)transform.position;
            }
        }

        public void AttachThread()
        {
            if (!_isSuspended && _stitchableTarget != null)
            {
                _stitchableTarget.AttachThread();
            }
        }

        public void UpdatePinnedWaypoints(IReadOnlyList<Vector2> waypoints)
        {
            if (!_isSuspended && _stitchableTarget != null)
            {
                _stitchableTarget.UpdatePinnedWaypoints(waypoints);
            }
        }

        public void UpdateInFlightNeedle(Vector2 needlePos)
        {
            if (!_isSuspended && _stitchableTarget != null)
            {
                _stitchableTarget.UpdateInFlightNeedle(needlePos);
            }
        }

        public void UpdateAimLeash(Vector2 playerPos, float maxLeashDistance)
        {
            if (!_isSuspended && _stitchableTarget != null)
            {
                _stitchableTarget.UpdateAimLeash(playerPos, maxLeashDistance);
            }
        }

        public void StartLoopAlongWaypoints(IReadOnlyList<Vector2> waypoints, LoopPlaybackMode mode)
        {
            if (!_isSuspended && _stitchableTarget != null)
            {
                _stitchableTarget.StartLoopAlongWaypoints(waypoints, mode);
            }
        }

        public void StartPlayerTether(Transform playerTransform, Rigidbody2D playerRb)
        {
            if (_stitchableTarget != null)
            {
                _stitchableTarget.StartPlayerTether(playerTransform, playerRb);
            }
        }

        public void ReleasePlayerTether()
        {
            if (_stitchableTarget != null)
            {
                _stitchableTarget.ReleasePlayerTether();
            }
        }

        public void CutThread()
        {
            if (_isSuspended)
            {
                SeverHangingThread();
            }
            else if (_stitchableTarget != null)
            {
                _stitchableTarget.CutThread();
            }
        }
        #endregion

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _stitchableTarget = GetComponent<StitchableObject>();

            if (_rb != null)
            {
                _originalBodyType = _rb.bodyType;
                _rb.bodyType = RigidbodyType2D.Static;
            }

            _initialPosition = transform.position;
            _initialRotation = transform.rotation;

            EnsureSutureThread();
        }

        private void Start()
        {
            InitializeThreadVisual();
        }

        private void OnEnable()
        {
            if (_sutureThread != null)
            {
                _sutureThread.OnSevered += SeverHangingThread;
            }

            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.OnPlayerRespawned += ResetToInitialState;
            }
        }

        private void OnDisable()
        {
            if (_sutureThread != null)
            {
                _sutureThread.OnSevered -= SeverHangingThread;
            }

            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.OnPlayerRespawned -= ResetToInitialState;
            }
        }

        private void Update()
        {
            if (_isSuspended && _dropOnPlayerTrigger)
            {
                UpdatePlayerTriggerDrop(Time.deltaTime);
            }

            if (_isRotatingDown && _mode == HangingMode.Drawbridge)
            {
                StepDrawbridgeRotation(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (_isFreeFalling)
            {
                CheckFallingCrushPlayer();
            }
        }

        private void CheckFallingCrushPlayer()
        {
            if (CheckpointManager.Instance != null && CheckpointManager.Instance.IsRespawning) return;

            PlayerController2D player = PlayerController2D.Instance;
            if (player == null || player.IsInputLocked) return;

            if (!CrushHazard.IsCrushHazard(gameObject, out CrushHazard hazardComp)) return;
            if (hazardComp != null && !hazardComp.CanCrushFalling) return;

            Collider2D myCol = GetComponent<Collider2D>();
            Collider2D playerCol = player.MainCollider;
            if (myCol == null || playerCol == null) return;

            Bounds bCrate = myCol.bounds;
            Bounds bPlayer = playerCol.bounds;

            // Check horizontal overlap between crate and player body
            bool overlapX = bPlayer.max.x > (bCrate.min.x + 0.04f) && bPlayer.min.x < (bCrate.max.x - 0.04f);
            if (!overlapX) return;

            // Check if the bottom of the falling crate has reached or penetrated the top of the player's head,
            // while the crate center is still above the player's feet
            bool hitPlayerTop = bCrate.min.y <= (bPlayer.max.y + 0.08f) && bCrate.max.y >= bPlayer.center.y;
            if (hitPlayerTop)
            {
                player.ForceCrushByFallingObject(myCol);
            }
        }

        private void UpdatePlayerTriggerDrop(float dt)
        {
            if (CheckpointManager.Instance != null && CheckpointManager.Instance.IsRespawning) return;

            if (_isTriggerDropPending)
            {
                _triggerDropTimer -= dt;
                if (_triggerDropTimer <= 0f)
                {
                    _isTriggerDropPending = false;
                    ExecuteTriggeredSnap();
                }
                return;
            }

            PlayerController2D player = PlayerController2D.Instance;
            if (player == null || player.IsInputLocked) return;

            Vector2 center = (Vector2)_initialPosition + _triggerBoxOffset;
            Vector2 halfExtents = _triggerBoxSize * 0.5f;
            Vector2 playerPos = player.transform.position;

            if (Mathf.Abs(playerPos.x - center.x) <= halfExtents.x &&
                Mathf.Abs(playerPos.y - center.y) <= halfExtents.y)
            {
                if (_dropDelay > 0.001f)
                {
                    _isTriggerDropPending = true;
                    _triggerDropTimer = _dropDelay;
                }
                else
                {
                    ExecuteTriggeredSnap();
                }
            }
        }

        private void ExecuteTriggeredSnap()
        {
            if (!_isSuspended) return;

            if (!string.IsNullOrEmpty(_snapSFXName) && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX(_snapSFXName);
            }

            SeverHangingThread();
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            if (!_isFreeFalling || _isLockedAtRest) return;

            // Ignore player collision so landing on the player's head in mid-air does not stop FreeFall
            if (PlayerController2D.IsPlayer(collision.collider)) return;

            // FreeFall landing detection: collision with floor/ground
            if (collision.relativeVelocity.magnitude > 1.2f || (collision.contacts.Length > 0 && collision.contacts[0].normal.y > 0.5f))
            {
                _isFreeFalling = false;
                _isLockedAtRest = true;

                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX("sfx_thud_impact");
                }

                if (_enableLandingShake)
                {
                    CameraController2D.Instance?.Shake(_landingShakeIntensity, _landingShakeDuration);
                }

                if (_lockAsStaticOnLanding && _stitchableTarget == null)
                {
                    if (_rb != null)
                    {
                        _rb.linearVelocity = Vector2.zero;
                        _rb.angularVelocity = 0f;
                        _rb.bodyType = RigidbodyType2D.Static;
                    }
                }
                else if (_stitchableTarget != null && _rb != null)
                {
                    // Restore original dynamic state for stitchable object hauling
                    if (!_stitchableTarget.IsTetheredToPlayer)
                    {
                        _rb.bodyType = _originalBodyType;
                    }
                }
            }
        }

        private void EnsureSutureThread()
        {
            if (_sutureThread != null && !_sutureThread.gameObject.scene.IsValid())
            {
                _sutureThread = null;
            }

            if (_sutureThread == null)
            {
                Transform ceilingChild = transform.Find("CeilingSutureThread");
                if (ceilingChild != null)
                {
                    _sutureThread = ceilingChild.GetComponent<SutureThread>();
                }
            }

            if (_sutureThread == null && _sutureThreadPrefab != null)
            {
                _sutureThread = Instantiate(_sutureThreadPrefab, transform);
                _sutureThread.name = "CeilingSutureThread";
                _sutureThread.transform.localPosition = Vector3.zero;
            }

            if (_sutureThread == null)
            {
                var stitchable = GetComponent<StitchableObject>();
                foreach (var st in GetComponentsInChildren<SutureThread>(true))
                {
                    if (stitchable != null && st == stitchable.SutureThread) continue;
                    if (st.name == "TetherSutureThread") continue;
                    _sutureThread = st;
                    break;
                }
            }

            if (_sutureThread != null)
            {
                _sutureThread.IsCuttable = true;
            }
        }

        public void InitializeThreadVisual()
        {
            if (_sutureThread == null || !_isSuspended) return;

            Vector2 ceilingPos = GetCeilingAnchorPosition();
            Vector2 objPos = GetObjectAnchorPosition();

            List<Vector2> initialPoints = new List<Vector2> { ceilingPos, objPos };
            _sutureThread.RenderPath(initialPoints);
            _sutureThread.SnapTaut(3.0f);
        }

        public void SeverHangingThread()
        {
            if (!_isSuspended) return;
            _isSuspended = false;

            if (_sutureThread != null)
            {
                _sutureThread.Clear();
            }

            if (_mode == HangingMode.Drawbridge)
            {
                _isRotatingDown = true;
                if (_rb != null)
                {
                    _rb.bodyType = RigidbodyType2D.Kinematic;
                    _rb.linearVelocity = Vector2.zero;
                    _rb.angularVelocity = 0f;
                }
            }
            else // FreeFall
            {
                _isFreeFalling = true;
                if (_rb != null)
                {
                    _rb.bodyType = RigidbodyType2D.Dynamic;
                    _rb.gravityScale = _freeFallGravityScale;
                    if (_freeFallInitialDownwardSpeed > 0f)
                    {
                        _rb.linearVelocity = new Vector2(0f, -_freeFallInitialDownwardSpeed);
                    }
                    if (_stitchableTarget != null && _stitchableTarget.IsTetheredToPlayer)
                    {
                        _rb.mass = Mathf.Max(0.1f, _stitchableTarget.OriginalMass * _stitchableTarget.TetheredMassMultiplier);
                        _stitchableTarget.SetHorizontalLock(true);
                    }
                    _rb.WakeUp();
                }
            }
        }

        private void StepDrawbridgeRotation(float dt)
        {
            Vector3 pivot = _hingePivot != null ? _hingePivot.position : transform.position;
            float currentZ = transform.eulerAngles.z;
            float targetZ = _flatRestAngle;

            float newZ = Mathf.MoveTowardsAngle(currentZ, targetZ, _fallRotationSpeed * dt);
            float angleDelta = Mathf.DeltaAngle(currentZ, newZ);

            transform.RotateAround(pivot, Vector3.forward, angleDelta);

            if (Mathf.Abs(Mathf.DeltaAngle(newZ, targetZ)) < 0.2f)
            {
                // Snap accurately to target flat angle
                float finalDelta = Mathf.DeltaAngle(transform.eulerAngles.z, targetZ);
                transform.RotateAround(pivot, Vector3.forward, finalDelta);

                _isRotatingDown = false;
                _isLockedAtRest = true;

                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlaySFX("sfx_thud_impact");
                }

                if (_enableLandingShake)
                {
                    CameraController2D.Instance?.Shake(_landingShakeIntensity, _landingShakeDuration);
                }

                if (_rb != null)
                {
                    _rb.bodyType = RigidbodyType2D.Static;
                }
            }
        }

        public void ResetToInitialState()
        {
            _isSuspended = true;
            _isRotatingDown = false;
            _isFreeFalling = false;
            _isLockedAtRest = false;
            _isTriggerDropPending = false;
            _triggerDropTimer = 0f;

            transform.position = _initialPosition;
            transform.rotation = _initialRotation;

            if (_rb != null)
            {
                if (_rb.bodyType != RigidbodyType2D.Static)
                {
                    _rb.linearVelocity = Vector2.zero;
                    _rb.angularVelocity = 0f;
                }
                _rb.bodyType = RigidbodyType2D.Static;
            }

            if (_stitchableTarget != null)
            {
                _stitchableTarget.RestoreOriginalBodyState();
                if (_rb != null)
                {
                    _rb.bodyType = RigidbodyType2D.Static;
                }
            }

            InitializeThreadVisual();
        }

        public Vector2 GetCeilingAnchorPosition()
        {
            if (_ceilingAnchor != null)
            {
                return _ceilingAnchor.position;
            }
            return (Vector2)transform.position + Vector2.up * 2.5f;
        }

        public Vector2 GetObjectAnchorPosition()
        {
            if (_objectAnchor != null)
            {
                return _objectAnchor.position;
            }
            return transform.position;
        }

        private void OnDrawGizmosSelected()
        {
            Vector2 ceilingPos = GetCeilingAnchorPosition();
            Vector2 objPos = GetObjectAnchorPosition();

            // Suture thread preview line
            Gizmos.color = new Color(0.92f, 0.15f, 0.15f, 0.95f);
            Gizmos.DrawLine(ceilingPos, objPos);

            // Ceiling anchor pin (Yellow)
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(ceilingPos, 0.15f);

            // Object attachment anchor (Cyan)
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(objPos, 0.1f);

            if (_dropOnPlayerTrigger)
            {
                Vector3 basePos = Application.isPlaying ? _initialPosition : transform.position;
                Vector3 triggerCenter = basePos + (Vector3)_triggerBoxOffset;
                Gizmos.color = new Color(1f, 0.35f, 0.15f, 0.75f);
                Gizmos.DrawWireCube(triggerCenter, (Vector3)_triggerBoxSize);
            }

            if (_mode == HangingMode.Drawbridge)
            {
                Vector3 pivot = _hingePivot != null ? _hingePivot.position : transform.position;
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(pivot, 0.18f);

                // Landing Ghost Preview: shows where the bridge will land when flat
                BoxCollider2D box = GetComponent<BoxCollider2D>();
                if (box != null)
                {
                    float currentZ = transform.eulerAngles.z;
                    float targetZ = _flatRestAngle;
                    float deltaAngle = Mathf.DeltaAngle(currentZ, targetZ);
                    Quaternion restRot = Quaternion.Euler(0f, 0f, targetZ);
                    Vector3 relativePos = transform.position - pivot;
                    Vector3 restPos = pivot + (Quaternion.Euler(0f, 0f, deltaAngle) * relativePos);

                    Matrix4x4 oldMatrix = Gizmos.matrix;
                    Gizmos.matrix = Matrix4x4.TRS(restPos, restRot, transform.lossyScale);
                    Gizmos.color = new Color(0.35f, 1f, 0.45f, 0.6f);
                    Gizmos.DrawWireCube((Vector3)box.offset, (Vector3)box.size);
                    Gizmos.matrix = oldMatrix;
                }
            }
        }
    }
}
