using System;
using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Operating cycle states for the corpse gas eruption vent.
    /// </summary>
    public enum GasVentState
    {
        Dormant = 0,
        Telegraph = 1,
        Erupting = 2
    }

    /// <summary>
    /// Periodic corpse gas eruption vent located on the giant rotting fish carcass.
    /// In the Erupting state, it applies a buoyant vertical updraft to any Rigidbody2D
    /// (such as the Stitchable Crate and the Player) inside its trigger zone, allowing the
    /// player to time their jump and clear the 2.4m obstacle wall.
    ///
    /// Dynamic Capping & Occlusion Logic:
    /// - Checks if a StitchableCrate covers the vent nozzle.
    /// - If blocked and untethered: upward acceleration is reduced below gravity so the crate seals
    ///   the hole with only a gentle pressure rumble, and the visual eruption height is clamped to 0.
    /// - If blocked but tethered to player: full normal lift acceleration is applied, lifting the
    ///   crate smoothly as an elevator while the gas column dynamically expands beneath it.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CorpseGasVent2D : MonoBehaviour
    {
        private const float k_MinDuration = 0.1f;

        [Header("Timing Cycles (Seconds)")]
        [Tooltip("Duration of the dormant / quiet state where no gas is vented.")]
        [SerializeField] private float _dormantDuration = 2.6f;

        [Tooltip("Duration of the warning telegraph (hissing, swelling, faint gas) before full eruption.")]
        [SerializeField] private float _telegraphDuration = 0.85f;

        [Tooltip("Duration of the powerful gas eruption geyser.")]
        [SerializeField] private float _eruptionDuration = 2.2f;

        [Header("Updraft Physics (Mass-Normalized)")]
        [Tooltip("Upward acceleration applied to objects inside the gas column (m/s²). Multiplied by rb.mass for uniform lift across all mass values.")]
        [SerializeField] private float _upwardAcceleration = 28f;

        [Tooltip("Maximum upward vertical velocity clamped while inside the gas updraft.")]
        [SerializeField] private float _maxUpwardVelocity = 5.5f;

        [Tooltip("Reduced upward acceleration applied when the vent is capped by an untethered StitchableCrate (less than gravity ~14.7 to prevent liftoff).")]
        [SerializeField] private float _blockedUpwardAcceleration = 4f;

        [Tooltip("Reduced maximum upward velocity when capped by an untethered StitchableCrate.")]
        [SerializeField] private float _blockedMaxUpwardVelocity = 0.5f;

        [Header("Visuals & Lighting")]
        [Tooltip("Visual object displayed during warning telegraph (faint smoke/hiss).")]
        [SerializeField] private GameObject _telegraphVisual;

        [Tooltip("Visual column object displayed during full eruption.")]
        [SerializeField] private GameObject _eruptionVisual;

        [Tooltip("Optional 2D light that intensifies during eruption.")]
        [SerializeField] private UnityEngine.Rendering.Universal.Light2D _ventLight;

        [SerializeField] private float _dimLightIntensity = 0.35f;
        [SerializeField] private float _brightLightIntensity = 1.25f;

        [Header("Gas Animation")]
        [Tooltip("Sequential frames for the animated corpse gas eruption.")]
        [SerializeField] private Sprite[] _gasFrames;

        [Tooltip("Playback speed of the gas eruption animation in frames per second.")]
        [SerializeField] private float _animationFps = 24f;

        [Tooltip("Speed multiplier during the warning telegraph phase.")]
        [SerializeField] private float _telegraphFpsMultiplier = 0.5f;

        [Header("Player Boost Settings")]
        [Tooltip("Maximum vertical velocity allowed for player inside the vent (permits high Super Jump arcs without clamping down).")]
        [SerializeField] private float _playerMaxUpwardVelocity = 22f;

        private readonly List<Rigidbody2D> _trackedBodies = new(8);

        public static CorpseGasVent2D Instance { get; private set; }

        private Collider2D _ventCollider;
        private SpriteRenderer _eruptionSprite;
        private SpriteRenderer _telegraphSprite;
        private Vector3 _defaultEruptionLocalScale = Vector3.one;
        private float _defaultEruptionHeight = 2.5f;
        private Vector3 _defaultEruptionLocalPos;
        private bool _hasDefaultEruptionValues;
        private float _animTimer = 0f;

        private GasVentState _currentState = GasVentState.Dormant;
        private float _stateTimer = 0f;

        public GasVentState CurrentState => _currentState;
        public bool IsErupting => _currentState == GasVentState.Erupting;

        public event Action<GasVentState> OnStateChanged;

        private void Awake()
        {
            Instance = this;

            _ventCollider = GetComponent<Collider2D>();
            if (_ventCollider != null)
            {
                _ventCollider.isTrigger = true;
                _defaultEruptionHeight = _ventCollider.bounds.size.y > 0.1f ? _ventCollider.bounds.size.y : 2.5f;
            }

            if (_eruptionVisual != null)
            {
                _eruptionSprite = _eruptionVisual.GetComponent<SpriteRenderer>();
                if (_eruptionSprite != null)
                {
                    _eruptionSprite.drawMode = SpriteDrawMode.Simple;
                    // Enforce sorting order behind crates so solid objects render cleanly in front
                    _eruptionSprite.sortingOrder = Mathf.Min(_eruptionSprite.sortingOrder, 2);
                }
                _defaultEruptionLocalPos = _eruptionVisual.transform.localPosition;
                _defaultEruptionLocalScale = _eruptionVisual.transform.localScale;
                _hasDefaultEruptionValues = true;
            }

            if (_telegraphVisual != null)
            {
                _telegraphSprite = _telegraphVisual.GetComponent<SpriteRenderer>();
                if (_telegraphSprite != null)
                {
                    _telegraphSprite.drawMode = SpriteDrawMode.Simple;
                    _telegraphSprite.sortingOrder = Mathf.Min(_telegraphSprite.sortingOrder, 2);
                }
            }

            SetState(GasVentState.Dormant);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            if (_ventCollider != null)
            {
                ContactFilter2D filter = new ContactFilter2D();
                filter.useTriggers = false;
                List<Collider2D> results = new List<Collider2D>();
                if (_ventCollider.Overlap(filter, results) > 0)
                {
                    foreach (var col in results)
                    {
                        if (col.attachedRigidbody != null && !_trackedBodies.Contains(col.attachedRigidbody))
                        {
                            _trackedBodies.Add(col.attachedRigidbody);
                        }
                    }
                }
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            _stateTimer -= dt;

            if (_stateTimer <= 0f)
            {
                AdvanceToNextState();
            }

            UpdateVisuals(dt);
        }

        private void FixedUpdate()
        {
            if (_currentState != GasVentState.Erupting) return;

            StitchableObject blockingCrate = FindBlockingCrate(out bool isTetheredToPlayer);
            bool isBlocked = blockingCrate != null;

            // ถ้ามีกล่องปิดรูพ่น แต่กล่องกำลังผูกสายจูงกับผู้เล่น (Tethered) -> ให้ทำแรงปกติได้เลย
            // ถ้ามีกล่องปิดรูพ่น และยังไม่ได้ผูกกับผู้เล่น -> ใช้แรงต่ำ (Blocked Updraft)
            // ถ้าไม่มีกล่องปิด -> ใช้แรงปกติ
            bool useNormalLift = !isBlocked || isTetheredToPlayer;
            float targetAcceleration = useNormalLift ? _upwardAcceleration : _blockedUpwardAcceleration;
            float maxVelocity = useNormalLift ? _maxUpwardVelocity : _blockedMaxUpwardVelocity;

            for (int i = _trackedBodies.Count - 1; i >= 0; i--)
            {
                Rigidbody2D rb = _trackedBodies[i];
                if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
                {
                    _trackedBodies.RemoveAt(i);
                    continue;
                }

                // หากกล่องยังไม่ได้ผูกและปิดรูพ่นอยู่ วัตถุอื่นที่อยู่เหนือกล่องจะถูกบังจากแรงพ่น
                if (isBlocked && !useNormalLift && rb.gameObject != blockingCrate.gameObject)
                {
                    continue;
                }

                bool isPlayer = PlayerController2D.IsPlayer(rb);
                bool hasSuperBoost = false;
                if (isPlayer)
                {
                    PlayerController2D pc = rb.GetComponent<PlayerController2D>() ?? rb.GetComponentInParent<PlayerController2D>() ?? PlayerController2D.Instance;
                    hasSuperBoost = pc != null && pc.IsVentSuperJumping;
                }
                float currentMaxVel = hasSuperBoost ? _playerMaxUpwardVelocity : maxVelocity;

                if (rb.linearVelocity.y < currentMaxVel)
                {
                    // F = m * a (Mass-Normalized Acceleration)
                    // ทำให้ทั้งผู้เล่น (mass 1) และกล่อง (mass 2 หรือ 20) ได้รับความเร่งที่แม่นยำสม่ำเสมอ
                    rb.AddForce(Vector2.up * (targetAcceleration * rb.mass), ForceMode2D.Force);

                    if (rb.linearVelocity.y > currentMaxVel)
                    {
                        rb.linearVelocity = new Vector2(rb.linearVelocity.x, currentMaxVel);
                    }
                }
                else if (!hasSuperBoost && rb.linearVelocity.y > currentMaxVel)
                {
                    // หากไม่เข้าเงื่อนไข Super Jump Boost ความเร็วลอยตัวขึ้นจะถูกตัดจำกัดไว้ที่ maxVelocity (5.5 m/s) เหมือนเดิม
                    rb.linearVelocity = new Vector2(rb.linearVelocity.x, currentMaxVel);
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other.isTrigger) return;

            Rigidbody2D rb = other.attachedRigidbody;
            if (rb != null && !_trackedBodies.Contains(rb))
            {
                _trackedBodies.Add(rb);
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            Rigidbody2D rb = other.attachedRigidbody;
            if (rb != null)
            {
                _trackedBodies.Remove(rb);
            }
        }

        /// <summary>
        /// Scans tracked bodies to find any StitchableCrate covering the vent nozzle.
        /// </summary>
        private StitchableObject FindBlockingCrate(out bool isTetheredToPlayer)
        {
            isTetheredToPlayer = false;
            if (_ventCollider == null) return null;

            Bounds ventBounds = _ventCollider.bounds;
            float ventCenterX = ventBounds.center.x;

            StitchableObject lowestBlockingCrate = null;
            float lowestBottomY = float.MaxValue;

            for (int i = 0; i < _trackedBodies.Count; i++)
            {
                Rigidbody2D rb = _trackedBodies[i];
                if (rb == null) continue;

                StitchableObject stitchable = rb.GetComponent<StitchableObject>();
                if (stitchable == null) continue;

                Collider2D crateCol = stitchable.Collider != null ? stitchable.Collider : rb.GetComponent<Collider2D>();
                if (crateCol == null) continue;

                Bounds crateBounds = crateCol.bounds;

                // ตรวจสอบว่ากล่องวางทับแนวรูพ่นในแนวนอน (Horizontal Overlap with nozzle)
                bool coversVentX = Mathf.Abs(crateBounds.center.x - ventCenterX) <= (crateBounds.extents.x + 0.35f);
                if (!coversVentX) continue;

                if (crateBounds.min.y < lowestBottomY)
                {
                    lowestBottomY = crateBounds.min.y;
                    lowestBlockingCrate = stitchable;
                }
            }

            if (lowestBlockingCrate != null)
            {
                isTetheredToPlayer = lowestBlockingCrate.IsTetheredToPlayer;
            }

            return lowestBlockingCrate;
        }

        /// <summary>
        /// Returns true if the specified GameObject has a Rigidbody2D currently within the vent trigger.
        /// </summary>
        public bool IsObjectInVent(GameObject obj)
        {
            if (obj == null) return false;
            for (int i = 0; i < _trackedBodies.Count; i++)
            {
                if (_trackedBodies[i] != null && (_trackedBodies[i].gameObject == obj || obj.transform.IsChildOf(_trackedBodies[i].transform) || _trackedBodies[i].transform.IsChildOf(obj.transform)))
                    return true;
            }
            if (_ventCollider != null)
            {
                Collider2D col = obj.GetComponentInChildren<Collider2D>();
                if (col != null && _ventCollider.bounds.Intersects(col.bounds))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if the player is currently inside the vent trigger.
        /// </summary>
        public bool IsPlayerInVent
        {
            get
            {
                for (int i = 0; i < _trackedBodies.Count; i++)
                {
                    if (_trackedBodies[i] != null && PlayerController2D.IsPlayer(_trackedBodies[i]))
                        return true;
                }
                return false;
            }
        }

        private void AdvanceToNextState()
        {
            switch (_currentState)
            {
                case GasVentState.Dormant:
                    SetState(GasVentState.Telegraph);
                    break;

                case GasVentState.Telegraph:
                    SetState(GasVentState.Erupting);
                    break;

                case GasVentState.Erupting:
                    SetState(GasVentState.Dormant);
                    break;
            }
        }

        private void SetState(GasVentState newState)
        {
            _currentState = newState;
            _animTimer = 0f;

            switch (newState)
            {
                case GasVentState.Dormant:
                    _stateTimer = Mathf.Max(k_MinDuration, _dormantDuration);
                    if (_telegraphVisual != null) _telegraphVisual.SetActive(false);
                    if (_eruptionVisual != null)
                    {
                        _eruptionVisual.SetActive(false);
                        if (_hasDefaultEruptionValues)
                        {
                            _eruptionVisual.transform.localScale = _defaultEruptionLocalScale;
                            _eruptionVisual.transform.localPosition = _defaultEruptionLocalPos;
                        }
                    }
                    if (_ventLight != null) _ventLight.intensity = _dimLightIntensity;
                    break;

                case GasVentState.Telegraph:
                    _stateTimer = Mathf.Max(k_MinDuration, _telegraphDuration);
                    if (_telegraphVisual != null) _telegraphVisual.SetActive(true);
                    if (_eruptionVisual != null) _eruptionVisual.SetActive(false);
                    if (_ventLight != null) _ventLight.intensity = (_dimLightIntensity + _brightLightIntensity) * 0.5f;
                    break;

                case GasVentState.Erupting:
                    _stateTimer = Mathf.Max(k_MinDuration, _eruptionDuration);
                    if (_telegraphVisual != null) _telegraphVisual.SetActive(false);
                    if (_eruptionVisual != null) _eruptionVisual.SetActive(true);
                    if (_ventLight != null) _ventLight.intensity = _brightLightIntensity;
                    break;
            }

            OnStateChanged?.Invoke(_currentState);
        }

        private void UpdateVisuals(float dt)
        {
            _animTimer += dt;

            if (_currentState == GasVentState.Telegraph && _telegraphVisual != null)
            {
                float pulse = 0.9f + Mathf.PingPong(Time.time * 6f, 0.2f);
                _telegraphVisual.transform.localScale = new Vector3(pulse, pulse, 1f);

                if (_telegraphSprite != null && _gasFrames != null && _gasFrames.Length > 0)
                {
                    int frame = (int)(_animTimer * (_animationFps * _telegraphFpsMultiplier)) % Mathf.Min(8, _gasFrames.Length);
                    _telegraphSprite.sprite = _gasFrames[frame];
                }
            }
            else if (_currentState == GasVentState.Erupting)
            {
                if (_eruptionSprite != null && _gasFrames != null && _gasFrames.Length > 0)
                {
                    int frame = (int)(_animTimer * _animationFps) % _gasFrames.Length;
                    _eruptionSprite.sprite = _gasFrames[frame];
                }

                StitchableObject blockingCrate = FindBlockingCrate(out bool isTetheredToPlayer);
                bool isBlocked = blockingCrate != null;

                if (_hasDefaultEruptionValues && _eruptionVisual != null)
                {
                    if (isBlocked)
                    {
                        Collider2D crateCol = blockingCrate.Collider != null ? blockingCrate.Collider : blockingCrate.GetComponent<Collider2D>();
                        float crateBottomY = crateCol != null ? crateCol.bounds.min.y : blockingCrate.transform.position.y - 0.5f;

                        // World position of the nozzle bottom
                        float nozzleWorldBottom = transform.position.y + (_defaultEruptionLocalPos.y - _defaultEruptionHeight * 0.5f);
                        float availableHeight = Mathf.Clamp(crateBottomY - nozzleWorldBottom, 0f, _defaultEruptionHeight);

                        // If the crate is resting on the nozzle, clamped height is 0 (gas does not penetrate)
                        if (availableHeight <= 0.08f)
                        {
                            _eruptionVisual.SetActive(false);
                        }
                        else
                        {
                            _eruptionVisual.SetActive(true);
                            float scaleY = Mathf.Clamp01(availableHeight / _defaultEruptionHeight);
                            _eruptionVisual.transform.localScale = new Vector3(
                                _defaultEruptionLocalScale.x,
                                _defaultEruptionLocalScale.y * scaleY,
                                _defaultEruptionLocalScale.z
                            );
                            float localBottom = _defaultEruptionLocalPos.y - _defaultEruptionHeight * 0.5f;
                            _eruptionVisual.transform.localPosition = new Vector3(
                                _defaultEruptionLocalPos.x,
                                localBottom + availableHeight * 0.5f,
                                _defaultEruptionLocalPos.z
                            );
                        }
                    }
                    else
                    {
                        _eruptionVisual.SetActive(true);
                        _eruptionVisual.transform.localScale = _defaultEruptionLocalScale;
                        _eruptionVisual.transform.localPosition = _defaultEruptionLocalPos;
                    }
                }

                if (_ventLight != null)
                {
                    bool isMuffled = isBlocked && !isTetheredToPlayer;
                    float targetIntensity = isMuffled ? _dimLightIntensity * 1.5f : _brightLightIntensity;
                    _ventLight.intensity = Mathf.MoveTowards(_ventLight.intensity, targetIntensity, dt * 8f);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (_gasFrames == null || _gasFrames.Length == 0)
            {
                var allAssets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath("Assets/_Project/Art/Sprites/env_rising_bubbles.png");
                var list = new List<Sprite>();
                foreach (var a in allAssets)
                {
                    if (a is Sprite s) list.Add(s);
                }
                if (list.Count > 0)
                {
                    list.Sort((a, b) =>
                    {
                        int.TryParse(a.name.Replace("env_rising_bubbles_", ""), out int numA);
                        int.TryParse(b.name.Replace("env_rising_bubbles_", ""), out int numB);
                        return numA.CompareTo(numB);
                    });
                    _gasFrames = list.ToArray();
                }
            }
        }
#endif

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Collider2D col = GetComponent<Collider2D>();
            if (col != null)
            {
                Gizmos.color = new Color(0.35f, 0.85f, 0.45f, 0.25f);
                Bounds b = col.bounds;
                Gizmos.DrawCube(b.center, b.size);
                Gizmos.color = new Color(0.35f, 0.85f, 0.45f, 0.9f);
                Gizmos.DrawWireCube(b.center, b.size);
            }
        }
#endif
    }
}
