using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using ThailandHorrorJam.Core;
using Debug = UnityEngine.Debug;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Controls the needle tool with strict level design constraints:
    /// - Max waypoints budget (default: 3 points)
    /// - Max thread length budget (default: 9.5 meters)
    /// - Surface validation (cannot pin in thin air; must anchor to physical walls/ground/objects)
    /// </summary>
    public sealed class NeedleController : MonoBehaviour
    {
        private const float k_MinWaypointDistance = 0.4f;
        private const float k_AutoPinDistance = 0.5f;
        private const float k_HitSearchRadius = 0.45f;
        private const float k_PlayerAimRadius = 1.25f;
        private const int k_PreviewCurveSteps = 20;

        private static readonly Color s_ValidColor = new Color(0.95f, 0.95f, 1f, 0.95f);
        private static readonly Color s_PlayerTetherColor = new Color(0.4f, 0.95f, 1f, 0.95f);
        private static readonly Color s_InvalidColor = new Color(1f, 0.35f, 0.35f, 0.9f);

        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
        private static readonly int s_TilingId = Shader.PropertyToID("_Tiling");

        [Header("Constraints & Budget")]
        [Tooltip("Maximum allowed total thread length across all segments.")]
        [SerializeField] private float _maxThreadLength = 9.5f;

        [Tooltip("Maximum distance the player can be from an object to start stitching (Waypoint 1). Set <= 0 for unlimited range.")]
        [SerializeField] private float _maxInteractionDistance = 3.5f;

        [Tooltip("Maximum number of waypoints per stitch (default: 2 = Start -> Destination).")]
        [FormerlySerializedAs("maxWaypoints")]
        [SerializeField] private int _maxWaypoints = 2;

        [Tooltip("Whether pins must snap to a physical surface (prevents stitching in mid-air).")]
        [SerializeField] private bool _requireSurfaceToPin = true;

        [Tooltip("When true, completing a new circuit severs the previous circuit (Faustian sacrifice).")]
        [SerializeField] private bool _singleCircuitLimit = true;

        [Header("Layer Masks")]
        [FormerlySerializedAs("stitchableLayer")]
        [SerializeField] private LayerMask _stitchableLayer;

        [Tooltip("Surfaces where needles can anchor (Walls, Ceilings, Ground, Platforms).")]
        [SerializeField] private LayerMask _anchorSurfaceLayer;

        [Header("Projectile Settings")]
        [Tooltip("Optional reference to NeedleProjectile component. Auto-created if null.")]
        [SerializeField] private NeedleProjectile _needleProjectile;

        [Tooltip("Linear flight speed of the needle projectile in meters/second.")]
        [SerializeField] private float _projectileSpeed = 16.0f;

        [Header("Aiming & Visuals")]
        [FormerlySerializedAs("previewLine")]
        [SerializeField] private LineRenderer _previewLine;

        [FormerlySerializedAs("mainCamera")]
        [SerializeField] private Camera _mainCamera;

        [FormerlySerializedAs("reticleTransform")]
        [SerializeField] private Transform _reticleTransform;

        private Transform _playerTransform;
        private Rigidbody2D _playerRb;
        private Collider2D _playerCol;
        private PlayerController2D _playerController;

        private IStitchable _activeTarget;
        private IStitchable _currentlyLoopingTarget;
        private IStitchable _tetheredStitchable;

        private readonly List<Vector2> _pinnedWaypoints = new List<Vector2>(8);
        private readonly Vector3[] _previewPointsBuffer = new Vector3[k_PreviewCurveSteps + 1];
        private readonly Collider2D[] _hitBuffer = new Collider2D[8];

        private ContactFilter2D _stitchableFilter;
        private ContactFilter2D _anchorFilter;

        private bool _isPinning;
        private bool _isNeedleInFlight;
        private Material _previewMaterial;
        private SpriteRenderer _reticleSprite;

        public bool IsPinning => _isPinning;
        public bool IsTetheredToPlayer => _tetheredStitchable != null;
        public float RemainingThreadLength => Mathf.Max(0f, _maxThreadLength - GetUsedThreadLength());

        private void Awake()
        {
            EnsurePlayerComponents();

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main;
                if (_mainCamera == null)
                {
                    _mainCamera = FindAnyObjectByType<Camera>();
                }
            }

            if (_reticleTransform != null)
            {
                _reticleSprite = _reticleTransform.GetComponent<SpriteRenderer>();
            }

            if (_stitchableLayer.value == 0)
            {
                int stitchLayer = LayerMask.NameToLayer("Stitchable");
                _stitchableLayer = stitchLayer != -1 ? (1 << stitchLayer) : ~0;
            }

            if (_anchorSurfaceLayer.value == 0)
            {
                int groundLayer = LayerMask.NameToLayer("Ground");
                int stitchLayer = LayerMask.NameToLayer("Stitchable");
                int defaultLayer = LayerMask.NameToLayer("Default");
                int mask = 0;
                if (groundLayer != -1) mask |= (1 << groundLayer);
                if (stitchLayer != -1) mask |= (1 << stitchLayer);
                if (defaultLayer != -1) mask |= (1 << defaultLayer);
                _anchorSurfaceLayer = mask != 0 ? mask : ~0;
            }

            _stitchableFilter = new ContactFilter2D();
            _stitchableFilter.SetLayerMask(_stitchableLayer);
            _stitchableFilter.useLayerMask = true;
            _stitchableFilter.useTriggers = true;

            _anchorFilter = new ContactFilter2D();
            _anchorFilter.SetLayerMask(_anchorSurfaceLayer);
            _anchorFilter.useLayerMask = true;
            _anchorFilter.useTriggers = false;

            if (_previewLine == null)
            {
                _previewLine = GetComponent<LineRenderer>();
                if (_previewLine == null)
                {
                    _previewLine = gameObject.AddComponent<LineRenderer>();
                }
            }

            ConfigurePreviewLine();
            EnsureNeedleProjectile();
        }

        private void EnsureNeedleProjectile()
        {
            if (_needleProjectile == null)
            {
                _needleProjectile = GetComponentInChildren<NeedleProjectile>(true);
                if (_needleProjectile == null)
                {
                    GameObject projObj = new GameObject("NeedleProjectile");
                    projObj.transform.SetParent(transform);
                    projObj.transform.localPosition = Vector3.zero;
                    _needleProjectile = projObj.AddComponent<NeedleProjectile>();
                }
            }
            _needleProjectile.gameObject.SetActive(false);
        }

        private void ConfigurePreviewLine()
        {
            _previewLine.useWorldSpace = true;

            AnimationCurve widthProfile = new AnimationCurve();
            widthProfile.AddKey(0.00f, 1.45f);
            widthProfile.AddKey(0.05f, 1.05f);
            widthProfile.AddKey(0.50f, 0.75f);
            widthProfile.AddKey(0.95f, 1.05f);
            widthProfile.AddKey(1.00f, 1.45f);
            _previewLine.widthCurve = widthProfile;
            _previewLine.widthMultiplier = 0.045f;

            _previewLine.startColor = s_ValidColor;
            _previewLine.endColor = s_ValidColor;
            _previewLine.positionCount = 0;
            _previewLine.sortingOrder = 15;
            _previewLine.textureMode = LineTextureMode.Tile;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/AnimatedSutureLine");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _previewMaterial = new Material(shader);
                _previewMaterial.SetColor(s_BaseColorId, s_ValidColor);
                _previewMaterial.SetFloat(s_ScrollSpeedId, 3.2f);
                _previewLine.material = _previewMaterial;
            }
        }

        private void Update()
        {
            SyncFirstWaypointWithActiveTarget();

            Vector3 worldAimPos = GetAimWorldPosition();
            UpdateReticle(worldAimPos);
            HandleInput(worldAimPos);
            UpdatePreviewVisual(worldAimPos);
            UpdateInFlightThread();
        }

        private void UpdateInFlightThread()
        {
            if (_isPinning && _isNeedleInFlight && _activeTarget != null && _needleProjectile != null)
            {
                Vector2 needlePos = _needleProjectile.CurrentPosition;
                if (_pinnedWaypoints.Count >= 2)
                {
                    _pinnedWaypoints[1] = needlePos;
                }
                _activeTarget.UpdateInFlightNeedle(needlePos);
            }
        }

        private void SyncFirstWaypointWithActiveTarget()
        {
            if (_isPinning && _activeTarget != null && _pinnedWaypoints.Count > 0)
            {
                _pinnedWaypoints[0] = _activeTarget.CurrentPosition;
            }
        }

        public Vector3 GetAimWorldPosition()
        {
            if (_mainCamera == null || Mouse.current == null) return transform.position;

            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, -_mainCamera.transform.position.z));
            worldPos.z = 0f;
            return worldPos;
        }

        public bool IsPlayerNearObject(IStitchable stitchable)
        {
            if (_maxInteractionDistance <= 0f) return true;
            if (_playerTransform == null || stitchable == null || stitchable.ObjectTransform == null) return false;

            Vector2 playerPos = _playerTransform.position;
            Collider2D col = stitchable.ObjectTransform.GetComponent<Collider2D>();
            Vector2 targetPoint = col != null ? col.ClosestPoint(playerPos) : stitchable.CurrentPosition;

            return Vector2.Distance(playerPos, targetPoint) <= _maxInteractionDistance;
        }

        private void UpdateReticle(Vector3 worldAimPos)
        {
            if (_reticleTransform != null)
            {
                _reticleTransform.position = worldAimPos;
            }

            if (!_isPinning && _reticleSprite != null)
            {
                IStitchable hovered = FindStitchableAt(worldAimPos);
                if (hovered != null)
                {
                    _reticleSprite.color = IsPlayerNearObject(hovered) ? s_ValidColor : s_InvalidColor;
                }
                else
                {
                    _reticleSprite.color = s_ValidColor;
                }
            }
        }

        private void HandleInput(Vector3 worldAimPos)
        {
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    OnPrimaryClick(worldAimPos);
                }

                if (Mouse.current.rightButton.wasPressedThisFrame)
                {
                    OnSecondaryClick(worldAimPos);
                }
            }

            if (Keyboard.current != null)
            {
                if (Keyboard.current.eKey.wasPressedThisFrame ||
                    Keyboard.current.enterKey.wasPressedThisFrame)
                {
                    OnCompleteKeyPressed(worldAimPos);
                }

                if (Keyboard.current.escapeKey.wasPressedThisFrame ||
                    Keyboard.current.qKey.wasPressedThisFrame)
                {
                    CancelStitchingOrTether();
                }
            }
        }

        private void OnPrimaryClick(Vector3 worldAimPos)
        {
            if (!_isPinning)
            {
                if (_tetheredStitchable != null)
                {
                    if (IsAimingAtPlayer(worldAimPos) || FindStitchableAt(worldAimPos) == _tetheredStitchable)
                    {
                        ReleasePlayerTether();
                        return;
                    }

                    if (TryGetValidAnchorPoint(worldAimPos, _tetheredStitchable, out Vector2 anchorPos))
                    {
                        float tetherTransferDist = Vector2.Distance(_tetheredStitchable.CurrentPosition, anchorPos);
                        if (tetherTransferDist >= k_MinWaypointDistance && tetherTransferDist <= _maxThreadLength)
                        {
                            TransferTetherToAnchor(anchorPos);
                        }
                        else
                        {
                            LogWarning("[Needle] Cannot transfer tether: target wall is out of thread budget or too close.");
                        }
                        return;
                    }
                }

                IStitchable stitchable = FindStitchableAt(worldAimPos);
                if (stitchable != null)
                {
                    if (IsPlayerNearObject(stitchable))
                    {
                        StartStitching(stitchable);
                    }
                    else
                    {
                        float dist = Vector2.Distance(_playerTransform.position, stitchable.CurrentPosition);
                        LogWarning($"[Needle] Too far from {stitchable.ObjectTransform.name} to stitch! Distance: {dist:F1}m (Max: {_maxInteractionDistance:F1}m). Move closer.");
                        CameraController2D.Instance?.Shake(0.025f, 0.08f);
                    }
                }
            }
            else
            {
                if (_isNeedleInFlight) return;

                Vector2 lastPoint = _pinnedWaypoints[_pinnedWaypoints.Count - 1];

                // 1. Check if clicking on Player to tether object to self (must be within thread budget!)
                if (IsAimingAtPlayer(worldAimPos))
                {
                    float distToPlayer = Vector2.Distance(lastPoint, _playerTransform.position);
                    if (distToPlayer <= RemainingThreadLength)
                    {
                        StartPlayerTether(_activeTarget);
                    }
                    else
                    {
                        LogWarning("[Needle] Player is too far from the object to tether!");
                    }
                    return;
                }

                // 2. Launch Needle Projectile towards cursor
                LaunchNeedleProjectile(worldAimPos);
            }
        }

        private void LaunchNeedleProjectile(Vector3 worldAimPos)
        {
            EnsureNeedleProjectile();
            if (_needleProjectile == null || _activeTarget == null) return;

            Vector2 launchOrigin = _pinnedWaypoints[0];
            Vector2 aimVector = (Vector2)worldAimPos - launchOrigin;
            Vector2 aimDir = aimVector.sqrMagnitude > 0.001f ? aimVector.normalized : Vector2.right;

            _isNeedleInFlight = true;

            if (_pinnedWaypoints.Count < 2)
            {
                _pinnedWaypoints.Add(launchOrigin);
            }
            else
            {
                _pinnedWaypoints[1] = launchOrigin;
            }

            float maxFlightDist = _maxThreadLength + 1.5f;

            // Offset start slightly outside the box collider so swept cast doesn't start overlapping with it
            Vector2 startPos = launchOrigin;
            Collider2D targetCol = _activeTarget.ObjectTransform != null ? _activeTarget.ObjectTransform.GetComponent<Collider2D>() : null;
            if (targetCol != null)
            {
                startPos = (Vector2)targetCol.bounds.center + aimDir * (Mathf.Max(targetCol.bounds.extents.x, targetCol.bounds.extents.y) + 0.1f);
            }

            _needleProjectile.Launch(
                origin: startPos,
                direction: aimDir,
                speed: _projectileSpeed,
                maxDistance: maxFlightDist,
                returnTransform: _activeTarget.ObjectTransform != null ? _activeTarget.ObjectTransform : _playerTransform,
                targetToIgnore: _activeTarget.ObjectTransform,
                collisionFilter: _anchorFilter,
                onHit: OnNeedleHitSurface,
                onRewindComplete: OnNeedleRewindComplete,
                playerTransform: _playerTransform
            );

            _activeTarget.UpdateInFlightNeedle(startPos);

            CameraController2D.Instance?.Punch(aimDir, 0.03f);
            Log($"[Needle] Threw needle projectile from Waypoint 1 at {_projectileSpeed}m/s towards {aimDir}!");
        }

        private void OnNeedleHitSurface(Vector2 hitPoint, Vector2 hitNormal, Collider2D hitCol)
        {
            if (!_isPinning || _activeTarget == null)
            {
                if (_needleProjectile != null) _needleProjectile.Cancel();
                _isNeedleInFlight = false;
                return;
            }

            // วางหมุด Pin แนบติดกับพื้นผิวจุดกระทบพอดีเป๊ะ พร้อมยกขอบขึ้นตามแนว Normal เล็กน้อย (2 cm) เพื่อไม่ให้จมเนื้อ collider
            Vector2 normal = hitNormal.sqrMagnitude > 0.001f ? hitNormal.normalized : Vector2.up;
            Vector2 anchorPos = hitPoint + normal * 0.02f;

            // Check thread length budget from Box (Waypoint 0)
            Vector2 boxPos = _pinnedWaypoints[0];
            float segDist = Vector2.Distance(boxPos, hitPoint);

            if (segDist < k_MinWaypointDistance || segDist > _maxThreadLength)
            {
                LogWarning($"[Needle] Hit point is out of thread budget ({segDist:F1}m > {_maxThreadLength:F1}m). Rewinding!");
                _needleProjectile.StartRewind();
                return;
            }

            // Anchor successfully!
            if (_pinnedWaypoints.Count < 2)
            {
                _pinnedWaypoints.Add(anchorPos);
            }
            else
            {
                _pinnedWaypoints[1] = anchorPos;
            }

            _needleProjectile.Cancel();
            _isNeedleInFlight = false;

            CameraController2D.Instance?.Shake(0.04f, 0.1f);
            Log($"[Needle] Needle embedded into surface at {anchorPos}! Completing rail.");
            CompleteStitchingRail();
        }

        private void OnNeedleRewindComplete()
        {
            _isNeedleInFlight = false;
            if (_pinnedWaypoints.Count > 1)
            {
                _pinnedWaypoints.RemoveAt(1);
            }

            if (_activeTarget != null)
            {
                _activeTarget.UpdatePinnedWaypoints(_pinnedWaypoints);
            }

            Log("[Needle] Needle auto-rewound to Waypoint 1. Ready to re-aim.");
        }

        private void OnSecondaryClick(Vector3 worldAimPos)
        {
            if (_isPinning)
            {
                CancelStitching();
            }
            else if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
            }
            else
            {
                if (SutureThread.TrySeverAnyThreadNear(worldAimPos, 0.75f))
                {
                    CameraController2D.Instance?.Shake(0.04f, 0.08f);
                    Log("[Needle] Thread severed by pointer release.");
                }
            }
        }

        public void StartPlayerTether(IStitchable target)
        {
            if (target == null) return;
            EnsurePlayerComponents();

            if (_singleCircuitLimit && _currentlyLoopingTarget != null && _currentlyLoopingTarget != target)
            {
                Log($"[Needle] Severed previous circuit on {_currentlyLoopingTarget.ObjectTransform.name} to tether new entity.");
                _currentlyLoopingTarget.CutThread();
                _currentlyLoopingTarget = null;
            }

            if (_tetheredStitchable != null && _tetheredStitchable != target)
            {
                _tetheredStitchable.ReleasePlayerTether();
            }

            _tetheredStitchable = target;
            _tetheredStitchable.StartPlayerTether(_playerTransform, _playerRb);

            if (_playerController != null)
            {
                _playerController.SetHauling(true);
            }

            CameraController2D.Instance?.Punch((_playerTransform.position - (Vector3)target.CurrentPosition).normalized, 0.04f);
            Log($"[Needle] Tethered {target.ObjectTransform.name} to Player! Walk to haul it. Right-click to release, or click a wall to anchor.");

            ResetPinningState();
        }

        public void ReleasePlayerTether()
        {
            if (_tetheredStitchable != null)
            {
                Log($"[Needle] Released player tether on {_tetheredStitchable.ObjectTransform.name}.");
                _tetheredStitchable.ReleasePlayerTether();
                _tetheredStitchable = null;
            }

            if (_playerController != null)
            {
                _playerController.SetHauling(false);
            }
        }

        private void TransferTetherToAnchor(Vector2 anchorPos)
        {
            if (_tetheredStitchable == null) return;

            IStitchable target = _tetheredStitchable;
            ReleasePlayerTether();

            if (_singleCircuitLimit && _currentlyLoopingTarget != null && _currentlyLoopingTarget != target)
            {
                _currentlyLoopingTarget.CutThread();
            }

            _pinnedWaypoints.Clear();
            _pinnedWaypoints.Add(target.CurrentPosition);
            _pinnedWaypoints.Add(anchorPos);

            target.StartLoopAlongWaypoints(_pinnedWaypoints, LoopPlaybackMode.PingPong);
            _currentlyLoopingTarget = target;
            CameraController2D.Instance?.Shake(0.06f, 0.12f);
            Log($"[Needle] Transferred tether on {target.ObjectTransform.name} to Wall at {anchorPos}!");
        }

        public void CancelStitchingOrTether()
        {
            if (_isPinning)
            {
                CancelStitching();
            }
            else if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
            }
        }

        public void CancelStitching()
        {
            if (_isPinning)
            {
                Log("[Needle] Canceled current stitch.");
                if (_needleProjectile != null)
                {
                    _needleProjectile.Cancel();
                }
                _isNeedleInFlight = false;
                if (_activeTarget != null)
                {
                    if (_currentlyLoopingTarget == _activeTarget)
                    {
                        _currentlyLoopingTarget = null;
                    }
                    _activeTarget.CutThread();
                }
                ResetPinningState();
            }
        }

        private void OnCompleteKeyPressed(Vector3 worldAimPos)
        {
            if (!_isPinning)
            {
                Log("[Needle] Pressing E ignored: not currently stitching an object. First click on an object to tether.");
                return;
            }

            if (_pinnedWaypoints.Count > 0 && _pinnedWaypoints.Count < _maxWaypoints)
            {
                if (TryGetValidAnchorPoint(worldAimPos, _activeTarget, out Vector2 anchorPos) && !IsAimingAtPlayer(worldAimPos))
                {
                    Vector2 lastPoint = _pinnedWaypoints[_pinnedWaypoints.Count - 1];
                    float dist = Vector2.Distance(lastPoint, anchorPos);

                    if (dist > k_AutoPinDistance && dist <= RemainingThreadLength)
                    {
                        _pinnedWaypoints.Add(anchorPos);
                        Log($"[Needle] Auto-added destination waypoint at {anchorPos} from E key");
                        if (_activeTarget != null)
                        {
                            _activeTarget.UpdatePinnedWaypoints(_pinnedWaypoints);
                        }
                    }
                }
            }

            if (_pinnedWaypoints.Count >= 2)
            {
                CompleteStitchingRail();
            }
            else
            {
                LogWarning("[Needle] Need at least 2 points on valid surfaces to create a rail.");
            }
        }

        private bool TryGetValidAnchorPoint(Vector3 worldAimPos, IStitchable ignoredStitchable, out Vector2 anchorPoint)
        {
            anchorPoint = worldAimPos;

            if (IsAimingAtPlayer(worldAimPos))
            {
                if (_playerTransform != null) anchorPoint = _playerTransform.position;
                return true;
            }

            if (!_requireSurfaceToPin) return true;

            // 1. Check if clicking on another valid IStitchable object
            IStitchable stitchable = FindStitchableAt(worldAimPos);
            if (stitchable != null && stitchable != ignoredStitchable)
            {
                anchorPoint = stitchable.CurrentPosition;
                return true;
            }

            // 2. Zero-alloc check with physical surfaces (excluding player and ignoredStitchable)
            int hitCount = Physics2D.OverlapCircle(worldAimPos, k_HitSearchRadius, _anchorFilter, _hitBuffer);
            float closestDistSq = float.MaxValue;
            bool foundValid = false;

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hit = _hitBuffer[i];
                if (hit == null || hit == _playerCol) continue;

                if (ignoredStitchable != null && ignoredStitchable.ObjectTransform != null &&
                    hit.transform.IsChildOf(ignoredStitchable.ObjectTransform))
                {
                    continue;
                }

                Vector2 candidatePoint = hit.ClosestPoint(worldAimPos);
                float distSq = ((Vector2)worldAimPos - candidatePoint).sqrMagnitude;
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    anchorPoint = candidatePoint;
                    foundValid = true;
                }
            }

            // วางจุด Waypoint แนบติดกับพื้นผิวจุดกระทบ พร้อมยกขอบขึ้นตามแนว Normal เล็กน้อย (2 cm) เพื่อไม่ให้จมเนื้อ collider
            if (foundValid)
            {
                Vector2 normal = ((Vector2)worldAimPos - anchorPoint).normalized;
                if (normal.sqrMagnitude < 0.001f) normal = Vector2.up;
                anchorPoint += normal * 0.02f;
            }

            return foundValid;
        }

        private void EnsurePlayerComponents()
        {
            if (_playerTransform == null) _playerTransform = transform;
            if (_playerRb == null) _playerRb = GetComponent<Rigidbody2D>();
            if (_playerCol == null) _playerCol = GetComponent<Collider2D>();
            if (_playerController == null) _playerController = GetComponent<PlayerController2D>();
        }

        private bool IsAimingAtPlayer(Vector3 worldAimPos)
        {
            EnsurePlayerComponents();
            if (_playerTransform == null) return false;
            return Vector2.Distance(worldAimPos, _playerTransform.position) <= k_PlayerAimRadius;
        }

        private IStitchable FindStitchableAt(Vector3 worldAimPos)
        {
            // Zero-allocation search on Stitchable layer first
            int hitCount = Physics2D.OverlapCircle(worldAimPos, k_HitSearchRadius, _stitchableFilter, _hitBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                IStitchable stitchable = _hitBuffer[i].GetComponentInParent<IStitchable>();
                if (stitchable != null) return stitchable;
            }

            // Fallback: Zero-allocation search across all layers
            hitCount = Physics2D.OverlapCircle(worldAimPos, k_HitSearchRadius, ContactFilter2D.noFilter, _hitBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                IStitchable stitchable = _hitBuffer[i].GetComponentInParent<IStitchable>();
                if (stitchable != null) return stitchable;
            }

            return null;
        }

        private void StartStitching(IStitchable target)
        {
            _activeTarget = target;
            _activeTarget.AttachThread();

            _pinnedWaypoints.Clear();
            _pinnedWaypoints.Add(_activeTarget.CurrentPosition);
            _activeTarget.UpdatePinnedWaypoints(_pinnedWaypoints);
            _isPinning = true;

            Log($"[Needle] Tethered to {target.ObjectTransform.name}. Remaining thread: {RemainingThreadLength:F1}m. Click a wall/surface to pin (Max points: {_maxWaypoints}).");
        }

        private void CompleteStitchingRail()
        {
            if (_activeTarget != null && _pinnedWaypoints.Count >= 2)
            {
                if (_singleCircuitLimit && _currentlyLoopingTarget != null && _currentlyLoopingTarget != _activeTarget)
                {
                    Log($"[Needle] Severed previous circuit on {_currentlyLoopingTarget.ObjectTransform.name} to power new circuit.");
                    _currentlyLoopingTarget.CutThread();
                }

                _currentlyLoopingTarget = _activeTarget;
                Log($"[Needle] Successfully completed rail with {_pinnedWaypoints.Count} waypoints! Starting loop on {_activeTarget.ObjectTransform.name}.");
                _activeTarget.StartLoopAlongWaypoints(_pinnedWaypoints, LoopPlaybackMode.PingPong);
                CameraController2D.Instance?.Shake(0.06f, 0.12f);
            }

            ResetPinningState();
        }

        private void ResetPinningState()
        {
            _isPinning = false;
            _activeTarget = null;
            _pinnedWaypoints.Clear();
            if (_needleProjectile != null)
            {
                _needleProjectile.Cancel();
            }
            _isNeedleInFlight = false;
            if (_previewLine != null)
            {
                _previewLine.positionCount = 0;
            }
            if (_reticleSprite != null)
            {
                _reticleSprite.color = s_ValidColor;
            }
        }

        private float GetUsedThreadLength()
        {
            float length = 0f;
            for (int i = 0; i < _pinnedWaypoints.Count - 1; i++)
            {
                length += Vector2.Distance(_pinnedWaypoints[i], _pinnedWaypoints[i + 1]);
            }
            return length;
        }

        private void UpdatePreviewVisual(Vector3 worldAimPos)
        {
            if (!_isPinning || _pinnedWaypoints.Count == 0 || _previewLine == null)
            {
                if (_previewLine != null && _previewLine.positionCount != 0)
                {
                    _previewLine.positionCount = 0;
                }
                return;
            }

            // Hide aim guide line while needle is actively flying in the air
            if (_isNeedleInFlight)
            {
                _previewLine.positionCount = 0;
                return;
            }

            Vector2 launchOrigin = _pinnedWaypoints[0];
            Vector2 playerPos = _playerTransform.position;
            Vector2 aimVector = (Vector2)worldAimPos - launchOrigin;
            float aimDist = aimVector.magnitude;
            Vector2 aimDir = aimDist > 0.001f ? aimVector / aimDist : Vector2.right;

            // 1. Check if aiming at player (to tether)
            bool isAimingPlayer = IsAimingAtPlayer(worldAimPos);
            if (isAimingPlayer)
            {
                float distToPlayer = Vector2.Distance(launchOrigin, playerPos);
                bool canTether = distToPlayer <= RemainingThreadLength;
                Color col = canTether ? s_PlayerTetherColor : s_InvalidColor;

                DrawAimTrajectory(launchOrigin, playerPos, col);
                if (_reticleSprite != null) _reticleSprite.color = col;
                return;
            }

            // 2. Aim guide from Waypoint 1 (Box) to predicted impact point
            Vector2 rayOrigin = launchOrigin;
            Collider2D targetCol = _activeTarget?.ObjectTransform != null ? _activeTarget.ObjectTransform.GetComponent<Collider2D>() : null;
            if (targetCol != null)
            {
                rayOrigin = (Vector2)targetCol.bounds.center + aimDir * (Mathf.Max(targetCol.bounds.extents.x, targetCol.bounds.extents.y) + 0.05f);
            }

            RaycastHit2D hit = Physics2D.Raycast(rayOrigin, aimDir, _maxThreadLength, _anchorSurfaceLayer);
            Vector2 endPoint;
            bool canPlace = false;

            if (hit.collider != null && hit.collider != _playerCol && (targetCol == null || hit.collider != targetCol))
            {
                endPoint = hit.point;
                float totalDist = Vector2.Distance(launchOrigin, endPoint);
                canPlace = totalDist >= k_MinWaypointDistance && totalDist <= _maxThreadLength;
            }
            else
            {
                endPoint = launchOrigin + aimDir * Mathf.Min(aimDist, _maxThreadLength);
                canPlace = false;
            }

            Color activeColor = canPlace ? s_ValidColor : s_InvalidColor;
            if (_reticleSprite != null)
            {
                _reticleSprite.color = activeColor;
            }

            DrawAimTrajectory(launchOrigin, endPoint, activeColor);
        }

        private void DrawAimTrajectory(Vector2 start, Vector2 end, Color color)
        {
            if (_previewLine == null) return;

            _previewLine.positionCount = 2;
            _previewPointsBuffer[0] = new Vector3(start.x, start.y, 0f);
            _previewPointsBuffer[1] = new Vector3(end.x, end.y, 0f);
            _previewLine.SetPosition(0, _previewPointsBuffer[0]);
            _previewLine.SetPosition(1, _previewPointsBuffer[1]);

            if (_previewMaterial != null)
            {
                _previewMaterial.SetColor(s_BaseColorId, color);
                float lineLen = Vector2.Distance(start, end);
                _previewMaterial.SetFloat(s_TilingId, Mathf.Max(1f, lineLen * 2.5f));
            }
        }

        private void OnDestroy()
        {
            if (_previewMaterial != null)
            {
                Destroy(_previewMaterial);
            }
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG")]
        private static void Log(string message)
        {
            Debug.Log(message);
        }

        [Conditional("UNITY_EDITOR"), Conditional("DEBUG")]
        private static void LogWarning(string message)
        {
            Debug.LogWarning(message);
        }
    }
}