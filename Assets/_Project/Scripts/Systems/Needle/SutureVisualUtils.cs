using UnityEngine;

namespace ThailandHorrorJam.Systems.Needle
{
    /// <summary>
    /// Shared visual presets and utilities for SutureThread and NeedleController line rendering.
    /// </summary>
    public static class SutureVisualUtils
    {
        public static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        public static readonly int ScrollSpeedId = Shader.PropertyToID("_ScrollSpeed");
        public static readonly int TilingId = Shader.PropertyToID("_Tiling");
        public static readonly int EnableOutlineId = Shader.PropertyToID("_EnableOutline");
        public static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        public static readonly int OutlineThicknessId = Shader.PropertyToID("_OutlineThickness");
        public static readonly int UsePixelStyleId = Shader.PropertyToID("_UsePixelStyle");
        public static readonly int UseProceduralDashId = Shader.PropertyToID("_UseProceduralDash");
        public static readonly int DashRatioId = Shader.PropertyToID("_DashRatio");

        private const string k_SutureShaderName = "Universal Render Pipeline/2D/AnimatedSutureLine";
        private const string k_FallbackShaderName = "Sprites/Default";

        /// <summary>
        /// Creates a uniform flat width profile curve for crisp pixel-art line rendering.
        /// </summary>
        public static AnimationCurve CreateWidthProfile()
        {
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(1f, 1f);
            return curve;
        }

        /// <summary>
        /// Creates a material using the specified override or finds the URP 2D suture shader (falling back to Sprites/Default).
        /// </summary>
        public static Material CreateSutureMaterial(
            Material materialOverride,
            Color color,
            float scrollSpeed,
            float tiling = 2.5f,
            bool enableOutline = true,
            Color? outlineColor = null,
            float outlineThickness = 0.333f,
            bool useProceduralDash = false,
            float dashRatio = 0.5f)
        {
            Material mat = null;
            if (materialOverride != null)
            {
                mat = new Material(materialOverride);
            }
            else
            {
                Shader shader = Shader.Find(k_SutureShaderName);
                if (shader == null) shader = Shader.Find(k_FallbackShaderName);
                if (shader != null)
                {
                    mat = new Material(shader);
                }
            }

            if (mat != null)
            {
                mat.SetColor(BaseColorId, color);
                mat.SetFloat(ScrollSpeedId, scrollSpeed);
                mat.SetFloat(TilingId, tiling);
                mat.SetFloat(EnableOutlineId, enableOutline ? 1f : 0f);
                mat.SetColor(OutlineColorId, outlineColor ?? new Color(0.08f, 0.06f, 0.08f, 1f));
                mat.SetFloat(OutlineThicknessId, outlineThickness);
                mat.SetFloat(UsePixelStyleId, 1f);
                mat.SetFloat(UseProceduralDashId, useProceduralDash ? 1f : 0f);
                mat.SetFloat(DashRatioId, dashRatio);
            }

            return mat;
        }
    }
}
