using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Gameplay;

namespace ThailandHorrorJam.Systems.Needle
{
    public enum HangingMode
    {
        [Tooltip("Drawbridge: Rotates down around a hinge/pivot until resting flat as a solid walkway.")]
        Drawbridge,

        [Tooltip("FreeFall: Drops straight down with gravity when cut.")]
        FreeFall
    }

    /// <summary>
    /// Suspends an object (such as a drawbridge plank or hanging crate) with a cuttable red suture thread from the start of the game.
    /// When the player cuts the thread with a needle, the bridge/object falls and locks into place as a walkable platform.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class HangingObject2D : MonoBehaviour, IStitchable
    {
        [Header("Mode")]
        [Tooltip("Drawbridge: Rotates around a pivot down to flat angle. FreeFall: Drops straight down with gravity.")]
        [SerializeField] private HangingMode _mode = HangingMode.Drawbridge;

        [Header("Ceiling & Attachment Anchors")]
        [Tooltip("Transform representing the anchor point on the ceiling/wall where the thread is tied.")]
        [SerializeField] private Transform _ceilingAnchor;

        [Tooltip("Attachment point on this object where the thread is tied (e.g. tip of the bridge). If null, uses object transform.")]
        [SerializeField] private Transform _objectAnchor;

        [Header("Drawbridge Settings (If Mode = Drawbridge)")]
        [Tooltip("Pivot point around which the bridge rotates. If null, rotates around this object's transform position.")]
        [SerializeField] private Transform _hingePivot;

        [Tooltip("Target Z rotation (in degrees) when fully lowered/flat (usually 0 for horizontal bridge).")]
        [SerializeField] private float _flatRestAngle = 0f;

        [Tooltip("Speed (degrees/sec) at which the bridge rotates down upon being cut.")]
        [SerializeField] private float _fallRotationSpeed = 160f;

        [Header("Designer Dimensions")]
        [Tooltip("Bridge plank length. Changing this automatically resizes the Visual sprite, BoxCollider2D, and TipAnchor!")]
        [Min(0.5f)]
        [SerializeField] private float _bridgeLength = 2.4f;

        [Tooltip("Bridge plank thickness/height.")]
        [Min(0.05f)]
        [SerializeField] private float _bridgeThickness = 0.35f;

        [Header("Thread Component")]
        [Tooltip("SutureThread component rendering the hanging thread. If unassigned, auto-finds child or instantiates prefab.")]
        [SerializeField] private SutureThread _sutureThread;

        [Tooltip("Prefab to instantiate if _sutureThread is missing.")]
        [SerializeField] private SutureThread _sutureThreadPrefab;

        private Rigidbody2D _rb;
        private bool _isSuspended = true;
        private bool _isRotatingDown;
        private bool _isLockedAtRest;

        private Vector3 _initialPosition;
        private Quaternion _initialRotation;

        public bool IsSuspended => _isSuspended;
        public bool IsLockedAtRest => _isLockedAtRest;
        public Transform CeilingAnchor => _ceilingAnchor;
        public Transform ObjectAnchor => _objectAnchor;
        public Transform HingePivot => _hingePivot;
        public SutureThread SutureThread => _sutureThread;
        public float BridgeLength => _bridgeLength;
        public float BridgeThickness => _bridgeThickness;
        public float FlatRestAngle => _flatRestAngle;

        private void OnValidate()
        {
            if (_mode == HangingMode.Drawbridge && !Application.isPlaying)
            {
                ApplyDimensions(_bridgeLength, _bridgeThickness);
            }
        }

        /// <summary>
        /// Resizes the BoxCollider2D, TipAnchor, and child Visual sprite together to ensure perfect alignment.
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

            // 3. Child Visual sprite
            Transform visual = transform.Find("Visual");
            if (visual != null)
            {
                visual.localPosition = (Vector3)centerOffset;
                visual.localScale = new Vector3(_bridgeLength, _bridgeThickness, 1f);
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
        public StitchState CurrentState => _isSuspended ? StitchState.Tethered : StitchState.Idle;
        public Transform ObjectTransform => transform;
        public Vector2 CurrentPosition => _rb != null ? _rb.position : (Vector2)transform.position;
        public void AttachThread() { }
        public void UpdatePinnedWaypoints(IReadOnlyList<Vector2> waypoints) { }
        public void UpdateInFlightNeedle(Vector2 needlePos) { }
        public void UpdateAimLeash(Vector2 playerPos, float maxLeashDistance) { }
        public void StartLoopAlongWaypoints(IReadOnlyList<Vector2> waypoints, LoopPlaybackMode mode) { }
        public void StartPlayerTether(Transform playerTransform, Rigidbody2D playerRb) { }
        public void ReleasePlayerTether() { }
        public void CutThread()
        {
            SeverHangingThread();
        }
        #endregion

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            if (_rb != null)
            {
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
            if (_isRotatingDown && _mode == HangingMode.Drawbridge)
            {
                StepDrawbridgeRotation(Time.deltaTime);
            }
        }

        private void EnsureSutureThread()
        {
            if (_sutureThread == null)
            {
                _sutureThread = GetComponentInChildren<SutureThread>(true);
            }

            if (_sutureThread == null && _sutureThreadPrefab != null)
            {
                _sutureThread = Instantiate(_sutureThreadPrefab, transform);
                _sutureThread.name = "HangingSutureThread";
                _sutureThread.transform.localPosition = Vector3.zero;
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
            else
            {
                if (_rb != null)
                {
                    _rb.bodyType = RigidbodyType2D.Dynamic;
                    _rb.gravityScale = 1f;
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
            _isLockedAtRest = false;

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

            // Object attachment tip anchor (Cyan)
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(objPos, 0.1f);

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
