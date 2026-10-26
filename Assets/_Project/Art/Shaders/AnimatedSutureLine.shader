Shader "Universal Render Pipeline/2D/AnimatedSutureLine"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.92, 0.15, 0.15, 0.95)
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        _ScrollSpeed("Scroll Speed", Float) = 2.5
        _Tiling("Tiling Frequency", Float) = 2.5
        _DashRatio("Dash Ratio", Range(0.1, 0.9)) = 0.65
        [Toggle] _UseProceduralDash("Use Procedural Suture Dashes", Float) = 0.0
        [Toggle] _EnableOutline("Enable Pixel Outline", Float) = 1.0
        _OutlineColor("Outline Color", Color) = (0.08, 0.06, 0.08, 1.0)
        _OutlineThickness("Outline Band Thickness", Range(0.05, 0.45)) = 0.333
        [Toggle] _UsePixelStyle("Use Solid Pixel Style (No Soft Fade)", Float) = 1.0
    }

    SubShader
    { 
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
            "RenderPipeline"="UniversalPipeline" 
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Unlit"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float4 _OutlineColor;
                float _ScrollSpeed;
                float _Tiling;
                float _DashRatio;
                float _UseProceduralDash;
                float _EnableOutline;
                float _OutlineThickness;
                float _UsePixelStyle;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color * _BaseColor;

                // Animate UV horizontally along the line using GPU Time
                float2 uv = input.uv;
                uv.x = (uv.x * _Tiling) - (_Time.y * _ScrollSpeed);
                output.uv = uv;

                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float alpha = input.color.a;

                // 1. Pixel Style with Optional Outline (Default for Retro Pixel Art)
                if (_UsePixelStyle > 0.5)
                {
                    // Check top and bottom outline bands
                    float edgeDist = min(input.uv.y, 1.0 - input.uv.y);
                    if (_EnableOutline > 0.5 && edgeDist < _OutlineThickness)
                    {
                        return float4(_OutlineColor.rgb, _OutlineColor.a * alpha);
                    }

                    // Core fill (solid crisp color)
                    return float4(input.color.rgb, alpha);
                }

                // 2. Legacy / Alternative Soft Procedural Dash
                if (_UseProceduralDash > 0.5)
                {
                    // Procedural moving suture dashes (ForeignGods style)
                    float dashPattern = frac(input.uv.x);
                    float dashMask = smoothstep(_DashRatio + 0.05, _DashRatio - 0.05, dashPattern);

                    // Soft round edge along thread thickness (Y axis 0..1)
                    float yDist = abs(input.uv.y - 0.5) * 2.0;
                    float edgeSoftness = 1.0 - (yDist * yDist);

                    alpha *= (dashMask * edgeSoftness);
                }
                else
                {
                    float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                    alpha *= tex.a;
                }

                return float4(input.color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
