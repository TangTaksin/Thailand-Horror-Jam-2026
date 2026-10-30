using UnityEngine;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Decor-only fish rain for background layers. No colliders, physics, or tweens:
    /// each fish is a SpriteRenderer driven by this single script. Fake depth + camera parallax.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class BackgroundFishWaterfall2D : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] private Sprite[] _frames;
        [SerializeField] private Material _material;
        [SerializeField] private float _fps = 12f;
        [SerializeField] private string _sortingLayerName = "Default";
        [SerializeField] private int _sortingOrder = -10;

        [Header("Rain Area (local to this object)")]
        [SerializeField] private float _minX = -12f;
        [SerializeField] private float _maxX = 12f;
        [SerializeField] private float _spawnY = 8f;
        [SerializeField] private float _despawnY = -10f;
        [SerializeField] private Vector2 _spawnInterval = new Vector2(0.06f, 0.14f);
        [SerializeField] private int _maxFish = 60;

        [Header("Fake Depth (t: 0 = far, 1 = near)")]
        [SerializeField] private Vector2 _scaleRange = new Vector2(0.35f, 0.7f);
        [SerializeField] private Vector2 _fallSpeedRange = new Vector2(1.6f, 3.2f);
        [SerializeField] private float _driftRange = 0.25f;
        [SerializeField] private Color _farTint = new Color(0.25f, 0.28f, 0.32f, 0.55f);
        [SerializeField] private Color _nearTint = new Color(0.55f, 0.58f, 0.62f, 0.85f);
        [SerializeField] private float _wiggleAngle = 10f;

        [Header("Parallax (0 = fixed in world, 1 = moves with camera)")]
        [SerializeField] private Vector2 _parallax = new Vector2(0.3f, 0.1f);

        [Header("Activation (camera world X)")]
        [SerializeField] private float _activeMinX = -9999f;
        [SerializeField] private float _activeMaxX = 9999f;

        private struct Fish
        {
            public Transform T;
            public SpriteRenderer R;
            public bool Active;
            public float Speed, Drift, AnimTimer, Phase;
            public int Frame;
        }

        private Fish[] _fish;
        private Transform _layer;
        private Transform _cam;
        private Vector3 _camStart;
        private bool _camStartSet;
        private float _timer;
        private float _nextSpawn;

        public int ActiveCount { get; private set; }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (_fish != null) return;

            _layer = new GameObject("FishLayer").transform;
            _layer.SetParent(transform, false);

            int count = Mathf.Max(1, _maxFish);
            _fish = new Fish[count];
            for (int i = 0; i < count; i++)
            {
                GameObject go = new GameObject("BGFish_" + i);
                go.transform.SetParent(_layer, false);
                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                if (_material != null) sr.sharedMaterial = _material;
                sr.sortingLayerName = _sortingLayerName;
                sr.sortingOrder = _sortingOrder;
                go.SetActive(false);
                _fish[i] = new Fish { T = go.transform, R = sr };
            }
            _nextSpawn = Random.Range(_spawnInterval.x, _spawnInterval.y);
        }

        private bool TryCacheCamera()
        {
            if (_cam == null)
            {
                Camera c = Camera.main;
                if (c == null) return false;
                _cam = c.transform;
            }
            if (!_camStartSet)
            {
                _camStart = _cam.position;
                _camStartSet = true;
            }
            return true;
        }

        private void Update()
        {
            EnsureInitialized();
            float dt = Time.deltaTime;

            bool inRange = true;
            if (TryCacheCamera())
            {
                float cx = _cam.position.x;
                inRange = cx >= _activeMinX && cx <= _activeMaxX;
            }

            if (inRange)
            {
                _timer += dt;
                if (_timer >= _nextSpawn)
                {
                    _timer = 0f;
                    _nextSpawn = Random.Range(_spawnInterval.x, _spawnInterval.y);
                    SpawnOne();
                }
            }

            TickFish(dt);
        }

        private void TickFish(float dt)
        {
            bool animate = _frames != null && _frames.Length > 0;
            float frameInterval = _fps > 0.1f ? 1f / _fps : 0.08f;

            for (int i = 0; i < _fish.Length; i++)
            {
                if (!_fish[i].Active) continue;

                Transform t = _fish[i].T;
                Vector3 p = t.localPosition;
                p.x += _fish[i].Drift * dt;
                p.y -= _fish[i].Speed * dt;
                t.localPosition = p;

                _fish[i].Phase += dt * 3.5f;
                t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(_fish[i].Phase) * _wiggleAngle);

                if (animate)
                {
                    _fish[i].AnimTimer += dt;
                    if (_fish[i].AnimTimer >= frameInterval)
                    {
                        _fish[i].AnimTimer -= frameInterval;
                        _fish[i].Frame = (_fish[i].Frame + 1) % _frames.Length;
                        _fish[i].R.sprite = _frames[_fish[i].Frame];
                    }
                }

                if (p.y <= _despawnY)
                {
                    _fish[i].Active = false;
                    t.gameObject.SetActive(false);
                    ActiveCount--;
                }
            }
        }

        /// <summary>Activates a free fish slot. Returns false if all slots are busy.</summary>
        public bool SpawnOne()
        {
            EnsureInitialized();

            for (int i = 0; i < _fish.Length; i++)
            {
                if (_fish[i].Active) continue;

                float depth = Random.value;
                float scale = Mathf.Lerp(_scaleRange.x, _scaleRange.y, depth);

                _fish[i].Active = true;
                _fish[i].Speed = Mathf.Lerp(_fallSpeedRange.x, _fallSpeedRange.y, depth);
                _fish[i].Drift = Random.Range(-_driftRange, _driftRange);
                _fish[i].Phase = Random.value * Mathf.PI * 2f;
                _fish[i].AnimTimer = 0f;
                _fish[i].Frame = (_frames != null && _frames.Length > 0) ? Random.Range(0, _frames.Length) : 0;

                Transform t = _fish[i].T;
                t.localPosition = new Vector3(Random.Range(_minX, _maxX), _spawnY, 0f);
                t.localScale = new Vector3(scale, scale, 1f);

                SpriteRenderer r = _fish[i].R;
                r.color = Color.Lerp(_farTint, _nearTint, depth);
                if (_frames != null && _frames.Length > 0) r.sprite = _frames[_fish[i].Frame];

                t.gameObject.SetActive(true);
                ActiveCount++;
                return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (_layer == null || !TryCacheCamera()) return;

            Vector3 d = _cam.position - _camStart;
            _layer.localPosition = new Vector3(d.x * _parallax.x, d.y * _parallax.y, 0f);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.6f, 0.8f, 0.6f);
            Vector3 c = transform.TransformPoint(new Vector3((_minX + _maxX) * 0.5f, (_spawnY + _despawnY) * 0.5f, 0f));
            Gizmos.DrawWireCube(c, new Vector3(_maxX - _minX, Mathf.Abs(_spawnY - _despawnY), 0.1f));
        }
    }
}
