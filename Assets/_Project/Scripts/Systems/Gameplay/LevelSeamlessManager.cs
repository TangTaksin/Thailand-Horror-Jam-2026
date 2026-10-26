using System;
using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;
using ThailandHorrorJam.Systems.Needle;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Coordinates seamless continuous level streaming (Limbo/Inside style).
    /// Keeps an active window of sectors (e.g. Current ± 1 adjacent sectors) while
    /// culling distant sectors (SetActive = false) to drastically reduce draw calls and 2D physics overhead.
    /// Handles cross-sector object reparenting when hauling tethered objects and synchronizes
    /// with CheckpointManager during respawn blackouts.
    /// </summary>
    public sealed class LevelSeamlessManager : MonoBehaviour
    {
        [System.Serializable]
        public sealed class SectorEntry
        {
            [Tooltip("Descriptive name of the sector (e.g., Level 1, Level 2).")]
            public string name = "New Sector";

            [Tooltip("Root GameObject containing all terrain, obstacles, and objects for this sector.")]
            public GameObject rootObject;

            [Tooltip("Optional checkpoint belonging to this sector. Used to re-activate this sector when respawning here.")]
            public Checkpoint defaultCheckpoint;
        }

        public static LevelSeamlessManager Instance { get; private set; }

        [Header("Player & Tools")]
        [SerializeField] private PlayerController2D _playerController;
        [SerializeField] private NeedleController _needleController;

        [Header("Sectors Configuration")]
        [SerializeField] private List<SectorEntry> _sectors = new List<SectorEntry>();

        [Header("Active Window Settings")]
        [Tooltip("Number of adjacent sectors to keep active on each side of the current sector (Default: 1 = Previous, Current, Next).")]
        [Range(0, 3)]
        [SerializeField] private int _activeRadius = 1;

        [Tooltip("Initial sector index to activate at start (-1 = auto-detect closest sector to player).")]
        [SerializeField] private int _initialSectorIndex = -1;

        private int _currentSectorIndex = -1;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            EnsurePlayerReferences();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnEnable()
        {
            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.OnPlayerDied += HandlePlayerDied;
                CheckpointManager.Instance.OnPlayerRespawned += HandlePlayerRespawned;
            }
        }

        private void OnDisable()
        {
            if (CheckpointManager.Instance != null)
            {
                CheckpointManager.Instance.OnPlayerDied -= HandlePlayerDied;
                CheckpointManager.Instance.OnPlayerRespawned -= HandlePlayerRespawned;
            }
        }

        private void Start()
        {
            EnsurePlayerReferences();

            if (_currentSectorIndex < 0)
            {
                int startingIndex = _initialSectorIndex >= 0 && _initialSectorIndex < _sectors.Count
                    ? _initialSectorIndex
                    : AutoDetectSectorFromPlayerPosition();

                SetCurrentSector(startingIndex);
            }
        }

        private void EnsurePlayerReferences()
        {
            if (_playerController == null)
            {
                _playerController = FindAnyObjectByType<PlayerController2D>();
            }

            if (_needleController == null)
            {
                _needleController = FindAnyObjectByType<NeedleController>();
            }
        }

        /// <summary>
        /// Transitions the active sector window to the target sector index.
        /// Handles cross-sector tether reparenting and updates active states.
        /// </summary>
        public void TransitionToSector(int targetIndex)
        {
            if (targetIndex < 0 || targetIndex >= _sectors.Count) return;
            if (targetIndex == _currentSectorIndex) return;

            // 1. Check if the player is currently hauling an object across the boundary
            HandleTetherReparenting(targetIndex);

            // 2. Switch sector window
            SetCurrentSector(targetIndex);
        }

        /// <summary>
        /// Transitions to the sector owning the given root GameObject.
        /// </summary>
        public void TransitionToSector(GameObject sectorObject)
        {
            int index = GetSectorIndex(sectorObject);
            if (index >= 0)
            {
                TransitionToSector(index);
            }
        }

        public int GetSectorIndex(GameObject sectorObject)
        {
            if (sectorObject == null) return -1;
            for (int i = 0; i < _sectors.Count; i++)
            {
                if (_sectors[i] != null && _sectors[i].rootObject == sectorObject)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// If the player is currently hauling a StitchableObject across sector boundaries,
        /// reparent the object to the incoming sector so it does not get disabled when the old sector unloads.
        /// </summary>
        private void HandleTetherReparenting(int targetIndex)
        {
            if (_needleController == null || !_needleController.IsTetheredToPlayer) return;

            IStitchable tethered = _needleController.TetheredStitchable;
            if (tethered == null || tethered.ObjectTransform == null) return;

            Transform objTransform = tethered.ObjectTransform;
            SectorEntry targetSector = _sectors[targetIndex];
            if (targetSector?.rootObject == null) return;

            // Reparent to the incoming sector to keep it active
            objTransform.SetParent(targetSector.rootObject.transform, worldPositionStays: true);
        }

        public void SetCurrentSector(int newIndex)
        {
            if (newIndex < 0 || newIndex >= _sectors.Count) return;

            _currentSectorIndex = newIndex;

            // Apply visibility window: Current ± _activeRadius
            for (int i = 0; i < _sectors.Count; i++)
            {
                SectorEntry sector = _sectors[i];
                if (sector == null || sector.rootObject == null) continue;

                bool shouldBeActive = Mathf.Abs(i - _currentSectorIndex) <= _activeRadius;

                if (sector.rootObject.activeSelf != shouldBeActive)
                {
                    sector.rootObject.SetActive(shouldBeActive);
                }
            }
        }

        /// <summary>
        /// Called during the blackout phase of death: switches the active sector
        /// to the one containing the active checkpoint so that the scene is fully ready before fade in.
        /// </summary>
        private void HandlePlayerDied()
        {
            if (CheckpointManager.Instance == null) return;

            Checkpoint cp = CheckpointManager.Instance.CurrentCheckpoint;
            if (cp != null)
            {
                int cpSectorIndex = FindSectorForCheckpoint(cp);
                if (cpSectorIndex >= 0)
                {
                    SetCurrentSector(cpSectorIndex);
                }
            }
        }

        private void HandlePlayerRespawned()
        {
            // Ensure player references are fresh after respawn
            EnsurePlayerReferences();
        }

        private int FindSectorForCheckpoint(Checkpoint cp)
        {
            if (cp == null) return -1;

            // 1. Direct association check
            for (int i = 0; i < _sectors.Count; i++)
            {
                if (_sectors[i] != null && _sectors[i].defaultCheckpoint == cp)
                {
                    return i;
                }
            }

            // 2. Hierarchy parentage check
            Transform cpTransform = cp.transform;
            for (int i = 0; i < _sectors.Count; i++)
            {
                if (_sectors[i] != null && _sectors[i].rootObject != null)
                {
                    if (cpTransform.IsChildOf(_sectors[i].rootObject.transform))
                    {
                        return i;
                    }
                }
            }

            // 3. Proximity to sector position
            Vector2 spawnPos = cp.SpawnPosition;
            float closestDistance = float.MaxValue;
            int closestIndex = 0;

            for (int i = 0; i < _sectors.Count; i++)
            {
                if (_sectors[i] != null && _sectors[i].rootObject != null)
                {
                    float dist = Mathf.Abs(spawnPos.x - _sectors[i].rootObject.transform.position.x);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestIndex = i;
                    }
                }
            }

            return closestIndex;
        }

        private int AutoDetectSectorFromPlayerPosition()
        {
            if (_playerController == null || _sectors.Count == 0) return 0;

            float playerX = _playerController.transform.position.x;
            float closestDistance = float.MaxValue;
            int closestIndex = 0;

            for (int i = 0; i < _sectors.Count; i++)
            {
                if (_sectors[i] != null && _sectors[i].rootObject != null)
                {
                    float sectorX = _sectors[i].rootObject.transform.position.x;
                    float dist = Mathf.Abs(playerX - sectorX);
                    if (dist < closestDistance)
                    {
                        closestDistance = dist;
                        closestIndex = i;
                    }
                }
            }

            return closestIndex;
        }

        private void OnDrawGizmosSelected()
        {
            if (_sectors == null) return;

            for (int i = 0; i < _sectors.Count; i++)
            {
                SectorEntry sector = _sectors[i];
                if (sector == null || sector.rootObject == null) continue;

                bool isActive = Application.isPlaying
                    ? sector.rootObject.activeSelf
                    : (i == _currentSectorIndex || (i == 0 && _currentSectorIndex < 0));

                Gizmos.color = isActive ? new Color(0.2f, 1f, 0.3f, 0.6f) : new Color(0.7f, 0.7f, 0.7f, 0.25f);
                Vector3 pos = sector.rootObject.transform.position;
                Gizmos.DrawWireCube(pos + Vector3.up * 2f, new Vector3(8f, 6f, 1f));
            }
        }
    }
}
 