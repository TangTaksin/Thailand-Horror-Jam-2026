using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    public enum HazardType
    {
        FallAbyss,
        Spikes,
        Crush,
        Instant
    }

    /// <summary>
    /// Trigger volume that kills the player on contact (initiating Limbo-style slow fade & respawn)
    /// and resets dropped puzzle boxes back to their designated spawn platform.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class KillZone : MonoBehaviour
    {
        [Header("Hazard Configuration")]
        [SerializeField] private HazardType _hazardType = HazardType.FallAbyss;
        [SerializeField] private float _cameraShakeIntensity = 0.15f;
        [SerializeField] private float _cameraShakeDuration = 0.3f;

        [Header("Puzzle Object Handling")]
        [Tooltip("When true, dropped StitchableObject boxes are instantly returned to their initial room position.")]
        [SerializeField] private bool _respawnBoxOnContact = true;

        private Collider2D _collider;

        public HazardType Type => _hazardType;
        public float CameraShakeIntensity => _cameraShakeIntensity;
        public float CameraShakeDuration => _cameraShakeDuration;

        private void Awake()
        {
            _collider = GetComponent<Collider2D>();
            if (_collider != null && !_collider.isTrigger)
            {
                _collider.isTrigger = true;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandleCollision(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            // Catches any object that was spawned or enabled inside the hazard zone
            HandleCollision(other);
        }

        private void HandleCollision(Collider2D other)
        {
            if (other == null) return;

            // 1. Check if the player collided with the hazard
            if (PlayerController2D.IsPlayer(other))
            {
                if (CheckpointManager.Instance != null && !CheckpointManager.Instance.IsRespawning)
                {
                    Debug.Log($"[KillZone] Player triggered KillZone '{gameObject.name}' (HazardType: {_hazardType}).");
                    CheckpointManager.Instance.TriggerPlayerDeath(this);
                }
                return;
            }

            // 2. Check if a puzzle box / StitchableObject fell into the abyss
            if (_respawnBoxOnContact)
            {
                StitchableObject stitchable = other.GetComponentInParent<StitchableObject>();
                if (stitchable != null)
                {
                    Debug.Log($"[KillZone] Resetting dropped StitchableObject '{stitchable.name}' back to initial state.");
                    stitchable.ResetToInitialState(forceReset: true);
                }
            }
        }

        private void OnDrawGizmos()
        {
            Collider2D col = _collider != null ? _collider : GetComponent<Collider2D>();
            if (col == null) return;

            Bounds b = col.bounds;
            Gizmos.color = new Color(0.95f, 0.15f, 0.15f, 0.75f);
            Gizmos.DrawWireCube(b.center, b.size);

            // Draw inner diagonal cross (X)
            Vector3 min = b.min;
            Vector3 max = b.max;
            Gizmos.DrawLine(new Vector3(min.x, min.y, 0f), new Vector3(max.x, max.y, 0f));
            Gizmos.DrawLine(new Vector3(min.x, max.y, 0f), new Vector3(max.x, min.y, 0f));
        }
    }
}
