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

        private const string k_SutureShaderName = "Universal Render Pipeline/2D/AnimatedSutureLine";
        private const string k_FallbackShaderName = "Sprites/Default";

        /// <summary>
        /// Creates the standard 5-key width profile curve for suture thread lines (tapered waist, flared endpoints).
        /// </summary>
        public static AnimationCurve CreateWidthProfile()
        {
            AnimationCurve curve = new AnimationCurve();
            curve.AddKey(0.00f, 1.45f);
            curve.AddKey(0.05f, 1.05f);
            curve.AddKey(0.50f, 0.75f);
            curve.AddKey(0.95f, 1.05f);
            curve.AddKey(1.00f, 1.45f);
            return curve;
        }

        /// <summary>
        /// Creates a material using the specified override or finds the URP 2D suture shader (falling back to Sprites/Default).
        /// </summary>
        public static Material CreateSutureMaterial(Material materialOverride, Color color, float scrollSpeed, float tiling = 2.5f)
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
            }

            return mat;
        }
    }
}
