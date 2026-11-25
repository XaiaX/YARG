Shader "YARG/Elite Double Kick Warning"
{
    Properties
    {
        _FlameAtlas ("Circular fret flame particles", 2D) = "white" {}
        _SparkAtlas ("Circular sustain sparks", 2D) = "white" {}
        _SongSeconds ("Song time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Art/Shaders/highways.hlsl"

            TEXTURE2D(_FlameAtlas);
            SAMPLER(sampler_FlameAtlas);
            TEXTURE2D(_SparkAtlas);
            SAMPLER(sampler_SparkAtlas);
            CBUFFER_START(UnityPerMaterial)
                float _SongSeconds;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float4 sparkCells : TEXCOORD1;
            };

            float2 sparkCell(float phase)
            {
                float frame = floor(frac(phase) * 4.0);
                return float2(fmod(frame, 2.0), 1.0 - floor(frame * .5));
            }

            Varyings vert(float3 positionOS : POSITION, float4 uv : TEXCOORD0, float2 spark : TEXCOORD1)
            {
                Varyings output;
                output.positionCS = YargTransformWorldToHClip(TransformObjectToWorld(positionOS));
                output.uv = uv;
                // Circular sustain's four-frame loops take 100 ms (inner) and 150 ms (outer).
                output.sparkCells.xy = sparkCell(_SongSeconds * 10.0 + spark.x);
                output.sparkCells.zw = spark.y > .5 ? sparkCell(_SongSeconds / .15 + spark.x) : float2(-1, -1);
                return output;
            }

            // Each rising puff uses one of the four silhouettes in the Circular fret atlas.
            half flamePuff(float2 uv, float phase, float offset, float tile)
            {
                float life = frac(phase);
                float2 center = float2(.5 + offset * (.3 + life), .13 + .68 * life);
                float2 size = float2(.47, .40) * (1.0 - .69 * life);
                float2 sprite = (uv - center) / size + .5;
                float inside = all(sprite >= 0) && all(sprite <= 1);
                float2 cell = float2(fmod(tile, 2.0), floor(tile * .5));
                half shape = SAMPLE_TEXTURE2D(_FlameAtlas, sampler_FlameAtlas, (cell + saturate(sprite)) * .5).a;
                return shape * inside * smoothstep(0, .12, life) * (1.0 - smoothstep(.63, 1, life));
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv.xy;
                float x = abs(uv.x * 2.0 - 1.0);
                if (input.uv.z > 1.5 && input.uv.z < 2.5)
                {
                    float t = _SongSeconds * 2.25;
                    float flame = max(flamePuff(uv, t, -.20, 0), flamePuff(uv, t + .45, .21, 1));
                    flame = max(flame, flamePuff(uv, t + .70, -.04, 2));
                    float core = saturate(1.0 - length((uv - float2(.5, .13)) * float2(3.0, 3.8)));
                    return half4(lerp(half3(.90, .13, .025), half3(1.0, .78, .22), core),
                        saturate(flame * .9 + core));
                }

                float endFade = 1.0 - smoothstep(.80, 1.0, uv.y);
                if (input.uv.z > 2.5)
                {
                    float2 card = float2(input.uv.w, uv.x);
                    bool hasOuter = input.sparkCells.z >= 0;
                    // Both layers meet at the foot of the kick. The inner spray is 3/4 as large.
                    float2 anchor = float2(.5, 0);
                    float2 innerUV = hasOuter ? (card - anchor) / .75 + anchor : card;
                    half4 inner = SAMPLE_TEXTURE2D(_SparkAtlas, sampler_SparkAtlas,
                        (input.sparkCells.xy + saturate(innerUV)) * .5);
                    half3 bright = lerp(half3(.85, .21, .025), half3(1.0, .75, .24), inner.r);
                    if (!hasOuter) return half4(bright, inner.a * endFade);

                    inner.a *= all(innerUV >= 0) && all(innerUV <= 1);
                    half4 outer = SAMPLE_TEXTURE2D(_SparkAtlas, sampler_SparkAtlas, (input.sparkCells.zw + card) * .5);
                    half3 warm = lerp(half3(.68, .115, .015), half3(1.0, .45, .095), outer.r);
                    half behind = outer.a * (1.0 - inner.a);
                    half alpha = inner.a + behind;
                    return half4((bright * inner.a + warm * behind) / max(alpha, .0001), alpha * endFade);
                }

                float core = 1.0 - smoothstep(.14, .86, x);
                return half4(lerp(half3(.79, .17, .025), half3(1.0, .64, .15), core),
                    endFade * (.50 + core * .44) * (1.0 - smoothstep(.82, 1.0, x)));
            }
            ENDHLSL
        }
    }
}
