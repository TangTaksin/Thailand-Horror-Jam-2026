using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Serialization;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Renders dynamic, elastic spider-silk suture thread rails inspired by 'Webbed' and 'Animated-Line-Renderer'.
    /// Enhanced with procedural twisted-ply pixel textures, suture knot pins, bending resistance,
    /// Poisson tension thinning, in-flight air whip, and harmonic standing-wave vibrations.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class SutureThread : MonoBehaviour
    {
        public enum SlackMode
        {
            PathContour,     // คำนวณจากความยาวเส้นทางตามแนวภูมิประเทศจริง (ไม่ตึงรั้งมุม และไม่ย้วยเป็นลอนยักษ์)
            StraightLine,    // คำนวณจากระยะขจัดเส้นตรงระหว่างจุดปลาย
            FixedExtraLength // เพิ่มความยาวด้ายคงที่เป็นเมตร
        }

        private const int HitBufferSize = 8;
        private const int InitialNodeCapacity = 48;
        private const int InitialSplineCapacity = 128;
        private const float MinDistanceEpsilon = 0.0001f;
        private const float MinSqrDistanceEpsilon = 0.00001f;
        private const float TangentSleepSqrThreshold = 0.000025f;

        [Header("Appearance")]
        [FormerlySerializedAs("lineRenderer")]
        [SerializeField] private LineRenderer _lineRenderer;

        [FormerlySerializedAs("threadColor")]
        [SerializeField] private Color _threadColor = new Color(0.92f, 0.15f, 0.15f, 0.95f);

        [FormerlySerializedAs("threadWidth")]
        [Tooltip("ความกว้างของเส้นด้าย (3 พิกเซลที่ 32 PPU = 3f / 32f = 0.09375f)")]
        [SerializeField] private float _threadWidth = 0.09375f;

        [Header("Thread Fiber & Realism")]
        [Tooltip("สร้าง Texture ลายเกลียวเส้นด้าย Pixel Art อัตโนมัติ เพื่อให้เห็นมิติการบิดเกลียวของด้าย")]
        [SerializeField] private bool _useProceduralBraidedTexture = true;

        [Tooltip("อัตราการรีดตัวบางลงของเส้นด้ายเมื่อถูกดึงตึงสุดขีด (0 = ไม่บางลง, 0.35 = บางลง 35% ก่อนขาด)")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _maxTensionThinning = 0.28f;

        [Tooltip("แรงต้านการพับหักมุม (ช่วยให้ด้ายโค้งมนเหมือนเส้นไหม ไม่หักเป็นมุมแหลม)")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _bendingStiffness = 0.18f;

        [Tooltip("ความแรงของคลื่นสะบัดกลางอากาศขณะที่เข็มกำลังพุ่ง")]
        [SerializeField] private float _inFlightWhipAmplitude = 0.045f;

        [Header("Pixel Style & Grid Snapping")]
        [Tooltip("เปิดใช้งานการ Snap จุดของเส้นด้ายลงบน Grid พิกเซล เพื่อให้เกิดรอยหยักบันไดแบบ Pixel Art")]
        [SerializeField] private bool _enablePixelSnapping = true;

        [Tooltip("Pixels Per Unit (PPU) สำหรับคำนวณขนาดตารางพิกเซล")]
        [SerializeField] private float _pixelSnapPPU = 32f;

        [Tooltip("เปิดใช้งานขอบดำบน-ล่าง สไตล์ Pixel Art ตามภาพอ้างอิง (ปิดเพื่อใช้สีแดงล้วนไม่มีขอบ)")]
        [SerializeField] private bool _enableOutline = true;

        [Tooltip("สีของขอบเส้นด้าย")]
        [SerializeField] private Color _outlineColor = new Color(0.08f, 0.06f, 0.08f, 1f);

        [Tooltip("สัดส่วนความหนาของขอบบนและล่าง (0.333 = 1 ใน 3 สำหรับเส้น 3 พิกเซล)")]
        [Range(0.05f, 0.45f)]
        [SerializeField] private float _outlineThickness = 0.333f;

        [Header("Tension & Snap Visuals")]
        [Tooltip("สีด้ายที่จะค่อยๆ เปลี่ยนไปเมื่อถูกยืดใกล้ขาด (แดงเลือดหมูเข้ม)")]
        [SerializeField] private Color _strainedThreadColor = new Color(0.48f, 0.04f, 0.06f, 0.95f);

        [Tooltip("ระดับความตึง (0..1) ที่เริ่มไล่เฉดสีอย่างนุ่มนวล")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float _tensionColorThreshold = 0.15f;

        [Tooltip("ระดับความตึง (0..1) ที่เริ่มเกิดการสั่นสะบัดแบบกายภาพ (Quiver)")]
        [Range(0.1f, 0.9f)]
        [SerializeField] private float _quiverStrainThreshold = 0.35f;

        [Tooltip("ระยะสั่นสะบัดสูงสุดของด้ายตอนตึงวิกฤต")]
        [SerializeField] private float _maxVibrationAmplitude = 0.035f;

        [Tooltip("Optional custom sprite for anchor pin dots. If null, a procedural stitch knot sprite is used.")]
        [SerializeField] private Sprite _pinSprite;

        [Tooltip("Target scale for anchor pin markers.")]
        [SerializeField] private float _pinScale = 0.6f;

        [Header("Webbed Physics (Verlet Rope)")]
        [Tooltip("Number of Verlet particles per segment (higher = smoother wave propagation).")]
        [SerializeField] private int _nodesPerSegment = 10;

        [Tooltip("Constraint solver relaxation iterations per physics step.")]
        [SerializeField] private int _constraintIterations = 6;

        [Tooltip("Gravity acceleration pulling down on the rope particles.")]
        [SerializeField] private float _gravity = 5.0f;

        [Tooltip("Air drag damping factor (0 = frictionless, 10 = heavy syrup).")]
        [SerializeField] private float _airDamping = 3.2f;

        [Tooltip("โหมดการคำนวณความหย่อนของด้าย: PathContour (แนะนำ) อิงตามความยาวแนวภูมิประเทศจริง, StraightLine อิงระยะขจัดเส้นตรง")]
        [SerializeField] private SlackMode _slackMode = SlackMode.PathContour;

        [Tooltip("Slack factor: >1.0 allows natural catenary sag; =1.0 is tight.")]
        [FormerlySerializedAs("_sagRatio")]
        [SerializeField] private float _slackFactor = 1.025f;

        [Tooltip("ระยะความหย่อนด้ายส่วนเกินคงที่ (เมตร)")]
        [SerializeField] private float _extraSlackMeters = 0.35f;

        [Tooltip("Transverse impulse (guitar-string pluck) applied on creation or impact.")]
        [FormerlySerializedAs("_twangImpulse")]
        [SerializeField] private float _twangImpulse = 4.2f;

        [Header("Floating Silk Wave (When Gravity < 0)")]
        [Tooltip("ความสูงของคลื่นลมพลิ้วไหวขณะลอยในอากาศ (เมตร) ช่วยให้ด้ายไม่ลอยแข็งเป็นสะพานโค้ง")]
        [SerializeField] private float _floatingWaveAmplitude = 0.12f;

        [Tooltip("ความเร็วในการกระเพื่อมของคลื่นลมลอย")]
        [SerializeField] private float _floatingWaveSpeed = 1.6f;

        [Tooltip("ความถี่ของลอนคลื่นตามแนวเส้นด้าย")]
        [SerializeField] private float _floatingWaveFrequency = 0.28f;

        [Header("Ground & Surface Collision")]
        [Tooltip("Whether the thread collides with and piles up on physical surfaces (Ground, Walls).")]
        [SerializeField] private bool _enableGroundCollision = true;

        [Tooltip("Surface layer mask that the thread collides with.")]
        [SerializeField] private LayerMask _collisionMask;

        [Tooltip("Friction applied to rope particles resting on surfaces (0 = slippery, 1 = sticky pile).")]
        [Range(0f, 1f)]
        [SerializeField] private float _groundFriction = 0.65f;

        [Tooltip("Offset distance of the rope above the collision surface.")]
        [SerializeField] private float _surfaceOffset = 0.035f;

        [Header("Catmull-Rom Spline Rendering")]
        [Tooltip("Subdivisions between each Verlet particle node for silk smoothness.")]
        [SerializeField] private int _splineSubdivisions = 8;

        [Header("Animated Flow (ForeignGods style)")]
        [SerializeField] private Material _materialOverride;
        [SerializeField] private bool _enableAnimation = true;
        [SerializeField] private float _scrollSpeed = 2.5f;
        [SerializeField] private float _tilingFrequency = 2.5f;

        [Header("Pin Visuals")]
        [Tooltip("If true, the destination waypoint pin is hidden because the embedded NeedleProjectile sprite is shown there.")]
        [SerializeField] private bool _hideDestinationPinDot = true;

        [Tooltip("If true, all pin markers are suppressed entirely.")]
        [SerializeField] private bool _hideAllPins = false;

        [Header("Pooled Thread Visuals (Aim Mode)")]
        [Tooltip("จำนวนขดลอนด้ายที่กองอยู่บนพื้นขณะเล็ง (ยิ่งเยอะด้ายยิ่งยาวและเป็นกองหนาขึ้น)")]
        [Range(3, 16)]
        [SerializeField] private int _pooledCoilCount = 8;

        [Tooltip("ระยะการแผ่กว้างของกองด้ายบนพื้นรอบเท้าตัวละคร (เมตร)")]
        [Range(0.1f, 1.2f)]
        [SerializeField] private float _pooledSpreadRadius = 0.42f;

        [Tooltip("ความสูงของลอนด้ายที่กองทับซ้อนกันบนพื้น (เมตร)")]
        [Range(0.01f, 0.15f)]
        [SerializeField] private float _pooledPileHeight = 0.055f;

        [Header("Severing & Cutting")]
        [Tooltip("When true, this thread can be severed by blades or cut needles. If false, it cannot be cut.")]
        [SerializeField] private bool _isCuttable = true;

        public event System.Action OnSevered;

        public bool IsCuttable
        {
            get => _isCuttable;
            set => _isCuttable = value;
        }

        public bool HideAllPins
        {
            get => _hideAllPins;
            set => _hideAllPins = value;
        }

        public bool EnablePixelSnapping
        {
            get => _enablePixelSnapping;
            set => _enablePixelSnapping = value;
        }

        public int PooledCoilCount
        {
            get => _pooledCoilCount;
            set => _pooledCoilCount = Mathf.Clamp(value, 3, 16);
        }

        public float PooledSpreadRadius
        {
            get => _pooledSpreadRadius;
            set => _pooledSpreadRadius = Mathf.Max(0.05f, value);
        }

        public float PooledPileHeight
        {
            get => _pooledPileHeight;
            set => _pooledPileHeight = Mathf.Max(0.005f, value);
        }

        private struct VerletNode
        {
            public Vector2 Position;
            public Vector2 OldPosition;
            public bool IsPinned;
        }

        private struct VerletLink
        {
            public int NodeA;
            public int NodeB;
            public float RestLength;
        }

        private static readonly List<SutureThread> s_ActiveThreads = new List<SutureThread>();
        public static IReadOnlyList<SutureThread> ActiveThreads => s_ActiveThreads;

        private static Sprite s_StitchKnotSprite;
        private static Texture2D s_BraidedThreadTexture;
        private static Transform s_PinContainer;

        private Material _threadMaterial;
        private IStitchable _cachedOwner;
        private bool _hasSearchedOwner;
        private bool _isRegistered;

        private ContactFilter2D _collisionFilter;
        private bool _isCollisionFilterInitialized;
        private readonly Collider2D[] _pointHitBuffer = new Collider2D[HitBufferSize];

        private readonly List<GameObject> _pinMarkers = new List<GameObject>();
        private readonly List<Vector2> _cachedWaypoints = new List<Vector2>();
        private readonly List<int> _pinnedWaypointIndices = new List<int>();

        private readonly List<VerletNode> _nodes = new List<VerletNode>(InitialNodeCapacity);
        private readonly List<VerletLink> _links = new List<VerletLink>(InitialNodeCapacity);
        private readonly List<Vector3> _renderBuffer = new List<Vector3>(InitialSplineCapacity);
        private Vector3[] _linePositionsArray = new Vector3[InitialSplineCapacity];

        private float _totalPathLength;
        private float _currentStrain;
        private Color _lastAppliedColor = Color.clear;
        private float _lastAppliedWidth = -1f;

        private bool _isSplineDirty;
        private bool _isVisualDirty;
        private bool _isTautMode;
        private bool _isInFlightMode;
        private bool _isPooledMode;

        private void Awake()
        {
            EnsureComponents();
            EnsureCollisionFilter();
            ConfigureLineRenderer();
        }

        private void OnEnable()
        {
            RegisterThread();
        }

        private void OnDisable()
        {
            UnregisterThread();
            HidePinMarkers();
        }

        private void OnDestroy()
        {
            UnregisterThread();
            DestroyPinMarkers();

            if (_threadMaterial != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(_threadMaterial);
                }
                else
                {
                    DestroyImmediate(_threadMaterial);
                }
            }
        }

        private void FixedUpdate()
        {
            if (_nodes.Count < 2 || _isInFlightMode) return;

            SimulateVerlet(Time.fixedDeltaTime);
            SolveConstraints();

            _isSplineDirty = true;
            _isVisualDirty = true;
        }

        private void LateUpdate()
        {
            if (_nodes.Count < 2) return;

            if (_isSplineDirty)
            {
                RebuildSplineData();
                UpdateShaderTiling();
                _isSplineDirty = false;
                _isVisualDirty = true;
            }

            if (_isVisualDirty || _currentStrain >= _quiverStrainThreshold || _isInFlightMode)
            {
                ApplySplineToLineRenderer();
                _isVisualDirty = false;
            }
        }

        public void SetTensionStrain(float strain)
        {
            float clamped = Mathf.Clamp01(strain);
            if (!Mathf.Approximately(_currentStrain, clamped))
            {
                _currentStrain = clamped;
                _isVisualDirty = true;
            }
        }

        public void RenderPath(IReadOnlyList<Vector2> waypoints)
        {
            _isInFlightMode = false;
            _isPooledMode = false;
            if (waypoints == null || waypoints.Count < 2)
            {
                Clear();
                return;
            }

            RegisterThread();

            _cachedWaypoints.Clear();
            for (int i = 0; i < waypoints.Count; i++)
            {
                _cachedWaypoints.Add(waypoints[i]);
            }

            BuildVerletRope(true);
            UpdatePinMarkers(_cachedWaypoints);
            FlushImmediateRender();
        }

        /// <summary>
        /// สั่งให้เส้นด้ายขึงตึงเปรี๊ยะทันที (Snap Taut) สำหรับโหมดรางลูป (Looping Rail)
        /// ดึงโหนดทั้งหมดให้ตรงเป๊ะเป็นแนวราง ไม่หย่อนลงดิน พร้อมเอฟเฟกต์ดีดสายสั่นสะเทือนแบบฮาร์มอนิก
        /// </summary>
        public void SnapTaut(float twangImpulse = 4.0f)
        {
            _isInFlightMode = false;
            _isTautMode = true;
            _isPooledMode = false;

            int segmentCount = _cachedWaypoints.Count - 1;
            if (segmentCount <= 0 || _nodes.Count < 2) return;

            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            for (int seg = 0; seg < segmentCount; seg++)
            {
                Vector2 pStart = _cachedWaypoints[seg];
                Vector2 pEnd = _cachedWaypoints[seg + 1];
                Vector2 segDelta = pEnd - pStart;
                float segDist = segDelta.magnitude;
                Vector2 segDir = segDist > 0.001f ? segDelta / segDist : Vector2.right;
                Vector2 segNormal = new Vector2(-segDir.y, segDir.x);

                int startNodeIdx = _pinnedWaypointIndices[seg];
                int endNodeIdx = _pinnedWaypointIndices[seg + 1];
                int nodeSpan = endNodeIdx - startNodeIdx;
                float invSpan = 1f / Mathf.Max(1, nodeSpan);
                float linkRest = segDist * invSpan;

                for (int i = 0; i <= nodeSpan; i++)
                {
                    int nIdx = startNodeIdx + i;
                    float t = i * invSpan;
                    Vector2 straightPos = Vector2.Lerp(pStart, pEnd, t);

                    // ผสมคลื่นความถี่หลัก (Fundamental) และความถี่ฮาร์มอนิกที่ 2 เพื่อให้เหมือนการดีดเส้นด้ายจริง
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    float harmonic = Mathf.Sin(t * Mathf.PI * 2f) * 0.35f;
                    Vector2 pluckVel = segNormal * (twangImpulse * (envelope + harmonic));
                    Vector2 pluckOffset = pluckVel * (0.035f * envelope);

                    VerletNode node = _nodes[nIdx];
                    node.Position = straightPos + pluckOffset;
                    node.OldPosition = straightPos - (pluckVel * dt);
                    _nodes[nIdx] = node;
                }

                for (int l = 0; l < _links.Count; l++)
                {
                    VerletLink link = _links[l];
                    if (link.NodeA >= startNodeIdx && link.NodeB <= endNodeIdx)
                    {
                        link.RestLength = linkRest;
                        _links[l] = link;
                    }
                }
            }

            if (_enableGroundCollision)
            {
                SolveCollisions();
            }

            FlushImmediateRender();
        }

        /// <summary>
        /// Continuously updates trailing suture thread connected to an in-flight needle projectile.
        /// Adds an organic air-whip wave along the trailing silk.
        /// </summary>
        public void UpdateInFlightNeedle(Vector2 origin, Vector2 needlePos)
        {
            _isInFlightMode = true;
            _isTautMode = true;
            _isPooledMode = false;

            if (_cachedWaypoints.Count < 2)
            {
                _cachedWaypoints.Clear();
                _cachedWaypoints.Add(origin);
                _cachedWaypoints.Add(needlePos);
            }
            else
            {
                _cachedWaypoints[0] = origin;
                _cachedWaypoints[1] = needlePos;
            }

            Vector2 delta = needlePos - origin;
            float segDist = delta.magnitude;
            Vector2 dir = segDist > 0.001f ? delta / segDist : Vector2.right;
            Vector2 normal = new Vector2(-dir.y, dir.x);

            int nodesCount = Mathf.Max(4, _nodesPerSegment);
            float invSpan = 1f / (nodesCount - 1);
            float linkRest = segDist * invSpan;

            if (_nodes.Count != nodesCount || _pinnedWaypointIndices.Count < 2)
            {
                _nodes.Clear();
                _links.Clear();
                _pinnedWaypointIndices.Clear();

                for (int i = 0; i < nodesCount; i++)
                {
                    Vector2 pos = EvaluateInFlightNodePosition(origin, needlePos, normal, i * invSpan, segDist);
                    _nodes.Add(new VerletNode
                    {
                        Position = pos,
                        OldPosition = pos,
                        IsPinned = (i == 0 || i == nodesCount - 1)
                    });
                }

                _pinnedWaypointIndices.Add(0);
                _pinnedWaypointIndices.Add(nodesCount - 1);

                for (int i = 0; i < nodesCount - 1; i++)
                {
                    _links.Add(new VerletLink
                    {
                        NodeA = i,
                        NodeB = i + 1,
                        RestLength = linkRest
                    });
                }
            }
            else
            {
                int firstIdx = _pinnedWaypointIndices[0];
                int lastIdx = _pinnedWaypointIndices[1];
                int span = lastIdx - firstIdx;
                float invNodeSpan = 1f / Mathf.Max(1, span);

                for (int i = 0; i <= span; i++)
                {
                    Vector2 pos = EvaluateInFlightNodePosition(origin, needlePos, normal, i * invNodeSpan, segDist);
                    VerletNode node = _nodes[firstIdx + i];
                    node.Position = pos;
                    node.OldPosition = pos;
                    _nodes[firstIdx + i] = node;
                }

                for (int i = 0; i < _links.Count; i++)
                {
                    VerletLink link = _links[i];
                    link.RestLength = linkRest;
                    _links[i] = link;
                }
            }

            if (_enableGroundCollision)
            {
                SolveCollisions();
            }

            UpdatePinMarkersInFlight(origin);
            FlushImmediateRender();
        }

        private Vector2 EvaluateInFlightNodePosition(Vector2 origin, Vector2 needlePos, Vector2 normal, float t, float distance)
        {
            Vector2 basePos = Vector2.Lerp(origin, needlePos, t);
            if (_inFlightWhipAmplitude <= 0f || t <= 0.001f || t >= 0.999f) return basePos;

            // คลื่นสะบัดในอากาศขณะที่ด้ายพุ่งตามเข็ม (ปลายสองฝั่งล็อกแน่น ตรงกลางพริ้วเป็นเกลียวคลื่น)
            float envelope = Mathf.Sin(t * Mathf.PI);
            float distanceScale = Mathf.Clamp01(distance * 0.35f);
            float wave = Mathf.Sin((t * 10f) - (Time.time * 28f)) * _inFlightWhipAmplitude * envelope * distanceScale;
            return basePos + (normal * wave);
        }

        /// <summary>
        /// Continuously updates start and end positions for a dynamic 2-point thread (e.g., tethered to the player hauling a burden).
        /// </summary>
        public void UpdateDynamicEndpoints(Vector2 startPos, Vector2 endPos, float restLengthBudget = -1f, bool hideEndPin = true)
        {
            _isInFlightMode = false;
            _isTautMode = false;
            _isPooledMode = false;

            if (_cachedWaypoints.Count < 2)
            {
                _cachedWaypoints.Clear();
                _cachedWaypoints.Add(startPos);
                _cachedWaypoints.Add(endPos);
            }
            else
            {
                _cachedWaypoints[0] = startPos;
                _cachedWaypoints[_cachedWaypoints.Count - 1] = endPos;
            }

            if (_nodes.Count < 2 || _pinnedWaypointIndices.Count < 2)
            {
                BuildVerletRope(false);
            }
            else
            {
                int firstIdx = _pinnedWaypointIndices[0];
                int lastIdx = _pinnedWaypointIndices[_pinnedWaypointIndices.Count - 1];

                VerletNode firstNode = _nodes[firstIdx];
                firstNode.Position = startPos;
                firstNode.OldPosition = startPos;
                _nodes[firstIdx] = firstNode;

                VerletNode lastNode = _nodes[lastIdx];
                lastNode.Position = endPos;
                lastNode.OldPosition = endPos;
                _nodes[lastIdx] = lastNode;
            }

            UpdatePinMarkers(_cachedWaypoints);

            if (hideEndPin && _pinMarkers.Count > 1 && _pinMarkers[1] != null && _pinMarkers[1].activeSelf)
            {
                _pinMarkers[1].transform.DOKill();
                _pinMarkers[1].SetActive(false);
            }

            int linkCount = _links.Count;
            if (linkCount > 0)
            {
                float straightDist = Vector2.Distance(startPos, endPos);
                float baseRest = restLengthBudget > 0f ? restLengthBudget : straightDist;
                float targetRestLength = (baseRest * Mathf.Max(1.0f, _slackFactor)) + _extraSlackMeters;
                float perLinkRest = targetRestLength / linkCount;
                for (int i = 0; i < linkCount; i++)
                {
                    VerletLink link = _links[i];
                    link.RestLength = perLinkRest;
                    _links[i] = link;
                }
            }

            if (_enableGroundCollision)
            {
                SolveCollisions();
            }

            _isSplineDirty = true;
            _isVisualDirty = true;
        }

        public bool IsPooledActive => _isPooledMode && _nodes.Count >= 2;

        /// <summary>
        /// Initializes a free-hanging physical thread from the player's hand that falls and pools onto the ground under gravity.
        /// </summary>
        public void InitializePooledRope(Vector2 handPos, Vector2 groundPos, float facingDir)
        {
            _isInFlightMode = false;
            _isTautMode = false;
            _isPooledMode = true;

            EnsureComponents();
            EnsureCollisionFilter();

            _cachedWaypoints.Clear();
            _cachedWaypoints.Add(handPos);

            _nodes.Clear();
            _links.Clear();
            _pinnedWaypointIndices.Clear();

            int totalNodes = Mathf.Clamp(_pooledCoilCount + 4, 8, 20);
            float dropDist = Mathf.Max(0.2f, handPos.y - groundPos.y);
            float totalRopeLength = dropDist + (_pooledSpreadRadius * 2.5f);
            float linkRest = totalRopeLength / (totalNodes - 1);

            // 1. Hand anchor (node 0 - pinned)
            _nodes.Add(new VerletNode
            {
                Position = handPos,
                OldPosition = handPos,
                IsPinned = true
            });
            _pinnedWaypointIndices.Add(0);

            // 2. Hanging nodes down towards the ground
            int dropNodes = Mathf.Clamp(Mathf.RoundToInt(dropDist / linkRest), 2, 5);
            for (int i = 1; i <= dropNodes; i++)
            {
                float t = (float)i / dropNodes;
                Vector2 pos = Vector2.Lerp(handPos, new Vector2(groundPos.x + facingDir * 0.08f, groundPos.y + _surfaceOffset), t);
                _nodes.Add(new VerletNode
                {
                    Position = pos,
                    OldPosition = pos,
                    IsPinned = false
                });
            }

            // 3. Excess rope nodes pooled on the ground
            int poolNodes = totalNodes - _nodes.Count;
            for (int i = 0; i < poolNodes; i++)
            {
                float side = Mathf.Sin(i * 1.8f);
                float xOffset = (facingDir * (0.08f + (i * 0.035f))) + (side * _pooledSpreadRadius * 0.35f);
                Vector2 pos = new Vector2(groundPos.x + xOffset, groundPos.y + _surfaceOffset + (i % 2) * _pooledPileHeight);
                _nodes.Add(new VerletNode
                {
                    Position = pos,
                    OldPosition = pos,
                    IsPinned = false
                });
            }

            // 4. Connect links
            for (int i = 0; i < _nodes.Count - 1; i++)
            {
                _links.Add(new VerletLink
                {
                    NodeA = i,
                    NodeB = i + 1,
                    RestLength = linkRest
                });
            }

            if (_hideAllPins)
            {
                HidePinMarkers();
            }
            else
            {
                UpdatePinMarkersInFlight(handPos);
            }

            // Settle initial positions against ground colliders
            for (int step = 0; step < 6; step++)
            {
                SimulateVerlet(0.016f);
                SolveConstraints();
            }

            FlushImmediateRender();
        }

        public void UpdatePooledHandPosition(Vector2 handPos)
        {
            if (!_isPooledMode || _nodes.Count < 2) return;

            if (_cachedWaypoints.Count > 0)
            {
                _cachedWaypoints[0] = handPos;
            }

            VerletNode topNode = _nodes[0];
            topNode.Position = handPos;
            topNode.OldPosition = handPos;
            topNode.IsPinned = true;
            _nodes[0] = topNode;

            if (!_hideAllPins)
            {
                UpdatePinMarkersInFlight(handPos);
            }

            _isSplineDirty = true;
            _isVisualDirty = true;
        }

        /// <summary>
        /// Renders an organic pile of thread draped from the player's hand down to the floor at their feet while aiming.
        /// </summary>
        public void UpdatePooledAtFeet(Vector2 handPos, Vector2 feetPos, float facingDir)
        {
            if (!IsPooledActive)
            {
                InitializePooledRope(handPos, feetPos, facingDir);
            }
            else
            {
                UpdatePooledHandPosition(handPos);
            }
        }

        public void Pluck(Vector2 worldPosition, Vector2 impulse, float radius = 1.4f)
        {
            int nodeCount = _nodes.Count;
            if (nodeCount == 0 || radius <= 0f) return;

            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
            float radiusSq = radius * radius;
            float invRadius = 1f / radius;

            for (int i = 0; i < nodeCount; i++)
            {
                VerletNode node = _nodes[i];
                if (node.IsPinned) continue;

                float distSq = (worldPosition - node.Position).sqrMagnitude;
                if (distSq < radiusSq)
                {
                    float t = Mathf.Sqrt(distSq) * invRadius;
                    float falloff = 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
                    node.OldPosition -= impulse * (falloff * dt);
                    _nodes[i] = node;
                }
            }
        }

        public void Clear()
        {
            _isInFlightMode = false;
            _isTautMode = false;
            _isPooledMode = false;
            _currentStrain = 0f;
            ApplyThreadColor(_threadColor, true);
            UpdateThreadWidth(0f);

            _cachedWaypoints.Clear();
            _pinnedWaypointIndices.Clear();
            _nodes.Clear();
            _links.Clear();
            _renderBuffer.Clear();
            _totalPathLength = 0f;
            _isSplineDirty = false;
            _isVisualDirty = false;

            if (_lineRenderer != null)
            {
                _lineRenderer.positionCount = 0;
            }

            HidePinMarkers();
        }

        public int NodesPerSegment
        {
            get => _nodesPerSegment;
            set => _nodesPerSegment = Mathf.Max(3, value);
        }

        public SlackMode SlackCalculationMode
        {
            get => _slackMode;
            set => _slackMode = value;
        }

        public float ExtraSlackMeters
        {
            get => _extraSlackMeters;
            set => _extraSlackMeters = Mathf.Max(0f, value);
        }

        public float SlackFactor
        {
            get => _slackFactor;
            set => _slackFactor = Mathf.Max(1.0f, value);
        }

        public float BendingStiffness
        {
            get => _bendingStiffness;
            set => _bendingStiffness = Mathf.Clamp01(value);
        }

        public float Gravity
        {
            get => _gravity;
            set => _gravity = value;
        }

        public float AirDamping
        {
            get => _airDamping;
            set => _airDamping = Mathf.Max(0f, value);
        }

        public float FloatingWaveAmplitude
        {
            get => _floatingWaveAmplitude;
            set => _floatingWaveAmplitude = Mathf.Max(0f, value);
        }

        public float FloatingWaveSpeed
        {
            get => _floatingWaveSpeed;
            set => _floatingWaveSpeed = value;
        }

        public float FloatingWaveFrequency
        {
            get => _floatingWaveFrequency;
            set => _floatingWaveFrequency = value;
        }

        /// <summary>
        /// Rapidly contracts all verlet particles towards a target position and dissolves the thread.
        /// Perfect for cutscene snap & reel-in effects.
        /// </summary>
        public void ReelInTo(Vector2 targetPos, float duration, System.Action onComplete = null)
        {
            StartCoroutine(RoutineReelInTo(targetPos, duration, onComplete));
        }

        private IEnumerator RoutineReelInTo(Vector2 targetPos, float duration, System.Action onComplete)
        {
            _isInFlightMode = true; // suppress normal verlet constraints
            float elapsed = 0f;
            int count = _nodes.Count;
            Vector2[] startPositions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                startPositions[i] = _nodes[i].Position;
            }

            Color initialColor = _threadColor;
            float initialWidth = _threadWidth;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float easeT = t * t; // InQuad

                for (int i = 0; i < count; i++)
                {
                    VerletNode node = _nodes[i];
                    node.Position = Vector2.Lerp(startPositions[i], targetPos, easeT);
                    node.OldPosition = node.Position;
                    _nodes[i] = node;
                }

                Color c = initialColor;
                c.a = Mathf.Lerp(initialColor.a, 0f, t);
                ApplyThreadColor(c, true);

                _isSplineDirty = true;
                _isVisualDirty = true;
                yield return null;
            }

            Clear();
            onComplete?.Invoke();
        }

        public void Sever()
        {
            OnSevered?.Invoke();

            IStitchable owner = GetOwner();
            if (owner != null)
            {
                owner.CutThread();
            }
            else
            {
                Clear();
            }
        }

        public bool IntersectsSegment(Vector2 sliceStart, Vector2 sliceEnd, float touchRadius = 0.2f)
        {
            if (!_isCuttable || _nodes.Count < 2) return false;

            if (_isSplineDirty)
            {
                RebuildSplineData();
                _isSplineDirty = false;
            }

            int pointCount = _renderBuffer.Count;
            if (pointCount < 2) return false;

            float sqrTouchRadius = touchRadius * touchRadius;
            float minX = Mathf.Min(sliceStart.x, sliceEnd.x) - touchRadius;
            float maxX = Mathf.Max(sliceStart.x, sliceEnd.x) + touchRadius;
            float minY = Mathf.Min(sliceStart.y, sliceEnd.y) - touchRadius;
            float maxY = Mathf.Max(sliceStart.y, sliceEnd.y) + touchRadius;

            for (int i = 0; i < pointCount - 1; i++)
            {
                Vector2 segA = _renderBuffer[i];
                Vector2 segB = _renderBuffer[i + 1];

                if (maxX < Mathf.Min(segA.x, segB.x) || minX > Mathf.Max(segA.x, segB.x) ||
                    maxY < Mathf.Min(segA.y, segB.y) || minY > Mathf.Max(segA.y, segB.y))
                {
                    continue;
                }

                if (SegmentsIntersect(sliceStart, sliceEnd, segA, segB))
                {
                    return true;
                }

                if (touchRadius > 0f &&
                    (SqrDistanceToSegment(sliceStart, segA, segB) <= sqrTouchRadius ||
                     SqrDistanceToSegment(sliceEnd, segA, segB) <= sqrTouchRadius ||
                     SqrDistanceToSegment(segA, sliceStart, sliceEnd) <= sqrTouchRadius ||
                     SqrDistanceToSegment(segB, sliceStart, sliceEnd) <= sqrTouchRadius))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TrySeverBySegment(Vector2 sliceStart, Vector2 sliceEnd, float touchRadius = 0.2f)
        {
            if (IntersectsSegment(sliceStart, sliceEnd, touchRadius))
            {
                Sever();
                return true;
            }

            return false;
        }

        public static bool AnyThreadIntersectsSegment(Vector2 sliceStart, Vector2 sliceEnd, float touchRadius = 0.2f)
        {
            for (int i = 0; i < s_ActiveThreads.Count; i++)
            {
                SutureThread thread = s_ActiveThreads[i];
                if (thread != null && thread.IntersectsSegment(sliceStart, sliceEnd, touchRadius))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool SegmentsIntersect(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            Vector2 r = b - a;
            Vector2 s = d - c;
            float rxs = r.x * s.y - r.y * s.x;

            if (Mathf.Abs(rxs) < 1e-7f) return false;

            Vector2 ca = c - a;
            float invRxs = 1f / rxs;
            float t = (ca.x * s.y - ca.y * s.x) * invRxs;
            if (t < 0f || t > 1f) return false;

            float u = (ca.x * r.y - ca.y * r.x) * invRxs;
            return u >= 0f && u <= 1f;
        }

        public static int SeverThreadsIntersectingSegment(Vector2 sliceStart, Vector2 sliceEnd, float touchRadius = 0.2f)
        {
            int severedCount = 0;
            for (int i = s_ActiveThreads.Count - 1; i >= 0; i--)
            {
                if (i < s_ActiveThreads.Count && s_ActiveThreads[i] != null)
                {
                    if (s_ActiveThreads[i].TrySeverBySegment(sliceStart, sliceEnd, touchRadius))
                    {
                        severedCount++;
                    }
                }
            }
            return severedCount;
        }

        private void RegisterThread()
        {
            if (!_isCuttable) return;

            if (!_isRegistered)
            {
                s_ActiveThreads.Add(this);
                _isRegistered = true;
            }
        }

        private void UnregisterThread()
        {
            if (_isRegistered)
            {
                s_ActiveThreads.Remove(this);
                _isRegistered = false;
            }
        }

        private IStitchable GetOwner()
        {
            if (!_hasSearchedOwner || _cachedOwner == null)
            {
                _cachedOwner = GetComponentInParent<IStitchable>();
                _hasSearchedOwner = true;
            }
            return _cachedOwner;
        }

        private void EnsureComponents()
        {
            if (_lineRenderer == null && !TryGetComponent(out _lineRenderer))
            {
                _lineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            GetOwner();

            if (_linePositionsArray == null || _linePositionsArray.Length == 0)
            {
                _linePositionsArray = new Vector3[InitialSplineCapacity];
            }
        }

        private void EnsureCollisionFilter()
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            if (_isCollisionFilterInitialized && _collisionMask.value != 0)
            {
                if (groundLayer != -1) _collisionMask.value |= (1 << groundLayer);
                _collisionFilter.SetLayerMask(_collisionMask);
                return;
            }

            if (_collisionMask.value == 0)
            {
                int defaultLayer = LayerMask.NameToLayer("Default");
                int stitchLayer = LayerMask.NameToLayer("Stitchable");
                int mask = 0;
                if (groundLayer != -1) mask |= (1 << groundLayer);
                if (defaultLayer != -1) mask |= (1 << defaultLayer);
                if (stitchLayer != -1) mask |= (1 << stitchLayer);
                _collisionMask = mask != 0 ? mask : LayerMask.GetMask("Ground", "Default", "Stitchable");
            }
            else if (groundLayer != -1)
            {
                _collisionMask.value |= (1 << groundLayer);
            }

            _collisionFilter = new ContactFilter2D
            {
                useLayerMask = true,
                useTriggers = false
            };
            _collisionFilter.SetLayerMask(_collisionMask);
            _isCollisionFilterInitialized = true;
        }

        private void ConfigureLineRenderer()
        {
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 0;
            _lineRenderer.sortingOrder = 10;
            _lineRenderer.textureMode = LineTextureMode.Tile;
            _lineRenderer.alignment = LineAlignment.View;
            _lineRenderer.numCapVertices = 4;
            _lineRenderer.numCornerVertices = 8;

            // ใช้ Width Curve ที่จำลองปมด้ายตรงจุดยึดและลอนเกลียวด้ายตรงกลาง
            _lineRenderer.widthCurve = CreateOrganicThreadWidthCurve();
            _lineRenderer.widthMultiplier = _threadWidth;

            _threadMaterial = SutureVisualUtils.CreateSutureMaterial(
                _materialOverride,
                _threadColor,
                _scrollSpeed,
                _tilingFrequency,
                _enableOutline,
                _outlineColor,
                _outlineThickness);

            if (_threadMaterial != null)
            {
                if (_useProceduralBraidedTexture && _threadMaterial.mainTexture == null)
                {
                    _threadMaterial.mainTexture = GetOrCreateBraidedThreadTexture();
                }
                _lineRenderer.material = _threadMaterial;
            }

            ApplyThreadColor(_threadColor, true);
        }

        /// <summary>
        /// สร้างกราฟความหนาของเส้นด้ายที่บานออกเล็กน้อยตรงจุดปักเข็ม และมีจังหวะคอดเหมือนด้ายตีเกลียว
        /// </summary>
        private static AnimationCurve CreateOrganicThreadWidthCurve()
        {
            AnimationCurve curve = new AnimationCurve(
                new Keyframe(0.00f, 1.28f, 0f, -2.5f),
                new Keyframe(0.08f, 0.94f, 0f, 0f),
                new Keyframe(0.28f, 1.03f, 0f, 0f),
                new Keyframe(0.50f, 0.92f, 0f, 0f),
                new Keyframe(0.72f, 1.03f, 0f, 0f),
                new Keyframe(0.92f, 0.94f, 0f, 0f),
                new Keyframe(1.00f, 1.28f, 2.5f, 0f)
            );
            return curve;
        }

        private void UpdateThreadWidth(float strain)
        {
            if (_lineRenderer == null) return;

            // Poisson Effect: เมื่อด้ายถูกดึงตึง เส้นด้ายจะค่อยๆ รีดตัวบางลง
            float thinFactor = 1f - (strain * _maxTensionThinning);
            float targetWidth = _threadWidth * thinFactor;

            if (!Mathf.Approximately(_lastAppliedWidth, targetWidth))
            {
                _lineRenderer.widthMultiplier = targetWidth;
                _lastAppliedWidth = targetWidth;
            }
        }

        private void ApplyThreadColor(Color color, bool forceUpdate = false)
        {
            if (!forceUpdate && color == _lastAppliedColor) return;
            _lastAppliedColor = color;

            if (_lineRenderer != null)
            {
                // ไล่ระดับความสว่างให้ปลายจุดยึดเข้มขึ้นเล็กน้อยเหมือนเงาตรงรูเข็ม
                Color anchorTint = new Color(color.r * 0.85f, color.g * 0.82f, color.b * 0.82f, color.a);
                _lineRenderer.startColor = anchorTint;
                _lineRenderer.endColor = anchorTint;
            }

            if (_threadMaterial != null)
            {
                _threadMaterial.SetColor(SutureVisualUtils.BaseColorId, color);
                if (forceUpdate)
                {
                    _threadMaterial.SetFloat(SutureVisualUtils.EnableOutlineId, _enableOutline ? 1f : 0f);
                    _threadMaterial.SetColor(SutureVisualUtils.OutlineColorId, _outlineColor);
                    _threadMaterial.SetFloat(SutureVisualUtils.OutlineThicknessId, _outlineThickness);
                }
            }
        }

        private void FlushImmediateRender()
        {
            RebuildSplineData();
            ApplySplineToLineRenderer();
            UpdateShaderTiling();
            _isSplineDirty = false;
            _isVisualDirty = false;
        }

        private void BuildVerletRope(bool applyTwang = false)
        {
            _nodes.Clear();
            _links.Clear();
            _pinnedWaypointIndices.Clear();

            int segmentCount = _cachedWaypoints.Count - 1;
            if (segmentCount <= 0) return;

            EnsureCollisionFilter();
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;
            int nodesThisSeg = Mathf.Max(3, _nodesPerSegment);
            float invSegSpan = 1f / (nodesThisSeg - 1);
            float slack = Mathf.Max(1.0f, _slackFactor);

            for (int seg = 0; seg < segmentCount; seg++)
            {
                Vector2 pStart = _cachedWaypoints[seg];
                Vector2 pEnd = _cachedWaypoints[seg + 1];
                float segDistance = Vector2.Distance(pStart, pEnd);

                Vector2 segDir = segDistance > 0.001f ? (pEnd - pStart) / segDistance : Vector2.right;
                Vector2 segNormal = new Vector2(-segDir.y, segDir.x);
                float linkRestLength = segDistance * invSegSpan * slack;

                int startNodeIdx;
                if (seg > 0)
                {
                    startNodeIdx = _nodes.Count - 1;
                }
                else
                {
                    startNodeIdx = 0;
                    _nodes.Add(new VerletNode
                    {
                        Position = pStart,
                        OldPosition = pStart,
                        IsPinned = true
                    });
                    _pinnedWaypointIndices.Add(0);
                }

                for (int i = 1; i < nodesThisSeg - 1; i++)
                {
                    float t = i * invSegSpan;
                    Vector2 basePos = Vector2.Lerp(pStart, pEnd, t);
                    float envelope = Mathf.Sin(t * Mathf.PI);

                    Vector2 pos = basePos;

                    if (_enableGroundCollision)
                    {
                        // Check if ground surface exists above basePos (e.g. spanning over hills/platforms)
                        float topRayY = Mathf.Max(pStart.y, pEnd.y) + 8f;
                        RaycastHit2D groundHit = Physics2D.Raycast(new Vector2(basePos.x, topRayY), Vector2.down, topRayY - basePos.y + 10f, _collisionMask);
                        if (groundHit.collider != null && !ShouldIgnoreCollider(groundHit.collider) && groundHit.point.y > basePos.y)
                        {
                            pos.y = groundHit.point.y + _surfaceOffset;
                        }
                        else
                        {
                            int hitCount = Physics2D.OverlapPoint(pos, _collisionFilter, _pointHitBuffer);
                            for (int h = 0; h < hitCount; h++)
                            {
                                if (TryResolveColliderContact(_pointHitBuffer[h], pos, _surfaceOffset, out Vector2 resolved, out _))
                                {
                                    pos = resolved;
                                    break;
                                }
                            }
                        }
                    }

                    Vector2 oldPos = pos;

                    if (applyTwang)
                    {
                        Vector2 pluckOffset = (segNormal * (_twangImpulse * 0.08f * envelope)) + (Vector2.down * (0.04f * envelope));
                        Vector2 candidatePos = pos + pluckOffset;

                        if (!_enableGroundCollision || Physics2D.OverlapPoint(candidatePos, _collisionFilter, _pointHitBuffer) == 0)
                        {
                            pos = candidatePos;
                            Vector2 initialVel = segNormal * (_twangImpulse * 0.65f * envelope);
                            oldPos = pos - (initialVel * dt);
                        }
                    }

                    _nodes.Add(new VerletNode
                    {
                        Position = pos,
                        OldPosition = oldPos,
                        IsPinned = false
                    });
                }

                _nodes.Add(new VerletNode
                {
                    Position = pEnd,
                    OldPosition = pEnd,
                    IsPinned = true
                });
                _pinnedWaypointIndices.Add(_nodes.Count - 1);

                int segEnd = _nodes.Count - 1;
                for (int n = startNodeIdx; n < segEnd; n++)
                {
                    _links.Add(new VerletLink
                    {
                        NodeA = n,
                        NodeB = n + 1,
                        RestLength = linkRestLength
                    });
                }
            }

            if (_enableGroundCollision)
            {
                SolveCollisions();
            }
        }

        private void SimulateVerlet(float dt)
        {
            float gravity = _isPooledMode ? Mathf.Max(_gravity * 2.5f, 15f) : _gravity;
            Vector2 gravityStep = _isTautMode ? Vector2.zero : Vector2.down * (gravity * dt * dt);
            float currentDamping = _isTautMode ? _airDamping * 1.6f : _airDamping;
            float dragFactor = Mathf.Clamp01(1f - (currentDamping * dt));

            bool applyFloatingWave = !_isTautMode && !_isPooledMode && gravity < 0f && _floatingWaveAmplitude > 0.001f;
            float time = Time.time;

            int nodeCount = _nodes.Count;
            float invNodeSpan = nodeCount > 1 ? 1f / (nodeCount - 1) : 1f;

            for (int i = 0; i < nodeCount; i++)
            {
                VerletNode node = _nodes[i];
                if (node.IsPinned) continue;

                Vector2 velocity = (node.Position - node.OldPosition) * dragFactor;
                node.OldPosition = node.Position;

                Vector2 forceStep = gravityStep;
                if (applyFloatingWave)
                {
                    float t = i * invNodeSpan;
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    float wave = (Mathf.Sin((time * _floatingWaveSpeed) + (i * _floatingWaveFrequency))
                                + Mathf.Sin((time * _floatingWaveSpeed * 1.7f) - (i * _floatingWaveFrequency * 1.8f)) * 0.45f)
                                * _floatingWaveAmplitude * envelope;
                    forceStep += new Vector2(0f, wave * dt);
                }

                node.Position += velocity + forceStep;
                _nodes[i] = node;
            }
        }

        private void SolveConstraints()
        {
            int pinnedCount = _pinnedWaypointIndices.Count;
            for (int w = 0; w < pinnedCount; w++)
            {
                if (w >= _cachedWaypoints.Count) break;
                int nodeIdx = _pinnedWaypointIndices[w];
                if (nodeIdx < 0 || nodeIdx >= _nodes.Count) continue;
                Vector2 targetPos = _cachedWaypoints[w];
                VerletNode pinnedNode = _nodes[nodeIdx];
                pinnedNode.Position = targetPos;
                pinnedNode.OldPosition = targetPos;
                _nodes[nodeIdx] = pinnedNode;
            }

            int linkCount = _links.Count;
            int nodeCount = _nodes.Count;
            bool applyBending = _bendingStiffness > 0.001f && !_isTautMode;

            for (int iter = 0; iter < _constraintIterations; iter++)
            {
                // 1. Primary distance constraints
                for (int i = 0; i < linkCount; i++)
                {
                    VerletLink link = _links[i];
                    VerletNode nodeA = _nodes[link.NodeA];
                    VerletNode nodeB = _nodes[link.NodeB];

                    Vector2 delta = nodeB.Position - nodeA.Position;
                    float dist = delta.magnitude;
                    if (dist < MinDistanceEpsilon) continue;

                    float error = (dist - link.RestLength) / dist;

                    if (!nodeA.IsPinned && !nodeB.IsPinned)
                    {
                        Vector2 correction = delta * (error * 0.5f);
                        nodeA.Position += correction;
                        nodeB.Position -= correction;
                        _nodes[link.NodeA] = nodeA;
                        _nodes[link.NodeB] = nodeB;
                    }
                    else if (!nodeA.IsPinned)
                    {
                        nodeA.Position += delta * error;
                        _nodes[link.NodeA] = nodeA;
                    }
                    else if (!nodeB.IsPinned)
                    {
                        nodeB.Position -= delta * error;
                        _nodes[link.NodeB] = nodeB;
                    }
                }

                // 2. Silk Bending Resistance: smooths sharp corner kinks so thread arcs smoothly
                if (applyBending && (iter & 1) == 0)
                {
                    float bendWeight = Mathf.Clamp01(_bendingStiffness * 0.5f);
                    for (int i = 1; i < nodeCount - 1; i++)
                    {
                        VerletNode mid = _nodes[i];
                        if (mid.IsPinned) continue;

                        Vector2 avgNeighbors = (_nodes[i - 1].Position + _nodes[i + 1].Position) * 0.5f;
                        mid.Position = Vector2.Lerp(mid.Position, avgNeighbors, bendWeight);
                        _nodes[i] = mid;
                    }
                }
            }

            if (_enableGroundCollision)
            {
                SolveCollisions();
            }
        }

        private bool ShouldIgnoreCollider(Collider2D hit)
        {
            if (hit == null || hit.isTrigger) return true;

            IStitchable owner = GetOwner();
            if (owner != null && owner.ObjectTransform != null &&
                hit.transform.IsChildOf(owner.ObjectTransform))
            {
                return true;
            }

            return PlayerController2D.IsPlayer(hit);
        }

        private bool TryResolveColliderContact(Collider2D col, Vector2 point, float surfaceOffset, out Vector2 resolvedPoint, out Vector2 outNormal)
        {
            resolvedPoint = point;
            outNormal = Vector2.up;

            if (col == null || ShouldIgnoreCollider(col)) return false;

            if (col is BoxCollider2D box)
            {
                Transform t = box.transform;
                Vector2 localPoint = t.InverseTransformPoint(point);
                Vector2 halfSize = box.size * 0.5f;
                Vector2 min = box.offset - halfSize;
                Vector2 max = box.offset + halfSize;

                // Corner fillet: rounds 90-degree sharp corners so threads curve naturally over edges
                float filletR = Mathf.Min(0.20f, Mathf.Min(halfSize.x, halfSize.y) * 0.45f);

                bool isInside = (localPoint.x >= min.x && localPoint.x <= max.x &&
                                 localPoint.y >= min.y && localPoint.y <= max.y);

                if (isInside)
                {
                    float dLeft = localPoint.x - min.x;
                    float dRight = max.x - localPoint.x;
                    float dBottom = localPoint.y - min.y;
                    float dTop = max.y - localPoint.y;

                    Vector2 localSurface = localPoint;
                    Vector2 localNorm = Vector2.up;

                    // Check top-left and top-right corners for smooth fillet arcing
                    if (dTop < filletR && dLeft < filletR)
                    {
                        Vector2 cCenter = new Vector2(min.x + filletR, max.y - filletR);
                        Vector2 cDiff = localPoint - cCenter;
                        localNorm = cDiff.sqrMagnitude > MinDistanceEpsilon ? cDiff.normalized : new Vector2(-0.7071f, 0.7071f);
                        localSurface = cCenter + localNorm * filletR;
                    }
                    else if (dTop < filletR && dRight < filletR)
                    {
                        Vector2 cCenter = new Vector2(max.x - filletR, max.y - filletR);
                        Vector2 cDiff = localPoint - cCenter;
                        localNorm = cDiff.sqrMagnitude > MinDistanceEpsilon ? cDiff.normalized : new Vector2(0.7071f, 0.7071f);
                        localSurface = cCenter + localNorm * filletR;
                    }
                    else
                    {
                        float minD = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBottom * 1.5f, dTop));
                        if (Mathf.Abs(minD - dTop) < 0.001f)
                        {
                            localSurface.y = max.y;
                            localNorm = Vector2.up;
                        }
                        else if (Mathf.Abs(minD - dLeft) < 0.001f)
                        {
                            localSurface.x = min.x;
                            localNorm = Vector2.left;
                        }
                        else if (Mathf.Abs(minD - dRight) < 0.001f)
                        {
                            localSurface.x = max.x;
                            localNorm = Vector2.right;
                        }
                        else
                        {
                            localSurface.y = min.y;
                            localNorm = Vector2.down;
                        }
                    }

                    Vector2 worldSurface = t.TransformPoint(localSurface);
                    Vector2 worldNorm = t.TransformDirection(localNorm).normalized;
                    resolvedPoint = worldSurface + worldNorm * surfaceOffset;
                    outNormal = worldNorm;
                    return true;
                }
                else
                {
                    Vector2 closest = box.ClosestPoint(point);
                    Vector2 diff = point - closest;
                    float distSq = diff.sqrMagnitude;
                    if (distSq < surfaceOffset * surfaceOffset)
                    {
                        float dist = Mathf.Sqrt(distSq);
                        Vector2 worldNorm = dist > MinDistanceEpsilon ? diff / dist : (Vector2)t.TransformDirection(Vector2.up).normalized;
                        resolvedPoint = closest + worldNorm * surfaceOffset;
                        outNormal = worldNorm;
                        return true;
                    }
                    return false;
                }
            }

            if (col is CircleCollider2D circle)
            {
                Transform t = circle.transform;
                Vector2 localPoint = t.InverseTransformPoint(point);
                Vector2 diff = localPoint - circle.offset;
                float dist = diff.magnitude;
                float radius = circle.radius;

                if (dist <= radius)
                {
                    Vector2 localNorm = dist > MinDistanceEpsilon ? diff / dist : Vector2.up;
                    Vector2 localSurface = circle.offset + localNorm * radius;
                    Vector2 worldSurface = t.TransformPoint(localSurface);
                    Vector2 worldNorm = t.TransformDirection(localNorm).normalized;
                    resolvedPoint = worldSurface + worldNorm * surfaceOffset;
                    outNormal = worldNorm;
                    return true;
                }
                else
                {
                    Vector2 closest = circle.ClosestPoint(point);
                    Vector2 worldDiff = point - closest;
                    float distSq = worldDiff.sqrMagnitude;
                    if (distSq < surfaceOffset * surfaceOffset)
                    {
                        float d = Mathf.Sqrt(distSq);
                        Vector2 worldNorm = d > MinDistanceEpsilon ? worldDiff / d : Vector2.up;
                        resolvedPoint = closest + worldNorm * surfaceOffset;
                        outNormal = worldNorm;
                        return true;
                    }
                    return false;
                }
            }

            // Generic PolygonCollider2D / CompositeCollider2D / Edge fallback
            bool isOverlapped = col.OverlapPoint(point);
            if (isOverlapped)
            {
                Vector2 center = col.bounds.center;
                Vector2 dir = (point - center);
                Vector2 outward = dir.sqrMagnitude > MinDistanceEpsilon ? dir.normalized : Vector2.up;

                float maxDim = Mathf.Max(col.bounds.size.x, col.bounds.size.y) + 1f;
                Vector2 rayStart = point + outward * maxDim;
                RaycastHit2D hit = Physics2D.Raycast(rayStart, -outward, maxDim * 2f, _collisionMask);
                if (hit.collider == col)
                {
                    resolvedPoint = hit.point + hit.normal * surfaceOffset;
                    outNormal = hit.normal;
                    return true;
                }

                resolvedPoint = new Vector2(point.x, col.bounds.max.y + surfaceOffset);
                outNormal = Vector2.up;
                return true;
            }
            else
            {
                Vector2 closest = col.ClosestPoint(point);
                Vector2 diff = point - closest;
                float distSq = diff.sqrMagnitude;
                if (distSq < surfaceOffset * surfaceOffset)
                {
                    float dist = Mathf.Sqrt(distSq);
                    Vector2 norm = dist > MinDistanceEpsilon ? diff / dist : Vector2.up;
                    resolvedPoint = closest + norm * surfaceOffset;
                    outNormal = norm;
                    return true;
                }
                return false;
            }
        }

        private void SolveCollisions()
        {
            EnsureCollisionFilter();

            int nodeCount = _nodes.Count;
            float checkRadius = Mathf.Max(_surfaceOffset * 2.0f, 0.12f);
            float frictionFactor = Mathf.Clamp01(1f - _groundFriction);

            for (int i = 0; i < nodeCount; i++)
            {
                VerletNode node = _nodes[i];
                if (node.IsPinned) continue;

                int hitCount = Physics2D.OverlapCircle(node.Position, checkRadius, _collisionFilter, _pointHitBuffer);
                if (hitCount == 0)
                {
                    hitCount = Physics2D.OverlapPoint(node.Position, _collisionFilter, _pointHitBuffer);
                    if (hitCount == 0) continue;
                }

                Vector2 accumulatedPos = node.Position;
                Vector2 lastNormal = Vector2.up;
                bool hadCollision = false;

                for (int h = 0; h < hitCount; h++)
                {
                    Collider2D hitCol = _pointHitBuffer[h];
                    if (TryResolveColliderContact(hitCol, accumulatedPos, _surfaceOffset, out Vector2 resolvedPos, out Vector2 norm))
                    {
                        accumulatedPos = resolvedPos;
                        lastNormal = norm;
                        hadCollision = true;
                    }
                }

                if (hadCollision)
                {
                    node.Position = accumulatedPos;
                    Vector2 vel = node.Position - node.OldPosition;
                    float normalVel = Vector2.Dot(vel, lastNormal);
                    if (normalVel < 0f)
                    {
                        vel -= lastNormal * normalVel;
                    }
                    Vector2 tangentVel = vel * frictionFactor;

                    if (tangentVel.sqrMagnitude < TangentSleepSqrThreshold)
                    {
                        tangentVel = Vector2.zero;
                    }

                    node.OldPosition = node.Position - tangentVel;
                    _nodes[i] = node;
                }
            }


        }

        private void RebuildSplineData()
        {
            _renderBuffer.Clear();
            _totalPathLength = 0f;

            int nodeCount = _nodes.Count;
            if (nodeCount < 2) return;

            int subdivisions = Mathf.Max(2, _splineSubdivisions);
            float invSubdivisions = 1f / subdivisions;
            float runningDist = 0f;
            Vector3 prevPt = default;
            bool hasPrev = false;

            for (int i = 0; i < nodeCount - 1; i++)
            {
                Vector2 p0 = (i == 0) ? (2f * _nodes[0].Position - _nodes[1].Position) : _nodes[i - 1].Position;
                Vector2 p1 = _nodes[i].Position;
                Vector2 p2 = _nodes[i + 1].Position;
                Vector2 p3 = (i + 2 >= nodeCount) ? (2f * _nodes[nodeCount - 1].Position - _nodes[nodeCount - 2].Position) : _nodes[i + 2].Position;

                int steps = (i == nodeCount - 2) ? subdivisions + 1 : subdivisions;

                for (int step = 0; step < steps; step++)
                {
                    float t = step * invSubdivisions;
                    Vector2 pt2D = EvaluateCatmullRom(p0, p1, p2, p3, t);



                    Vector3 pt = new Vector3(pt2D.x, pt2D.y, 0f);

                    if (hasPrev)
                    {
                        runningDist += Vector3.Distance(prevPt, pt);
                    }
                    prevPt = pt;
                    hasPrev = true;

                    _renderBuffer.Add(pt);
                }
            }

            _totalPathLength = runningDist;
        }

        private void ApplySplineToLineRenderer()
        {
            if (_lineRenderer == null) EnsureComponents();

            int count = _renderBuffer.Count;
            if (count < 2)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            if (_linePositionsArray.Length < count)
            {
                _linePositionsArray = new Vector3[Mathf.NextPowerOfTwo(Mathf.Max(InitialSplineCapacity, count))];
            }

            Color currentColor = _threadColor;
            if (_currentStrain > _tensionColorThreshold)
            {
                float t = Mathf.Clamp01((_currentStrain - _tensionColorThreshold) / (1f - _tensionColorThreshold));
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                currentColor = Color.Lerp(_threadColor, _strainedThreadColor, smoothT);
            }

            ApplyThreadColor(currentColor);
            UpdateThreadWidth(_currentStrain);

            bool applyQuiver = _currentStrain >= _quiverStrainThreshold;
            float vibeAmp = 0f;
            float timePhase = 0f;

            if (applyQuiver)
            {
                float vibeT = Mathf.Clamp01((_currentStrain - _quiverStrainThreshold) / (1f - _quiverStrainThreshold));
                vibeAmp = _maxVibrationAmplitude * vibeT;
                timePhase = Time.time * 55f;
            }

            float ppu = Mathf.Max(1f, _pixelSnapPPU);
            float pixelSize = 1f / ppu;
            float invCountMinusOne = 1f / (count - 1);
            int validCount = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = _renderBuffer[i];

                // สั่นตามแนวตั้งฉากของเส้นโค้งจริง (Local Tangent Normal) พร้อม Standing-Wave Envelope
                if (applyQuiver && i > 0 && i < count - 1)
                {
                    Vector2 prevPt = _renderBuffer[i - 1];
                    Vector2 nextPt = _renderBuffer[i + 1];
                    Vector2 tangent = nextPt - prevPt;
                    Vector2 localNormal = tangent.sqrMagnitude > MinSqrDistanceEpsilon
                        ? new Vector2(-tangent.y, tangent.x).normalized
                        : Vector2.up;

                    float normalizedProgress = i * invCountMinusOne;
                    float standingEnvelope = Mathf.Sin(normalizedProgress * Mathf.PI);

                    // ผสมคลื่นสั่นความถี่หลัก + ความถี่ซ้อนเพื่อให้ดูเหมือนเส้นด้ายตึงใกล้ขาด
                    float wave = Mathf.Sin(timePhase + i * 1.8f) + (0.4f * Mathf.Cos(timePhase * 1.7f - i * 3.1f));
                    float offset = wave * vibeAmp * standingEnvelope * 0.7f;

                    pos.x += localNormal.x * offset;
                    pos.y += localNormal.y * offset;
                }

                if (_enablePixelSnapping)
                {
                    pos.x = Mathf.Round(pos.x * ppu) * pixelSize;
                    pos.y = Mathf.Round(pos.y * ppu) * pixelSize;

                    if (validCount > 0 && i < count - 1)
                    {
                        Vector3 prev = _linePositionsArray[validCount - 1];
                        if ((pos - prev).sqrMagnitude < 1e-6f)
                        {
                            continue;
                        }
                    }
                }

                _linePositionsArray[validCount++] = pos;
            }

            if (validCount < 2)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            _lineRenderer.positionCount = validCount;
            _lineRenderer.SetPositions(_linePositionsArray);
        }

        private static Vector2 EvaluateCatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;

            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        private void UpdateShaderTiling()
        {
            if (!_enableAnimation || _threadMaterial == null || _renderBuffer.Count < 2) return;

            _threadMaterial.SetFloat(SutureVisualUtils.TilingId, Mathf.Max(1f, _totalPathLength * _tilingFrequency));
            _threadMaterial.SetFloat(SutureVisualUtils.ScrollSpeedId, _scrollSpeed);
        }

        private Vector2 SnapToPixelGrid(Vector2 pos)
        {
            if (!_enablePixelSnapping) return pos;
            float ppu = Mathf.Max(1f, _pixelSnapPPU);
            float pixelSize = 1f / ppu;
            return new Vector2(
                Mathf.Round(pos.x * ppu) * pixelSize,
                Mathf.Round(pos.y * ppu) * pixelSize);
        }

        private void AnimatePinPop(GameObject pin)
        {
            if (pin == null) return;
            float targetScale = _pinScale > 0f ? _pinScale : 0.6f;
            Transform pinTransform = pin.transform;
            pinTransform.DOKill();
            pinTransform.localScale = Vector3.one * (targetScale * 1.6f);
            pinTransform.DOScale(Vector3.one * targetScale, 0.18f).SetEase(Ease.OutBack);
        }

        private void UpdatePinMarkersInFlight(Vector2 origin)
        {
            if (_hideAllPins)
            {
                HidePinMarkers();
                return;
            }

            if (_pinMarkers.Count == 0)
            {
                _pinMarkers.Add(CreatePinMarker());
            }

            GameObject firstPin = _pinMarkers[0];
            if (!firstPin.activeSelf)
            {
                firstPin.SetActive(true);
                AnimatePinPop(firstPin);
            }
            firstPin.transform.position = SnapToPixelGrid(origin);

            for (int i = 1; i < _pinMarkers.Count; i++)
            {
                GameObject pin = _pinMarkers[i];
                if (pin != null && pin.activeSelf)
                {
                    pin.transform.DOKill();
                    pin.SetActive(false);
                }
            }
        }

        private void UpdatePinMarkers(IReadOnlyList<Vector2> waypoints)
        {
            if (_hideAllPins)
            {
                HidePinMarkers();
                return;
            }

            while (_pinMarkers.Count < waypoints.Count)
            {
                _pinMarkers.Add(CreatePinMarker());
            }

            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                GameObject pin = _pinMarkers[i];
                if (pin == null) continue;

                if (i < waypoints.Count && !(i == 1 && _hideDestinationPinDot))
                {
                    if (!pin.activeSelf)
                    {
                        pin.SetActive(true);
                        AnimatePinPop(pin);
                    }
                    pin.transform.position = SnapToPixelGrid(waypoints[i]);
                }
                else if (pin.activeSelf)
                {
                    pin.transform.DOKill();
                    pin.SetActive(false);
                }
            }
        }

        private static Transform GetOrCreatePinContainer()
        {
            if (s_PinContainer == null)
            {
                GameObject container = GameObject.Find("[NeedlePins_Runtime]");
                if (container == null)
                {
                    container = new GameObject("[NeedlePins_Runtime]")
                    {
                        hideFlags = HideFlags.DontSave
                    };
                }
                s_PinContainer = container.transform;
            }
            return s_PinContainer;
        }

        private GameObject CreatePinMarker()
        {
            GameObject pin = new GameObject("NeedlePin_2D")
            {
                hideFlags = HideFlags.DontSave
            };

            Transform container = GetOrCreatePinContainer();
            if (container != null)
            {
                pin.transform.SetParent(container, false);
            }

            float targetScale = _pinScale > 0f ? _pinScale : 0.6f;
            pin.transform.localScale = Vector3.one * targetScale;

            SpriteRenderer sr = pin.AddComponent<SpriteRenderer>();
            sr.sprite = _pinSprite != null ? _pinSprite : GetOrCreateStitchKnotSprite();
            sr.color = _threadColor;
            if (_lineRenderer != null)
            {
                sr.sortingLayerID = _lineRenderer.sortingLayerID;
                sr.sortingOrder = Mathf.Max(12, _lineRenderer.sortingOrder + 2);
            }
            else
            {
                sr.sortingOrder = 12;
            }

            return pin;
        }

        public void SetSortingLayer(string layerName, int order)
        {
            EnsureComponents();
            if (_lineRenderer != null)
            {
                _lineRenderer.sortingLayerName = layerName;
                _lineRenderer.sortingOrder = order;
            }

            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                if (_pinMarkers[i] != null && _pinMarkers[i].TryGetComponent(out SpriteRenderer sr))
                {
                    sr.sortingLayerName = layerName;
                    sr.sortingOrder = order + 2;
                }
            }
        }

        /// <summary>
        /// สร้าง Texture ลายเกลียวเชือก/เส้นด้ายแบบ Pixel Art (12x4 พิกเซล) เพื่อให้เส้นด้ายมีร่องเกลียวและไฮไลต์
        /// </summary>
        private static Texture2D GetOrCreateBraidedThreadTexture()
        {
            if (s_BraidedThreadTexture != null) return s_BraidedThreadTexture;

            const int width = 12;
            const int height = 4;
            s_BraidedThreadTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "ProceduralBraidedThread_PixelTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };

            Color highlight = new Color(1f, 1f, 1f, 1f);
            Color midTone = new Color(0.88f, 0.88f, 0.88f, 1f);
            Color groove = new Color(0.58f, 0.52f, 0.55f, 1f);

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // สร้างลวดลายบิดเกลียวเฉียง (Diagonal ply twist)
                    int phase = (x + y * 2) % 6;
                    Color c;
                    if (phase == 0)
                        c = groove; // ร่องเกลียวด้าย
                    else if (phase == 2 || phase == 3)
                        c = highlight; // สันเกลียวสะท้อนแสง
                    else
                        c = midTone; // เนื้อด้ายปกติ

                    s_BraidedThreadTexture.SetPixel(x, y, c);
                }
            }

            s_BraidedThreadTexture.Apply(false, true);
            return s_BraidedThreadTexture;
        }

        /// <summary>
        /// สร้าง Sprite ปมด้ายเย็บแผล (Stitch Knot 9x9 พิกเซล) แทนจุดสี่เหลี่ยมขาวธรรมดา
        /// </summary>
        private static Sprite GetOrCreateStitchKnotSprite()
        {
            if (s_StitchKnotSprite != null) return s_StitchKnotSprite;

            const int size = 9;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ProceduralStitchKnot_PixelTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color outline = new Color(0.12f, 0.08f, 0.12f, 1f);
            Color shade = new Color(0.72f, 0.72f, 0.72f, 1f);
            Color core = new Color(0.95f, 0.95f, 0.95f, 1f);
            Color spec = new Color(1f, 1f, 1f, 1f);

            // แผนผังปมด้ายเย็บแผลแบบ Pixel Art (0=ใส, 1=ขอบดำ, 2=เงาปม, 3=เนื้อด้าย, 4=ไฮไลต์)
            int[,] pattern =
            {
                { 0, 1, 1, 0, 0, 0, 1, 1, 0 },
                { 1, 3, 2, 1, 1, 1, 2, 3, 1 },
                { 1, 2, 3, 2, 3, 2, 3, 2, 1 },
                { 0, 1, 2, 3, 4, 3, 2, 1, 0 },
                { 0, 1, 3, 4, 4, 3, 3, 1, 0 },
                { 0, 1, 2, 3, 3, 2, 2, 1, 0 },
                { 1, 2, 3, 2, 3, 2, 3, 2, 1 },
                { 1, 3, 2, 1, 1, 1, 2, 3, 1 },
                { 0, 1, 1, 0, 0, 0, 1, 1, 0 }
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color pixel = pattern[y, x] switch
                    {
                        1 => outline,
                        2 => shade,
                        3 => core,
                        4 => spec,
                        _ => clear
                    };
                    tex.SetPixel(x, y, pixel);
                }
            }

            tex.Apply(false, true);
            s_StitchKnotSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16f);
            return s_StitchKnotSprite;
        }

        private void HidePinMarkers()
        {
            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                GameObject pin = _pinMarkers[i];
                if (pin != null && pin.activeSelf)
                {
                    pin.transform.DOKill();
                    pin.SetActive(false);
                }
            }
        }

        private void DestroyPinMarkers()
        {
            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                GameObject pin = _pinMarkers[i];
                if (pin != null)
                {
                    pin.transform.DOKill();
                    if (Application.isPlaying)
                    {
                        Destroy(pin);
                    }
                    else
                    {
                        DestroyImmediate(pin);
                    }
                }
            }
            _pinMarkers.Clear();
        }

        private static float SqrDistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < MinSqrDistanceEpsilon) return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 projection = a + t * ab;
            return (p - projection).sqrMagnitude;
        }
    }
}