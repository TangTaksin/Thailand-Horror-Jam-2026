using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Silent, invisible checkpoint trigger that registers the player's respawn position
    /// and tracks puzzle elements for that specific section.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class Checkpoint : MonoBehaviour
    {
        [Header("Respawn Point Settings")]
        [Tooltip("Offset relative to this transform where the player actually spawns.")]
        [SerializeField] private Vector2 _spawnOffset = Vector2.zero;
        [Tooltip("Facing direction of the player when waking up at this checkpoint.")]
        [SerializeField] private bool _respawnFacingRight = true;

        [Header("Associated Puzzle Objects")]
        [Tooltip("Boxes or movable objects belonging to this room/section. If empty, CheckpointManager will reset all active StitchableObjects.")]
        [SerializeField] private List<StitchableObject> _associatedStitchables = new List<StitchableObject>();

        [Header("Trigger Settings")]
        [Tooltip("When true, this checkpoint only activates once and disables its trigger.")]
        [SerializeField] private bool _isOneShot = false;

        private Collider2D _collider;
        private bool _isActivated;

        public Vector2 SpawnPosition => (Vector2)transform.position + _spawnOffset;
        public bool RespawnFacingRight => _respawnFacingRight;
        public IReadOnlyList<StitchableObject> AssociatedStitchables => _associatedStitchables;
        public bool IsActivated => _isActivated;

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
            if (_isOneShot && _isActivated) return;

            if (PlayerController2D.IsPlayer(other))
            {
                _isActivated = true;
                if (CheckpointManager.Instance != null)
                {
                    CheckpointManager.Instance.SetActiveCheckpoint(this);
                }

                if (_isOneShot && _collider != null)
                {
                    _collider.enabled = false;
                }
            }
        }

        private void OnDrawGizmos()
        {
            Collider2D col = _collider != null ? _collider : GetComponent<Collider2D>();
            if (col != null)
            {
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.35f);
                Gizmos.DrawCube(col.bounds.center, col.bounds.size);
                Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
                Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
            }

            // Draw spawn marker
            Vector2 spawnPos = SpawnPosition;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(spawnPos, 0.22f);
            Gizmos.DrawLine(spawnPos + Vector2.left * 0.25f, spawnPos + Vector2.right * 0.25f);
            Gizmos.DrawLine(spawnPos + Vector2.down * 0.25f, spawnPos + Vector2.up * 0.25f);

            // Draw facing arrow
            Vector2 forward = _respawnFacingRight ? Vector2.right : Vector2.left;
            Gizmos.color = Color.green;
            Gizmos.DrawRay(spawnPos, forward * 0.5f);

            // Draw lines to associated puzzle boxes
            if (_associatedStitchables != null)
            {
                Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
                for (int i = 0; i < _associatedStitchables.Count; i++)
                {
                    if (_associatedStitchables[i] != null)
                    {
                        Gizmos.DrawLine(spawnPos, _associatedStitchables[i].CurrentPosition);
                    }
                }
            }
        }
    }
}
