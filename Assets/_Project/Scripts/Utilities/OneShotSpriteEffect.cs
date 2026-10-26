using UnityEngine;

namespace ThailandHorrorJam.Utilities
{
    /// <summary>
    /// Lightweight component for playing a one-shot sprite frame sequence and auto-destroying upon completion.
    /// Perfect for particle-like sprite animations (e.g. throw shockwaves, hit sparks, dust puffs).
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class OneShotSpriteEffect : MonoBehaviour
    {
        [SerializeField] private Sprite[] _frames;
        [SerializeField] private float _fps = 24f;
        [SerializeField] private bool _autoPlayOnStart = true;
        [SerializeField] private bool _destroyOnFinish = true;

        private SpriteRenderer _spriteRenderer;
        private int _currentFrame;
        private float _timer;
        private bool _isPlaying;

        public Sprite[] Frames
        {
            get => _frames;
            set => _frames = value;
        }

        public float Fps
        {
            get => _fps;
            set => _fps = value;
        }

        private void Awake()
        {
            _spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void Start()
        {
            if (_autoPlayOnStart)
            {
                Play();
            }
        }

        public void Play()
        {
            if (_frames == null || _frames.Length == 0)
            {
                if (_destroyOnFinish) Destroy(gameObject);
                return;
            }

            _currentFrame = 0;
            _timer = 0f;
            _isPlaying = true;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.sprite = _frames[0];
            }
        }

        private void Update()
        {
            if (!_isPlaying || _frames == null || _frames.Length == 0) return;

            float frameDuration = _fps > 0.01f ? (1f / _fps) : 0.05f;
            _timer += Time.deltaTime;

            if (_timer >= frameDuration)
            {
                _timer -= frameDuration;
                _currentFrame++;

                if (_currentFrame < _frames.Length)
                {
                    if (_spriteRenderer != null)
                    {
                        _spriteRenderer.sprite = _frames[_currentFrame];
                    }
                }
                else
                {
                    _isPlaying = false;
                    if (_destroyOnFinish)
                    {
                        Destroy(gameObject);
                    }
                }
            }
        }
    }
}
