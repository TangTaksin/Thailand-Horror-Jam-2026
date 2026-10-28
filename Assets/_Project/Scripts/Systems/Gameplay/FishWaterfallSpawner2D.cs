using System.Collections.Generic;
using UnityEngine;
using ThailandHorrorJam.Core;

namespace ThailandHorrorJam.Systems.Gameplay
{
    public enum FishSpawnMode
    {
        [Tooltip("Sector 05: 3 lanes with safe gaps for chasm traversal timing.")]
        DiscreteLanes = 0,

        [Tooltip("Sector 06: Chaotic continuous fish rain with random X coordinates and speeds.")]
        ContinuousRain = 1
    }

    /// <summary>
    /// Spawner for the Sector 05 fish waterfall hazard above the chasm (X: 107.0 - 112.0)
    /// and Sector 06 torrential chaotic dead fish rain (X: 136.0 - 160.5).
    /// Features:
    /// - DiscreteLanes mode (3 lanes with safe gaps).
    /// - ContinuousRain mode (random X coordinates, fall speed variance, and horizontal drift).
    /// - Built-in zero-allocation object pool (supports 120+ instances smoothly for WebGL).
    /// - Proximity / Sector check: only runs when the player is within sector bounds.
    /// </summary>
    public sealed class FishWaterfallSpawner2D : MonoBehaviour
    {
        [Header("Spawn Mode")]
        [Tooltip("DiscreteLanes for Sector 05 puzzle; ContinuousRain for Sector 06 chaotic downpour.")]
        [SerializeField] private FishSpawnMode _spawnMode = FishSpawnMode.DiscreteLanes;

        [Header("Discrete Spawn Lanes (Sector 05)")]
        [Tooltip("World X positions for discrete lanes across the chasm.")]
        [SerializeField] private float[] _laneXPositions = new float[] { 108.0f, 109.5f, 111.0f };

        [Tooltip("World Y height at which fish spawn.")]
        [SerializeField] private float _spawnHeightY = 7.5f;

        [Header("Rhythm & Timing (Discrete Lanes)")]
        [Tooltip("Minimum time between spawn waves (seconds).")]
        [SerializeField] private float _minInterval = 1.15f;

        [Tooltip("Maximum time between spawn waves (seconds).")]
        [SerializeField] private float _maxInterval = 1.65f;

        [Header("Continuous Rain Settings (Sector 06 Downpour)")]
        [Tooltip("Minimum X world position for random rain drops.")]
        [SerializeField] private float _rainMinX = 136.0f;

        [Tooltip("Maximum X world position for random rain drops.")]
        [SerializeField] private float _rainMaxX = 160.5f;

        [Tooltip("Minimum time between rain drops (seconds).")]
        [SerializeField] private float _rainDropMinInterval = 0.08f;

        [Tooltip("Maximum time between rain drops (seconds).")]
        [SerializeField] private float _rainDropMaxInterval = 0.16f;

        [Tooltip("Multiplier variance range for fish fall speed (e.g. 0.85 to 1.25).")]
        [SerializeField] private Vector2 _speedVarianceRange = new Vector2(0.85f, 1.25f);

        [Tooltip("Max horizontal drift velocity applied to falling fish.")]
        [SerializeField] private float _horizontalDriftRange = 0.35f;

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

        [Header("Sector 06 / Ambient Behavior")]
        [Tooltip("If false, fish will not knockback or harm the player on contact (ambient fish rain).")]
        [SerializeField] private bool _harmPlayer = true;

        [Tooltip("If true, fish will detect the ground, stop falling, and flop on the floor continuously.")]
        [SerializeField] private bool _landAndFlopOnGround = false;

        [Tooltip("Ground layer mask for landing surfaces.")]
        [SerializeField] private LayerMask _groundLayerMask = 1 << 6;

        [Tooltip("Duration in seconds that fish flop on ground before recycling (0 = infinite).")]
        [SerializeField] private float _groundFlopDuration = 16f;

