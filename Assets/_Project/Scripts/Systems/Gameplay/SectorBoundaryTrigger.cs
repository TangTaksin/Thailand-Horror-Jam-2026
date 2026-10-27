using UnityEngine;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Invisible 2D trigger placed at the boundary between two level sectors.
    /// When the player crosses through, it informs LevelSeamlessManager to smoothly
    /// slide the active sector window (activating the new sector and culling distant ones).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class SectorBoundaryTrigger : MonoBehaviour
    {
        [Header("Target Sector")]
        [Tooltip("Index of the sector the player enters when passing this trigger.")]
        [SerializeField] private int _targetSectorIndex;

        [Tooltip("Optional direct reference to the sector GameObject. If assigned, overrides index lookup.")]
        [SerializeField] private GameObject _targetSectorObject;

        [Header("Directional Filter (Optional)")]
        [Tooltip("When enabled, only triggers when the player moves in the specified direction.")]
        [SerializeField] private bool _requireDirection;

        [Tooltip("Expected travel direction in world space (e.g., Right = (1, 0)).")]
        [SerializeField] private Vector2 _expectedDirection = Vector2.right;

        [Tooltip("Minimum dot product between player movement velocity and expected direction.")]
        [SerializeField] private float _directionTolerance = 0.25f;

        [Header("Bidirectional Transition (Optional)")]
        [Tooltip("When enabled, acts as a two-way boundary between Sector A (moving backward) and Sector B (moving forward).")]
        [SerializeField] private bool _isBidirectional;

        [Tooltip("Sector to enter when moving AGAINST _expectedDirection (e.g., walking Left).")]
        [SerializeField] private GameObject _sectorA;

        [Tooltip("Sector to enter when moving ALONG _expectedDirection (e.g., walking Right).")]
        [SerializeField] private GameObject _sectorB;

        private Collider2D _collider;

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
            HandlePlayerContact(other);
        }

        private void HandlePlayerContact(Collider2D other)
        {
            if (!PlayerController2D.IsPlayer(other)) return;

            // 1. Two-way boundary check
            if (_isBidirectional && _sectorA != null && _sectorB != null)
            {
                Rigidbody2D playerRb = other.attachedRigidbody;
                if (playerRb != null)
                {
                    Vector2 playerVel = playerRb.linearVelocity;
                    if (playerVel.sqrMagnitude > 0.04f)
                    {
                        float dot = Vector2.Dot(playerVel.normalized, _expectedDirection.normalized);
                        if (dot > _directionTolerance)
                        {
                            LevelSeamlessManager.Instance?.TransitionToSector(_sectorB);
                        }
                        else if (dot < -_directionTolerance)
                        {
                            LevelSeamlessManager.Instance?.TransitionToSector(_sectorA);
                        }
                    }
                }
                return;
            }

            // 2. One-way directional filter check
            if (_requireDirection)
            {
                Rigidbody2D playerRb = other.attachedRigidbody;
                if (playerRb != null)
                {
                    Vector2 playerVel = playerRb.linearVelocity;
                    if (playerVel.sqrMagnitude > 0.04f)
                    {
                        float dot = Vector2.Dot(playerVel.normalized, _expectedDirection.normalized);
                        if (dot < _directionTolerance)
                        {
                            return; // Not moving in the intended transition direction
                        }
                    }
                }
            }

            if (LevelSeamlessManager.Instance != null)
            {
                if (_targetSectorObject != null)
                {
                    LevelSeamlessManager.Instance.TransitionToSector(_targetSectorObject);
                }
                else
                {
                    LevelSeamlessManager.Instance.TransitionToSector(_targetSectorIndex);
                }
            }
        }

        private void OnDrawGizmos()
        {
            Collider2D col = _collider != null ? _collider : GetComponent<Collider2D>();
            if (col == null) return;

            Bounds b = col.bounds;
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.7f);
            Gizmos.DrawWireCube(b.center, b.size);

            if (_isBidirectional)
            {
                Vector3 origin = b.center;
                Vector3 forward = (Vector3)_expectedDirection.normalized * 1.2f;
                // Forward (Sector B)
                Gizmos.color = Color.cyan;
                Gizmos.DrawRay(origin, forward);
                Gizmos.DrawSphere(origin + forward, 0.12f);
                // Backward (Sector A)
                Gizmos.color = new Color(1f, 0.5f, 0.2f, 1f);
                Gizmos.DrawRay(origin, -forward);
                Gizmos.DrawSphere(origin - forward, 0.12f);
            }
            else if (_requireDirection)
            {
                Gizmos.color = Color.cyan;
                Vector3 origin = b.center;
                Vector3 dir = (Vector3)_expectedDirection.normalized * 1.2f;
                Gizmos.DrawRay(origin, dir);
                Gizmos.DrawSphere(origin + dir, 0.12f);
            }
        }
    }
}
