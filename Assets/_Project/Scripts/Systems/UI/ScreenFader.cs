using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ThailandHorrorJam.Systems.UI
{
    /// <summary>
    /// Persistent singleton screen fader overlay (DontDestroyOnLoad).
    /// Provides smooth black/solid color fades across scene transitions and gameplay wake-up moments.
    /// Works purely in code without relying on specific shader materials or canvas sorting bugs.
    /// </summary>
    public class ScreenFader : MonoBehaviour
    {
        private static ScreenFader _instance;
        public static ScreenFader Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<ScreenFader>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("[ScreenFader]");
                        _instance = go.AddComponent<ScreenFader>();
                    }
                }
                return _instance;
            }
        }

        private Canvas _canvas;
        private CanvasGroup _canvasGroup;
        private Image _overlayImage;
        private Coroutine _fadeRoutine;

        public float CurrentAlpha => _canvasGroup != null ? _canvasGroup.alpha : 0f;
        public bool IsFading => _fadeRoutine != null;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
            BuildUI();
        }

        private void BuildUI()
        {
            if (_canvas != null) return;

            // 1. Root Canvas
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32760; // Absolute top of all UI layers

            // 2. Scaler & Raycaster
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            // 3. CanvasGroup for butter-smooth alpha lerping
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;

            // 4. Fullscreen overlay Image
            GameObject imgObj = new GameObject("ScreenOverlay");
            imgObj.transform.SetParent(transform, false);

            RectTransform rect = imgObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;

            _overlayImage = imgObj.AddComponent<Image>();
            _overlayImage.color = Color.black;
            _overlayImage.raycastTarget = false;
        }

        /// <summary>
        /// Sets the color of the screen overlay (e.g. Color.black, Color.white).
        /// </summary>
        public void SetColor(Color color)
        {
            if (_canvasGroup == null) BuildUI();
            if (_overlayImage != null)
            {
                _overlayImage.color = color;
            }
        }

        /// <summary>
        /// Immediately sets overlay opacity.
        /// </summary>
        public void SetAlpha(float alpha)
        {
            if (_canvasGroup == null) BuildUI();
            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
            }
            _canvasGroup.alpha = Mathf.Clamp01(alpha);
            _canvasGroup.blocksRaycasts = _canvasGroup.alpha > 0.01f;
        }

        /// <summary>
        /// Fades the screen overlay to black (alpha -> 1.0).
        /// </summary>
        public void FadeOut(float duration, Action onComplete = null)
        {
            SetColor(Color.black);
            FadeTo(1f, duration, onComplete);
        }

        /// <summary>
        /// Fades the screen overlay to white (alpha -> 1.0).
        /// </summary>
        public void FadeOutWhite(float duration, Action onComplete = null)
        {
            SetColor(Color.white);
            FadeTo(1f, duration, onComplete);
        }

        /// <summary>
        /// Fades the screen overlay to clear (alpha -> 0.0), revealing gameplay.
        /// </summary>
        public void FadeIn(float duration, Action onComplete = null)
        {
            FadeTo(0f, duration, onComplete);
        }

        public void FadeTo(float targetAlpha, float duration, Action onComplete = null)
        {
            if (_canvasGroup == null) BuildUI();

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
            }

            _fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, duration, onComplete));
        }

        private IEnumerator FadeRoutine(float targetAlpha, float duration, Action onComplete)
        {
            float startAlpha = _canvasGroup.alpha;
            targetAlpha = Mathf.Clamp01(targetAlpha);

            _canvasGroup.blocksRaycasts = targetAlpha > 0.01f;

            if (duration <= 0.001f)
            {
                _canvasGroup.alpha = targetAlpha;
                _canvasGroup.blocksRaycasts = targetAlpha > 0.01f;
                _fadeRoutine = null;
                onComplete?.Invoke();
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                // Cinematic smooth curve
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                _canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, smoothT);
                yield return null;
            }

            _canvasGroup.alpha = targetAlpha;
            _canvasGroup.blocksRaycasts = targetAlpha > 0.01f;
            _fadeRoutine = null;
            onComplete?.Invoke();
        }
    }
}