        public FishSpawnMode SpawnMode => _spawnMode;
        public float RainMinX => _rainMinX;
        public float RainMaxX => _rainMaxX;
        public bool HarmPlayer => _harmPlayer;
        public bool LandAndFlopOnGround => _landAndFlopOnGround;

        private readonly Queue<FallingFishHazard2D> _pool = new Queue<FallingFishHazard2D>();
        private readonly List<FallingFishHazard2D> _allSpawned = new List<FallingFishHazard2D>();
        private float _waveTimer;
        private float _currentWaveInterval = 1.3f;
        private Transform _playerTransform;
        private Transform _poolRoot;

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

            if (_poolRoot == null)
            {
                _poolRoot = new GameObject("FishPool").transform;
                _poolRoot.SetParent(transform, false);
            }

            for (int i = 0; i < _poolSize; i++)
            {
                FallingFishHazard2D fish = Instantiate(_fishPrefab, _poolRoot);
                fish.SetPoolParent(_poolRoot);
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
                if (_spawnMode == FishSpawnMode.ContinuousRain)
                {
                    _currentWaveInterval = Random.Range(_rainDropMinInterval, _rainDropMaxInterval);
                    SpawnRainDrop();
                }
                else
                {
                    _currentWaveInterval = Random.Range(_minInterval, _maxInterval);
                    SpawnWave();
                }
            }
        }

        /// <summary>
        /// Spawns a single rain drop fish at a continuous random X position across [_rainMinX, _rainMaxX].
        /// </summary>
        public void SpawnRainDrop()
        {
            FallingFishHazard2D fish = GetFishFromPool();
            if (fish == null) return;

            float randomX = Random.Range(_rainMinX, _rainMaxX);
            Vector3 spawnPos = new Vector3(randomX, _spawnHeightY, 0f);

            fish.ConfigureBehavior(_harmPlayer, _landAndFlopOnGround, _groundLayerMask, _groundFlopDuration);

            float speedMul = Random.Range(_speedVarianceRange.x, _speedVarianceRange.y);
            float driftX = Random.Range(-_horizontalDriftRange, _horizontalDriftRange);

            fish.Spawn(spawnPos, ReturnFishToPool, _deadFishFrames, speedMul, driftX);
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

            fish.ConfigureBehavior(_harmPlayer, _landAndFlopOnGround, _groundLayerMask, _groundFlopDuration);
            Vector3 spawnPos = new Vector3(_laneXPositions[laneIndex], _spawnHeightY, 0f);
            fish.Spawn(spawnPos, ReturnFishToPool, _deadFishFrames);
        }

        private FallingFishHazard2D GetFishFromPool()
        {
            if (_pool.Count > 0)
            {
                return _pool.Dequeue();
            }

            // หากเปิดโหมดดิ้นบนพื้นและพูลหมด ให้รีไซเคิลปลาตัวที่ดิ้นบนพื้นมานานที่สุดเพื่อนำมาใช้ใหม่
            if (_landAndFlopOnGround)
            {
                for (int i = 0; i < _allSpawned.Count; i++)
                {
                    if (_allSpawned[i] != null && _allSpawned[i].gameObject.activeSelf && _allSpawned[i].IsFloppingOnGround)
                    {
                        _allSpawned[i].Despawn();
                        if (_pool.Count > 0)
                        {
                            return _pool.Dequeue();
                        }
                    }
                }
            }

            // Fallback: instantiate a new one if pool is exhausted and under safety cap
            if (_fishPrefab != null && _allSpawned.Count < 150)
            {
                Transform parent = _poolRoot != null ? _poolRoot : transform;
                FallingFishHazard2D newFish = Instantiate(_fishPrefab, parent);
                newFish.SetPoolParent(parent);
                _allSpawned.Add(newFish);
                return newFish;
            }

            return null;
        }

        private void ReturnFishToPool(FallingFishHazard2D fish)
        {
            if (fish == null) return;
            if (_poolRoot != null && fish.transform.parent != _poolRoot)
            {
                fish.transform.SetParent(_poolRoot, false);
            }
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
