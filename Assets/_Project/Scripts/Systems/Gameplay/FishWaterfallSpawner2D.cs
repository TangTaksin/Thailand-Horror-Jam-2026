using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Spawner for the Sector 05 fish waterfall hazard above the chasm (X: 107.0 - 112.0).
    /// Features:
    /// - 3 spawn lanes (Left, Center, Right).
    /// - Safe gap wave rhythm: Spawns 1 or 2 fish per wave, guaranteeing at least one safe gap in every wave.
    /// - Built-in zero-allocation object pool for WebGL performance.
    /// - Proximity / Sector check: only runs when the player is within Sector 05 bounds.
    /// </summary>
    public sealed class FishWaterfallSpawner2D : MonoBehaviour
    {
        [Header("Spawn Lanes")]
        [Tooltip("World X positions for the 3 lanes across the 5m chasm.")]
        [SerializeField] private float[] _laneXPositions = new float[] { 108.0f, 109.5f, 111.0f };

        [Tooltip("World Y height at which fish spawn.")]
        [SerializeField] private float _spawnHeightY = 7.5f;

        [Header("Rhythm & Timing")]
        [Tooltip("Minimum time between spawn waves (seconds).")]
        [SerializeField] private float _minInterval = 1.15f;

        [Tooltip("Maximum time between spawn waves (seconds).")]
        [SerializeField] private float _maxInterval = 1.65f;

        [Header("Object Pooling")]
        [Tooltip("Prefab for the falling fish hazard.")]
        [SerializeField] private FallingFishHazard2D _fishPrefab;

        [Tooltip("Initial number of pooled fish instances.")]
        [SerializeField] private int _poolSize = 10;

        [Header("Sprite Sheet Animation")]
        [Tooltip("24 frames loaded from Dead_Fish_SpriteSheet.")]
        [SerializeField] private Sprite[] _deadFishFrames;

        [Header("Optimization / Proximity")]
        [Tooltip("Minimum player X position to activate spawner.")]
        [SerializeField] private float _activeMinX = 94.0f;

        [Tooltip("Maximum player X position to activate spawner.")]
        [SerializeField] private float _activeMaxX = 126.0f;

        private readonly Queue<FallingFishHazard2D> _pool = new Queue<FallingFishHazard2D>();
        private readonly List<FallingFishHazard2D> _allSpawned = new List<FallingFishHazard2D>();
        private float _waveTimer;
        private float _currentWaveInterval = 1.3f;
        private Transform _playerTransform;

        private void Awake()
        {
            InitializePool();
        }

        private void Start()
        {
            FindPlayer();
            _waveTimer = Random.Range(0.2f, 0.6f);
        }

        private void InitializePool()
        {
            if (_fishPrefab == null)
            {
                return;
            }

            Transform poolRoot = new GameObject("FishPool").transform;
            poolRoot.SetParent(transform, false);

            for (int i = 0; i < _poolSize; i++)
            {
                FallingFishHazard2D fish = Instantiate(_fishPrefab, poolRoot);
                fish.gameObject.SetActive(false);
                _pool.Enqueue(fish);
                _allSpawned.Add(fish);
            }
        }

        private void Update()
        {
            if (!IsPlayerInProximity())
            {
                return;
            }

            _waveTimer += Time.deltaTime;
            if (_waveTimer >= _currentWaveInterval)
            {
                _waveTimer = 0f;
                _currentWaveInterval = Random.Range(_minInterval, _maxInterval);
                SpawnWave();
            }
        }

        /// <summary>
        /// Spawns a wave with 1 or 2 fish, leaving at least 1 lane clear.
        /// </summary>
        private void SpawnWave()
        {
            if (_laneXPositions == null || _laneXPositions.Length == 0) return;

            int laneCount = _laneXPositions.Length;

            // Pick 1 lane to always keep safe
            int safeLaneIndex = Random.Range(0, laneCount);

            // Decide whether to spawn 1 or 2 fish
            bool spawnTwo = Random.value > 0.45f && laneCount >= 3;

            for (int i = 0; i < laneCount; i++)
            {
                if (i == safeLaneIndex) continue; // Safe gap!

                if (!spawnTwo)
                {
                    // Spawn exactly one fish in this non-safe lane, then exit loop
                    SpawnFishAtLane(i);
                    break;
                }

                SpawnFishAtLane(i);
            }
        }

        private void SpawnFishAtLane(int laneIndex)
        {
            if (laneIndex < 0 || laneIndex >= _laneXPositions.Length) return;

            FallingFishHazard2D fish = GetFishFromPool();
            if (fish == null) return;

            Vector3 spawnPos = new Vector3(_laneXPositions[laneIndex], _spawnHeightY, 0f);
            fish.Spawn(spawnPos, ReturnFishToPool, _deadFishFrames);
        }

        private FallingFishHazard2D GetFishFromPool()
        {
            if (_pool.Count > 0)
            {
                return _pool.Dequeue();
            }

            // Fallback: instantiate a new one if pool is exhausted
            if (_fishPrefab != null)
            {
                FallingFishHazard2D newFish = Instantiate(_fishPrefab, transform);
                _allSpawned.Add(newFish);
                return newFish;
            }

            return null;
        }

        private void ReturnFishToPool(FallingFishHazard2D fish)
        {
            if (fish == null) return;
            if (!_pool.Contains(fish))
            {
                _pool.Enqueue(fish);
            }
        }

        private bool IsPlayerInProximity()
        {
            if (_playerTransform == null)
            {
                FindPlayer();
                if (_playerTransform == null) return true; // fallback
            }

            float px = _playerTransform.position.x;
            return px >= _activeMinX && px <= _activeMaxX;
        }

        private void FindPlayer()
        {
            PlayerController2D player = FindAnyObjectByType<PlayerController2D>();
            if (player != null)
            {
                _playerTransform = player.transform;
            }
        }

        public void SetFishFrames(Sprite[] frames)
        {
            _deadFishFrames = frames;
        }

        public void SetFishPrefab(FallingFishHazard2D prefab)
        {
            _fishPrefab = prefab;
            if (_pool.Count == 0)
            {
                InitializePool();
            }
        }

        private void OnDrawGizmos()
        {
            if (_laneXPositions == null) return;

            Gizmos.color = new Color(0.2f, 0.85f, 0.95f, 0.65f);
            for (int i = 0; i < _laneXPositions.Length; i++)
            {
                Vector3 top = new Vector3(_laneXPositions[i], _spawnHeightY, 0f);
                Vector3 btm = new Vector3(_laneXPositions[i], -3.2f, 0f);
                Gizmos.DrawLine(top, btm);
                Gizmos.DrawWireSphere(top, 0.25f);
            }
        }
    }
}
