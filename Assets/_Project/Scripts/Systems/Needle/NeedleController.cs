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
    /// - Max thread length budget (default: 9.0 meters)
    /// - Surface validation (cannot pin in thin air; must anchor to physical walls/ground/objects)
    /// </summary>
    public sealed class NeedleController : MonoBehaviour
    {
        private const float k_MinWaypointDistance = 0.4f;
        private const float k_AutoPinDistance = 0.5f;
        private const float k_HitSearchRadius = 0.45f;
        private const float k_PlayerAimRadius = 1.25f;

        private static readonly Color s_ValidColor = new Color(0.95f, 0.95f, 1f, 0.95f);
        private static readonly Color s_PlayerTetherColor = new Color(0.4f, 0.95f, 1f, 0.95f);
        private static readonly Color s_InvalidColor = new Color(1f, 0.35f, 0.35f, 0.9f);

        [Header("Constraints & Budget")]
        [Tooltip("Maximum allowed total thread length across all segments.")]
        [SerializeField] private float _maxThreadLength = 9.5f;

        [Tooltip("Maximum number of waypoints per stitch (e.g. 3 = Start -> Pivot -> End).")]
        [FormerlySerializedAs("maxWaypoints")]
        [SerializeField] private int _maxWaypoints = 3;

        [Tooltip("Whether pins must snap to a physical surface (prevents stitching in mid-air).")]
        [SerializeField] private bool _requireSurfaceToPin = true;

        [Tooltip("When true, completing a new circuit severs the previous circuit (Faustian sacrifice).")]
        [SerializeField] private bool _singleCircuitLimit = true;

        [Header("Layer Masks")]
        [FormerlySerializedAs("stitchableLayer")]
        [SerializeField] private LayerMask _stitchableLayer;

        [Tooltip("Surfaces where needles can anchor (Walls, Ceilings, Ground, Platforms).")]
        [SerializeField] private LayerMask _anchorSurfaceLayer;

        [Header("Aiming & Visuals")]
        [FormerlySerializedAs("previewLine")]
        [SerializeField] private LineRenderer _previewLine;

        [FormerlySerializedAs("mainCamera")]
        [SerializeField] private Camera _mainCamera;

        [FormerlySerializedAs("reticleTransform")]
        [SerializeField] private Transform _reticleTransform;

        private Transform _playerTransform;
        private Rigidbody2D _playerRb;
        private PlayerController2D _playerController;
        private IStitchable _activeTarget;
        private IStitchable _currentlyLoopingTarget;
        private IStitchable _tetheredStitchable;
        private readonly List<Vector2> _pinnedWaypoints = new List<Vector2>();
        private readonly List<Vector3> _previewPointsBuffer = new List<Vector3>(32);
        private bool _isPinning;
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

            // Ensure layer masks are never empty
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

            if (_previewLine == null)
            {
                _previewLine = GetComponent<LineRenderer>();
                if (_previewLine == null)
                {
                    _previewLine = gameObject.AddComponent<LineRenderer>();
                }
            }

            ConfigurePreviewLine();
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
                _previewMaterial.SetColor("_BaseColor", s_ValidColor);
                _previewMaterial.SetFloat("_ScrollSpeed", 3.2f);
                _previewLine.material = _previewMaterial;
            }
        }

        private void Update()
        {
            Vector3 worldAimPos = GetAimWorldPosition();
            UpdateReticle(worldAimPos);
            HandleInput(worldAimPos);
            UpdatePreviewVisual(worldAimPos);
        }

        public Vector3 GetAimWorldPosition()
        {
            if (_mainCamera == null || Mouse.current == null) return transform.position;

            Vector2 mouseScreenPos = Mouse.current.position.ReadValue();
            Vector3 worldPos = _mainCamera.ScreenToWorldPoint(new Vector3(mouseScreenPos.x, mouseScreenPos.y, -_mainCamera.transform.position.z));
            worldPos.z = 0f;
            return worldPos;
        }

        private void UpdateReticle(Vector3 worldAimPos)
        {
            if (_reticleTransform != null)
            {
                _reticleTransform.position = worldAimPos;
            }
        }

        private void HandleInput(Vector3 worldAimPos)
        {
            if (Mouse.current != null)
            {
                // Primary Click (Left): Select object / Pin waypoint / Tether to self / Anchor
                if (Mouse.current.leftButton.wasPressedThisFrame)
                {
                    OnPrimaryClick(worldAimPos);
                }

                // Secondary Click (Right): Cancel pinning, untether from self, or sever thread
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
                // If already tethered to player, clicking on an anchor surface transfers it to a wall!
                if (_tetheredStitchable != null)
                {
                    if (TryGetValidAnchorPoint(worldAimPos, out Vector2 anchorPos) && !IsAimingAtPlayer(worldAimPos))
                    {
                        TransferTetherToAnchor(anchorPos);
                        return;
                    }
                    else if (IsAimingAtPlayer(worldAimPos) || FindStitchableAt(worldAimPos) == _tetheredStitchable)
                    {
                        // Clicking on player or the burden again untethers
                        ReleasePlayerTether();
                        return;
                    }
                }

                IStitchable stitchable = FindStitchableAt(worldAimPos);
                if (stitchable != null)
                {
                    StartStitching(stitchable);
                }
            }
            else
            {
                // 1. Check if clicking on Player to tether object to self!
                if (IsAimingAtPlayer(worldAimPos))
                {
                    StartPlayerTether(_activeTarget);
                    return;
                }

                // 2. Check if clicking on original target to complete a loop
                IStitchable clickedTarget = FindStitchableAt(worldAimPos);
                if (clickedTarget != null && clickedTarget == _activeTarget && _pinnedWaypoints.Count >= 2)
                {
                    CompleteStitchingRail();
                    return;
                }

                // 3. Surface Validation (Cannot pin in empty air)
                if (!TryGetValidAnchorPoint(worldAimPos, out Vector2 anchorPos))
                {
                    LogWarning("[Needle] Cannot pin in empty air! Must pin to a wall, ceiling, ground, or yourself.");
                    return;
                }

                // 4. Thread Length Budget Check
                Vector2 lastPoint = _pinnedWaypoints[_pinnedWaypoints.Count - 1];
                float segDist = Vector2.Distance(lastPoint, anchorPos);

                if (segDist < k_MinWaypointDistance) return;

                if (segDist > RemainingThreadLength)
                {
                    LogWarning("[Needle] Out of thread! Cannot reach that far.");
                    return;
                }

                // Add waypoint
                _pinnedWaypoints.Add(anchorPos);
                Log($"[Needle] Added Waypoint #{_pinnedWaypoints.Count} at {anchorPos} (Remaining thread: {RemainingThreadLength:F1}m)");

                CameraController2D.Instance?.Punch(-(anchorPos - lastPoint).normalized, 0.035f);

                if (_activeTarget != null)
                {
                    _activeTarget.UpdatePinnedWaypoints(_pinnedWaypoints);
                }

                if (_pinnedWaypoints.Count >= _maxWaypoints)
                {
                    CompleteStitchingRail();
                }
            }
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
                // Check if clicking near any active suture thread to sever it!
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

            // Single Circuit Limit: sever any previous loop circuit
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
                if (_activeTarget != null)
                {
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

            if (_pinnedWaypoints.Count > 0)
            {
                if (TryGetValidAnchorPoint(worldAimPos, out Vector2 anchorPos))
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

        private bool TryGetValidAnchorPoint(Vector3 worldAimPos, out Vector2 anchorPoint)
        {
            anchorPoint = worldAimPos;

            // Player body is always a valid anchor/tether target
            if (IsAimingAtPlayer(worldAimPos))
            {
                if (_playerTransform != null) anchorPoint = _playerTransform.position;
                return true;
            }

            if (!_requireSurfaceToPin) return true;

            // 1. Check if clicking on an IStitchable object
            if (FindStitchableAt(worldAimPos) != null)
            {
                return true;
            }

            // 2. Check overlap with physical surface (Walls, Ceilings, Ground)
            Collider2D hit = Physics2D.OverlapCircle(worldAimPos, k_HitSearchRadius, _anchorSurfaceLayer);
            if (hit != null)
            {
                // Snap to closest point on collider
                anchorPoint = hit.ClosestPoint(worldAimPos);
                return true;
            }

            return false;
        }

        private void EnsurePlayerComponents()
        {
            if (_playerTransform == null) _playerTransform = transform;
            if (_playerRb == null) _playerRb = GetComponent<Rigidbody2D>();
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
            Collider2D hit = Physics2D.OverlapCircle(worldAimPos, k_HitSearchRadius, _stitchableLayer);
            if (hit != null)
            {
                IStitchable stitchable = hit.GetComponentInParent<IStitchable>();
                if (stitchable != null) return stitchable;
            }

            Collider2D[] allHits = Physics2D.OverlapCircleAll(worldAimPos, k_HitSearchRadius);
            for (int i = 0; i < allHits.Length; i++)
            {
                IStitchable stitchable = allHits[i].GetComponentInParent<IStitchable>();
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
                // Sever previous loop circuit if stitching a new entity! (Single Circuit Rule / Faustian Sacrifice)
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
                if (_previewLine != null) _previewLine.positionCount = 0;
                return;
            }

            Vector2 lastPoint = _pinnedWaypoints[_pinnedWaypoints.Count - 1];
            Vector2 cursor = (Vector2)worldAimPos;
            Vector2 dir = cursor - lastPoint;
            float rawDist = dir.magnitude;

            // 1. Thread Budget Clamp
            float remainingBudget = RemainingThreadLength;
            float clampedDist = Mathf.Min(rawDist, remainingBudget);
            Vector2 clampedTarget = rawDist > 0.001f ? lastPoint + (dir / rawDist) * clampedDist : lastPoint;

            bool isAimingPlayer = IsAimingAtPlayer(worldAimPos);
            if (isAimingPlayer && _playerTransform != null)
            {
                clampedTarget = _playerTransform.position;
                clampedDist = Vector2.Distance(lastPoint, clampedTarget);
            }

            // 2. Surface Validation Check
            bool isValidSurface = isAimingPlayer || TryGetValidAnchorPoint(clampedTarget, out Vector2 anchorPos);
            bool isOverBudget = rawDist > remainingBudget + 0.1f;
            bool canPlace = isValidSurface && (!isOverBudget || isAimingPlayer);

            // Tint feedback
            Color activeColor = isAimingPlayer ? s_PlayerTetherColor : (canPlace ? s_ValidColor : s_InvalidColor);
            if (_reticleSprite != null)
            {
                _reticleSprite.color = activeColor;
            }
            if (_previewMaterial != null)
            {
                _previewMaterial.SetColor("_BaseColor", activeColor);
            }

            // Draw curved preview arc
            float sag = Mathf.Min(clampedDist * 0.08f, 0.45f);
            Vector2 midControl = (lastPoint + clampedTarget) * 0.5f + (Vector2.down * sag);

            const int steps = 20;
            _previewPointsBuffer.Clear();

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float u = 1f - t;
                Vector2 pt = (u * u * lastPoint) + (2f * u * t * midControl) + (t * t * clampedTarget);
                _previewPointsBuffer.Add(new Vector3(pt.x, pt.y, 0f));
            }

            _previewLine.positionCount = _previewPointsBuffer.Count;
            for (int i = 0; i < _previewPointsBuffer.Count; i++)
            {
                _previewLine.SetPosition(i, _previewPointsBuffer[i]);
            }

            if (_previewMaterial != null)
            {
                _previewMaterial.SetFloat("_Tiling", Mathf.Max(1f, clampedDist * 2.5f));
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
