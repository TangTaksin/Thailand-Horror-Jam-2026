using System.Collections.Generic;
using UnityEngine;

namespace ThailandHorrorJam.Systems.Gameplay
{
    /// <summary>
    /// Mounted on the overhead moving Stitchable Crate in Sector 06.
    /// Intercepts falling dead fish hazards from the ceiling waterfall, shielding
    /// the player walking beneath. Captured fish stick and pile up on the roof of the
    /// crate as accumulating dead fish debris, bounded for WebGL performance.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public sealed class CrateFishShieldCollector2D : MonoBehaviour
    {
        [Header("Roof Dimensions")]
        [Tooltip("Horizontal width over which collected fish are distributed.")]
        [SerializeField] private float _roofWidth = 1.6f;

        [Tooltip("Vertical offset from crate center to roof surface.")]
        [SerializeField] private float _roofHeightOffset = 0.5f;

        [Tooltip("Vertical step height added as fish accumulate into layers.")]
        [SerializeField] private float _layerStepHeight = 0.08f;

        [Header("Optimization / Performance")]
        [Tooltip("Maximum number of visual dead fish instances kept on the roof simultaneously (prevents GC & draw call bloat on WebGL).")]
        [SerializeField] private int _maxCollectedFish = 16;

        private readonly List<FallingFishHazard2D> _collectedFish = new List<FallingFishHazard2D>(16);
        private int _collectedCount;

        public int CollectedCount => _collectedCount;
        public int ActiveDebrisCount => _collectedCount;
        public int MaxCollectedFish => _maxCollectedFish;

        private void Awake()
        {
            var col = GetComponent<Collider2D>();
            if (col != null)
            {
                col.isTrigger = true;
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null) return;
            FallingFishHazard2D fish = other.GetComponentInParent<FallingFishHazard2D>();
            InterceptFish(fish);
        }

        /// <summary>
        /// Intercepts and parents a falling fish onto the roof shield.
        /// If max capacity is reached, recycles the oldest fish (FIFO) to keep visual pile dynamic
        /// without ever exceeding capacity (bounded for WebGL 60fps).
        /// </summary>
        public void InterceptFish(FallingFishHazard2D fish)
        {
            if (fish == null || fish.IsDeflected) return;

            if (_collectedCount < _maxCollectedFish)
            {
                float xOffset = Random.Range(-_roofWidth * 0.45f, _roofWidth * 0.45f);
                float yOffset = _roofHeightOffset + (_collectedCount / 3) * _layerStepHeight + Random.Range(-0.02f, 0.04f);
                Vector3 roofLocalPos = new Vector3(xOffset, yOffset, 0f);

                fish.CollectOnShield(transform, roofLocalPos);
                _collectedFish.Add(fish);
                _collectedCount++;
            }
            else
            {
                // FIFO: recycle oldest fish so new splats remain visible while maintaining strict ceiling
                if (_collectedFish.Count > 0)
                {
                    var oldest = _collectedFish[0];
                    _collectedFish.RemoveAt(0);
                    if (oldest != null)
                    {
                        oldest.Despawn();
                    }
                }

                float xOffset = Random.Range(-_roofWidth * 0.45f, _roofWidth * 0.45f);
                float yOffset = _roofHeightOffset + (_collectedFish.Count / 3) * _layerStepHeight + Random.Range(-0.02f, 0.04f);
                Vector3 roofLocalPos = new Vector3(xOffset, yOffset, 0f);

                fish.CollectOnShield(transform, roofLocalPos);
                _collectedFish.Add(fish);
                _collectedCount = _collectedFish.Count;
            }
        }

        /// <summary>
        /// Clears all collected fish when the crate or puzzle is reset.
        /// </summary>
        public void ClearCollectedFish()
        {
            for (int i = 0; i < _collectedFish.Count; i++)
            {
                if (_collectedFish[i] != null)
                {
                    _collectedFish[i].Despawn();
                }
            }
            _collectedFish.Clear();
            _collectedCount = 0;
        }
    }
}
