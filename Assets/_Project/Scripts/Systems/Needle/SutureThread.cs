using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
        [SerializeField] private float _gravity = 7.5f;

        [Tooltip("Air drag damping factor (0 = frictionless, 10 = heavy syrup).")]
        [SerializeField] private float _airDamping = 3.2f;

        [Tooltip("Slack factor: >1.0 allows natural catenary sag; =1.0 is tight.")]
        [FormerlySerializedAs("_sagRatio")]
        [SerializeField] private float _slackFactor = 1.025f;

        [Tooltip("Transverse impulse (guitar-string pluck) applied on creation or impact.")]
        [FormerlySerializedAs("_twangImpulse")]
        [SerializeField] private float _twangImpulse = 4.2f;

        [Header("Catmull-Rom Spline Rendering")]
        [Tooltip("Subdivisions between each Verlet particle node for silk smoothness.")]
        [SerializeField] private int _splineSubdivisions = 4;

        [Header("Animated Flow (ForeignGods style)")]
        [SerializeField] private Material _materialOverride;
        [SerializeField] private bool _enableAnimation = true;
        [SerializeField] private float _scrollSpeed = 2.5f;
        [SerializeField] private float _tilingFrequency = 2.5f;

        private class VerletNode
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

        private static Sprite s_PinSprite;
        private Material _threadMaterial;

        private readonly List<GameObject> _pinMarkers = new List<GameObject>();
        private readonly List<Vector2> _cachedWaypoints = new List<Vector2>();
        private readonly List<int> _pinnedWaypointIndices = new List<int>();

        private readonly List<VerletNode> _nodes = new List<VerletNode>(48);
        private readonly List<VerletLink> _links = new List<VerletLink>(48);
        private readonly List<Vector3> _renderBuffer = new List<Vector3>(128);
        private readonly List<float> _cumulativeLengths = new List<float>(128);
        private float _totalPathLength;

        public int NodeCount => _nodes.Count;
        public float TotalPathLength => _totalPathLength;

        private void Awake()
        {
            if (_lineRenderer == null)
            {
                _lineRenderer = GetComponent<LineRenderer>();
            }

            ConfigureLineRenderer();
        }

        private void ConfigureLineRenderer()
        {
            _lineRenderer.useWorldSpace = true;
            _lineRenderer.positionCount = 0;
            _lineRenderer.sortingOrder = 10;
            _lineRenderer.textureMode = LineTextureMode.Tile;

            // Organic Webbed Width Profile: Anchor-flare at endpoints, slender in the middle
            AnimationCurve widthProfile = new AnimationCurve();
            widthProfile.AddKey(0.00f, 1.45f); // Start pin flare
            widthProfile.AddKey(0.05f, 1.05f);
            widthProfile.AddKey(0.50f, 0.75f); // Sleek middle section
            widthProfile.AddKey(0.95f, 1.05f);
            widthProfile.AddKey(1.00f, 1.45f); // End pin flare
            _lineRenderer.widthCurve = widthProfile;
            _lineRenderer.widthMultiplier = _threadWidth;

            _lineRenderer.startColor = _threadColor;
            _lineRenderer.endColor = _threadColor;

            // Material initialization
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
            if (_nodes.Count < 2) return;

            float dt = Time.fixedDeltaTime;
            SimulateVerlet(dt);
            SolveConstraints();
            DrawCatmullRomSpline();
        }

        private void Update()
        {
            if (_nodes.Count < 2) return;

            DrawCatmullRomSpline();
            UpdateShaderTiling();
        }

        public void RenderPath(IReadOnlyList<Vector2> waypoints)
        {
            if (waypoints == null || waypoints.Count < 2)
            {
                Clear();
                return;
            }

            _cachedWaypoints.Clear();
            _cachedWaypoints.AddRange(waypoints);

            BuildVerletRope();
            UpdatePinMarkers(_cachedWaypoints);
            DrawCatmullRomSpline();
            UpdateShaderTiling();
        }

        /// <summary>
        /// Continuously updates start and end positions for a dynamic 2-point thread (e.g., tethered to the player hauling a burden).
        /// Dynamically adjusts link rest lengths so slack and tension behave realistically as distance changes.
        /// </summary>
        public void UpdateDynamicEndpoints(Vector2 startPos, Vector2 endPos, float restLengthBudget = -1f)
        {
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

            if (_nodes.Count >= 2 && _pinnedWaypointIndices.Count >= 2)
            {
                int firstIdx = _pinnedWaypointIndices[0];
                int lastIdx = _pinnedWaypointIndices[_pinnedWaypointIndices.Count - 1];

                _nodes[firstIdx].Position = startPos;
                _nodes[firstIdx].OldPosition = startPos;

                _nodes[lastIdx].Position = endPos;
                _nodes[lastIdx].OldPosition = endPos;
            }

            if (restLengthBudget > 0f && _links.Count > 0)
            {
                float perLinkRest = (restLengthBudget / _links.Count) * Mathf.Max(1.0f, _slackFactor);
                for (int i = 0; i < _links.Count; i++)
                {
                    VerletLink link = _links[i];
                    link.RestLength = perLinkRest;
                    _links[i] = link;
                }
            }

            UpdatePinMarkers(_cachedWaypoints);
        }

        private void BuildVerletRope()
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
                Vector2 segNormal = new Vector2(-segDir.y, segDir.x); // Transverse normal for guitar twang

                int nodesThisSeg = Mathf.Max(3, _nodesPerSegment);
                float linkRestLength = (segDistance / (nodesThisSeg - 1)) * Mathf.Max(1.0f, _slackFactor);

                int startNodeIdx = _nodes.Count;
                if (seg > 0)
                {
                    // Reuse previous end node as start node
                    startNodeIdx = _nodes.Count - 1;
                }
                else
                {
                    // First waypoint node
                    var startNode = new VerletNode
                    {
                        Position = pStart,
                        OldPosition = pStart,
                        IsPinned = true
                    };
                    _nodes.Add(startNode);
                    _pinnedWaypointIndices.Add(0);
                }

                // Add interior nodes
                for (int i = 1; i < nodesThisSeg - 1; i++)
                {
                    float t = (float)i / (nodesThisSeg - 1);
                    Vector2 basePos = Vector2.Lerp(pStart, pEnd, t);

                    // Transverse plucked wave
                    float envelope = Mathf.Sin(t * Mathf.PI);
                    Vector2 pluckOffset = (segNormal * (_twangImpulse * 0.12f * envelope)) + (Vector2.down * (0.05f * envelope));
                    Vector2 pos = basePos + pluckOffset;

                    // Initial velocity via OldPosition offset
                    Vector2 initialVel = segNormal * (_twangImpulse * envelope);
                    Vector2 oldPos = pos - (initialVel * dt);

                    var interiorNode = new VerletNode
                    {
                        Position = pos,
                        OldPosition = oldPos,
                        IsPinned = false
                    };
                    _nodes.Add(interiorNode);
                }

                // End waypoint node
                var endNode = new VerletNode
                {
                    Position = pEnd,
                    OldPosition = pEnd,
                    IsPinned = true
                };
                _nodes.Add(endNode);
                _pinnedWaypointIndices.Add(_nodes.Count - 1);

                // Build links for this segment
                int segStart = (seg == 0) ? startNodeIdx : startNodeIdx;
                int segEnd = _nodes.Count - 1;
                for (int n = segStart; n < segEnd; n++)
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
            Vector2 gravityStep = Vector2.down * (_gravity * dt * dt);
            float dragFactor = Mathf.Clamp01(1f - (_airDamping * dt));

            for (int i = 0; i < _nodes.Count; i++)
            {
                var node = _nodes[i];
                if (node.IsPinned) continue;

                Vector2 velocity = (node.Position - node.OldPosition) * dragFactor;
                node.OldPosition = node.Position;
                node.Position += velocity + gravityStep;
            }
        }

        private void SolveConstraints()
        {
            int linkCount = _links.Count;

            for (int iter = 0; iter < _constraintIterations; iter++)
            {
                // Distance constraints between adjacent particles
                for (int i = 0; i < linkCount; i++)
                {
                    var link = _links[i];
                    var nodeA = _nodes[link.NodeA];
                    var nodeB = _nodes[link.NodeB];

                    Vector2 delta = nodeB.Position - nodeA.Position;
                    float dist = delta.magnitude;
                    if (dist < 0.0001f) continue;

                    float error = (dist - link.RestLength) / dist;
                    Vector2 correction = delta * (error * 0.5f);

                    if (!nodeA.IsPinned && !nodeB.IsPinned)
                    {
                        nodeA.Position += correction;
                        nodeB.Position -= correction;
                    }
                    else if (!nodeA.IsPinned)
                    {
                        nodeA.Position += correction * 2f;
                    }
                    else if (!nodeB.IsPinned)
                    {
                        nodeB.Position -= correction * 2f;
                    }
                }

                // Strict waypoint pinning
                for (int w = 0; w < _pinnedWaypointIndices.Count; w++)
                {
                    int nodeIdx = _pinnedWaypointIndices[w];
                    Vector2 targetPos = _cachedWaypoints[w];
                    _nodes[nodeIdx].Position = targetPos;
                    _nodes[nodeIdx].OldPosition = targetPos;
                }
            }
        }

        private void DrawCatmullRomSpline()
        {
            if (_nodes.Count < 2)
            {
                _lineRenderer.positionCount = 0;
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
                    _renderBuffer.Add(new Vector3(pt.x, pt.y, 0f));
                }
            }

            _cumulativeLengths.Clear();
            _cumulativeLengths.Add(0f);
            float runningDist = 0f;
            for (int i = 0; i < _renderBuffer.Count - 1; i++)
            {
                runningDist += Vector3.Distance(_renderBuffer[i], _renderBuffer[i + 1]);
                _cumulativeLengths.Add(runningDist);
            }
            _totalPathLength = runningDist;

            _lineRenderer.positionCount = _renderBuffer.Count;
            for (int i = 0; i < _renderBuffer.Count; i++)
            {
                _lineRenderer.SetPosition(i, _renderBuffer[i]);
            }
        }

        /// <summary>
        /// Samples an exact world position and tangent along the physical sagging/curving thread at a specified distance.
        /// </summary>
        public Vector2 GetPointAtDistance(float distance, out Vector2 tangent)
        {
            if (_renderBuffer.Count == 0)
            {
                tangent = Vector2.right;
                return transform.position;
            }

            if (_renderBuffer.Count == 1 || _totalPathLength <= 0.0001f)
            {
                tangent = Vector2.right;
                return _renderBuffer[0];
            }

            if (distance <= 0f)
            {
                Vector2 p0 = _renderBuffer[0];
                Vector2 p1 = _renderBuffer[1];
                tangent = (p1 - p0).normalized;
                return p0;
            }

            if (distance >= _totalPathLength)
            {
                int last = _renderBuffer.Count - 1;
                Vector2 pLast = _renderBuffer[last];
                Vector2 pPrev = _renderBuffer[last - 1];
                tangent = (pLast - pPrev).normalized;
                return pLast;
            }

            int search = _cumulativeLengths.BinarySearch(distance);
            if (search >= 0)
            {
                int tIdx = Mathf.Min(search + 1, _renderBuffer.Count - 1);
                Vector2 pA = _renderBuffer[search];
                Vector2 pB = _renderBuffer[tIdx];
                tangent = (pB - pA).normalized;
                return pA;
            }

            int nextIdx = ~search;
            int prevIdx = nextIdx - 1;

            float segDistA = _cumulativeLengths[prevIdx];
            float segDistB = _cumulativeLengths[nextIdx];
            float segSpan = segDistB - segDistA;

            float t = segSpan > 0.00001f ? (distance - segDistA) / segSpan : 0f;

            Vector2 ptA = _renderBuffer[prevIdx];
            Vector2 ptB = _renderBuffer[nextIdx];
            Vector2 diff = ptB - ptA;
            tangent = diff.sqrMagnitude > 0.0001f ? diff.normalized : Vector2.right;

            return Vector2.Lerp(ptA, ptB, t);
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

        /// <summary>
        /// Pluck the suture thread at a given world position to trigger organic vibration (Webbed style).
        /// Applies smooth cosine falloff across the specified radius.
        /// </summary>
        public void Pluck(Vector2 worldPosition, Vector2 impulse, float radius = 1.4f)
        {
            if (_nodes.Count == 0) return;
            float dt = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].IsPinned) continue;
                float dist = Vector2.Distance(worldPosition, _nodes[i].Position);
                if (dist < radius)
                {
                    float t = dist / radius;
                    float falloff = 0.5f * (1f + Mathf.Cos(t * Mathf.PI));
                    _nodes[i].OldPosition -= impulse * (falloff * dt);
                }
            }
        }

        private void UpdateShaderTiling()
        {
            if (!_enableAnimation || _threadMaterial == null || _renderBuffer.Count < 2) return;

            float totalLength = 0f;
            for (int i = 0; i < _renderBuffer.Count - 1; i++)
            {
                totalLength += Vector3.Distance(_renderBuffer[i], _renderBuffer[i + 1]);
            }

            _threadMaterial.SetFloat(s_TilingId, Mathf.Max(1f, totalLength * _tilingFrequency));
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
                    _pinMarkers[i].transform.position = waypoints[i];
                    _pinMarkers[i].SetActive(true);
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
            pin.transform.localScale = Vector3.one * 0.38f; // Start with juicy pop scale

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
                transform.localScale = Vector3.MoveTowards(transform.localScale, Vector3.one * k_TargetScale, Time.deltaTime * 1.8f);
                if (Mathf.Abs(transform.localScale.x - k_TargetScale) < 0.005f)
                {
                    transform.localScale = Vector3.one * k_TargetScale;
                    Destroy(this);
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
            _cachedWaypoints.Clear();
            _pinnedWaypointIndices.Clear();
            _nodes.Clear();
            _links.Clear();
            _renderBuffer.Clear();
            _cumulativeLengths.Clear();
            _totalPathLength = 0f;

            if (_lineRenderer != null)
            {
                _lineRenderer.positionCount = 0;
            }

            ClearPinMarkers();
        }

        private void ClearPinMarkers()
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
            ClearPinMarkers();
        }

        /// <summary>
        /// Check if a world point (e.g. mouse pointer or interaction zone) is close to any point on this suture thread line, and severs it.
        /// </summary>
        public bool TrySeverAt(Vector2 point, float radius)
        {
            if (_renderBuffer.Count < 2) return false;

            float sqrRadius = radius * radius;

            for (int i = 0; i < _renderBuffer.Count; i++)
            {
                Vector2 threadPt = _renderBuffer[i];
                float sqrDist = (threadPt - point).sqrMagnitude;
                if (sqrDist <= sqrRadius)
                {
                    IStitchable owner = GetComponentInParent<IStitchable>();
                    if (owner != null)
                    {
                        owner.CutThread();
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

        /// <summary>
        /// Checks all active threads in the world and severs any thread passing near the given point.
        /// </summary>
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
            ClearPinMarkers();
            if (_threadMaterial != null)
            {
                Destroy(_threadMaterial);
            }
        }
    }
}
