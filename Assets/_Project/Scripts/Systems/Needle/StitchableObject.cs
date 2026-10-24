using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Component attached to any 2D object that can be tethered and scripted into a movement loop along pinned waypoints.
    /// </summary>
    public sealed class StitchableObject : MonoBehaviour, IStitchable
    {
        private const float k_ArrivalThreshold = 0.03f;

        [Header("Movement Settings")]
        [FormerlySerializedAs("travelSpeed")]
        [SerializeField] private float _travelSpeed = 3.5f;

        [Header("Ground Collision Settings")]
        [Tooltip("Surfaces that the stitchable object must not penetrate through (e.g. Ground, Walls).")]
        [SerializeField] private LayerMask _groundLayer;

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

        [Header("Webbed Trampoline Settings")]
        [Tooltip("Multiplier for the bounce deflection impulse when the player lands.")]
        [SerializeField] private float _landingPluckMultiplier = 1.4f;

        [Tooltip("Impulse applied to the thread when the player jumps off.")]
        [SerializeField] private float _jumpKickImpulse = 2.4f;

        private Rigidbody2D _rb;
        private RigidbodyType2D _originalBodyType;
        private float _originalMass = 1f;
        private bool _hasRb;
        private Collider2D _col;
        private float _halfHeight = 0.5f;
        private Vector2 _boxCastSize = new Vector2(0.9f, 0.05f);
        private ContactFilter2D _groundFilter;
        private readonly RaycastHit2D[] _groundRaycastBuffer = new RaycastHit2D[4];

        private DistanceJoint2D _tetherJoint;
        private Transform _tetheredPlayerTransform;
        private Rigidbody2D _tetheredPlayerRb;
        private PlayerController2D _tetheredPlayerController;
        private NeedleController _tetheredNeedleController;
        private float _activeTetherLeashLength;
        private bool _isReleasingTether;

        private StitchState _currentState = StitchState.Idle;
        private readonly List<Vector2> _waypoints = new List<Vector2>();
        private LoopPlaybackMode _activeLoopMode;
        private int _currentWaypointIndex;
        private bool _isReversing;
        private Vector2 _previousPosition;
        private Vector2 _targetFramePosition;
        private Vector2 _currentVelocity;

        public StitchState CurrentState => _currentState;
        public Transform ObjectTransform => transform;
        public Vector2 CurrentPosition => _hasRb ? _rb.position : (Vector2)transform.position;
        public Vector2 CurrentVelocity => _currentVelocity;
        public bool IsTetheredToPlayer => _currentState == StitchState.PlayerHarness;
        public float MaxTetherDistance => _maxTetherDistance;

        private void Awake()
        {
            _hasRb = TryGetComponent(out _rb);
            if (_hasRb)
            {
                _originalBodyType = _rb.bodyType;
                _originalMass = _rb.mass;
            }

            EnsureGroundFilter();

            _previousPosition = CurrentPosition;
            _targetFramePosition = _previousPosition;

            _tetherJoint = GetComponent<DistanceJoint2D>();
            if (_tetherJoint != null)
            {
                _tetherJoint.enabled = false;
            }

            if (_sutureThread == null)
            {
                _sutureThread = GetComponentInChildren<SutureThread>();
                if (_sutureThread == null)
                {
                    GameObject threadObj = new GameObject("SutureThread");
                    threadObj.transform.SetParent(transform);
                    threadObj.transform.localPosition = Vector3.zero;
                    _sutureThread = threadObj.AddComponent<SutureThread>();
                }
            }
        }

        private void EnsureGroundFilter()
        {
            if (_col == null)
            {
                _col = GetComponent<Collider2D>();
                if (_col != null)
                {
                    _halfHeight = _col.bounds.extents.y;
                    _boxCastSize = new Vector2(_col.bounds.size.x * 0.9f, 0.05f);
                }
            }

            if (_groundFilter.useLayerMask) return;

            if (_groundLayer.value == 0)
            {
                int groundLayer = LayerMask.NameToLayer("Ground");
                _groundLayer = groundLayer != -1 ? (1 << groundLayer) : LayerMask.GetMask("Ground", "Default");
            }

            _groundFilter = new ContactFilter2D();
            _groundFilter.SetLayerMask(_groundLayer);
            _groundFilter.useLayerMask = true;
            _groundFilter.useTriggers = false;
        }

        private void FixedUpdate()
        {
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
            if (_sutureThread != null && _currentState == StitchState.Looping)
            {
                float force = Mathf.Clamp(impactSpeed * _landingPluckMultiplier, 1.8f, 10f);
                _sutureThread.Pluck(contactPoint, Vector2.down * force, 1.8f);
            }
        }

        public void OnPlayerJumpedOff(Vector2 contactPoint)
        {
            if (_sutureThread != null && _currentState == StitchState.Looping)
            {
                _sutureThread.Pluck(contactPoint, Vector2.down * _jumpKickImpulse, 1.5f);
            }
        }

        public void OnPlayerWalkingStep(Vector2 contactPoint)
        {
            if (_sutureThread != null && _currentState == StitchState.Looping)
            {
                _sutureThread.Pluck(contactPoint, Vector2.down * 0.45f, 1.0f);
            }
        }

        public void AttachThread()
        {
            _currentState = StitchState.Tethered;
            _waypoints.Clear();
            _waypoints.Add(CurrentPosition);

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void UpdatePinnedWaypoints(IReadOnlyList<Vector2> waypoints)
        {
            _waypoints.Clear();
            for (int i = 0; i < waypoints.Count; i++)
            {
                _waypoints.Add(waypoints[i]);
            }

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void UpdateInFlightNeedle(Vector2 needlePos)
        {
            if (_waypoints.Count == 0)
            {
                _waypoints.Add(CurrentPosition);
                _waypoints.Add(needlePos);
            }
            else if (_waypoints.Count == 1)
            {
                _waypoints.Add(needlePos);
            }
            else
            {
                _waypoints[0] = CurrentPosition;
                _waypoints[1] = needlePos;
            }

            if (_sutureThread != null)
            {
                _sutureThread.UpdateInFlightNeedle(_waypoints[0], needlePos);
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

            _waypoints.Clear();
            for (int i = 0; i < waypoints.Count; i++)
            {
                _waypoints.Add(waypoints[i]);
            }

            // ซิงค์จุดเริ่มต้นให้ตรงกับตำแหน่งปัจจุบันของวัตถุ ป้องกันการกระชากวาร์ปในเฟรมแรก
            _waypoints[0] = CurrentPosition;

            _activeLoopMode = mode;
            _currentState = StitchState.Looping;
            _currentWaypointIndex = 0;
            _isReversing = false;

            if (_hasRb)
            {
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;
                _rb.bodyType = RigidbodyType2D.Static;
            }

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
                _sutureThread.SnapTaut(3.8f);
            }

            // คำนวณจุดหยุดปลายทางของกล่อง เพื่อให้ขอบหน้าของกล่องแตะกับผนังพอดีเป๊ะ (Flush 0 cm)
            // โดยที่หมุดเข็มและเส้นด้ายยังคงตรึงแน่นอยู่ที่จุด P1 (hitPoint บนพื้นผิวจริง)
            if (_waypoints.Count >= 2)
            {
                Vector2 p0 = _waypoints[0];
                Vector2 p1 = _waypoints[_waypoints.Count - 1];
                Vector2 segDelta = p1 - p0;
                float segDist = segDelta.magnitude;
                if (segDist > 0.001f)
                {
                    Vector2 dir = segDelta / segDist;
                    EnsureGroundFilter();
                    float halfW = _col != null ? _col.bounds.extents.x : 0.5f;
                    float halfH = _col != null ? _col.bounds.extents.y : 0.5f;
                    float frontExtent = Mathf.Abs(dir.x) * halfW + Mathf.Abs(dir.y) * halfH;

                    float maxTravel = Mathf.Max(0.1f, segDist - frontExtent);
                    _waypoints[_waypoints.Count - 1] = p0 + dir * maxTravel;
                }
            }
        }

        private void TraverseWaypoints()
        {
            if (_waypoints.Count < 2) return;

            int targetIndex = _isReversing ? _currentWaypointIndex - 1 : _currentWaypointIndex + 1;

            if (!_isReversing && targetIndex >= _waypoints.Count)
            {
                if (_activeLoopMode == LoopPlaybackMode.PingPong)
                {
                    _isReversing = true;
                    targetIndex = _waypoints.Count - 2;
                }
                else
                {
                    targetIndex = 0;
                    MoveToPosition(_waypoints[0]);
                    _currentWaypointIndex = 0;
                    return;
                }
            }
            else if (_isReversing && targetIndex < 0)
            {
                _isReversing = false;
                targetIndex = 1;
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
            // ในโหมดรางลูป (Looping) วัตถุจะแล่นไปตามแนวด้ายตรงเป๊ะโดยไม่ถูกกดลงพื้น
            Vector2 validPos = (_currentState == StitchState.Looping) ? pos : ClampToGroundSurface(pos);
            _targetFramePosition = validPos;
            if (_hasRb)
            {
                _rb.position = validPos;
                transform.position = validPos;
            }
            else
            {
                transform.position = validPos;
            }
        }

        /// <summary>
        /// ป้องกันไม่ให้วัตถุทะลุผ่านพื้นดินหรือสิ่งกีดขวาง โดยล็อกระดับ Y ให้อยู่เสมอกับผิวดินพอดีเป๊ะ (Flush 0 cm)
        /// </summary>
        public Vector2 ClampToGroundSurface(Vector2 targetPos)
        {
            EnsureGroundFilter();
            if (_col == null) return targetPos;

            Vector2 currentPos = CurrentPosition;
            float castTopY = Mathf.Max(currentPos.y, targetPos.y) + _halfHeight + 0.2f;
            Vector2 origin = new Vector2(targetPos.x, castTopY);
            float castDist = castTopY - (targetPos.y - _halfHeight) + 0.5f;

            int count = Physics2D.BoxCast(origin, _boxCastSize, 0f, Vector2.down, _groundFilter, _groundRaycastBuffer, castDist);

            float highestGroundY = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Collider2D hitCol = _groundRaycastBuffer[i].collider;
                if (hitCol == null || hitCol == _col || hitCol.transform.IsChildOf(transform)) continue;

                if (_groundRaycastBuffer[i].point.y > highestGroundY)
                {
                    highestGroundY = _groundRaycastBuffer[i].point.y;
                }
            }

            if (highestGroundY > float.MinValue)
            {
                float minY = highestGroundY + _halfHeight;
                if (targetPos.y < minY)
                {
                    return new Vector2(targetPos.x, minY);
                }
            }

            return targetPos;
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
                _rb.bodyType = RigidbodyType2D.Dynamic;
                _rb.mass = Mathf.Max(0.1f, _originalMass * _tetheredMassMultiplier);
            }

            if (_tetherJoint == null)
            {
                _tetherJoint = gameObject.GetComponent<DistanceJoint2D>();
                if (_tetherJoint == null)
                {
                    _tetherJoint = gameObject.AddComponent<DistanceJoint2D>();
                }
            }

            float currentDist = Vector2.Distance(CurrentPosition, playerTransform.position);
            _activeTetherLeashLength = Mathf.Clamp(currentDist, _minTetherDistance, _maxTetherDistance);

            _tetherJoint.enabled = true;
            _tetherJoint.connectedBody = playerRb;
            _tetherJoint.autoConfigureDistance = false;
            _tetherJoint.maxDistanceOnly = true;
            _tetherJoint.distance = _activeTetherLeashLength;
            _tetherJoint.enableCollision = true;

            _waypoints.Clear();
            _waypoints.Add(CurrentPosition);
            _waypoints.Add(playerTransform.position);

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

            if (_hasRb)
            {
                _rb.bodyType = _originalBodyType;
                _rb.mass = _originalMass;
            }

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
            Vector2 playerPos = _tetheredPlayerTransform.position;
            float dist = Vector2.Distance(objPos, playerPos);

            // Snap if pulled beyond extreme distance (e.g. falling into pit)
            if (dist > _maxTetherDistance * 1.8f)
            {
                ReleasePlayerTether();
                return;
            }

            if (_sutureThread != null)
            {
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

            if (_hasRb)
            {
                _rb.bodyType = _originalBodyType;
                _rb.mass = _originalMass;
            }

            if (_sutureThread != null)
            {
                _sutureThread.Clear();
            }
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