using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using DG.Tweening;
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
        private const float k_HitSearchRadius = 0.45f;
        private const float k_PlayerAimRadius = 1.25f;
        private const float k_SurfacePinOffset = 0.02f;
        private const float k_ColliderOriginPadding = 0.05f;

        private static readonly Color s_ValidColor = new Color(0.95f, 0.95f, 1f, 0.95f);
        private static readonly Color s_PlayerTetherColor = new Color(0.4f, 0.95f, 1f, 0.95f);
        private static readonly Color s_InvalidColor = new Color(1f, 0.35f, 0.35f, 0.9f);

        [Header("Constraints & Budget")]
        [Tooltip("Minimum distance required between waypoints to place a valid pin.")]
        [SerializeField] private float _minWaypointDistance = 0.5f;

        [Tooltip("Maximum allowed total thread length across all segments.")]
        [SerializeField] private float _maxThreadLength = 9.5f;

        [Tooltip("Maximum distance the player can be from an object to start stitching (Waypoint 1). Set <= 0 for unlimited range.")]
        [SerializeField] private float _maxInteractionDistance = 3.5f;

        [Tooltip("Maximum leash distance the player can walk away from the stitched object while aiming. Set <= 0 to use maxInteractionDistance.")]
        [SerializeField] private float _maxAimLeashDistance = 3.5f;

        [Tooltip("Maximum number of waypoints per stitch (default: 2 = Start -> Destination).")]
        [FormerlySerializedAs("maxWaypoints")]
        [SerializeField] private int _maxWaypoints = 2;

        [Tooltip("Whether pins must snap to a physical surface (prevents stitching in mid-air).")]
        [SerializeField] private bool _requireSurfaceToPin = true;

        [Tooltip("When true, needles can ONLY anchor onto objects with an active NeedleAnchorPoint component. Normal walls will deflect/rewind the needle.")]
        [SerializeField] private bool _requireAnchorComponent = true;

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

        [Tooltip("Material used for rendering the dashed suture line. If unassigned, defaults to Universal Render Pipeline/2D/AnimatedSutureLine.")]
        [SerializeField] private Material _lineMaterial;

        [FormerlySerializedAs("mainCamera")]
        [SerializeField] private Camera _mainCamera;

        [FormerlySerializedAs("reticleTransform")]
        [SerializeField] private Transform _reticleTransform;

        [Header("Slice to Cut (Cut the Rope Style)")]
        [Tooltip("Minimum distance cursor must move before evaluating slice segment intersection.")]
        [SerializeField] private float _minSliceDistance = 0.04f;
        [Tooltip("Lifetime of the slicing blade trail in seconds.")]
        [SerializeField] private float _sliceTrailDuration = 0.18f;
        [Tooltip("Touch/grazing radius around the slice segment to make cutting feel responsive and forgiving.")]
        [SerializeField] private float _sliceTouchRadius = 0.2f;
        [Tooltip("Starting color of the blade slash trail (cursor tip).")]
        [SerializeField] private Color _sliceTrailStartColor = new Color(1f, 1f, 1f, 0.95f);
        [Tooltip("Ending color of the blade slash trail.")]
        [SerializeField] private Color _sliceTrailEndColor = new Color(0.9f, 0.2f, 0.2f, 0f);

        private Transform _playerTransform;
        private Rigidbody2D _playerRb;
        private Collider2D _playerCol;
        private PlayerController2D _playerController;

        private IStitchable _activeTarget;
        private IStitchable _currentlyLoopingTarget;
        private IStitchable _tetheredStitchable;
        private NeedleAnchorPoint _currentPinnedAnchor;

        private readonly List<Vector2> _pinnedWaypoints = new List<Vector2>(8);
        private readonly Collider2D[] _hitBuffer = new Collider2D[8];
        private readonly RaycastHit2D[] _rayHitBuffer = new RaycastHit2D[8];

        private ContactFilter2D _stitchableFilter;
        private ContactFilter2D _anchorFilter;

        private bool _isPinning;
        private bool _isNeedleInFlight;
        private Material _previewMaterial;
        private SpriteRenderer _reticleSprite;
        private Vector3 _reticleBaseScale = Vector3.one;
        private float _deniedFlashTimer;

        private TrailRenderer _sliceTrailRenderer;
        private GameObject _sliceTrailObj;
        private Material _sliceTrailMaterial;
        private bool _isSlicing;
        private Vector2 _lastSliceWorldPos;

        public bool IsPinning => _isPinning;
        public bool IsTetheredToPlayer => _tetheredStitchable != null;
        public IStitchable TetheredStitchable => _tetheredStitchable;
        public float MaxAimLeashDistance => _maxAimLeashDistance > 0f ? _maxAimLeashDistance : (_maxInteractionDistance > 0f ? _maxInteractionDistance : 3.5f);
        public float RemainingThreadLength => Mathf.Max(0f, _maxThreadLength - GetUsedThreadLength());

        public event System.Action<Vector2> OnNeedleLaunched;
        public event System.Action OnActionDenied;
        public event System.Action OnThreadSevered;
        public event System.Action OnPlayerTetherAttached;

        private void Awake()
        {
            EnsurePlayerComponents();

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            }

            if (_reticleTransform != null)
            {
                _reticleSprite = _reticleTransform.GetComponent<SpriteRenderer>();
                _reticleBaseScale = _reticleTransform.localScale;
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
            _anchorFilter.useTriggers = true;

            if (_previewLine == null && !TryGetComponent(out _previewLine))
            {
                _previewLine = gameObject.AddComponent<LineRenderer>();
            }

            ConfigurePreviewLine();
            EnsureNeedleProjectile();
        }

        private void EnsureNeedleProjectile()
        {
            if (_needleProjectile == null || !_needleProjectile.gameObject.scene.IsValid())
            {
                NeedleProjectile sceneChild = GetComponentInChildren<NeedleProjectile>(true);
                if (sceneChild != null && sceneChild.gameObject.scene.IsValid())
                {
                    _needleProjectile = sceneChild;
                }
                else if (_needleProjectile != null)
                {
                    NeedleProjectile prefabAsset = _needleProjectile;
                    _needleProjectile = Instantiate(prefabAsset, transform);
                    _needleProjectile.name = "NeedleProjectile";
                }
                else
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
            _previewLine.widthCurve = SutureVisualUtils.CreateWidthProfile();
            _previewLine.widthMultiplier = 0.045f;

            _previewLine.startColor = s_ValidColor;
            _previewLine.endColor = s_ValidColor;
            _previewLine.positionCount = 0;
            _previewLine.sortingOrder = 15;
            _previewLine.textureMode = LineTextureMode.Tile;

            _previewMaterial = SutureVisualUtils.CreateSutureMaterial(_lineMaterial, s_ValidColor, 3.2f);
            if (_previewMaterial != null)
            {
                _previewLine.material = _previewMaterial;
            }
        }

        private void Update()
        {
            if (_deniedFlashTimer > 0f)
            {
                _deniedFlashTimer -= Time.deltaTime;
            }

            SyncFirstWaypointWithActiveTarget();

            Vector3 worldAimPos = GetAimWorldPosition();
            UpdateReticle(worldAimPos);
            HandleInput(worldAimPos);
            UpdatePreviewVisual(worldAimPos);
            UpdateInFlightThread();
            UpdateAimLeashThread();

            if (_currentPinnedAnchor != null || (_needleProjectile != null && _needleProjectile.IsEmbedded))
            {
                if (_currentlyLoopingTarget == null || _currentlyLoopingTarget.CurrentState != StitchState.Looping)
                {
                    if (_needleProjectile != null && _needleProjectile.IsEmbedded)
                    {
                        _needleProjectile.Cancel();
                    }
                    _currentlyLoopingTarget = null;
                    ClearCurrentPinnedAnchor();
                }
            }
        }

        private void ClearCurrentPinnedAnchor()
        {
            if (_currentPinnedAnchor != null)
            {
                _currentPinnedAnchor.NotifyUnpinned();
                _currentPinnedAnchor = null;
            }
        }

        private void UpdateAimLeashThread()
        {
            if (_isPinning && !_isNeedleInFlight && _activeTarget != null)
            {
                Vector2 objPos = _pinnedWaypoints.Count > 0 ? _pinnedWaypoints[0] : _activeTarget.CurrentPosition;
                EnsurePlayerComponents();
                if (_playerController != null)
                {
                    _playerController.UpdateAimLeash(objPos, MaxAimLeashDistance);
                }
            }
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

        private bool IsWithinThreadBudget(float distance)
        {
            return distance >= _minWaypointDistance && distance <= _maxThreadLength;
        }

        private static Vector2 GetCastOrigin(Vector2 fallbackOrigin, Transform targetTransform, Vector2 dir)
        {
            Collider2D col = targetTransform != null ? targetTransform.GetComponent<Collider2D>() : null;
            if (col == null) return fallbackOrigin;

            float maxExtent = Mathf.Max(col.bounds.extents.x, col.bounds.extents.y);
            return (Vector2)col.bounds.center + dir * (maxExtent + k_ColliderOriginPadding);
        }

        private static Vector2 ResolveAnchorPosition(Vector2 point, Vector2 normal, NeedleAnchorPoint anchor)
        {
            Vector2 safeNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector2.up;
            return anchor != null
                ? anchor.GetAnchorPosition(point, safeNormal)
                : point + safeNormal * k_SurfacePinOffset;
        }

        private bool CanInteractWithStitchable(IStitchable stitchable, string actionDesc)
        {
            if (stitchable == null || stitchable.ObjectTransform == null) return false;

            if (_playerController != null && _playerController.IsStandingOn(stitchable.ObjectTransform))
            {
                LogWarning($"[Needle] Cannot {actionDesc} {stitchable.ObjectTransform.name} while standing on it!");
                CameraController2D.Instance?.Shake(0.025f, 0.08f);
                NotifyActionDenied();
                return false;
            }

            if (!IsPlayerNearObject(stitchable))
            {
                float dist = Vector2.Distance(_playerTransform.position, stitchable.CurrentPosition);
                LogWarning($"[Needle] Too far from {stitchable.ObjectTransform.name} to {actionDesc}! Distance: {dist:F1}m (Max: {_maxInteractionDistance:F1}m). Move closer.");
                CameraController2D.Instance?.Shake(0.025f, 0.08f);
                NotifyActionDenied();
                return false;
            }

            return true;
        }

        private void SetReticleColor(Color color)
        {
            if (_reticleSprite == null) return;
            _reticleSprite.color = _deniedFlashTimer > 0f ? s_InvalidColor : color;
        }

        private void UpdateReticle(Vector3 worldAimPos)
        {
            if (_reticleTransform != null)
            {
                _reticleTransform.position = worldAimPos;
            }

            if (!_isPinning && _reticleSprite != null)
            {
                if (_tetheredStitchable != null)
                {
                    if (TryGetValidAnchorPoint(worldAimPos, _tetheredStitchable, out Vector2 anchorPos, out NeedleAnchorPoint anchor))
                    {
                        float dist = Vector2.Distance(_tetheredStitchable.CurrentPosition, anchorPos);
                        bool hasLOS = HasLineOfSight(_tetheredStitchable.CurrentPosition, anchorPos, _tetheredStitchable.ObjectTransform, anchor);
                        bool canTransfer = IsWithinThreadBudget(dist) && hasLOS;
                        SetReticleColor(canTransfer ? s_ValidColor : s_InvalidColor);
                    }
                    else
                    {
                        SetReticleColor(s_PlayerTetherColor);
                    }
                }
                else
                {
                    IStitchable hovered = FindStitchableAt(worldAimPos);
                    if (hovered != null)
                    {
                        if (hovered.CurrentState == StitchState.Looping)
                        {
                            SetReticleColor(s_ValidColor);
                        }
                        else
                        {
                            bool isStandingOnHovered = _playerController != null && _playerController.IsStandingOn(hovered.ObjectTransform);
                            bool isValid = IsPlayerNearObject(hovered) && !isStandingOnHovered;
                            SetReticleColor(isValid ? s_PlayerTetherColor : s_InvalidColor);
                        }
                    }
                    else
                    {
                        SetReticleColor(s_ValidColor);
                    }
                }
            }
        }

        private void HandleInput(Vector3 worldAimPos)
        {
            if (Mouse.current != null)
            {
                bool isOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

                if (!isOverUI)
                {
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                    {
                        OnPrimaryDown(worldAimPos);
                    }

                    if (Mouse.current.leftButton.isPressed)
                    {
                        OnPrimaryHold(worldAimPos);
                    }

                    if (Mouse.current.leftButton.wasReleasedThisFrame)
                    {
                        OnPrimaryUp(worldAimPos);
                    }

                    if (Mouse.current.rightButton.wasPressedThisFrame)
                    {
                        OnSecondaryClick(worldAimPos);
                    }
                }
                else
                {
                    if (_isSlicing)
                    {
                        StopSlicing();
                    }
                }
            }

            if (Keyboard.current != null &&
                (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.qKey.wasPressedThisFrame))
            {
                CancelStitchingOrTether();
            }
        }

        private void OnPrimaryDown(Vector3 worldAimPos)
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

                    if (TryGetValidAnchorPoint(worldAimPos, _tetheredStitchable, out Vector2 anchorPos, out NeedleAnchorPoint foundAnchor))
                    {
                        float tetherTransferDist = Vector2.Distance(_tetheredStitchable.CurrentPosition, anchorPos);
                        bool hasLOS = HasLineOfSight(_tetheredStitchable.CurrentPosition, anchorPos, _tetheredStitchable.ObjectTransform, foundAnchor);

                        if (IsWithinThreadBudget(tetherTransferDist) && hasLOS)
                        {
                            TransferTetherToAnchor(anchorPos, foundAnchor);
                        }
                        else
                        {
                            LogWarning(!hasLOS
                                ? "[Needle] Cannot transfer tether: wall or obstacle is blocking the path to the anchor."
                                : "[Needle] Cannot transfer tether: target wall is out of thread budget or too close.");
                            NotifyActionDenied();
                        }
                        return;
                    }
                }

                IStitchable stitchable = FindStitchableAt(worldAimPos);
                if (stitchable != null)
                {
                    // ถ้ากำลัง Loop อยู่: ให้ Ignore ข้ามไป ไม่แช่แข็งกล่อง และไม่เริ่มเย็บใหม่
                    if (stitchable.CurrentState == StitchState.Looping)
                    {
                        return;
                    }

                    if (CanInteractWithStitchable(stitchable, "stitch"))
                    {
                        StartStitching(stitchable);
                    }
                    return;
                }

                // คลิกบนพื้นที่ว่าง (Empty Space) -> เริ่มต้นโหมดลากตัดเส้นด้าย (Slice Drag)
                StartSlicing(worldAimPos);
            }
            else
            {
                if (_isNeedleInFlight) return;

                // ตรวจสอบว่าคลิกที่ตัวผู้เล่นหรือกล่องเดิมซ้ำเพื่อผูกลากหรือไม่ (Fallback 2-Step Tether สำหรับคนถนัดคลิกซ้าย)
                if (IsAimingAtPlayer(worldAimPos) || FindStitchableAt(worldAimPos) == _activeTarget)
                {
                    if (_activeTarget != null && CanInteractWithStitchable(_activeTarget, "tether"))
                    {
                        StartPlayerTether(_activeTarget);
                    }
                    return;
                }

                // ยิงเข็ม (Needle Projectile) พุ่งไปยังตำแหน่งเป้าหมาย
                LaunchNeedleProjectile(worldAimPos);
            }
        }

        private void OnPrimaryHold(Vector3 worldAimPos)
        {
            if (_isSlicing)
            {
                UpdateSlicing(worldAimPos);
            }
        }

        private void OnPrimaryUp(Vector3 worldAimPos)
        {
            if (_isSlicing)
            {
                UpdateSlicing(worldAimPos);
                StopSlicing();
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
            Vector2 startPos = GetCastOrigin(launchOrigin, _activeTarget.ObjectTransform, aimDir);

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
            OnNeedleLaunched?.Invoke(aimDir);
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

            NeedleAnchorPoint anchor = hitCol != null ? hitCol.GetComponentInParent<NeedleAnchorPoint>() : null;
            bool hasValidAnchorComp = anchor != null && anchor.IsAnchorEnabled;

            if (_requireAnchorComponent && !hasValidAnchorComp)
            {
                LogWarning($"[Needle] Surface '{(hitCol != null ? hitCol.name : "null")}' has no active NeedleAnchorPoint. Rewinding!");
                _needleProjectile.StartRewind();
                NotifyActionDenied();
                return;
            }

            Vector2 anchorPos = ResolveAnchorPosition(hitPoint, hitNormal, hasValidAnchorComp ? anchor : null);

            // Check thread length budget from Box (Waypoint 0)
            Vector2 boxPos = _pinnedWaypoints[0];
            float segDist = Vector2.Distance(boxPos, anchorPos);

            if (!IsWithinThreadBudget(segDist))
            {
                LogWarning($"[Needle] Hit point is out of thread budget ({segDist:F1}m > {_maxThreadLength:F1}m). Rewinding!");
                _needleProjectile.StartRewind();
                NotifyActionDenied();
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

            ClearCurrentPinnedAnchor();
            if (hasValidAnchorComp)
            {
                _currentPinnedAnchor = anchor;
                _currentPinnedAnchor.NotifyPinned();
            }

            Vector2 hitDir = (_pinnedWaypoints[1] - _pinnedWaypoints[0]).normalized;
            _needleProjectile.Embed(anchorPos, hitDir);
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
            if (_isSlicing)
            {
                StopSlicing();
            }

            if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
                OnThreadSevered?.Invoke();
                return;
            }

            // คลิกขวาที่ StitchableObject เพื่อผูกลากทันที (Instant Haul Tether) 1-Click
            IStitchable stitchable = FindStitchableAt(worldAimPos);
            if (stitchable != null)
            {
                if (_isPinning)
                {
                    CancelStitching();
                }

                if (CanInteractWithStitchable(stitchable, "tether"))
                {
                    StartPlayerTether(stitchable);
                }
                return;
            }

            if (_isPinning)
            {
                CancelStitching();
                OnThreadSevered?.Invoke();
                return;
            }
        }

        private void EnsureSliceTrail()
        {
            if (_sliceTrailObj == null)
            {
                _sliceTrailObj = new GameObject("SliceBladeTrail");
                _sliceTrailObj.transform.SetParent(transform);
                _sliceTrailRenderer = _sliceTrailObj.AddComponent<TrailRenderer>();
                _sliceTrailRenderer.time = _sliceTrailDuration;

                AnimationCurve widthCurve = new AnimationCurve();
                widthCurve.AddKey(0f, 0.2f);
                widthCurve.AddKey(0.15f, 1.0f);
                widthCurve.AddKey(1f, 0.0f);
                _sliceTrailRenderer.widthCurve = widthCurve;
                _sliceTrailRenderer.widthMultiplier = 0.08f;
                _sliceTrailRenderer.sortingOrder = 20;

                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _sliceTrailMaterial = new Material(shader);
                    _sliceTrailRenderer.material = _sliceTrailMaterial;
                }

                Gradient gradient = new Gradient();
                gradient.SetKeys(
                    new GradientColorKey[] {
                        new GradientColorKey(_sliceTrailStartColor, 0f),
                        new GradientColorKey(_sliceTrailEndColor, 1f)
                    },
                    new GradientAlphaKey[] {
                        new GradientAlphaKey(_sliceTrailStartColor.a, 0f),
                        new GradientAlphaKey(0f, 1f)
                    }
                );
                _sliceTrailRenderer.colorGradient = gradient;
                _sliceTrailRenderer.emitting = false;
            }
        }

        private void StartSlicing(Vector2 startPos)
        {
            _isSlicing = true;
            _lastSliceWorldPos = startPos;
            EnsureSliceTrail();
            if (_sliceTrailObj != null)
            {
                _sliceTrailObj.transform.position = startPos;
                _sliceTrailRenderer.Clear();
                _sliceTrailRenderer.emitting = true;
            }

            // Check if user clicked directly onto or right next to an existing thread
            CheckAndSeverSlice(startPos, startPos);
        }

        private void UpdateSlicing(Vector2 currentPos)
        {
            if (!_isSlicing) return;

            if (_sliceTrailObj != null)
            {
                _sliceTrailObj.transform.position = currentPos;
            }

            float movedDist = Vector2.Distance(_lastSliceWorldPos, currentPos);
            if (movedDist >= _minSliceDistance)
            {
                CheckAndSeverSlice(_lastSliceWorldPos, currentPos);
                _lastSliceWorldPos = currentPos;
            }
        }

        private void CheckAndSeverSlice(Vector2 sliceStart, Vector2 sliceEnd)
        {
            int severedCount = SutureThread.SeverThreadsIntersectingSegment(sliceStart, sliceEnd, _sliceTouchRadius);
            if (severedCount > 0)
            {
                if (_currentlyLoopingTarget != null && _currentlyLoopingTarget.CurrentState != StitchState.Looping)
                {
                    _currentlyLoopingTarget = null;
                }
                if (_needleProjectile != null)
                {
                    _needleProjectile.Cancel();
                }
                ClearCurrentPinnedAnchor();
                CameraController2D.Instance?.Shake(0.045f, 0.09f);
                OnThreadSevered?.Invoke();
                Log($"[Needle] Severed {severedCount} thread(s) via swipe slice!");
            }
        }

        private void StopSlicing()
        {
            if (!_isSlicing) return;
            _isSlicing = false;
            if (_sliceTrailRenderer != null)
            {
                _sliceTrailRenderer.emitting = false;
            }
        }

        private void SeverActiveCircuitIfNeeded(IStitchable nextTarget, bool notifySevered)
        {
            if (!_singleCircuitLimit || _currentlyLoopingTarget == null) return;

            if (_currentlyLoopingTarget != nextTarget)
            {
                Log($"[Needle] Severed previous circuit on {_currentlyLoopingTarget.ObjectTransform.name}.");
                _currentlyLoopingTarget.CutThread();
                if (notifySevered)
                {
                    OnThreadSevered?.Invoke();
                }
            }

            _currentlyLoopingTarget = null;
            ClearCurrentPinnedAnchor();
        }

        public void StartPlayerTether(IStitchable target)
        {
            if (target == null) return;
            EnsurePlayerComponents();

            SeverActiveCircuitIfNeeded(target, notifySevered: true);
            if (_needleProjectile != null && _needleProjectile.IsEmbedded)
            {
                _needleProjectile.Cancel();
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
            OnPlayerTetherAttached?.Invoke();
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

        private void TransferTetherToAnchor(Vector2 anchorPos, NeedleAnchorPoint anchorPointComp = null)
        {
            if (_tetheredStitchable == null) return;

            IStitchable target = _tetheredStitchable;
            ReleasePlayerTether();

            SeverActiveCircuitIfNeeded(target, notifySevered: true);

            if (anchorPointComp != null)
            {
                _currentPinnedAnchor = anchorPointComp;
                _currentPinnedAnchor.NotifyPinned();
            }

            _pinnedWaypoints.Clear();
            _pinnedWaypoints.Add(target.CurrentPosition);
            _pinnedWaypoints.Add(anchorPos);

            if (_needleProjectile != null)
            {
                Vector2 hitDir = (anchorPos - target.CurrentPosition).normalized;
                _needleProjectile.Embed(anchorPos, hitDir);
            }

            target.StartLoopAlongWaypoints(_pinnedWaypoints, LoopPlaybackMode.PingPong);
            _currentlyLoopingTarget = target;
            CameraController2D.Instance?.Shake(0.06f, 0.12f);
            Log($"[Needle] Transferred tether on {target.ObjectTransform.name} to Wall at {anchorPos}!");
        }

        public void CancelStitchingOrTether()
        {
            if (_isSlicing)
            {
                StopSlicing();
            }

            if (_isPinning)
            {
                CancelStitching();
            }
            else if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
            }
        }

        public void ResetAllNeedleStates()
        {
            CancelStitching();

            if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
            }

            if (_currentlyLoopingTarget != null)
            {
                _currentlyLoopingTarget.CutThread();
                _currentlyLoopingTarget = null;
            }

            ClearCurrentPinnedAnchor();
            ResetPinningState();
        }

        public void CancelStitching()
        {
            if (!_isPinning) return;

            Log("[Needle] Canceled current stitch.");
            if (_activeTarget != null)
            {
                if (_currentlyLoopingTarget == _activeTarget)
                {
                    _currentlyLoopingTarget = null;
                    ClearCurrentPinnedAnchor();
                }
                _activeTarget.CutThread();
            }
            ResetPinningState();
        }

        private bool TryGetValidAnchorPoint(
            Vector3 worldAimPos,
            IStitchable ignoredStitchable,
            out Vector2 anchorPoint,
            out NeedleAnchorPoint foundAnchor)
        {
            anchorPoint = worldAimPos;
            foundAnchor = null;

            if (!_requireSurfaceToPin) return true;

            // 1. Check if clicking on another valid IStitchable object
            if (!_requireAnchorComponent)
            {
                IStitchable stitchable = FindStitchableAt(worldAimPos);
                if (stitchable != null && stitchable != ignoredStitchable)
                {
                    anchorPoint = stitchable.CurrentPosition;
                    return true;
                }
            }

            // 2. Zero-alloc check with physical surfaces / anchor points (excluding player and ignoredStitchable)
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

                NeedleAnchorPoint anchor = hit.GetComponentInParent<NeedleAnchorPoint>();
                bool isValidAnchor = anchor != null && anchor.IsAnchorEnabled;

                if (hit.isTrigger && !isValidAnchor) continue;
                if (_requireAnchorComponent && !isValidAnchor) continue;

                Vector2 candidatePoint = hit.ClosestPoint(worldAimPos);
                float distSq = ((Vector2)worldAimPos - candidatePoint).sqrMagnitude;
                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    anchorPoint = candidatePoint;
                    foundAnchor = isValidAnchor ? anchor : null;
                    foundValid = true;
                }
            }

            if (foundValid)
            {
                Vector2 normal = (Vector2)worldAimPos - anchorPoint;
                anchorPoint = ResolveAnchorPosition(anchorPoint, normal, foundAnchor);
            }

            return foundValid;
        }

        private void EnsurePlayerComponents()
        {
            if (_playerTransform == null) _playerTransform = transform;
            if (_playerRb == null)
            {
                _playerRb = GetComponent<Rigidbody2D>();
                if (_playerRb == null) _playerRb = GetComponentInParent<Rigidbody2D>();
            }
            if (_playerCol == null)
            {
                _playerCol = GetComponent<Collider2D>();
                if (_playerCol == null) _playerCol = GetComponentInChildren<Collider2D>();
            }
            if (_playerController == null)
            {
                _playerController = GetComponent<PlayerController2D>();
                if (_playerController == null) _playerController = GetComponentInParent<PlayerController2D>();
                if (_playerController == null) _playerController = GetComponentInChildren<PlayerController2D>();
            }
        }

        private bool IsAimingAtPlayer(Vector3 worldAimPos)
        {
            EnsurePlayerComponents();
            return _playerTransform != null && Vector2.Distance(worldAimPos, _playerTransform.position) <= k_PlayerAimRadius;
        }

        private IStitchable FindStitchableAt(Vector3 worldAimPos)
        {
            int hitCount = Physics2D.OverlapCircle(worldAimPos, k_HitSearchRadius, _stitchableFilter, _hitBuffer);
            IStitchable bestCandidate = null;
            int highestSortingOrder = int.MinValue;
            float closestDistanceSqr = float.MaxValue;

            for (int i = 0; i < hitCount; i++)
            {
                Collider2D hitCol = _hitBuffer[i];
                if (hitCol == null || hitCol == _playerCol) continue;

                IStitchable stitchable = hitCol.GetComponentInParent<IStitchable>();
                if (stitchable == null || stitchable.ObjectTransform == null) continue;

                SpriteRenderer sr = stitchable.ObjectTransform.GetComponentInChildren<SpriteRenderer>();
                int sortingOrder = sr != null ? sr.sortingOrder : 0;

                Vector2 targetCenter = hitCol.bounds.center;
                float distSqr = ((Vector2)worldAimPos - targetCenter).sqrMagnitude;

                if (sortingOrder > highestSortingOrder || (sortingOrder == highestSortingOrder && distSqr < closestDistanceSqr))
                {
                    highestSortingOrder = sortingOrder;
                    closestDistanceSqr = distSqr;
                    bestCandidate = stitchable;
                }
            }

            return bestCandidate;
        }

        private void StartStitching(IStitchable target)
        {
            if (_needleProjectile != null)
            {
                _needleProjectile.Cancel();
            }

            _activeTarget = target;
            _activeTarget.AttachThread();

            _pinnedWaypoints.Clear();
            _pinnedWaypoints.Add(_activeTarget.CurrentPosition);
            _activeTarget.UpdatePinnedWaypoints(_pinnedWaypoints);
            _isPinning = true;

            EnsurePlayerComponents();
            if (_playerController != null)
            {
                _playerController.SetAiming(true, _activeTarget.CurrentPosition, MaxAimLeashDistance);
            }

            Log($"[Needle] Aiming needle from {target.ObjectTransform.name}. Aim leash: {MaxAimLeashDistance:F1}m. Click a wall/surface to pin (Max points: {_maxWaypoints}).");
        }

        private void CompleteStitchingRail()
        {
            if (_activeTarget != null && _pinnedWaypoints.Count >= 2)
            {
                if (_singleCircuitLimit && _currentlyLoopingTarget != null && _currentlyLoopingTarget != _activeTarget)
                {
                    Log($"[Needle] Severed previous circuit on {_currentlyLoopingTarget.ObjectTransform.name} to power new circuit.");
                    _currentlyLoopingTarget.CutThread();
                    OnThreadSevered?.Invoke();
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

            EnsurePlayerComponents();
            if (_playerController != null)
            {
                _playerController.SetAiming(false);
            }

            if (_needleProjectile != null && !_needleProjectile.IsEmbedded)
            {
                _needleProjectile.Cancel();
            }
            _isNeedleInFlight = false;
            if (_previewLine != null)
            {
                _previewLine.positionCount = 0;
            }
            SetReticleColor(s_ValidColor);
            UpdateAnchorPointsFeedback(false, Vector2.zero, null);
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

        private bool TryCastNeedleTrajectory(
            Vector2 origin,
            Vector2 direction,
            float maxDistance,
            out RaycastHit2D validHit,
            out NeedleAnchorPoint hitAnchor)
        {
            float sweepRadius = _needleProjectile != null ? _needleProjectile.SweepRadius : 0.08f;
            int hitCount = Physics2D.CircleCast(origin, sweepRadius, direction, _anchorFilter, _rayHitBuffer, maxDistance);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _rayHitBuffer[i];
                Collider2D col = hit.collider;
                if (col == null || col == _playerCol) continue;

                if (_playerTransform != null && (col.transform == _playerTransform || col.transform.IsChildOf(_playerTransform)))
                {
                    continue;
                }

                if (_activeTarget?.ObjectTransform != null &&
                    (col.transform == _activeTarget.ObjectTransform || col.transform.IsChildOf(_activeTarget.ObjectTransform)))
                {
                    continue;
                }

                NeedleAnchorPoint anchor = col.GetComponentInParent<NeedleAnchorPoint>();
                bool isValidAnchor = anchor != null && anchor.IsAnchorEnabled;

                if (col.isTrigger && !isValidAnchor) continue;

                validHit = hit;
                hitAnchor = isValidAnchor ? anchor : null;
                return true;
            }

            validHit = default;
            hitAnchor = null;
            return false;
        }

        private bool HasLineOfSight(Vector2 fromPos, Vector2 toPos, Transform ignoredOrigin, NeedleAnchorPoint targetAnchor)
        {
            Vector2 delta = toPos - fromPos;
            float dist = delta.magnitude;
            if (dist <= 0.05f) return true;

            Vector2 dir = delta / dist;
            Vector2 rayOrigin = GetCastOrigin(fromPos, ignoredOrigin, dir);

            float rayDist = Vector2.Distance(rayOrigin, toPos);
            if (rayDist <= 0.05f) return true;

            int hitCount = Physics2D.CircleCast(rayOrigin, 0.06f, dir, _anchorFilter, _rayHitBuffer, rayDist);
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _rayHitBuffer[i];
                Collider2D col = hit.collider;
                if (col == null || col == _playerCol || col.isTrigger) continue;

                if (_playerTransform != null && (col.transform == _playerTransform || col.transform.IsChildOf(_playerTransform)))
                    continue;
                if (ignoredOrigin != null && (col.transform == ignoredOrigin || col.transform.IsChildOf(ignoredOrigin)))
                    continue;
                if (targetAnchor != null && (col.transform == targetAnchor.transform || col.transform.IsChildOf(targetAnchor.transform)))
                    continue;

                if (Vector2.Distance(hit.point, toPos) > 0.12f)
                {
                    return false;
                }
            }

            return true;
        }

        private void UpdatePreviewVisual(Vector3 worldAimPos)
        {
            if (!_isPinning || _pinnedWaypoints.Count == 0 || _previewLine == null)
            {
                if (_previewLine != null && _previewLine.positionCount != 0)
                {
                    _previewLine.positionCount = 0;
                }
                UpdateAnchorPointsFeedback(false, Vector2.zero, null);
                return;
            }

            // Hide aim guide line while needle is actively flying in the air
            if (_isNeedleInFlight)
            {
                _previewLine.positionCount = 0;
                UpdateAnchorPointsFeedback(false, Vector2.zero, null);
                return;
            }

            Vector2 launchOrigin = _pinnedWaypoints[0];
            Vector2 aimVector = (Vector2)worldAimPos - launchOrigin;
            float aimDist = aimVector.magnitude;
            Vector2 aimDir = aimDist > 0.001f ? aimVector / aimDist : Vector2.right;

            Vector2 rayOrigin = GetCastOrigin(launchOrigin, _activeTarget?.ObjectTransform, aimDir);

            bool hasValidHit = TryCastNeedleTrajectory(rayOrigin, aimDir, _maxThreadLength, out RaycastHit2D hit, out NeedleAnchorPoint hitAnchor);
            Vector2 endPoint;
            bool canPlace = false;

            if (hasValidHit)
            {
                if (_requireAnchorComponent && hitAnchor == null)
                {
                    endPoint = hit.point;
                }
                else
                {
                    endPoint = ResolveAnchorPosition(hit.point, hit.normal, hitAnchor);
                    float totalDist = Vector2.Distance(launchOrigin, endPoint);
                    canPlace = IsWithinThreadBudget(totalDist);
                }
            }
            else
            {
                endPoint = launchOrigin + aimDir * Mathf.Min(aimDist, _maxThreadLength);
            }

            UpdateAnchorPointsFeedback(true, launchOrigin, canPlace ? hitAnchor : null);

            Color activeColor = canPlace ? s_ValidColor : s_InvalidColor;
            SetReticleColor(activeColor);
            DrawAimTrajectory(launchOrigin, endPoint, activeColor);
        }

        private void UpdateAnchorPointsFeedback(bool isAiming, Vector2 launchOrigin, NeedleAnchorPoint hoveredAnchor)
        {
            IReadOnlyList<NeedleAnchorPoint> anchors = NeedleAnchorPoint.ActiveAnchors;
            int count = anchors.Count;
            for (int i = 0; i < count; i++)
            {
                NeedleAnchorPoint anchor = anchors[i];
                if (anchor == null) continue;

                anchor.UpdateAimFeedback(
                    isAiming,
                    launchOrigin,
                    _minWaypointDistance,
                    _maxThreadLength,
                    isAiming && anchor == hoveredAnchor);
            }
        }

        private void DrawAimTrajectory(Vector2 start, Vector2 end, Color color)
        {
            if (_previewLine == null) return;

            _previewLine.positionCount = 2;
            _previewLine.SetPosition(0, new Vector3(start.x, start.y, 0f));
            _previewLine.SetPosition(1, new Vector3(end.x, end.y, 0f));

            if (_previewMaterial != null)
            {
                _previewMaterial.SetColor(SutureVisualUtils.BaseColorId, color);
                float lineLen = Vector2.Distance(start, end);
                _previewMaterial.SetFloat(SutureVisualUtils.TilingId, Mathf.Max(1f, lineLen * 2.5f));
            }
        }

        private void NotifyActionDenied()
        {
            TriggerReticleDeniedFeedback();
            OnActionDenied?.Invoke();
        }

        private void TriggerReticleDeniedFeedback()
        {
            _deniedFlashTimer = 0.22f;

            if (_reticleTransform != null)
            {
                _reticleTransform.DOKill();
                _reticleTransform.localScale = _reticleBaseScale;
                _reticleTransform.DOPunchScale(_reticleBaseScale * 0.35f, 0.18f, vibrato: 12, elasticity: 0.5f);
            }

            if (_reticleSprite != null)
            {
                _reticleSprite.color = s_InvalidColor;
            }
        }

        private void OnDisable()
        {
            if (_isSlicing)
            {
                StopSlicing();
            }
        }

        private void OnDestroy()
        {
            if (_reticleTransform != null) _reticleTransform.DOKill();
            if (_reticleSprite != null) _reticleSprite.DOKill();
            if (_previewMaterial != null)
            {
                Destroy(_previewMaterial);
            }
            if (_sliceTrailMaterial != null)
            {
                Destroy(_sliceTrailMaterial);
            }
            if (_sliceTrailObj != null)
            {
                Destroy(_sliceTrailObj);
            }
            if (_needleProjectile != null && _needleProjectile.gameObject != null && _needleProjectile.gameObject.scene.IsValid())
            {
                Destroy(_needleProjectile.gameObject);
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

        private void OnDrawGizmosSelected()
        {
            Vector3 center = transform.position;

            if (_maxInteractionDistance > 0f)
            {
                Gizmos.color = new Color(0.2f, 0.9f, 1f, 0.45f);
                Gizmos.DrawWireSphere(center, _maxInteractionDistance);
            }

            Vector3 originPos = center;
            if (_isPinning && _pinnedWaypoints.Count > 0)
            {
                originPos = _pinnedWaypoints[0];
            }
            else if (_activeTarget != null)
            {
                originPos = _activeTarget.CurrentPosition;
            }

            if (_minWaypointDistance > 0f)
            {
                Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.6f);
                Gizmos.DrawWireSphere(originPos, _minWaypointDistance);
            }

            if (_maxThreadLength > 0f)
            {
                Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.75f);
                Gizmos.DrawWireSphere(originPos, _maxThreadLength);
            }

            if (_isPinning && _maxAimLeashDistance > 0f)
            {
                Gizmos.color = new Color(0.85f, 0.4f, 1f, 0.5f);
                Gizmos.DrawWireSphere(originPos, _maxAimLeashDistance);
            }
        }
    }
}