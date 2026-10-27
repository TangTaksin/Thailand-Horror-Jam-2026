using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Component attached to any 2D object that can be tethered and scripted into a movement loop along pinned waypoints.
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class StitchableObject : MonoBehaviour, IStitchable
    {
        private const float k_ArrivalThreshold = 0.03f;

        [Header("Movement Settings")]
        [FormerlySerializedAs("travelSpeed")]
        [SerializeField] private float _travelSpeed = 3.5f;

        [Tooltip("ระยะเวลาหยุดพักชั่วคราว (วินาที) เมื่อเคลื่อนที่ถึงจุดหมายปลายทางของแต่ละรอบ ก่อนเริ่มเคลื่อนที่ย้อนกลับ")]
        [SerializeField] private float _waypointDwellTime = 0.2f;

        [Header("Thread Component")]
        [FormerlySerializedAs("sutureThread")]
        [SerializeField] private SutureThread _sutureThread;

        [Header("Hauling / Tether Settings")]
        [Tooltip("Minimum tether leash distance.")]
        [SerializeField] private float _minTetherDistance = 0.8f;

        [Tooltip("Maximum tether leash distance budget.")]
        [SerializeField] private float _maxTetherDistance = 2.2f;

        [Tooltip("Multiplier applied to Rigidbody2D mass while hauled by player (e.g. 0.1 = 10% of original mass). Makes dragging lighter and prevents the player from being pulled backward.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _tetheredMassMultiplier = 0.1f;

        [Tooltip("ระยะตกแนวดิ่งสูงสุดเมื่อกล่องร่วงลงต่ำกว่าตัวผู้เล่น ก่อนที่สายด้ายจะขาด (เช่น ถูกผลักตกแท่น/ตกเหว)")]
        [SerializeField] private float _maxTetherVerticalDrop = 0.85f;

        [Tooltip("ระยะเวลาที่กล่องลอยค้างกลางอากาศขณะถูกจูง ก่อนที่สายด้ายจะขาดอัตโนมัติ (วินาที)")]
        [SerializeField] private float _maxTetherAirborneDuration = 0.2f;

        [Header("Push Resistance")]
        [Tooltip("เมื่อเปิดใช้งาน วัตถุจะล็อคแกน X ขณะอยู่เฉยๆ เพื่อไม่ให้ Player เดินชนแล้วดันวัตถุได้ และจะปลดล็อค X อัตโนมัติเฉพาะตอนถูกผูกเชือกลาก (Hauling)")]
        [SerializeField] private bool _freezePositionXWhenIdle = true;
        [Tooltip("ระยะเผื่อความหย่อนของสายด้ายก่อนถือว่าตึงและเริ่มปลดล็อคแกน X")]
        [SerializeField] private float _tetherSlackTolerance = 0.05f;

        [Header("Webbed Trampoline Settings")]
        [Tooltip("Multiplier for the bounce deflection impulse when the player lands.")]
        [SerializeField] private float _landingPluckMultiplier = 1.4f;

        [Tooltip("Impulse applied to the thread when the player jumps off.")]
        [SerializeField] private float _jumpKickImpulse = 2.4f;

        private Rigidbody2D _rb;
        private RigidbodyType2D _originalBodyType;
        private RigidbodyConstraints2D _originalConstraints;
        private float _originalMass = 1f;
        private float _originalGravityScale = 1f;
        private bool _hasRb;
        private Collider2D _col;

        private DistanceJoint2D _tetherJoint;
        private Transform _tetheredPlayerTransform;
        private Rigidbody2D _tetheredPlayerRb;
        private PlayerController2D _tetheredPlayerController;
        private NeedleController _tetheredNeedleController;
        private float _activeTetherLeashLength;
        private float _tetherAirborneTimer;
        private bool _isReleasingTether;
        private readonly RaycastHit2D[] _boxGroundHits = new RaycastHit2D[4];

        private StitchState _currentState = StitchState.Idle;
        private readonly List<Vector2> _waypoints = new List<Vector2>();
        private LoopPlaybackMode _activeLoopMode;
        private int _currentWaypointIndex;
        private bool _isReversing;
        private float _dwellTimer;
        private Vector2 _previousPosition;
        private Vector2 _targetFramePosition;
        private Vector2 _currentVelocity;
        private Vector2 _initialPosition;
        private Quaternion _initialRotation;

        public StitchState CurrentState => _currentState;
        public Transform ObjectTransform => transform;
        public Collider2D Collider => _col;
        public Vector2 CurrentPosition => _hasRb ? _rb.position : (Vector2)transform.position;
        public Vector2 CurrentVelocity => _currentVelocity;
        public Vector2 FrameDelta { get; private set; }
        public bool IsTetheredToPlayer => _currentState == StitchState.PlayerHarness;
        public float MaxTetherDistance => Mathf.Max(_maxTetherDistance, _minTetherDistance);

        private void Awake()
        {
            _hasRb = TryGetComponent(out _rb);
            if (_hasRb)
            {
                _originalBodyType = _rb.bodyType;
                _originalMass = _rb.mass;
                _originalGravityScale = _rb.gravityScale;
                _originalConstraints = _rb.constraints;

                SetHorizontalLock(true);
            }

            _col = GetComponent<Collider2D>();

            _previousPosition = CurrentPosition;
            _targetFramePosition = _previousPosition;
            _initialPosition = CurrentPosition;
            _initialRotation = transform.rotation;

            if (TryGetComponent(out _tetherJoint))
            {
                _tetherJoint.enabled = false;
            }

            EnsureSutureThreadInstance();
        }

        private void EnsureSutureThreadInstance()
        {
            // 1. กรณีที่ _sutureThread ชี้ไปยัง Prefab Asset ใน Project Window (ไม่ใช่ Object ใน Scene)
            if (_sutureThread != null && !_sutureThread.gameObject.scene.IsValid())
            {
                SutureThread sceneChild = GetComponentInChildren<SutureThread>(true);
                if (sceneChild != null && sceneChild.gameObject.scene.IsValid())
                {
                    _sutureThread = sceneChild;
                }
                else
                {
                    SutureThread prefabAsset = _sutureThread;
                    _sutureThread = Instantiate(prefabAsset, transform);
                    _sutureThread.name = "SutureThread";
                    _sutureThread.transform.localPosition = Vector3.zero;
                }
                return;
            }

            // 2. ถ้ายังเป็น null ให้ค้นหา Child ในตัวเอง
            if (_sutureThread == null)
            {
                _sutureThread = GetComponentInChildren<SutureThread>(true);
            }

            if (_sutureThread == null && Application.isPlaying)
            {
                Debug.LogWarning($"[StitchableObject] Missing SutureThread reference on '{name}'! Please assign SutureThread.prefab in the Inspector.", this);
            }
        }

        private void FixedUpdate()
        {
            FrameDelta = Vector2.zero;
            _targetFramePosition = CurrentPosition;

            if (_currentState == StitchState.Looping)
            {
                TraverseWaypoints();
            }
            else if (_currentState == StitchState.PlayerHarness)
            {
                UpdatePlayerHarness();
            }

            if (Time.fixedDeltaTime > 0f)
            {
                // ใช้ _targetFramePosition เพื่อให้ได้ความเร็วที่แม่นยำในเฟรมปัจจุบันทันที แม้จะขยับด้วย _rb.MovePosition
                _currentVelocity = (_targetFramePosition - _previousPosition) / Time.fixedDeltaTime;
            }
            _previousPosition = _targetFramePosition;
        }

        public void OnPlayerLanded(float impactSpeed, Vector2 contactPoint)
        {
            float force = Mathf.Clamp(impactSpeed * _landingPluckMultiplier, 1.8f, 10f);
            PluckIfLooping(contactPoint, force, 1.8f);
        }

        public void OnPlayerJumpedOff(Vector2 contactPoint)
        {
            PluckIfLooping(contactPoint, _jumpKickImpulse, 1.5f);
        }

        public void OnPlayerWalkingStep(Vector2 contactPoint)
        {
            PluckIfLooping(contactPoint, 0.45f, 1.0f);
        }

        private void PluckIfLooping(Vector2 contactPoint, float downwardForce, float radius)
        {
            if (_sutureThread != null && _currentState == StitchState.Looping)
            {
                _sutureThread.Pluck(contactPoint, Vector2.down * downwardForce, radius);
            }
        }

        private void SetHorizontalLock(bool locked)
        {
            if (!_hasRb || !_freezePositionXWhenIdle || _rb.bodyType == RigidbodyType2D.Static) return;

            RigidbodyConstraints2D targetConstraints = locked
                ? (RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX)
                : RigidbodyConstraints2D.FreezeRotation;

            if (_rb.constraints != targetConstraints)
            {
                if (locked && _rb.bodyType != RigidbodyType2D.Static)
                {
                    _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
                }
                _rb.constraints = targetConstraints;
                _rb.WakeUp();
            }
        }

        private void UpdateTetherLockState(Rigidbody2D pullerRb, DistanceJoint2D joint)
        {
            if (!_freezePositionXWhenIdle || !_hasRb) return;

            if (pullerRb == null || joint == null || !joint.enabled)
            {
                SetHorizontalLock(true);
                return;
            }

            // เช็กว่าสายด้ายตึงหรือยัง
            Vector2 anchorWorld = _rb.transform.TransformPoint(joint.anchor);
            Vector2 connectedWorld = pullerRb.transform.TransformPoint(joint.connectedAnchor);
            float currentDist = Vector2.Distance(anchorWorld, connectedWorld);
            bool isTaut = currentDist >= (joint.distance - _tetherSlackTolerance);

            // เช็กทิศทางการป้อน Input และความเร็วของผู้เล่น
            float dirToPullerX = pullerRb.position.x - _rb.position.x;
            float moveInputX = _tetheredPlayerController != null ? _tetheredPlayerController.HorizontalInput : 0f;

            // เช็กว่าผู้ดึงกำลังเคลื่อนที่หรือกดเดิน "ออกห่าง" จากกล่อง (ดึง)
            bool isMovingAway = (dirToPullerX * pullerRb.linearVelocity.x) > 0.01f || (dirToPullerX * moveInputX) > 0.01f;
            bool isPulling = isTaut && isMovingAway;

            // เช็กว่าผู้เล่นกำลังเคลื่อนที่หรือกดเดิน "เข้าหากล่อง" เพื่อผลัก (ดัน)
            bool isMovingTowards = (dirToPullerX * pullerRb.linearVelocity.x) < -0.01f || (dirToPullerX * moveInputX) < -0.01f;
            bool isPushing = false;

            if (isMovingTowards)
            {
                // ตรวจสอบว่าอยู่ชิดกล่องในระยะที่สามารถผลักได้
                Collider2D pullerCol = _tetheredPlayerController != null && _tetheredPlayerController.MainCollider != null
                    ? _tetheredPlayerController.MainCollider
                    : pullerRb.GetComponentInChildren<Collider2D>();

                if (pullerCol != null && _col != null)
                {
                    ColliderDistance2D colDist = _col.Distance(pullerCol);
                    isPushing = colDist.isOverlapped || colDist.distance <= 0.35f;
                }
                else
                {
                    float boxHalfWidth = _col != null ? _col.bounds.extents.x : 0.8f;
                    float centerDistX = Mathf.Abs(dirToPullerX);
                    isPushing = centerDistX <= (boxHalfWidth + 0.6f);
                }
            }

            // ปลดล็อคแกน X เมื่อ: กำลังดึง (สายตึงและเดินออกห่าง) หรือ กำลังผลัก (เดินชนเข้าหากล่อง)
            bool shouldUnlock = isPulling || isPushing;
            SetHorizontalLock(!shouldUnlock);
        }

        private bool IsBoxGrounded()
        {
            if (_col == null) return false;

            Bounds bounds = _col.bounds;
            Vector2 checkOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.05f);
            float checkDist = 0.2f;

            ContactFilter2D filter = new ContactFilter2D();
            filter.SetLayerMask(~LayerMask.GetMask("Player", "Needle", "Ignore Raycast"));
            filter.useLayerMask = true;
            filter.useTriggers = false;

            // ตรวจสอบตรงกลาง
            int hitCount = Physics2D.Raycast(checkOrigin, Vector2.down, filter, _boxGroundHits, checkDist + 0.05f);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = _boxGroundHits[i].collider;
                if (hit != null && hit != _col && !hit.isTrigger && !hit.transform.IsChildOf(transform) && !PlayerController2D.IsPlayer(hit))
                {
                    return true;
                }
            }

            // ตรวจสอบขอบซ้ายและขวา เพื่อรองรับกรณีกล่องยืนหมิ่นขอบแท่น (Corner overhang)
            float halfWidth = bounds.extents.x * 0.75f;
            int leftHits = Physics2D.Raycast(checkOrigin + Vector2.left * halfWidth, Vector2.down, filter, _boxGroundHits, checkDist + 0.05f);
            for (int i = 0; i < leftHits; i++)
            {
                Collider2D hit = _boxGroundHits[i].collider;
                if (hit != null && hit != _col && !hit.isTrigger && !hit.transform.IsChildOf(transform) && !PlayerController2D.IsPlayer(hit))
                {
                    return true;
                }
            }

            int rightHits = Physics2D.Raycast(checkOrigin + Vector2.right * halfWidth, Vector2.down, filter, _boxGroundHits, checkDist + 0.05f);
            for (int i = 0; i < rightHits; i++)
            {
                Collider2D hit = _boxGroundHits[i].collider;
                if (hit != null && hit != _col && !hit.isTrigger && !hit.transform.IsChildOf(transform) && !PlayerController2D.IsPlayer(hit))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetStaticState()
        {
            if (!_hasRb) return;
            if (_rb.bodyType != RigidbodyType2D.Static)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;
                _rb.bodyType = RigidbodyType2D.Static;
            }
        }

        public void RestoreOriginalBodyState()
        {
            if (!_hasRb) return;
            _rb.bodyType = _originalBodyType;
            _rb.gravityScale = _originalGravityScale;
            _rb.mass = _originalMass;

            if (_rb.bodyType != RigidbodyType2D.Static)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;

                if (_freezePositionXWhenIdle)
                {
                    SetHorizontalLock(true);
                }
                else
                {
                    _rb.constraints = _originalConstraints;
                }

                _rb.WakeUp();
            }
        }

        private void SetWaypoints(IReadOnlyList<Vector2> waypoints)
        {
            _waypoints.Clear();
            if (waypoints == null) return;

            for (int i = 0; i < waypoints.Count; i++)
            {
                _waypoints.Add(waypoints[i]);
            }
        }

        public void AttachThread()
        {
            if (_currentState == StitchState.PlayerHarness)
            {
                DisableTetherJointAndClearPlayerRefs();
            }

            _currentState = StitchState.Tethered;
            _waypoints.Clear();
            _waypoints.Add(CurrentPosition);

            SetStaticState();
            _targetFramePosition = CurrentPosition;
            _previousPosition = CurrentPosition;
            _currentVelocity = Vector2.zero;

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void UpdatePinnedWaypoints(IReadOnlyList<Vector2> waypoints)
        {
            SetWaypoints(waypoints);

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void UpdateInFlightNeedle(Vector2 needlePos)
        {
            Vector2 currentPos = CurrentPosition;
            if (_waypoints.Count == 0)
            {
                _waypoints.Add(currentPos);
                _waypoints.Add(needlePos);
            }
            else if (_waypoints.Count == 1)
            {
                _waypoints[0] = currentPos;
                _waypoints.Add(needlePos);
            }
            else
            {
                _waypoints[0] = currentPos;
                _waypoints[1] = needlePos;
            }

            if (_sutureThread != null)
            {
                _sutureThread.UpdateInFlightNeedle(currentPos, needlePos);
            }
        }

        public void UpdateAimLeash(Vector2 playerPos, float maxLeashDistance)
        {
            if (_currentState != StitchState.Tethered) return;

            Vector2 objPos = CurrentPosition;
            float currentDist = Vector2.Distance(objPos, playerPos);
            float leashRest = Mathf.Clamp(currentDist, 0.5f, maxLeashDistance);

            if (_sutureThread != null)
            {
                _sutureThread.UpdateDynamicEndpoints(objPos, playerPos, leashRest);
            }
        }

        public void StartLoopAlongWaypoints(IReadOnlyList<Vector2> waypoints, LoopPlaybackMode mode)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                CutThread();
                return;
            }

            if (_currentState == StitchState.PlayerHarness)
            {
                DisableTetherJointAndClearPlayerRefs();
            }

            SetWaypoints(waypoints);

            // ซิงค์จุดเริ่มต้นให้ตรงกับตำแหน่งปัจจุบันของวัตถุ ป้องกันการกระชากวาร์ปในเฟรมแรก
            _waypoints[0] = CurrentPosition;

            _activeLoopMode = mode;
            _currentState = StitchState.Looping;
            _currentWaypointIndex = 0;
            _isReversing = false;
            _dwellTimer = 0f;

            SetStaticState();
            _targetFramePosition = CurrentPosition;
            _previousPosition = CurrentPosition;
            _currentVelocity = Vector2.zero;

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
                _sutureThread.SnapTaut(3.8f);
            }

            // คำนวณจุดหยุดปลายทางของกล่อง เพื่อให้ขอบหน้าของกล่องแตะกับผนังพอดีเป๊ะ (Flush 0 cm)
            // โดยที่หมุดเข็มและเส้นด้ายยังคงตรึงแน่นอยู่ที่จุด P1 (hitPoint บนพื้นผิวจริง)
            Vector2 p0 = _waypoints[0];
            Vector2 p1 = _waypoints[_waypoints.Count - 1];
            Vector2 segDelta = p1 - p0;
            float segDist = segDelta.magnitude;
            if (segDist > 0.001f)
            {
                Vector2 dir = segDelta / segDist;
                float halfW = _col != null ? _col.bounds.extents.x : 0.5f;
                float halfH = _col != null ? _col.bounds.extents.y : 0.5f;
                float frontExtent = Mathf.Abs(dir.x) * halfW + Mathf.Abs(dir.y) * halfH;

                float maxTravel = Mathf.Max(0.1f, segDist - frontExtent);
                _waypoints[_waypoints.Count - 1] = p0 + dir * maxTravel;
            }
        }

        private void TraverseWaypoints()
        {
            if (_waypoints.Count < 2) return;

            if (_dwellTimer > 0f)
            {
                _dwellTimer -= Time.fixedDeltaTime;
                MoveToPosition(CurrentPosition);
                return;
            }

            int targetIndex = _isReversing ? _currentWaypointIndex - 1 : _currentWaypointIndex + 1;

            if (!_isReversing && targetIndex >= _waypoints.Count)
            {
                if (_activeLoopMode == LoopPlaybackMode.PingPong)
                {
                    _isReversing = true;
                    targetIndex = _waypoints.Count - 2;
                    if (_waypointDwellTime > 0f)
                    {
                        _dwellTimer = _waypointDwellTime;
                        MoveToPosition(CurrentPosition);
                        return;
                    }
                }
                else
                {
                    MoveToPosition(_waypoints[0]);
                    _currentWaypointIndex = 0;
                    if (_waypointDwellTime > 0f)
                    {
                        _dwellTimer = _waypointDwellTime;
                    }
                    return;
                }
            }
            else if (_isReversing && targetIndex < 0)
            {
                _isReversing = false;
                targetIndex = 1;
                if (_waypointDwellTime > 0f)
                {
                    _dwellTimer = _waypointDwellTime;
                    MoveToPosition(CurrentPosition);
                    return;
                }
            }

            Vector2 targetPos = _waypoints[targetIndex];
            Vector2 currentPos = CurrentPosition;
            float step = _travelSpeed * Time.fixedDeltaTime;

            Vector2 newPos = Vector2.MoveTowards(currentPos, targetPos, step);
            MoveToPosition(newPos);

            if (Vector2.Distance(newPos, targetPos) < k_ArrivalThreshold)
            {
                _currentWaypointIndex = targetIndex;
            }
        }

        private void MoveToPosition(Vector2 pos)
        {
            Vector2 prevPos = CurrentPosition;
            _targetFramePosition = pos;
            FrameDelta = pos - prevPos;

            if (_hasRb)
            {
                _rb.position = pos;
                transform.position = pos;
            }
            else
            {
                transform.position = pos;
            }
        }

        public void StartPlayerTether(Transform playerTransform, Rigidbody2D playerRb)
        {
            if (playerTransform == null) return;

            _tetheredPlayerTransform = playerTransform;
            _tetheredPlayerRb = playerRb;
            _tetheredPlayerController = playerTransform.GetComponent<PlayerController2D>();
            _tetheredNeedleController = playerTransform.GetComponent<NeedleController>();
            _currentState = StitchState.PlayerHarness;

            if (_hasRb)
            {
                _rb.bodyType = _originalBodyType;
                _rb.gravityScale = _originalGravityScale;
                _rb.mass = Mathf.Max(0.1f, _originalMass * _tetheredMassMultiplier);
                SetHorizontalLock(true);
            }

            if (_tetherJoint == null && !TryGetComponent(out _tetherJoint))
            {
                _tetherJoint = gameObject.AddComponent<DistanceJoint2D>();
            }

            Vector2 playerPos = playerRb != null ? playerRb.position : (Vector2)playerTransform.position;
            float currentDist = Vector2.Distance(CurrentPosition, playerPos);
            // ให้ระยะสายจูงเริ่มต้นอิงตามระยะจริงของผู้เล่น แต่ไม่ต่ำกว่า _minTetherDistance และไม่เกิน MaxTetherDistance
            _activeTetherLeashLength = Mathf.Clamp(currentDist, _minTetherDistance, MaxTetherDistance);
            _tetherAirborneTimer = 0f;

            bool isStandingOnThis = _tetheredPlayerController != null && _tetheredPlayerController.IsStandingOn(this);

            _tetherJoint.enabled = !isStandingOnThis;
            _tetherJoint.connectedBody = playerRb;
            _tetherJoint.autoConfigureDistance = false;
            _tetherJoint.maxDistanceOnly = true;
            _tetherJoint.distance = _activeTetherLeashLength;
            _tetherJoint.enableCollision = true;

            UpdateTetherLockState(_tetheredPlayerRb, _tetherJoint);

            _waypoints.Clear();
            _waypoints.Add(CurrentPosition);
            _waypoints.Add(playerPos);

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void ReleasePlayerTether()
        {
            if (_isReleasingTether) return;
            _isReleasingTether = true;

            NeedleController needleCtrl = _tetheredNeedleController;
            PlayerController2D playerCtrl = _tetheredPlayerController;

            DisableTetherJointAndClearPlayerRefs();

            _currentState = StitchState.Idle;
            _waypoints.Clear();

            RestoreOriginalBodyState();

            if (_sutureThread != null)
            {
                _sutureThread.Clear();
            }

            // ซิงค์สถานะกลับไปยัง PlayerController2D และ NeedleController กรณีด้ายขาดเองหรือถูกตัดจากภายนอก
            if (playerCtrl != null)
            {
                playerCtrl.SetHauling(false);
            }

            if (needleCtrl != null && needleCtrl.IsTetheredToPlayer)
            {
                needleCtrl.ReleasePlayerTether();
            }

            _isReleasingTether = false;
        }

        private void DisableTetherJointAndClearPlayerRefs()
        {
            if (_tetherJoint != null)
            {
                _tetherJoint.enabled = false;
                _tetherJoint.connectedBody = null;
            }

            if (_hasRb)
            {
                _rb.mass = _originalMass;
                SetHorizontalLock(true);
            }

            if (_sutureThread != null)
            {
                _sutureThread.SetTensionStrain(0f);
            }

            _tetheredPlayerTransform = null;
            _tetheredPlayerRb = null;
            _tetheredPlayerController = null;
            _tetheredNeedleController = null;
        }

        private void UpdatePlayerHarness()
        {
            if (_tetheredPlayerTransform == null)
            {
                ReleasePlayerTether();
                return;
            }

            Vector2 objPos = CurrentPosition;
            Vector2 playerPos = _tetheredPlayerRb != null ? _tetheredPlayerRb.position : (Vector2)_tetheredPlayerTransform.position;
            float dist = Vector2.Distance(objPos, playerPos);

            // หากผู้เล่นยืนอยู่บนวัตถุชิ้นนี้ ให้พักการทำงานของ Joint ฟิสิกส์ชั่วคราวและล็อคแกน X
            // เพื่อป้องกันแรงดึง Joint ลากวัตถุใต้เท้า (hoverboard exploit) แต่ยังคงสถานะ Tether และวาดเส้นด้ายไว้
            if (_tetheredPlayerController != null && _tetheredPlayerController.IsStandingOn(this))
            {
                if (_tetherJoint != null && _tetherJoint.enabled)
                {
                    _tetherJoint.enabled = false;
                }
                SetHorizontalLock(true);

                if (_sutureThread != null)
                {
                    _sutureThread.SetTensionStrain(0f);
                    _sutureThread.UpdateDynamicEndpoints(objPos, playerPos, _activeTetherLeashLength);
                }
                return;
            }

            // เมื่อผู้เล่นก้าวหรือกระโดดลงจากวัตถุแล้ว ให้เปิด Joint ฟิสิกส์กลับมาทำงานตามปกติ
            if (_tetherJoint != null && !_tetherJoint.enabled)
            {
                _tetherJoint.enabled = true;
                _activeTetherLeashLength = Mathf.Clamp(dist, _minTetherDistance, MaxTetherDistance);
                _tetherJoint.distance = _activeTetherLeashLength;
            }

            // ตรวจสอบกรณีกล่องถูกผลักหรือร่วงตกจากแท่น (Ledge / Cliff / Pit Drop)
            // ถ้ากล่องไม่มีพื้นรองรับและร่วงลงต่ำกว่าระดับตัวผู้เล่น ให้ตัดสายด้ายขาดทันที กล่องจะได้ร่วงลงไปอย่างเป็นธรรมชาติ
            float verticalDrop = playerPos.y - objPos.y;
            bool isBoxAirborne = !IsBoxGrounded();

            if (isBoxAirborne)
            {
                _tetherAirborneTimer += Time.fixedDeltaTime;

                // 1. กล่องตกลงต่ำกว่าระดับผู้เล่นเกินระยะกำหนด (ตกแท่นสูง/เหว)
                bool isDroppingBelow = verticalDrop > _maxTetherVerticalDrop;

                // 2. กล่องลอยค้างกลางอากาศเกินกำหนด (เช่น ห้อยต่องแต่งอยู่ใต้ขอบแท่น)
                bool isHangingTooLong = _tetherAirborneTimer >= _maxTetherAirborneDuration && verticalDrop > 0.45f;

                // 3. กล่องมีความเร็วร่วงลงแนวดิ่งอย่างรวดเร็ว (Freefall shock)
                bool isFallingFast = _hasRb && _rb.linearVelocity.y < -1.2f && verticalDrop > 0.5f;

                if (isDroppingBelow || isHangingTooLong || isFallingFast)
                {
                    _tetherAirborneTimer = 0f;
                    CameraController2D.Instance?.Shake(0.05f, 0.12f);
                    ReleasePlayerTether();
                    return;
                }
            }
            else
            {
                _tetherAirborneTimer = 0f;
            }

            // คลายระยะสายจูงออกได้อย่างยืดหยุ่นตามระยะเดินของผู้เล่นจนถึงขีดจำกัดสูงสุด MaxTetherDistance
            if (dist > _activeTetherLeashLength && dist <= MaxTetherDistance)
            {
                _activeTetherLeashLength = dist;
                if (_tetherJoint != null)
                {
                    _tetherJoint.distance = _activeTetherLeashLength;
                }
            }

            // คำนวณอัตราความตึง (Strain 0.0 -> 1.0) เมื่อด้ายถูกยืดเกินระยะปกติ
            float strain = 0f;
            if (dist > MaxTetherDistance)
            {
                float stretchBudget = MaxTetherDistance * 1.2f;
                strain = Mathf.Clamp01((dist - MaxTetherDistance) / stretchBudget);
            }

            // Snap if pulled beyond extreme distance (e.g. falling into pit or pulled hard)
            if (dist > MaxTetherDistance * 1.35f)
            {
                CameraController2D.Instance?.Shake(0.07f, 0.14f);
                ReleasePlayerTether();
                return;
            }

            UpdateTetherLockState(_tetheredPlayerRb, _tetherJoint);

            if (_sutureThread != null)
            {
                _sutureThread.SetTensionStrain(strain);
                _sutureThread.UpdateDynamicEndpoints(objPos, playerPos, _activeTetherLeashLength);
            }
        }

        public void CutThread()
        {
            if (_currentState == StitchState.PlayerHarness)
            {
                ReleasePlayerTether();
                return;
            }

            _currentState = StitchState.Idle;
            _waypoints.Clear();
            _isReversing = false;
            _dwellTimer = 0f;

            RestoreOriginalBodyState();

            if (_sutureThread != null)
            {
                _sutureThread.Clear();
            }
        }

        public void ResetToInitialState()
        {
            CutThread();

            if (_hasRb)
            {
                _rb.bodyType = _originalBodyType;
                _rb.gravityScale = _originalGravityScale;
                _rb.position = _initialPosition;
                _rb.rotation = _initialRotation.eulerAngles.z;
                if (_rb.bodyType != RigidbodyType2D.Static)
                {
                    _rb.linearVelocity = Vector2.zero;
                    _rb.angularVelocity = 0f;
                }
            }

            transform.position = _initialPosition;
            transform.rotation = _initialRotation;

            _previousPosition = _initialPosition;
            _targetFramePosition = _initialPosition;
            FrameDelta = Vector2.zero;
            _currentVelocity = Vector2.zero;
        }

        private void OnDisable()
        {
            if (_currentState == StitchState.PlayerHarness)
            {
                ReleasePlayerTether();
            }
        }
    }
}