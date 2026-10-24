using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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

        [Header("Thread Component")]
        [FormerlySerializedAs("sutureThread")]
        [SerializeField] private SutureThread _sutureThread;

        [Header("Hauling / Tether Settings")]
        [Tooltip("Minimum tether leash distance.")]
        [SerializeField] private float _minTetherDistance = 1.0f;

        [Tooltip("Maximum tether leash distance budget.")]
        [SerializeField] private float _maxTetherDistance = 4.5f;

        [Header("Webbed Trampoline Settings")]
        [Tooltip("Multiplier for the bounce deflection impulse when the player lands.")]
        [SerializeField] private float _landingPluckMultiplier = 1.4f;

        [Tooltip("Impulse applied to the thread when the player jumps off.")]
        [SerializeField] private float _jumpKickImpulse = 2.4f;

        private Rigidbody2D _rb;
        private RigidbodyType2D _originalBodyType;
        private bool _hasRb;

        private DistanceJoint2D _tetherJoint;
        private Transform _tetheredPlayerTransform;
        private Rigidbody2D _tetheredPlayerRb;
        private float _activeTetherLeashLength;

        private StitchState _currentState = StitchState.Idle;
        private readonly List<Vector2> _waypoints = new List<Vector2>();
        private LoopPlaybackMode _activeLoopMode;
        private int _currentWaypointIndex;
        private float _distanceAlongThread;
        private bool _isReversing;
        private Vector2 _previousPosition;
        private Vector2 _currentVelocity;

        public StitchState CurrentState => _currentState;
        public Transform ObjectTransform => transform;
        public Vector2 CurrentPosition => transform.position;
        public Vector2 CurrentVelocity => _currentVelocity;
        public bool IsTetheredToPlayer => _currentState == StitchState.PlayerHarness;

        private void Awake()
        {
            _hasRb = TryGetComponent(out _rb);
            if (_hasRb)
            {
                _originalBodyType = _rb.bodyType;
            }

            _tetherJoint = GetComponent<DistanceJoint2D>();

            if (_sutureThread == null)
            {
                _sutureThread = GetComponentInChildren<SutureThread>();
                if (_sutureThread == null)
                {
                    GameObject threadObj = new GameObject("SutureThread");
                    threadObj.transform.SetParent(transform);
                    _sutureThread = threadObj.AddComponent<SutureThread>();
                }
            }
        }

        private void FixedUpdate()
        {
            if (_currentState == StitchState.Looping)
            {
                TraverseWaypoints();
            }
            else if (_currentState == StitchState.PlayerHarness)
            {
                UpdatePlayerHarness();
            }

            Vector2 curPos = transform.position;
            if (Time.fixedDeltaTime > 0f)
            {
                _currentVelocity = (curPos - _previousPosition) / Time.fixedDeltaTime;
            }
            _previousPosition = curPos;
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
            _waypoints.Add(transform.position);

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void UpdatePinnedWaypoints(IReadOnlyList<Vector2> waypoints)
        {
            _waypoints.Clear();
            _waypoints.AddRange(waypoints);

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
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
                if (_tetherJoint != null)
                {
                    _tetherJoint.enabled = false;
                    _tetherJoint.connectedBody = null;
                }
                _tetheredPlayerTransform = null;
                _tetheredPlayerRb = null;
            }

            _waypoints.Clear();
            _waypoints.AddRange(waypoints);
            _activeLoopMode = mode;
            _currentState = StitchState.Looping;
            _currentWaypointIndex = 0;
            _distanceAlongThread = 0f;
            _isReversing = false;

            if (_hasRb)
            {
                _rb.bodyType = RigidbodyType2D.Kinematic;
                _rb.linearVelocity = Vector2.zero;
                _rb.angularVelocity = 0f;
            }

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        private void TraverseWaypoints()
        {
            if (_waypoints.Count < 2) return;

            if (_sutureThread != null && _sutureThread.TotalPathLength > 0.05f)
            {
                TraverseAlongThread();
            }
            else
            {
                TraverseWaypointsLinearFallback();
            }
        }

        private void TraverseAlongThread()
        {
            float totalLength = _sutureThread.TotalPathLength;
            float step = _travelSpeed * Time.fixedDeltaTime;

            if (!_isReversing)
            {
                _distanceAlongThread += step;
                if (_distanceAlongThread >= totalLength)
                {
                    if (_activeLoopMode == LoopPlaybackMode.PingPong)
                    {
                        _distanceAlongThread = totalLength;
                        _isReversing = true;
                    }
                    else
                    {
                        _distanceAlongThread = 0f;
                    }
                }
            }
            else
            {
                _distanceAlongThread -= step;
                if (_distanceAlongThread <= 0f)
                {
                    _distanceAlongThread = 0f;
                    _isReversing = false;
                }
            }

            Vector2 targetPos = _sutureThread.GetPointAtDistance(_distanceAlongThread, out _);
            MoveToPosition(targetPos);
        }

        private void TraverseWaypointsLinearFallback()
        {
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
            Vector2 currentPos = transform.position;
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
            if (_hasRb)
            {
                _rb.MovePosition(pos);
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
            _currentState = StitchState.PlayerHarness;

            if (_hasRb)
            {
                _rb.bodyType = RigidbodyType2D.Dynamic;
            }

            if (_tetherJoint == null)
            {
                _tetherJoint = gameObject.GetComponent<DistanceJoint2D>();
                if (_tetherJoint == null)
                {
                    _tetherJoint = gameObject.AddComponent<DistanceJoint2D>();
                }
            }

            float currentDist = Vector2.Distance(transform.position, playerTransform.position);
            _activeTetherLeashLength = Mathf.Clamp(currentDist, _minTetherDistance, _maxTetherDistance);

            _tetherJoint.enabled = true;
            _tetherJoint.connectedBody = playerRb;
            _tetherJoint.autoConfigureDistance = false;
            _tetherJoint.maxDistanceOnly = true;
            _tetherJoint.distance = _activeTetherLeashLength;
            _tetherJoint.enableCollision = true;

            _waypoints.Clear();
            _waypoints.Add(transform.position);
            _waypoints.Add(playerTransform.position);

            if (_sutureThread != null)
            {
                _sutureThread.RenderPath(_waypoints);
            }
        }

        public void ReleasePlayerTether()
        {
            if (_tetherJoint != null)
            {
                _tetherJoint.enabled = false;
                _tetherJoint.connectedBody = null;
            }

            _tetheredPlayerTransform = null;
            _tetheredPlayerRb = null;
            _currentState = StitchState.Idle;

            if (_sutureThread != null)
            {
                _sutureThread.Clear();
            }
        }

        private void UpdatePlayerHarness()
        {
            if (_tetheredPlayerTransform == null)
            {
                ReleasePlayerTether();
                return;
            }

            Vector2 objPos = transform.position;
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
            _distanceAlongThread = 0f;
            _isReversing = false;

            if (_hasRb)
            {
                _rb.bodyType = _originalBodyType;
            }

            if (_sutureThread != null)
            {
                _sutureThread.Clear();
            }
        }
    }
}
