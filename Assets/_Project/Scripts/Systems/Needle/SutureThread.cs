using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Renders dynamic, elastic spider-silk thread rails inspired by 'Webbed' and 'Animated-Line-Renderer'.
    /// Powered by multi-node Verlet particle physics (rope chain simulation),
    /// Catmull-Rom cubic spline interpolation, organic anchor-flare width curves,
    /// and harmonic wave vibration (transverse guitar-string twang).
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public sealed class SutureThread : MonoBehaviour
    {
        private static readonly int s_ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
        private static readonly int s_TilingId = Shader.PropertyToID("_Tiling");
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Appearance")]
        [FormerlySerializedAs("lineRenderer")]
        [SerializeField] private LineRenderer _lineRenderer;

        [FormerlySerializedAs("threadColor")]
        [SerializeField] private Color _threadColor = new Color(0.95f, 0.95f, 1f, 0.95f);

        [FormerlySerializedAs("threadWidth")]
        [SerializeField] private float _threadWidth = 0.045f;

        [Header("Webbed Physics (Verlet Rope)")]
        [Tooltip("Number of Verlet particles per segment (higher = smoother wave propagation).")]
        [SerializeField] private int _nodesPerSegment = 10;

        [Tooltip("Constraint solver relaxation iterations per physics step.")]
        [SerializeField] private int _constraintIterations = 6;

        [Tooltip("Gravity acceleration pulling down on the rope particles.")]
        [SerializeField] private float _gravity = 5.0f;

        [Tooltip("Air drag damping factor (0 = frictionless, 10 = heavy syrup).")]
        [SerializeField] private float _airDamping = 3.2f;

        [Tooltip("Slack factor: >1.0 allows natural catenary sag; =1.0 is tight.")]
        [FormerlySerializedAs("_sagRatio")]
        [SerializeField] private float _slackFactor = 1.025f;

        [Tooltip("Transverse impulse (guitar-string pluck) applied on creation or impact.")]
        [FormerlySerializedAs("_twangImpulse")]
        [SerializeField] private float _twangImpulse = 4.2f;

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
        [SerializeField] private int _splineSubdivisions = 4;

        [Header("Animated Flow (ForeignGods style)")]
        [SerializeField] private Material _materialOverride;
        [SerializeField] private bool _enableAnimation = true;
        [SerializeField] private float _scrollSpeed = 2.5f;
        [SerializeField] private float _tilingFrequency = 2.5f;

        [Header("Pin Visuals")]
        [Tooltip("If true, the destination waypoint pin (white dot) is hidden because the embedded NeedleProjectile sprite is shown there.")]
        [SerializeField] private bool _hideDestinationPinDot = true;

        // เปลี่ยนเป็น struct เพื่อป้องกัน Heap Allocation ทุกครั้งที่สร้างเส้นด้ายใหม่
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

        private static readonly Vector2[] s_CardinalDirections = { Vector2.up, Vector2.down, Vector2.right, Vector2.left };

        private static Sprite s_PinSprite;
        private Material _threadMaterial;
        private IStitchable _cachedOwner;
        private ContactFilter2D _collisionFilter;
        private readonly Collider2D[] _pointHitBuffer = new Collider2D[2];

        private readonly List<GameObject> _pinMarkers = new List<GameObject>();
        private readonly List<Vector2> _cachedWaypoints = new List<Vector2>();
        private readonly List<int> _pinnedWaypointIndices = new List<int>();

        private readonly List<VerletNode> _nodes = new List<VerletNode>(48);
        private readonly List<VerletLink> _links = new List<VerletLink>(48);
        private readonly List<Vector3> _renderBuffer = new List<Vector3>(128);
        private Vector3[] _linePositionsArray = new Vector3[128];
        private float _totalPathLength;
        private bool _isSplineDirty;
        private bool _isTautMode;
        private bool _isInFlightMode;

        public int NodeCount => _nodes.Count;
        public float TotalPathLength => _totalPathLength;
        public bool IsTaut => _isTautMode;
        public IReadOnlyList<Vector2> CachedWaypoints => _cachedWaypoints;

        private void Awake()
        {
            EnsureComponents();
            EnsureCollisionFilter();
            ConfigureLineRenderer();
        }

        private void EnsureComponents()
        {
            if (_lineRenderer == null)
            {
                _lineRenderer = GetComponent<LineRenderer>();
                if (_lineRenderer == null)
                {
                    _lineRenderer = gameObject.AddComponent<LineRenderer>();
                }
            }

            if (_cachedOwner == null)
            {
                _cachedOwner = GetComponentInParent<IStitchable>();
            }

            if (_linePositionsArray == null || _linePositionsArray.Length == 0)
            {
                _linePositionsArray = new Vector3[128];
            }
        }

        private void EnsureCollisionFilter()
        {
            if (_collisionFilter.useLayerMask) return;

            if (_collisionMask.value == 0)
            {
                int groundLayer = LayerMask.NameToLayer("Ground");
                _collisionMask = groundLayer != -1 ? (1 << groundLayer) : LayerMask.GetMask("Ground", "Default");
            }

            _collisionFilter = new ContactFilter2D();
            _collisionFilter.SetLayerMask(_collisionMask);
            _collisionFilter.useLayerMask = true;
            _collisionFilter.useTriggers = false;
        }

        private void ConfigureLineRenderer()
        {
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 0;
            _lineRenderer.sortingOrder = 10;
            _lineRenderer.textureMode = LineTextureMode.Tile;

            AnimationCurve widthProfile = new AnimationCurve();
            widthProfile.AddKey(0.00f, 1.45f);
            widthProfile.AddKey(0.05f, 1.05f);
            widthProfile.AddKey(0.50f, 0.75f);
            widthProfile.AddKey(0.95f, 1.05f);
            widthProfile.AddKey(1.00f, 1.45f);
            _lineRenderer.widthCurve = widthProfile;
            _lineRenderer.widthMultiplier = _threadWidth;

            _lineRenderer.startColor = _threadColor;
            _lineRenderer.endColor = _threadColor;

            if (_materialOverride != null)
            {
                _threadMaterial = new Material(_materialOverride);
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/2D/AnimatedSutureLine");
                if (shader == null) shader = Shader.Find("Sprites/Default");

                if (shader != null)
                {
                    _threadMaterial = new Material(shader);
                }
            }

            if (_threadMaterial != null)
            {
                _threadMaterial.SetColor(s_BaseColorId, _threadColor);
                _threadMaterial.SetFloat(s_ScrollSpeedId, _scrollSpeed);
                _threadMaterial.SetFloat(s_TilingId, _tilingFrequency);
                _lineRenderer.material = _threadMaterial;
            }
        }

        private void FixedUpdate()
        {
            if (_nodes.Count < 2 || _isInFlightMode) return;

            float dt = Time.fixedDeltaTime;
            SimulateVerlet(dt);
            SolveConstraints();
            RebuildSplineData();
            _isSplineDirty = true;
        }

        private void LateUpdate()
        {
            if (_nodes.Count < 2) return;

            if (_isSplineDirty)
            {
                ApplySplineToLineRenderer();
                UpdateShaderTiling();
                _isSplineDirty = false;
            }
        }

        public void RenderPath(IReadOnlyList<Vector2> waypoints)
        {
            _isInFlightMode = false;
            if (waypoints == null || waypoints.Count < 2)
            {
                Clear();
                return;
            }

            _cachedWaypoints.Clear();
            for (int i = 0; i < waypoints.Count; i++)
            {
                _cachedWaypoints.Add(waypoints[i]);
            }

            BuildVerletRope(false);
            UpdatePinMarkers(_cachedWaypoints);
            RebuildSplineData();
            ApplySplineToLineRenderer();
            UpdateShaderTiling();
            _isSplineDirty = false;
        }

        /// <summary>
        /// สั่งให้เส้นด้ายขึงตึงเปรี๊ยะทันที (Snap Taut) สำหรับโหมดรางลูป (Looping Rail)
        /// ดึงโหนดทั้งหมดให้ตรงเป๊ะเป็นแนวราง ไม่หย่อนลงดิน พร้อมเอฟเฟกต์ดีดสายสั่นสะเทือนแบบฮาร์มอนิก
        /// </summary>
        public void SnapTaut(float twangImpulse = 4.0f)
        {
            _isInFlightMode = false;
            _isTautMode = true;

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
                float linkRest = segDist / Mathf.Max(1, nodeSpan);

                for (int i = 0; i <= nodeSpan; i++)
                {
                    int nIdx = startNodeIdx + i;
                    float t = (float)i / nodeSpan;
                    Vector2 straightPos = Vector2.Lerp(pStart, pEnd, t);

                    // ดีดสายสั่นแบบสายกีตาร์ที่เพิ่งถูกขันตึง
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    Vector2 pluckVel = segNormal * (twangImpulse * envelope);
                    Vector2 pluckOffset = pluckVel * (0.04f * envelope);

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

            RebuildSplineData();
            ApplySplineToLineRenderer();
            _isSplineDirty = true;
        }

        /// <summary>
        /// Continuously updates trailing suture thread connected to an in-flight needle projectile.
        /// Stretches smoothly and straight between the origin (box) and the flying needle without snapping to nearby terrain.
        /// </summary>
        public void UpdateInFlightNeedle(Vector2 origin, Vector2 needlePos)
        {
            _isInFlightMode = true;
            _isTautMode = true;

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

            int nodesCount = Mathf.Max(4, _nodesPerSegment);
            if (_nodes.Count != nodesCount || _pinnedWaypointIndices.Count < 2)
            {
                _nodes.Clear();
                _links.Clear();
                _pinnedWaypointIndices.Clear();

                for (int i = 0; i < nodesCount; i++)
                {
                    float t = (float)i / (nodesCount - 1);
                    Vector2 pos = Vector2.Lerp(origin, needlePos, t);
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
                        RestLength = Vector2.Distance(origin, needlePos) / (nodesCount - 1)
                    });
                }
            }
            else
            {
                int firstIdx = _pinnedWaypointIndices[0];
                int lastIdx = _pinnedWaypointIndices[1];
                int span = lastIdx - firstIdx;
                float segDist = Vector2.Distance(origin, needlePos);
                float linkRest = segDist / Mathf.Max(1, span);

                for (int i = 0; i <= span; i++)
                {
                    float t = (float)i / span;
                    Vector2 pos = Vector2.Lerp(origin, needlePos, t);
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

            UpdatePinMarkersInFlight(origin);
            RebuildSplineData();
            ApplySplineToLineRenderer();
            UpdateShaderTiling();
            _isSplineDirty = false;
        }

        private void UpdatePinMarkersInFlight(Vector2 origin)
        {
            if (_pinMarkers.Count == 0)
            {
                GameObject pin = CreatePinMarker();
                _pinMarkers.Add(pin);
            }

            if (!_pinMarkers[0].activeSelf)
            {
                _pinMarkers[0].transform.localScale = Vector3.one * 0.38f;
                _pinMarkers[0].SetActive(true);
            }
            _pinMarkers[0].transform.position = origin;

            for (int i = 1; i < _pinMarkers.Count; i++)
            {
                if (_pinMarkers[i] != null)
                {
                    _pinMarkers[i].SetActive(false);
                }
            }
        }

        /// <summary>
        /// Continuously updates start and end positions for a dynamic 2-point thread (e.g., tethered to the player hauling a burden).
        /// </summary>
        public void UpdateDynamicEndpoints(Vector2 startPos, Vector2 endPos, float restLengthBudget = -1f, bool hideEndPin = true)
        {
            _isInFlightMode = false;
            _isTautMode = false;

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

            // หากยังไม่ได้สร้างโหนดเชือก ให้สร้างทันที
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

            if (hideEndPin && _pinMarkers.Count > 1 && _pinMarkers[1] != null)
            {
                _pinMarkers[1].SetActive(false);
            }

            float targetRestLength = restLengthBudget > 0f
                ? restLengthBudget
                : Vector2.Distance(startPos, endPos);

            if (_links.Count > 0)
            {
                float perLinkRest = (targetRestLength / _links.Count) * Mathf.Max(1.0f, _slackFactor);
                for (int i = 0; i < _links.Count; i++)
                {
                    VerletLink link = _links[i];
                    link.RestLength = perLinkRest;
                    _links[i] = link;
                }
            }

            RebuildSplineData();
            _isSplineDirty = true;
        }

        private void BuildVerletRope(bool applyTwang = false)
        {
            _nodes.Clear();
            _links.Clear();
            _pinnedWaypointIndices.Clear();

            int segmentCount = _cachedWaypoints.Count - 1;
            if (segmentCount <= 0) return;

            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            for (int seg = 0; seg < segmentCount; seg++)
            {
                Vector2 pStart = _cachedWaypoints[seg];
                Vector2 pEnd = _cachedWaypoints[seg + 1];
                float segDistance = Vector2.Distance(pStart, pEnd);

                Vector2 segDir = segDistance > 0.001f ? (pEnd - pStart) / segDistance : Vector2.right;
                Vector2 segNormal = new Vector2(-segDir.y, segDir.x);

                int nodesThisSeg = Mathf.Max(3, _nodesPerSegment);
                float linkRestLength = (segDistance / (nodesThisSeg - 1)) * Mathf.Max(1.0f, _slackFactor);

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
                    float t = (float)i / (nodesThisSeg - 1);
                    Vector2 basePos = Vector2.Lerp(pStart, pEnd, t);

                    float envelope = Mathf.Sin(t * Mathf.PI);
                    Vector2 pos = basePos;
                    Vector2 oldPos = basePos;

                    if (applyTwang)
                    {
                        Vector2 pluckOffset = (segNormal * (_twangImpulse * 0.12f * envelope)) + (Vector2.down * (0.05f * envelope));
                        Vector2 candidatePos = basePos + pluckOffset;

                        // ถ้าจุด candidatePos ชนเพดาน/กำแพง ไม่ต้องดีดสายเข้าสิ่งกีดขวาง
                        if (!_enableGroundCollision || Physics2D.OverlapPoint(candidatePos, _collisionFilter, _pointHitBuffer) == 0)
                        {
                            pos = candidatePos;
                            Vector2 initialVel = segNormal * (_twangImpulse * envelope);
                            oldPos = pos - (initialVel * dt);
                        }
                    }
                    else
                    {
                        // หย่อนลงเล็กน้อยตามธรรมชาติของสายเชือก (Natural catenary sag) ป้องกันการพุ่งเข้าหาเพดาน
                        pos += Vector2.down * (0.02f * envelope);
                        oldPos = pos;
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
        }

        private void SimulateVerlet(float dt)
        {
            Vector2 gravityStep = _isTautMode ? Vector2.zero : Vector2.down * (_gravity * dt * dt);
            float currentDamping = _isTautMode ? _airDamping * 1.6f : _airDamping;
            float dragFactor = Mathf.Clamp01(1f - (currentDamping * dt));

            for (int i = 0; i < _nodes.Count; i++)
            {
                VerletNode node = _nodes[i];
                if (node.IsPinned) continue;

                Vector2 velocity = (node.Position - node.OldPosition) * dragFactor;
                node.OldPosition = node.Position;
                node.Position += velocity + gravityStep;
                _nodes[i] = node;
            }
        }

        private void SolveConstraints()
        {
            int linkCount = _links.Count;

            for (int iter = 0; iter < _constraintIterations; iter++)
            {
                for (int i = 0; i < linkCount; i++)
                {
                    VerletLink link = _links[i];
                    VerletNode nodeA = _nodes[link.NodeA];
                    VerletNode nodeB = _nodes[link.NodeB];

                    Vector2 delta = nodeB.Position - nodeA.Position;
                    float dist = delta.magnitude;
                    if (dist < 0.0001f) continue;

                    float error = (dist - link.RestLength) / dist;
                    Vector2 correction = delta * (error * 0.5f);

                    if (!nodeA.IsPinned && !nodeB.IsPinned)
                    {
                        nodeA.Position += correction;
                        nodeB.Position -= correction;
                        _nodes[link.NodeA] = nodeA;
                        _nodes[link.NodeB] = nodeB;
                    }
                    else if (!nodeA.IsPinned)
                    {
                        nodeA.Position += correction * 2f;
                        _nodes[link.NodeA] = nodeA;
                    }
                    else if (!nodeB.IsPinned)
                    {
                        nodeB.Position -= correction * 2f;
                        _nodes[link.NodeB] = nodeB;
                    }
                }

                if (_enableGroundCollision && !_isTautMode)
                {
                    SolveCollisions();
                }

                for (int w = 0; w < _pinnedWaypointIndices.Count; w++)
                {
                    int nodeIdx = _pinnedWaypointIndices[w];
                    Vector2 targetPos = _cachedWaypoints[w];
                    VerletNode pinnedNode = _nodes[nodeIdx];
                    pinnedNode.Position = targetPos;
                    pinnedNode.OldPosition = targetPos;
                    _nodes[nodeIdx] = pinnedNode;
                }
            }
        }

        private void SolveCollisions()
        {
            EnsureCollisionFilter();

            int nodeCount = _nodes.Count;
            for (int i = 0; i < nodeCount; i++)
            {
                VerletNode node = _nodes[i];
                if (node.IsPinned) continue;

                int hitCount = Physics2D.OverlapPoint(node.Position, _collisionFilter, _pointHitBuffer);
                if (hitCount > 0)
                {
                    Collider2D hit = _pointHitBuffer[0];
                    if (hit == null) continue;

                    // กรอง collider ของวัตถุเจ้าของด้ายออก ไม่ให้ด้ายติดตัวเอง
                    if (_cachedOwner != null && _cachedOwner.ObjectTransform != null &&
                        hit.transform.IsChildOf(_cachedOwner.ObjectTransform))
                    {
                        continue;
                    }

                    // กรอง collider ของผู้เล่นออก เพื่อไม่ให้ด้ายติดขัดในตัวผู้เล่น
                    if (hit.CompareTag("Player") || hit.GetComponentInParent<PlayerController2D>() != null)
                    {
                        continue;
                    }

                    if (ResolvePenetration(hit, node.Position, node.OldPosition, out Vector2 surfacePoint, out Vector2 normal))
                    {
                        node.Position = surfacePoint + normal * _surfaceOffset;

                        // ลบความเร็วในแนวพุ่งเข้าหาพื้นผิว (Inward velocity) ป้องกันการเด้งทะลุ
                        Vector2 vel = node.Position - node.OldPosition;
                        float inwardVel = Vector2.Dot(vel, -normal);
                        if (inwardVel > 0f)
                        {
                            vel += normal * inwardVel;
                        }

                        // ใส่แรงเสียดทาน (Friction) เมื่อสัมผัสพื้นผิว เพื่อให้ด้ายกองนิ่ง ไม่ลื่นไถล
                        vel *= (1f - _groundFriction);
                        node.OldPosition = node.Position - vel;

                        _nodes[i] = node;
                    }
                }
            }
        }

        private bool ResolvePenetration(Collider2D hit, Vector2 pos, Vector2 oldPos, out Vector2 surfacePoint, out Vector2 normal)
        {
            surfacePoint = pos;
            normal = Vector2.zero;

            // 1. ถ้า oldPos อยู่นอก collider: ใช้ Linecast จาก oldPos มา pos จะได้จุดกระทบและ normal ที่แม่นยำที่สุด
            if (!hit.OverlapPoint(oldPos))
            {
                RaycastHit2D lineHit = Physics2D.Linecast(oldPos, pos, _collisionMask);
                if (lineHit.collider == hit)
                {
                    surfacePoint = lineHit.point;
                    normal = lineHit.normal;
                    return true;
                }

                Vector2 cp = hit.ClosestPoint(oldPos);
                Vector2 diff = oldPos - cp;
                if (diff.sqrMagnitude > 0.0001f)
                {
                    surfacePoint = cp;
                    normal = diff.normalized;
                    return true;
                }
            }

            // 2. ถ้า oldPos อยู่ใน collider ด้วย (เช่น โหนดถูกลากทะลุเข้ามา หรือเริ่มต้นอยู่ใน collider)
            // ให้ยิง ray ย้อนกลับเข้ามาจาก 4 ทิศทาง (บน, ล่าง, ซ้าย, ขวา) เพื่อหาขอบผิวที่ใกล้ที่สุด (ทางออกที่สั้นที่สุด)
            // ทำให้กรณีเป็นเพดาน จะได้ทางออกด้านล่าง (normal ชี้ลง) เสมอ ไม่ดีดขึ้นหลังคา
            float searchDist = Mathf.Max(3.0f, hit.bounds.extents.magnitude * 1.5f);
            float bestDist = float.MaxValue;
            Vector2 bestPoint = pos;
            Vector2 bestNormal = Vector2.zero;
            bool foundExit = false;

            for (int d = 0; d < s_CardinalDirections.Length; d++)
            {
                Vector2 rayDir = s_CardinalDirections[d];
                Vector2 origin = pos - rayDir * searchDist;

                if (!hit.OverlapPoint(origin))
                {
                    RaycastHit2D rayHit = Physics2D.Raycast(origin, rayDir, searchDist + 0.1f, _collisionMask);
                    if (rayHit.collider == hit)
                    {
                        float dist = Vector2.Distance(pos, rayHit.point);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            bestPoint = rayHit.point;
                            bestNormal = rayHit.normal;
                            foundExit = true;
                        }
                    }
                }
            }

            if (foundExit)
            {
                surfacePoint = bestPoint;
                normal = bestNormal;
                return true;
            }

            // 3. Fallback: ดึงออกจากทิศทางของ Waypoint เริ่มต้น (ตำแหน่งกล่อง/ผู้เล่น) ซึ่งอยู่นอก collider
            if (_cachedWaypoints.Count > 0)
            {
                for (int w = 0; w < _cachedWaypoints.Count; w++)
                {
                    Vector2 anchor = _cachedWaypoints[w];
                    if (!hit.OverlapPoint(anchor))
                    {
                        RaycastHit2D anchorRay = Physics2D.Linecast(anchor, pos, _collisionMask);
                        if (anchorRay.collider == hit)
                        {
                            surfacePoint = anchorRay.point;
                            normal = anchorRay.normal;
                            return true;
                        }
                    }
                }
            }

            // 4. Fallback สุดท้าย: ดันกลับไปหา oldPos หรือทิศทางลงล่าง (ถ้าเป็นเพดาน) ห้ามดันขึ้น Vector2.up เด็ดขาด
            Vector2 fallbackDiff = oldPos - pos;
            if (fallbackDiff.sqrMagnitude > 0.0001f)
            {
                normal = fallbackDiff.normalized;
            }
            else
            {
                normal = Vector2.down;
            }

            surfacePoint = pos;
            return true;
        }

        private void RebuildSplineData()
        {
            if (_nodes.Count < 2)
            {
                _renderBuffer.Clear();
                _totalPathLength = 0f;
                return;
            }

            _renderBuffer.Clear();
            int nodeCount = _nodes.Count;
            int subdivisions = Mathf.Max(2, _splineSubdivisions);

            for (int i = 0; i < nodeCount - 1; i++)
            {
                Vector2 p0 = (i == 0) ? (2f * _nodes[0].Position - _nodes[1].Position) : _nodes[i - 1].Position;
                Vector2 p1 = _nodes[i].Position;
                Vector2 p2 = _nodes[i + 1].Position;
                Vector2 p3 = (i + 2 >= nodeCount) ? (2f * _nodes[nodeCount - 1].Position - _nodes[nodeCount - 2].Position) : _nodes[i + 2].Position;

                int steps = (i == nodeCount - 2) ? subdivisions + 1 : subdivisions;

                for (int step = 0; step < steps; step++)
                {
                    float t = (float)step / subdivisions;
                    Vector2 pt = EvaluateCatmullRom(p0, p1, p2, p3, t);

                    if (!_isTautMode && _enableGroundCollision && Physics2D.OverlapPoint(pt, _collisionFilter, _pointHitBuffer) > 0)
                    {
                        Collider2D groundHit = _pointHitBuffer[0];
                        if (groundHit != null &&
                            (_cachedOwner == null || _cachedOwner.ObjectTransform == null || !groundHit.transform.IsChildOf(_cachedOwner.ObjectTransform)) &&
                            !groundHit.CompareTag("Player") && groundHit.GetComponentInParent<PlayerController2D>() == null)
                        {
                            Vector2 segMid = (p1 + p2) * 0.5f;
                            Vector2 surfacePoint = groundHit.ClosestPoint(segMid);
                            Vector2 outwardDir = segMid - surfacePoint;
                            if (outwardDir.sqrMagnitude > 0.0001f)
                            {
                                pt = surfacePoint + outwardDir.normalized * _surfaceOffset;
                            }
                            else
                            {
                                pt = segMid;
                            }
                        }
                    }

                    _renderBuffer.Add(new Vector3(pt.x, pt.y, 0f));
                }
            }

            float runningDist = 0f;
            for (int i = 0; i < _renderBuffer.Count - 1; i++)
            {
                runningDist += Vector3.Distance(_renderBuffer[i], _renderBuffer[i + 1]);
            }
            _totalPathLength = runningDist;
        }

        private void ApplySplineToLineRenderer()
        {
            EnsureComponents();
            int count = _renderBuffer.Count;
            if (count < 2)
            {
                _lineRenderer.positionCount = 0;
                return;
            }

            if (_linePositionsArray == null || _linePositionsArray.Length < count)
            {
                _linePositionsArray = new Vector3[Mathf.NextPowerOfTwo(Mathf.Max(128, count))];
            }

            for (int i = 0; i < count; i++)
            {
                _linePositionsArray[i] = _renderBuffer[i];
            }

            _lineRenderer.positionCount = count;
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

        public void Pluck(Vector2 worldPosition, Vector2 impulse, float radius = 1.4f)
        {
            if (_nodes.Count == 0) return;
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            for (int i = 0; i < _nodes.Count; i++)
            {
                VerletNode node = _nodes[i];
                if (node.IsPinned) continue;

                float dist = Vector2.Distance(worldPosition, node.Position);
                if (dist < radius)
                {
                    float t = dist / radius;
                    float falloff = 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
                    node.OldPosition -= impulse * (falloff * dt);
                    _nodes[i] = node;
                }
            }
        }

        private void UpdateShaderTiling()
        {
            if (!_enableAnimation || _threadMaterial == null || _renderBuffer.Count < 2) return;

            // ใช้ _totalPathLength ที่คำนวณไว้แล้วแทนการวนลูป Vector3.Distance ซ้ำ
            _threadMaterial.SetFloat(s_TilingId, Mathf.Max(1f, _totalPathLength * _tilingFrequency));
            _threadMaterial.SetFloat(s_ScrollSpeedId, _scrollSpeed);
        }

        private void UpdatePinMarkers(IReadOnlyList<Vector2> waypoints)
        {
            while (_pinMarkers.Count < waypoints.Count)
            {
                GameObject pin = CreatePinMarker();
                _pinMarkers.Add(pin);
            }

            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                if (i < waypoints.Count)
                {
                    if (i == 1 && _hideDestinationPinDot)
                    {
                        _pinMarkers[i].SetActive(false);
                        continue;
                    }

                    if (!_pinMarkers[i].activeSelf)
                    {
                        _pinMarkers[i].transform.localScale = Vector3.one * 0.38f;
                        _pinMarkers[i].SetActive(true);
                    }
                    _pinMarkers[i].transform.SetParent(null);
                    _pinMarkers[i].transform.position = waypoints[i];
                }
                else
                {
                    _pinMarkers[i].SetActive(false);
                }
            }
        }

        private GameObject CreatePinMarker()
        {
            GameObject pin = new GameObject("NeedlePin_2D");
            pin.transform.SetParent(null);
            pin.transform.localScale = Vector3.one * 0.38f;

            SpriteRenderer sr = pin.AddComponent<SpriteRenderer>();
            if (s_PinSprite == null)
            {
                s_PinSprite = CreateDotSprite();
            }

            sr.sprite = s_PinSprite;
            sr.color = _threadColor;
            sr.sortingOrder = 12;

            pin.AddComponent<PinMarkerPopAnimation>();
            return pin;
        }

        private sealed class PinMarkerPopAnimation : MonoBehaviour
        {
            private const float k_TargetScale = 0.22f;

            private void Update()
            {
                if (Mathf.Abs(transform.localScale.x - k_TargetScale) > 0.005f)
                {
                    transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.one * k_TargetScale, Time.deltaTime * 1.8f);
                }
                else
                {
                    transform.localScale = Vector3.one * k_TargetScale;
                }
            }
        }

        private static Sprite CreateDotSprite()
        {
            const int size = 16;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float radius = (size - 1) * 0.5f;
            Vector2 center = new Vector2(radius, radius);

            Color[] colors = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    colors[y * size + x] = dist <= radius ? Color.white : Color.clear;
                }
            }

            tex.SetPixels(colors);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 16);
        }

        public void Clear()
        {
            _isInFlightMode = false;
            _isTautMode = false;
            _cachedWaypoints.Clear();
            _pinnedWaypointIndices.Clear();
            _nodes.Clear();
            _links.Clear();
            _renderBuffer.Clear();
            _totalPathLength = 0f;
            _isSplineDirty = false;

            if (_lineRenderer != null)
            {
                _lineRenderer.positionCount = 0;
            }

            HidePinMarkers();
        }

        private void HidePinMarkers()
        {
            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                if (_pinMarkers[i] != null)
                {
                    _pinMarkers[i].SetActive(false);
                }
            }
        }

        private void DestroyPinMarkers()
        {
            for (int i = 0; i < _pinMarkers.Count; i++)
            {
                if (_pinMarkers[i] != null)
                {
                    Destroy(_pinMarkers[i]);
                }
            }
            _pinMarkers.Clear();
        }

        private void OnEnable()
        {
            if (!s_ActiveThreads.Contains(this))
            {
                s_ActiveThreads.Add(this);
            }
        }

        private void OnDisable()
        {
            s_ActiveThreads.Remove(this);
            HidePinMarkers();
        }

        public bool TrySeverAt(Vector2 point, float radius)
        {
            if (_renderBuffer.Count < 2) return false;

            float sqrRadius = radius * radius;

            for (int i = 0; i < _renderBuffer.Count - 1; i++)
            {
                Vector2 segA = _renderBuffer[i];
                Vector2 segB = _renderBuffer[i + 1];

                if (SqrDistanceToSegment(point, segA, segB) <= sqrRadius)
                {
                    if (_cachedOwner == null)
                    {
                        _cachedOwner = GetComponentInParent<IStitchable>();
                    }

                    if (_cachedOwner != null)
                    {
                        _cachedOwner.CutThread();
                    }
                    else
                    {
                        Clear();
                    }
                    return true;
                }
            }

            return false;
        }

        private static float SqrDistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lenSq = ab.sqrMagnitude;
            if (lenSq < 0.00001f) return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq);
            Vector2 projection = a + t * ab;
            return (p - projection).sqrMagnitude;
        }

        public static bool TrySeverAnyThreadNear(Vector2 point, float radius)
        {
            bool anySevered = false;
            for (int i = s_ActiveThreads.Count - 1; i >= 0; i--)
            {
                if (s_ActiveThreads[i] != null && s_ActiveThreads[i].TrySeverAt(point, radius))
                {
                    anySevered = true;
                }
            }
            return anySevered;
        }

        private void OnDestroy()
        {
            DestroyPinMarkers();
            if (_threadMaterial != null)
            {
                Destroy(_threadMaterial);
            }
        }
    }
}