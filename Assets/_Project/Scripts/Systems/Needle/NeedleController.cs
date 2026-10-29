using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using DG.Tweening;
using ThailandHorrorJam.Core;

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
        private static readonly Color s_CutAimIdleColor = new Color(1f, 1f, 1f, 0.65f);

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

        [Tooltip("When true, the player must be grounded to start or maintain aiming.")]
        [SerializeField] private bool _requireGroundedToAim = true;

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

        [Header("Aim Reticle (Eyelet & Pin)")]
        [FormerlySerializedAs("reticleTransform")]
        [SerializeField] private Transform _reticleTransform;
        [SerializeField] private SpriteRenderer _reticleRingSprite;
        [SerializeField] private SpriteRenderer _reticleDotSprite;
        [SerializeField] private float _reticlePulseSpeed = 7.5f;
        [SerializeField] private float _reticlePulseScale = 0.12f;

        [Header("Aim & Shoot to Cut")]
        [Tooltip("Effective touch/blade radius around the projectile to cut through suture threads.")]
        [SerializeField] private float _cutHitRadius = 0.28f;

        [Tooltip("Optional SutureThread prefab for the red thread trailing from the player during cut needle flight. If null, one is created automatically.")]
        [SerializeField] private SutureThread _sutureThreadPrefab;

        [Tooltip("จำนวนลอนด้ายที่กองบนพื้นตอนเล็ง (Aim)")]
        [Range(3, 16)]
        [SerializeField] private int _cutAimPooledCoilCount = 8;

        [Tooltip("ระยะการแผ่กว้างของกองด้ายที่พื้น (เมตร)")]
        [Range(0.1f, 1.2f)]
        [SerializeField] private float _cutAimPooledSpreadRadius = 0.42f;

        [Tooltip("ความสูงของลอนด้ายที่กองทับซ้อนกันบนพื้น (เมตร)")]
        [Range(0.01f, 0.15f)]
        [SerializeField] private float _cutAimPooledPileHeight = 0.055f;

        private Transform _playerTransform;
        private Rigidbody2D _playerRb;
        private Collider2D _playerCol;
        private PlayerController2D _playerController;
        private PlayerVisuals _playerVisuals;

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
        private bool _isCutAiming;
        private bool _isCutNeedleInFlight;
        private bool _didCutSeverThread;
        private NeedleProjectile _cutNeedleProjectile;
        private SutureThread _cutNeedleThread;

        private Material _previewMaterial;
        private SpriteRenderer _reticleSprite;
        private Vector3 _reticleBaseScale = Vector3.one;
        private float _deniedFlashTimer;

        public bool RequireGroundedToAim
        {
            get => _requireGroundedToAim;
            set => _requireGroundedToAim = value;
        }

        public bool IsPinning => _isPinning;
        public bool IsCutAiming => _isCutAiming;
        public bool IsAiming => _isPinning || _isCutAiming;
        public bool IsNeedleInFlight => _isNeedleInFlight;
        public bool IsAnyNeedleInFlight => _isNeedleInFlight || _isCutNeedleInFlight;
        public bool IsTetheredToPlayer => _tetheredStitchable != null;
        public IStitchable TetheredStitchable => _tetheredStitchable;
        public float MaxAimLeashDistance => _maxAimLeashDistance > 0f ? _maxAimLeashDistance : (_maxInteractionDistance > 0f ? _maxInteractionDistance : 3.5f);

        public event System.Action<Vector2> OnNeedleLaunched;
        public event System.Action OnActionDenied;
        public event System.Action OnThreadSevered;

        private void TriggerThreadSeveredSound()
        {
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_thread_cut");
            }
        }
        public event System.Action OnPlayerTetherAttached;

        private void Awake()
        {
            EnsurePlayerComponents();

            if (_mainCamera == null)
            {
                _mainCamera = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            }

            EnsureReticle();
            SetReticleActive(false);

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
            EnsureCutNeedleProjectile();
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

        private void EnsureCutNeedleProjectile()
        {
            if (_cutNeedleProjectile == null || !_cutNeedleProjectile.gameObject.scene.IsValid())
            {
                Transform found = transform.Find("CutNeedleProjectile");
                if (found != null && found.TryGetComponent(out NeedleProjectile sceneChild))
                {
                    _cutNeedleProjectile = sceneChild;
                }
                else if (_needleProjectile != null)
                {
                    NeedleProjectile prefabAsset = _needleProjectile;
                    _cutNeedleProjectile = Instantiate(prefabAsset, transform);
                    _cutNeedleProjectile.name = "CutNeedleProjectile";
                }
                else
                {
                    GameObject projObj = new GameObject("CutNeedleProjectile");
                    projObj.transform.SetParent(transform);
                    projObj.transform.localPosition = Vector3.zero;
                    _cutNeedleProjectile = projObj.AddComponent<NeedleProjectile>();
                }
            }
            _cutNeedleProjectile.gameObject.SetActive(false);
        }

        private void EnsureCutNeedleThread()
        {
            if (_cutNeedleThread == null || !_cutNeedleThread.gameObject.scene.IsValid())
            {
                Transform found = transform.Find("CutNeedleThread");
                if (found != null && found.TryGetComponent(out SutureThread sceneChild))
                {
                    _cutNeedleThread = sceneChild;
                }
                else if (_sutureThreadPrefab != null)
                {
                    _cutNeedleThread = Instantiate(_sutureThreadPrefab, transform);
                    _cutNeedleThread.name = "CutNeedleThread";
                }
                else
                {
                    GameObject threadObj = new GameObject("CutNeedleThread");
                    threadObj.transform.SetParent(transform);
                    threadObj.transform.localPosition = Vector3.zero;
                    threadObj.AddComponent<LineRenderer>();
                    _cutNeedleThread = threadObj.AddComponent<SutureThread>();
                }

                if (_cutNeedleThread != null)
                {
                    _cutNeedleThread.IsCuttable = false;
                    _cutNeedleThread.HideAllPins = false;
                    _cutNeedleThread.PooledCoilCount = _cutAimPooledCoilCount;
                    _cutNeedleThread.PooledSpreadRadius = _cutAimPooledSpreadRadius;
                    _cutNeedleThread.PooledPileHeight = _cutAimPooledPileHeight;

                    EnsurePlayerComponents();
                    if (_playerVisuals != null && _playerVisuals.MainRenderer != null)
                    {
                        _cutNeedleThread.SetSortingLayer(
                            _playerVisuals.MainRenderer.sortingLayerName,
                            _playerVisuals.MainRenderer.sortingOrder + 2
                        );
                    }
                }
            }
        }

        private void ConfigurePreviewLine()
        {
            _previewLine.useWorldSpace = true;
            _previewLine.widthCurve = SutureVisualUtils.CreateWidthProfile();
            _previewLine.widthMultiplier = 0.05f;

            _previewLine.startColor = s_ValidColor;
            _previewLine.endColor = s_ValidColor;
            _previewLine.positionCount = 0;
            _previewLine.sortingOrder = 15;
            _previewLine.textureMode = LineTextureMode.Tile;

            _previewMaterial = SutureVisualUtils.CreateSutureMaterial(
                _lineMaterial,
                s_ValidColor,
                scrollSpeed: 2.0f,
                tiling: 4.0f,
                enableOutline: true,
                outlineColor: new Color(0f, 0f, 0f, 0.65f),
                outlineThickness: 0.25f,
                useProceduralDash: true,
                dashRatio: 0.55f
            );
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

            // ถ้ากำลังเล็งอยู่ (Pinning หรือ Cut Aiming) แล้วผู้เล่นลอยอยู่กลางอากาศ (เช่น ตกขอบหรือกระโดด) ให้ยกเลิกการเล็งทันที
            if (_requireGroundedToAim && IsAiming)
            {
                EnsurePlayerComponents();
                if (_playerController != null && !_playerController.IsGrounded)
                {
                    if (_isPinning && !_isNeedleInFlight)
                    {
                        LogWarning("[Needle] Aim canceled because player became airborne!");
                        CancelStitching();
                        TriggerThreadSeveredSound(); OnThreadSevered?.Invoke();
                    }
                    else if (_isCutAiming && !_isCutNeedleInFlight)
                    {
                        LogWarning("[Needle] Cut aim canceled because player became airborne!");
                        ResetCutAimState();
                    }
                }
            }

            SyncFirstWaypointWithActiveTarget();

            Vector3 worldAimPos = GetAimWorldPosition();
            UpdateReticle(worldAimPos);
            HandleInput(worldAimPos);
            UpdatePreviewVisual(worldAimPos);
            UpdateInFlightThread();
            UpdateCutNeedleThread(worldAimPos);
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

                Vector2 playerHandPos;
                if (!TryGetNeedleHandPosition(out playerHandPos))
                {
                    playerHandPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : objPos;
                }

                if (_playerController != null)
                {
                    _playerController.UpdateAimLeash(objPos, MaxAimLeashDistance);
                }

                _activeTarget.UpdateAimLeash(playerHandPos, MaxAimLeashDistance);
            }
        }

        private void UpdateInFlightThread()
        {
            if (_isPinning && _isNeedleInFlight && _activeTarget != null && _needleProjectile != null)
            {
                Vector2 launchOrigin = _pinnedWaypoints.Count > 0 ? _pinnedWaypoints[0] : _activeTarget.CurrentPosition;
                Vector2 needlePos = _needleProjectile.CurrentPosition;

                Vector2 toNeedle = needlePos - launchOrigin;
                float currentDist = toNeedle.magnitude;

                // Enforce strict thread length limit: clamp position and rewind if needle drifted beyond max length
                if (currentDist > _maxThreadLength)
                {
                    needlePos = launchOrigin + (toNeedle / currentDist) * _maxThreadLength;
                    _needleProjectile.StartRewind();
                }

                if (_pinnedWaypoints.Count >= 2)
                {
                    _pinnedWaypoints[1] = needlePos;
                }
                _activeTarget.UpdateInFlightNeedle(needlePos);
            }
        }

        private void UpdateCutNeedleThread(Vector3 worldAimPos)
        {
            if (_isCutNeedleInFlight && _cutNeedleProjectile != null)
            {
                EnsureCutNeedleThread();
                if (_cutNeedleThread != null)
                {
                    Vector2 playerHandPos;
                    if (!TryGetNeedleHandPosition(out playerHandPos))
                    {
                        playerHandPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : (Vector2)transform.position;
                    }

                    Vector2 needlePos = _cutNeedleProjectile.CurrentPosition;
                    _cutNeedleThread.UpdateInFlightNeedle(playerHandPos, needlePos);
                }
            }
            else if (_isCutAiming && !_isCutNeedleInFlight)
            {
                EnsureCutNeedleThread();
                if (_cutNeedleThread != null)
                {
                    Vector2 playerHandPos;
                    if (!TryGetNeedleHandPosition(out playerHandPos))
                    {
                        playerHandPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : (Vector2)transform.position;
                    }

                    if (!_cutNeedleThread.IsPooledActive)
                    {
                        Vector2 feetPos = GetPlayerFeetPosition();
                        float facingDir = (worldAimPos.x - playerHandPos.x) >= 0f ? 1f : -1f;
                        _cutNeedleThread.InitializePooledRope(playerHandPos, feetPos, facingDir);
                    }
                    else
                    {
                        _cutNeedleThread.UpdatePooledHandPosition(playerHandPos);
                    }
                }
            }
        }

        private Vector2 GetPlayerFeetPosition()
        {
            if (_playerTransform == null) return transform.position;

            Vector2 playerPos = _playerTransform.position;
            RaycastHit2D hit = Physics2D.Raycast(playerPos, Vector2.down, 1.8f, _anchorFilter.layerMask);
            if (hit.collider != null)
            {
                return hit.point;
            }

            if (_playerCol != null)
            {
                return new Vector2(playerPos.x, _playerCol.bounds.min.y);
            }

            return playerPos + new Vector2(0f, -0.45f);
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
            return distance >= _minWaypointDistance && distance <= (_maxThreadLength + 0.05f);
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

            EnsurePlayerComponents();

            // ตรวจสอบว่าผู้เล่นลอยอยู่กลางอากาศหรือไม่ ถ้าต้องการปักเย็บ/เล็ง (ห้ามเล็งตอนลอยอยู่)
            if (_requireGroundedToAim && actionDesc == "stitch" && _playerController != null && !_playerController.IsGrounded)
            {
                LogWarning($"[Needle] Cannot {actionDesc} {stitchable.ObjectTransform.name} while airborne!");
                CameraController2D.Instance?.Shake(0.025f, 0.08f);
                NotifyActionDenied();
                return false;
            }

            // อนุญาตให้ Tether ขณะยืนอยู่บนวัตถุได้ โดยระบบฟิสิกส์จะพักแรง Joint ชั่วคราวเพื่อป้องกัน hoverboard
            bool allowWhileStanding = actionDesc == "tether";
            if (!allowWhileStanding && _playerController != null && _playerController.IsStandingOn(stitchable.ObjectTransform))
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
            Color finalColor = _deniedFlashTimer > 0f ? s_InvalidColor : color;
            if (_reticleSprite != null) _reticleSprite.color = finalColor;
            if (_reticleRingSprite != null) _reticleRingSprite.color = finalColor;
            if (_reticleDotSprite != null) _reticleDotSprite.color = finalColor;
        }

        private void UpdateReticle(Vector3 worldAimPos)
        {
            if (!IsAiming)
            {
                SetReticleActive(false);
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

                    if (Mouse.current.rightButton.wasPressedThisFrame)
                    {
                        OnSecondaryClick(worldAimPos);
                    }
                }
            }
        }

        private void OnPrimaryDown(Vector3 worldAimPos)
        {
            if (!IsAiming)
            {
                if (_tetheredStitchable != null)
                {
                    EnsurePlayerComponents();
                    if (_requireGroundedToAim && _playerController != null && !_playerController.IsGrounded)
                    {
                        LogWarning($"[Needle] Cannot aim while airborne!");
                        CameraController2D.Instance?.Shake(0.025f, 0.08f);
                        NotifyActionDenied();
                        return;
                    }

                    IStitchable target = _tetheredStitchable;
                    if (_playerController != null && _playerController.IsStandingOn(target.ObjectTransform))
                    {
                        LogWarning($"[Needle] Cannot aim {target.ObjectTransform.name} while standing on it!");
                        CameraController2D.Instance?.Shake(0.025f, 0.08f);
                        NotifyActionDenied();
                        return;
                    }

                    float dist = Vector2.Distance(_playerTransform.position, target.CurrentPosition);
                    if (dist <= MaxAimLeashDistance + 0.5f)
                    {
                        ReleasePlayerTether();
                        StartStitching(target);
                        return;
                    }
                    else
                    {
                        LogWarning($"[Needle] Too far from {target.ObjectTransform.name} to aim! Distance: {dist:F1}m (Max: {MaxAimLeashDistance:F1}m). Move closer.");
                        CameraController2D.Instance?.Shake(0.025f, 0.08f);
                        NotifyActionDenied();
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

                // คลิกซ้ายบนพื้นที่ว่างขณะไม่ได้เล็ง -> ไม่ทำอะไร (โหมดเล็งตัดด้ายใช้คลิกขวา Toggle)
            }
            else if (_isPinning)
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

                // ยิงเข็ม (Needle Projectile) พุ่งไปยังตำแหน่งเป้าหมายเพื่อปัก Anchor
                LaunchNeedleProjectile(worldAimPos);
            }
            else if (_isCutAiming)
            {
                if (_isCutNeedleInFlight) return;

                // ยิงเข็ม (Cut Needle Projectile) พุ่งไปตัดเส้นด้าย Loop
                LaunchCutNeedleProjectile(worldAimPos);
            }
        }

        private void LaunchNeedleProjectile(Vector3 worldAimPos)
        {
            EnsureNeedleProjectile();
            if (_needleProjectile == null || _activeTarget == null) return;

            Vector2 launchOrigin = _pinnedWaypoints[0];
            Vector2 startPos = launchOrigin;
            if (TryGetNeedleTipPosition(out Vector2 tipPos))
            {
                startPos = tipPos;
            }

            Vector2 aimVector = (Vector2)worldAimPos - startPos;
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

            float maxFlightDist = Mathf.Max(0.2f, _maxThreadLength);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_needle_shoot");
            }
            _needleProjectile.Launch(
                origin: startPos,
                direction: aimDir,
                speed: _projectileSpeed,
                maxDistance: maxFlightDist,
                returnTransform: _playerTransform,
                targetToIgnore: _activeTarget.ObjectTransform,
                collisionFilter: _anchorFilter,
                onHit: OnNeedleHitSurface,
                onRewindComplete: OnNeedleRewindComplete,
                playerTransform: _playerTransform,
                returnOffset: new Vector3(0f, 0.35f, 0f)
            );

            _activeTarget.UpdateInFlightNeedle(startPos);
            SetReticleActive(false);

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

            // Check Line of Sight between Waypoint 0 (Box) and Hit Point (Anchor) to prevent stitching rails through solid geometry
            if (!HasLineOfSight(boxPos, anchorPos, _activeTarget?.ObjectTransform, hasValidAnchorComp ? anchor : null))
            {
                LogWarning($"[Needle] Cannot complete rail: Wall or obstacle is blocking the path between {_activeTarget.ObjectTransform.name} and anchor at {anchorPos}. Rewinding!");
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
            // 1. ถ้ามีการผูกสายจูงลาก (Player Tether) อยู่ -> คลิกขวาเพื่อปลดสายจูงทันที (Release Tether)
            if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
                TriggerThreadSeveredSound(); OnThreadSevered?.Invoke();
                return;
            }

            // 2. ถ้าอยู่ในโหมด Pinning อยู่แล้ว -> คลิกขวาเพื่อยกเลิกการเย็บ (Cancel Pinning)
            if (_isPinning)
            {
                CancelStitching();
                TriggerThreadSeveredSound(); OnThreadSevered?.Invoke();
                return;
            }

            // 3. ถ้าอยู่ในโหมด Cut Aim อยู่แล้ว -> คลิกขวาเพื่อยกเลิก / ปิดโหมดเล็ง (Toggle OFF)
            if (_isCutAiming)
            {
                ResetCutAimState();
                return;
            }

            // 4. คลิกขวาที่ StitchableObject เพื่อผูกลากทันที (Instant Haul Tether) 1-Click (รวมถึงขณะยืนอยู่บนวัตถุ)
            IStitchable stitchable = FindStitchableAt(worldAimPos);
            if (stitchable != null)
            {
                if (CanInteractWithStitchable(stitchable, "tether"))
                {
                    StartPlayerTether(stitchable);
                }
                return;
            }

            // 5. ถ้าไม่ได้เล็งอะไรอยู่ และคลิกขวาบนพื้นที่ว่าง/ด้าย -> เปิดโหมดเล็งยิงเข็มตัดด้าย (Toggle ON)
            StartCutAiming();
        }

        public void StartCutAiming()
        {
            if (_isCutAiming) return;

            EnsurePlayerComponents();
            if (_requireGroundedToAim && _playerController != null && !_playerController.IsGrounded)
            {
                LogWarning("[Needle] Cannot enter Cut Aim mode while airborne!");
                CameraController2D.Instance?.Shake(0.025f, 0.08f);
                NotifyActionDenied();
                return;
            }

            EnsureCutNeedleProjectile();
            if (_cutNeedleProjectile != null)
            {
                _cutNeedleProjectile.Cancel();
            }

            _isCutAiming = true;
            _didCutSeverThread = false;

            EnsurePlayerComponents();
            if (_playerController != null)
            {
                _playerController.SetAiming(true);
            }
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_aim_needle_enter");
            }

            Log("[Needle] Entered Cut Aim mode. Aim at threads and click left mouse to shoot cut needle. Right-click to cancel.");
        }

        public void ResetCutAimState()
        {
            if (!_isCutAiming && !_isCutNeedleInFlight) return;

            _isCutAiming = false;
            _didCutSeverThread = false;

            if (_cutNeedleProjectile != null && !_cutNeedleProjectile.IsEmbedded)
            {
                _cutNeedleProjectile.Cancel();
            }
            _isCutNeedleInFlight = false;
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_aim_cancel");
            }

            if (_cutNeedleThread != null)
            {
                _cutNeedleThread.Clear();
            }

            EnsurePlayerComponents();
            if (_playerController != null && !_isPinning)
            {
                _playerController.SetAiming(false);
            }

            if (!_isPinning)
            {
                if (_previewLine != null)
                {
                    _previewLine.positionCount = 0;
                }
                SetReticleColor(s_ValidColor);
                SetReticleActive(false);
            }
        }

        private void LaunchCutNeedleProjectile(Vector3 worldAimPos)
        {
            EnsureCutNeedleProjectile();
            if (_cutNeedleProjectile == null) return;

            EnsurePlayerComponents();
            Vector2 playerHandPos;
            if (!TryGetNeedleHandPosition(out playerHandPos))
            {
                playerHandPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : (Vector2)transform.position;
            }

            Vector2 startPos = playerHandPos;
            if (TryGetNeedleTipPosition(out Vector2 tipPos))
            {
                startPos = tipPos;
            }

            Vector2 aimVector = (Vector2)worldAimPos - startPos;
            Vector2 aimDir = aimVector.sqrMagnitude > 0.001f ? aimVector.normalized : Vector2.right;

            _isCutNeedleInFlight = true;
            _didCutSeverThread = false;

            EnsureCutNeedleThread();
            if (_cutNeedleThread != null)
            {
                _cutNeedleThread.UpdateInFlightNeedle(playerHandPos, startPos);
            }

            // Point-blank check: ป้องกันเข็มยิงข้ามเส้นด้ายที่อยู่ชิดตัวผู้เล่นในช่วงมือถึงปลายเข็ม (handPos -> startPos)
            if (TrySeverAlongSegment(playerHandPos, startPos))
            {
                _didCutSeverThread = true;
            }

            float maxFlightDist = Mathf.Max(0.2f, _maxThreadLength);

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_needle_shoot");
            }
            _cutNeedleProjectile.Launch(
                origin: startPos,
                direction: aimDir,
                speed: _projectileSpeed,
                maxDistance: maxFlightDist,
                returnTransform: _playerTransform,
                targetToIgnore: null,
                collisionFilter: _anchorFilter,
                onHit: OnCutNeedleHitSurface,
                onRewindComplete: OnCutNeedleRewindComplete,
                playerTransform: _playerTransform,
                returnOffset: new Vector3(0f, 0.35f, 0f),
                onStepSegment: OnCutNeedleStepSegment
            );

            SetReticleActive(false);

            CameraController2D.Instance?.Punch(aimDir, 0.03f);
            OnNeedleLaunched?.Invoke(aimDir);
            Log($"[Needle] Threw cut needle projectile at {_projectileSpeed}m/s towards {aimDir}!");
        }

        private bool OnCutNeedleStepSegment(Vector2 segStart, Vector2 segEnd)
        {
            if (TrySeverAlongSegment(segStart, segEnd))
            {
                _didCutSeverThread = true;
                return true;
            }
            return false;
        }

        private bool TrySeverAlongSegment(Vector2 segStart, Vector2 segEnd)
        {
            int severed = SutureThread.SeverThreadsIntersectingSegment(segStart, segEnd, _cutHitRadius);
            if (severed > 0)
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
                TriggerThreadSeveredSound(); OnThreadSevered?.Invoke();
                Log($"[Needle] Cut needle severed {severed} thread(s) along segment!");
                return true;
            }
            return false;
        }

        private void OnCutNeedleHitSurface(Vector2 hitPoint, Vector2 hitNormal, Collider2D hitCol)
        {
            if (_cutNeedleProjectile != null)
            {
                _cutNeedleProjectile.StartRewind();
            }
        }

        private void OnCutNeedleRewindComplete()
        {
            _isCutNeedleInFlight = false;
            if (_cutNeedleThread != null)
            {
                _cutNeedleThread.Clear();
            }

            // ถ้าตัดด้ายสำเร็จ -> ออกจากโหมด Aim อัตโนมัติ
            // ถ้ายิงพลาด -> ยังคงอยู่ในโหมด Cut Aim ต่อไปเพื่อให้ยิงซ้ำได้ทันที
            if (_didCutSeverThread)
            {
                ResetCutAimState();
                Log("[Needle] Thread cut successfully! Exited Aim mode.");
            }
            else
            {
                Log("[Needle] Cut needle rewound to hand. Ready to re-aim.");
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
                    TriggerThreadSeveredSound(); OnThreadSevered?.Invoke();
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
            Log($"[Needle] Tethered {target.ObjectTransform.name} to Player! Walk to haul it. Left-click to aim, Right-click to release.");

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
            if (_isPinning)
            {
                CancelStitching();
            }
            else if (_isCutAiming)
            {
                ResetCutAimState();
            }
            else if (_tetheredStitchable != null)
            {
                ReleasePlayerTether();
            }
        }

        public void ResetAllNeedleStates()
        {
            CancelStitching();
            ResetCutAimState();

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
            if (_playerVisuals == null)
            {
                _playerVisuals = GetComponent<PlayerVisuals>();
                if (_playerVisuals == null) _playerVisuals = GetComponentInChildren<PlayerVisuals>();
                if (_playerVisuals == null && _playerTransform != null) _playerVisuals = _playerTransform.GetComponentInChildren<PlayerVisuals>();
            }
        }

        private bool TryGetNeedleTipPosition(out Vector2 tipPos)
        {
            EnsurePlayerComponents();
            if (_playerVisuals != null && _playerVisuals.TryGetNeedleTipPosition(out tipPos))
            {
                return true;
            }

            tipPos = Vector2.zero;
            return false;
        }

        private bool TryGetNeedleHandPosition(out Vector2 handPos)
        {
            EnsurePlayerComponents();
            if (_playerVisuals != null && _playerVisuals.TryGetNeedleHandPosition(out handPos))
            {
                return true;
            }

            handPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : Vector2.zero;
            return false;
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
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_aim_needle_enter");
            }

            Vector2 playerHandPos;
            if (!TryGetNeedleHandPosition(out playerHandPos))
            {
                playerHandPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : _activeTarget.CurrentPosition;
            }
            _activeTarget.UpdateAimLeash(playerHandPos, MaxAimLeashDistance);

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
                    TriggerThreadSeveredSound(); OnThreadSevered?.Invoke();
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
            if (_activeTarget != null && _currentlyLoopingTarget != _activeTarget && _tetheredStitchable != _activeTarget)
            {
                _activeTarget.CutThread();
            }

            _isPinning = false;
            _activeTarget = null;
            _pinnedWaypoints.Clear();
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlaySFX("sfx_aim_cancel");
            }

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
            SetReticleActive(false);
            UpdateAnchorPointsFeedback(false, Vector2.zero, null);
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

                if (PlayerController2D.IsPlayer(col))
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
                if (PlayerController2D.IsPlayer(col))
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
            if (!IsAiming || _previewLine == null)
            {
                if (_previewLine != null && _previewLine.positionCount != 0)
                {
                    _previewLine.positionCount = 0;
                }
                UpdateAnchorPointsFeedback(false, Vector2.zero, null);
                SetReticleActive(false);
                return;
            }

            // ซ่อนเส้นเล็งเมื่อมีเข็มกำลังพุ่งในอากาศ
            if (IsAnyNeedleInFlight)
            {
                _previewLine.positionCount = 0;
                UpdateAnchorPointsFeedback(false, Vector2.zero, null);
                SetReticleActive(false);
                return;
            }

            if (_isPinning && _pinnedWaypoints.Count > 0)
            {
                UpdatePinningPreviewVisual(worldAimPos);
            }
            else if (_isCutAiming)
            {
                UpdateCutAimPreviewVisual(worldAimPos);
            }
        }

        private void UpdatePinningPreviewVisual(Vector3 worldAimPos)
        {
            Vector2 launchOrigin = _pinnedWaypoints[0];
            Vector2 aimLineStart = launchOrigin;
            if (TryGetNeedleTipPosition(out Vector2 tipPos))
            {
                aimLineStart = tipPos;
            }

            Vector2 aimVector = (Vector2)worldAimPos - aimLineStart;
            float aimDist = aimVector.magnitude;
            Vector2 aimDir = aimDist > 0.001f ? aimVector / aimDist : Vector2.right;

            Vector2 rayOrigin = aimLineStart;
            float maxCastDist = Mathf.Max(0.1f, _maxThreadLength);

            bool hasValidHit = TryCastNeedleTrajectory(rayOrigin, aimDir, maxCastDist, out RaycastHit2D hit, out NeedleAnchorPoint hitAnchor);
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
                    bool hasLOS = HasLineOfSight(launchOrigin, endPoint, _activeTarget?.ObjectTransform, hitAnchor);
                    canPlace = IsWithinThreadBudget(totalDist) && hasLOS;
                }
            }
            else
            {
                endPoint = aimLineStart + aimDir * Mathf.Min(aimDist, _maxThreadLength);
            }

            UpdateAnchorPointsFeedback(true, launchOrigin, canPlace ? hitAnchor : null);

            Color activeColor = canPlace ? s_ValidColor : s_InvalidColor;
            SetReticleColor(activeColor);
            DrawAimTrajectory(aimLineStart, endPoint, activeColor);
            UpdateAimReticleVisual(endPoint, canPlace, activeColor);
        }

        private void UpdateCutAimPreviewVisual(Vector3 worldAimPos)
        {
            UpdateAnchorPointsFeedback(false, Vector2.zero, null);

            EnsurePlayerComponents();
            Vector2 playerHandPos;
            if (!TryGetNeedleHandPosition(out playerHandPos))
            {
                playerHandPos = _playerTransform != null ? (Vector2)_playerTransform.position + new Vector2(0f, 0.35f) : (Vector2)transform.position;
            }

            Vector2 aimLineStart = playerHandPos;
            if (TryGetNeedleTipPosition(out Vector2 tipPos))
            {
                aimLineStart = tipPos;
            }

            Vector2 aimVector = (Vector2)worldAimPos - aimLineStart;
            float aimDist = aimVector.magnitude;
            Vector2 aimDir = aimDist > 0.001f ? aimVector / aimDist : Vector2.right;

            float maxCastDist = Mathf.Max(0.1f, _maxThreadLength);

            // หาจุดชนสิ่งกีดขวางทึบ
            int hitCount = Physics2D.CircleCast(aimLineStart, 0.06f, aimDir, _anchorFilter, _rayHitBuffer, maxCastDist);
            Vector2 obstacleHitPoint = aimLineStart + aimDir * maxCastDist;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _rayHitBuffer[i];
                Collider2D col = hit.collider;
                if (col == null || col == _playerCol || col.isTrigger) continue;
                if (_playerTransform != null && (col.transform == _playerTransform || col.transform.IsChildOf(_playerTransform))) continue;

                obstacleHitPoint = hit.point;
                break;
            }

            // ปลายเส้นเล็งจำกัดไม่เกินระยะเมาส์หรือสิ่งกีดขวาง
            float distToTarget = Mathf.Min(aimDist, Vector2.Distance(aimLineStart, obstacleHitPoint));
            Vector2 endPoint = aimLineStart + aimDir * distToTarget;

            // ตรวจสอบว่าตลอดแนววิถีกระสุนจริงพาดผ่านเส้นด้ายหรือไม่
            bool willIntersectThread = SutureThread.AnyThreadIntersectsSegment(playerHandPos, obstacleHitPoint, _cutHitRadius);

            Color activeColor = willIntersectThread ? s_ValidColor : s_CutAimIdleColor;
            SetReticleColor(activeColor);
            DrawAimTrajectory(aimLineStart, endPoint, activeColor);
            UpdateAimReticleVisual(endPoint, willIntersectThread, activeColor);
        }

        private void EnsureReticle()
        {
            if (_reticleTransform == null)
            {
                Transform found = transform.Find("AimReticle");
                if (found == null && _playerTransform != null)
                {
                    found = _playerTransform.Find("AimReticle");
                }

                if (found != null)
                {
                    _reticleTransform = found;
                }
            }

            if (_reticleTransform != null)
            {
                if (_reticleSprite == null) _reticleSprite = _reticleTransform.GetComponent<SpriteRenderer>();
                if (_reticleRingSprite == null) _reticleRingSprite = _reticleTransform.Find("EyeletRing")?.GetComponent<SpriteRenderer>();
                if (_reticleDotSprite == null) _reticleDotSprite = _reticleTransform.Find("PinDot")?.GetComponent<SpriteRenderer>();
                _reticleBaseScale = _reticleTransform.localScale;
            }
        }

        private void SetReticleActive(bool active)
        {
            if (_reticleTransform != null && _reticleTransform.gameObject.activeSelf != active)
            {
                _reticleTransform.gameObject.SetActive(active);
            }
        }

        private void UpdateAimReticleVisual(Vector2 position, bool canPlace, Color activeColor)
        {
            EnsureReticle();
            if (_reticleTransform == null) return;

            SetReticleActive(true);
            _reticleTransform.position = new Vector3(position.x, position.y, 0f);

            if (_deniedFlashTimer > 0f)
            {
                activeColor = s_InvalidColor;
                canPlace = false;
            }

            Color ringColor = canPlace ? activeColor : new Color(activeColor.r, activeColor.g, activeColor.b, 0.35f);
            Color dotColor = canPlace ? activeColor : new Color(activeColor.r, activeColor.g, activeColor.b, 0.7f);

            if (_reticleSprite != null)
            {
                _reticleSprite.color = activeColor;
            }
            if (_reticleRingSprite != null)
            {
                _reticleRingSprite.color = ringColor;
            }
            if (_reticleDotSprite != null)
            {
                _reticleDotSprite.color = dotColor;
            }

            if (_reticleRingSprite != null)
            {
                Transform ringTransform = _reticleRingSprite.transform;
                if (canPlace)
                {
                    float pulse = 1f + Mathf.Sin(Time.time * _reticlePulseSpeed) * _reticlePulseScale;
                    ringTransform.localScale = Vector3.one * pulse;
                    ringTransform.localRotation = Quaternion.Euler(0f, 0f, Time.time * 45f);
                }
                else
                {
                    ringTransform.localScale = Vector3.one * 0.75f;
                    ringTransform.localRotation = Quaternion.identity;
                }
            }
            else if (_reticleTransform != null && canPlace)
            {
                float pulse = 1f + Mathf.Sin(Time.time * _reticlePulseSpeed) * _reticlePulseScale;
                _reticleTransform.localScale = _reticleBaseScale * pulse;
            }
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
                _previewMaterial.SetFloat(SutureVisualUtils.TilingId, 4.0f);
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
            if (_reticleRingSprite != null)
            {
                _reticleRingSprite.color = s_InvalidColor;
            }
            if (_reticleDotSprite != null)
            {
                _reticleDotSprite.color = s_InvalidColor;
            }
        }

        private void OnDisable()
        {
            if (_isCutAiming)
            {
                ResetCutAimState();
            }
        }

        private void OnDestroy()
        {
            if (_reticleTransform != null) _reticleTransform.DOKill();
            if (_reticleSprite != null) _reticleSprite.DOKill();
            if (_reticleRingSprite != null) _reticleRingSprite.transform.DOKill();
            if (_previewMaterial != null)
            {
                Destroy(_previewMaterial);
            }
            if (_needleProjectile != null && _needleProjectile.gameObject != null && _needleProjectile.gameObject.scene.IsValid())
            {
                Destroy(_needleProjectile.gameObject);
            }
            if (_cutNeedleProjectile != null && _cutNeedleProjectile.gameObject != null && _cutNeedleProjectile.gameObject.scene.IsValid())
            {
                Destroy(_cutNeedleProjectile.gameObject);
            }
            if (_cutNeedleThread != null && _cutNeedleThread.gameObject != null && _cutNeedleThread.gameObject.scene.IsValid())
            {
                Destroy(_cutNeedleThread.gameObject);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG")]
        private static void Log(string message)
        {
            Debug.Log(message);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEBUG")]
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