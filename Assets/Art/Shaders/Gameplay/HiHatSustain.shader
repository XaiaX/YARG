Shader "YARG/HiHatSustain"
{
    Properties
    {
        _MainTex("Soft sustain profile", 2D) = "white" {}
        _NoiseTex("Cymbal highlight pattern", 2D) = "gray" {}
        _MotionStrength("Ribbon motion", Range(0, 1)) = 1
        _PatternStrength("Moving highlights", Range(0, 1)) = .75
        _PulseStrength("Beat brightness pulse", Range(0, .15)) = .08
        _VisualTime("Visual song time", Float) = 0
        _BeatPhase("Visual quarter-note phase", Float) = 0
        _FarZ("Far fade", Float) = 8
        _HeadRange("Cymbal head footprint", Vector) = (0, 1, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Art/Shaders/highways.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex);
            SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float _VisualTime;
                float _FarZ;
                float _BeatPhase;
                float _MotionStrength;
                float _PatternStrength;
                float _PulseStrength;
                float4 _HeadRange;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 fade : TEXCOORD1;
                float2 onset : TEXCOORD2;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 fade : TEXCOORD1;
                float4 color : COLOR;
                float z : TEXCOORD2;
                float2 onset : TEXCOORD3;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = YargTransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv;
                output.fade = input.fade;
                output.onset = input.onset;
                output.color = input.color;
                output.z = input.positionOS.z;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float held = input.fade.y;
                float activity = input.color.a > .2 ? lerp(.3, 1, held) : 0;
                // Reuse the highway's sine-based horizontal UV warp. Its inputs
                // use explicit song time so pausing and replay seeking are stable.
                float wave = sin(50 * (input.uv.y * .055 + _VisualTime * .1)) * .09;
                wave += sin(input.uv.y * 8.5 - _VisualTime * 3.2) * .035;
                float2 flow = float2(input.uv.y * .7 - _VisualTime * .22,
                    input.uv.x * .9 + _VisualTime * .14);
                // The texture repeats. Keep UVs unwrapped so mip derivatives stay
                // continuous across tile boundaries instead of producing stripes.
                float first = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, flow).r;
                float detail = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex,
                    flow.yx * float2(.8, .6) + float2(first * .35, -first * .3)).r;
                float offset = (wave + (first - detail) * .035) * _MotionStrength * activity;
                float across = input.uv.x + offset;
                // Keep a broad body across the pedal footprint. Secondary's narrow
                // imported alpha formerly collapsed the visible tail to a thin line.
                // Its center provides opacity only; the envelope defines the width.
                half4 silhouette = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, float2(.5, .5), 0);
                float diffuse = 1 - smoothstep(.58, 1, abs(2 * across - 1));
                half profile = diffuse * silhouette.r * sqrt(saturate(silhouette.a));
                float edge = smoothstep(0, .07, input.uv.x) * smoothstep(0, .07, 1 - input.uv.x);
                // Like the cymbal shader, retain a floor under the moving dark areas.
                float highlights = lerp(1, max(.4, detail), _PatternStrength * activity);
                float beat = .5 + .5 * cos(_BeatPhase * 6.2831853);
                float pulse = 1 + _PulseStrength * beat * beat * beat * beat * activity;
                float remaining = saturate(input.fade.x);
                // Logarithmic decay keeps the front bright; a soft final third
                // dissipates visibly instead of looking opaque until the last pixel.
                float fade = log2(1 + 15 * remaining) / 4 * smoothstep(0, .35, remaining);
                // Grow only the shoulders beyond the actual cymbal column. Use
                // unwarped U so motion cannot erase the normal-opacity center.
                float left = max(0, (_HeadRange.x - input.uv.x) / max(_HeadRange.x, .0001));
                float right = max(0, (input.uv.x - _HeadRange.y) / max(1 - _HeadRange.y, .0001));
                float shoulder = max(left, right);
                float growth = shoulder > 0 ? smoothstep(0, 1, saturate(input.onset.x) / max(shoulder, .0001)) : 1;
                float onset = lerp(growth, 1, input.onset.y); // splash heads are already wide
                float distanceFade = saturate(_FarZ - input.z);
                return half4(input.color.rgb * lerp(.9, 1.8, held) * highlights * pulse,
                    profile * edge * fade * onset * input.color.a * distanceFade);
            }
            ENDHLSL
        }
    }
}
