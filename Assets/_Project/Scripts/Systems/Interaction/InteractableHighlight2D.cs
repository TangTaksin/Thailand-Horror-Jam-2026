using UnityEngine;

namespace ThailandHorrorJam.Systems.Interaction
{
    /// <summary>
    /// Renders a subtle rim/silhouette outline behind a 2D <see cref="SpriteRenderer"/> when the object
    /// is focused by <see cref="PlayerInteractor"/>. Preserves URP 2D lighting on the primary sprite.
    /// </summary>
    public sealed class InteractableHighlight2D : MonoBehaviour
    {
        [Header("Target Sprite")]
        [Tooltip("Primary SpriteRenderer of the interactable object. Auto-resolved if left empty.")]
        [SerializeField] private SpriteRenderer _targetRenderer;

        [Header("Outline Appearance")]
        [Tooltip("Subtle rim color shown when the object is focused in close range.")]
        [SerializeField] private Color _outlineColor = new Color(1f, 0.98f, 0.9f, 0.65f);

        [Tooltip("Uniform scale multiplier for the silhouette rim sprite relative to the target sprite.")]
        [SerializeField] private Vector2 _outlineScaleMultiplier = new Vector2(1.08f, 1.05f);

        [Tooltip("Fade transition duration in seconds (~0.12s).")]
        [SerializeField] private float _fadeDuration = 0.12f;

        private SpriteRenderer _outlineRenderer;
        private GameObject _outlineObject;
        private float _currentAlpha;
        private float _targetAlpha;

        public bool IsHighlighted => _targetAlpha > 0.001f;

        private void Awake()
        {
            if (_targetRenderer == null)
            {
                _targetRenderer = GetComponent<SpriteRenderer>();
                if (_targetRenderer == null)
                {
                    _targetRenderer = GetComponentInChildren<SpriteRenderer>();
                }
            }

            EnsureOutlineRenderer();
            ApplyAlphaImmediate(0f);
        }

        private void OnDisable()
        {
            _targetAlpha = 0f;
            ApplyAlphaImmediate(0f);
        }

        private void LateUpdate()
        {
            if (_outlineRenderer == null || _targetRenderer == null) return;

            if (Mathf.Abs(_currentAlpha - _targetAlpha) > 0.001f)
            {
                float speed = _fadeDuration > 0.001f ? (1f / _fadeDuration) : 20f;
                _currentAlpha = Mathf.MoveTowards(_currentAlpha, _targetAlpha, speed * Time.deltaTime);
                UpdateOutlineVisuals();
            }
            else if (_currentAlpha > 0.001f)
            {
                // Keep sprite frame and flip state synced if target sprite animates
                SyncSpriteWithTarget();
            }
        }

        /// <summary>
        /// Enables or disables the focus outline with a smooth fade.
        /// </summary>
        public void SetHighlighted(bool highlighted)
        {
            _targetAlpha = highlighted ? _outlineColor.a : 0f;
            if (highlighted && _outlineObject != null && !_outlineObject.activeSelf)
            {
                _outlineObject.SetActive(true);
                SyncSpriteWithTarget();
            }
        }

        private void EnsureOutlineRenderer()
        {
            if (_targetRenderer == null || _outlineRenderer != null) return;

            Transform existing = _targetRenderer.transform.Find("[FocusOutline]");
            if (existing != null)
            {
                _outlineObject = existing.gameObject;
                _outlineRenderer = _outlineObject.GetComponent<SpriteRenderer>();
            }
            else
            {
                _outlineObject = new GameObject("[FocusOutline]");
                _outlineObject.transform.SetParent(_targetRenderer.transform, false);
                _outlineObject.transform.localPosition = Vector3.zero;
                _outlineObject.transform.localRotation = Quaternion.identity;
                _outlineRenderer = _outlineObject.AddComponent<SpriteRenderer>();
            }

            _outlineObject.transform.localScale = new Vector3(_outlineScaleMultiplier.x, _outlineScaleMultiplier.y, 1f);

            if (_targetRenderer.sharedMaterial != null)
            {
                _outlineRenderer.sharedMaterial = _targetRenderer.sharedMaterial;
            }

            SyncSpriteWithTarget();
        }

        /// <summary>
        /// Synchronizes the rim outline renderer with the target sprite.
        /// Preserves Simple draw mode to avoid Unity's Tight-mesh Full Rect warning while dynamically
        /// scaling the outline transform to match any Sliced or Tiled target dimensions.
        /// </summary>
        private void SyncSpriteWithTarget()
        {
            if (_outlineRenderer == null || _targetRenderer == null) return;

            if (_outlineRenderer.sprite != _targetRenderer.sprite)
            {
                _outlineRenderer.sprite = _targetRenderer.sprite;
            }

            if (_outlineRenderer.flipX != _targetRenderer.flipX)
            {
                _outlineRenderer.flipX = _targetRenderer.flipX;
            }

            if (_outlineRenderer.flipY != _targetRenderer.flipY)
            {
                _outlineRenderer.flipY = _targetRenderer.flipY;
            }

            // Always keep outline renderer in Simple draw mode to prevent Unity's
            // "Sprite Tiling might not appear correctly because the Sprite used is not generated with Full Rect"
            // warning when interactables use Sliced/Tiled drawMode on Tight-mesh sprites (e.g. built-in Square).
            // Instead, we scale the outline transform to match the target's rendered size.
            if (_outlineRenderer.drawMode != SpriteDrawMode.Simple)
            {
                _outlineRenderer.drawMode = SpriteDrawMode.Simple;
            }

            Vector3 targetScale = new Vector3(_outlineScaleMultiplier.x, _outlineScaleMultiplier.y, 1f);
            if (_targetRenderer.drawMode != SpriteDrawMode.Simple && _targetRenderer.sprite != null)
            {
                Vector2 spriteSize = _targetRenderer.sprite.rect.size / _targetRenderer.sprite.pixelsPerUnit;
                if (spriteSize.x > 0.0001f && spriteSize.y > 0.0001f)
                {
                    targetScale.x = (_targetRenderer.size.x / spriteSize.x) * _outlineScaleMultiplier.x;
                    targetScale.y = (_targetRenderer.size.y / spriteSize.y) * _outlineScaleMultiplier.y;
                }
            }

            if (_outlineObject != null && _outlineObject.transform.localScale != targetScale)
            {
                _outlineObject.transform.localScale = targetScale;
            }

            if (_outlineRenderer.sortingLayerID != _targetRenderer.sortingLayerID)
            {
                _outlineRenderer.sortingLayerID = _targetRenderer.sortingLayerID;
            }

            int targetSortingOrder = _targetRenderer.sortingOrder - 1;
            if (_outlineRenderer.sortingOrder != targetSortingOrder)
            {
                _outlineRenderer.sortingOrder = targetSortingOrder;
            }
        }

        private void ApplyAlphaImmediate(float alpha)
        {
            _currentAlpha = alpha;
            UpdateOutlineVisuals();
        }

        private void UpdateOutlineVisuals()
        {
            if (_outlineRenderer == null) return;

            SyncSpriteWithTarget();
            Color c = _outlineColor;
            c.a = _currentAlpha;
            _outlineRenderer.color = c;

            bool shouldBeActive = _currentAlpha > 0.001f || _targetAlpha > 0.001f;
            if (_outlineObject != null && _outlineObject.activeSelf != shouldBeActive)
            {
                _outlineObject.SetActive(shouldBeActive);
            }
        }
    }
}
