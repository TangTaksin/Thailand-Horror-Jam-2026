Shader "Universal Render Pipeline/2D/AnimatedSutureLine"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.95, 0.95, 1.0, 0.95)
        [MainTexture] _BaseMap("Texture", 2D) = "white" {}
        _ScrollSpeed("Scroll Speed", Float) = 2.5
        _Tiling("Tiling Frequency", Float) = 2.5
        _DashRatio("Dash Ratio", Range(0.1, 0.9)) = 0.65
        [Toggle] _UseProceduralDash("Use Procedural Suture Dashes", Float) = 1.0
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
                float _ScrollSpeed;
                float _Tiling;
                float _DashRatio;
                float _UseProceduralDash;
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
