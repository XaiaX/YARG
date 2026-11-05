Shader "YARG/HiHatSustain"
{
    Properties
    {
        _MainTex("Soft sustain profile", 2D) = "white" {}
        _MotionStrength("Ribbon motion", Range(0, 1)) = 1
        _PatternStrength("Shimmer strength", Range(0, 1)) = .75
        _WaveStrength("Dark sound-wave contrast", Range(0, .6)) = .4
        _PulseStrength("Beat brightness pulse", Range(0, .15)) = .08
        _VisualTime("Visual song time", Float) = 0
        _BeatPhase("Visual quarter-note phase", Float) = 0
        _FarZ("Far fade", Float) = 8
        _HeadRange("Cymbal head footprint", Vector) = (0, 1, 0, 0)
        _GlowLevels("Waiting and held brightness", Vector) = (.35, .7, 0, 0)
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
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Art/Shaders/highways.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Hashes.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float _VisualTime;
                float _FarZ;
                float _BeatPhase;
                float _MotionStrength;
                float _PatternStrength;
                float _WaveStrength;
                float _PulseStrength;
                float4 _HeadRange;
                float4 _GlowLevels;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float2 fade : TEXCOORD1;
                float2 state : TEXCOORD2;
                float4 waves : TEXCOORD3;
                float3 envelope : TEXCOORD4;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 fade : TEXCOORD1;
                float4 color : COLOR;
                float z : TEXCOORD2;
                float2 state : TEXCOORD3;
                float4 waves : TEXCOORD4;
                float3 envelope : TEXCOORD5;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = YargTransformWorldToHClip(TransformObjectToWorld(input.positionOS.xyz));
                output.uv = input.uv;
                output.fade = input.fade;
                output.state = input.state;
                output.waves = input.waves;
                output.envelope = input.envelope;
                output.color = input.color;
                output.z = input.positionOS.z;
                return output;
            }
            // pattern: Functional Core
            // Independent local flashes, reconstructed solely from chart UVs and
            // explicit song time. No shared frame clock regenerates the whole field.
            float TransientShimmer(float2 uv, float time)
            {
                float2 grid = uv * float2(18, 48);
                float2 cell = floor(grid);
                float3 seed;
                Hash_Tchou_2_3_float(cell, seed);
                float rate = lerp(2.1, 3.4, seed.x);
                float clock = time * rate + seed.y * 11;
                float age = frac(clock) / rate;
                float3 event;
                Hash_Tchou_3_3_float(float3(cell, floor(clock)), event);
                float lifetime = lerp(.06, .105, event.z);
                float flash = smoothstep(0, .008, age) * (1 - smoothstep(.018, lifetime, age));
                float2 center = .3 + event.xy * .4;
                float2 radius = float2(.15, .065);
                // Derivatives of unwrapped coordinates avoid seams at cell edges.
                // Fade under minification instead of producing subpixel flicker.
                float2 footprint = max(fwidth(grid), .0001);
                float2 aa = min(footprint * .5, radius * .5);
                float2 shape = 1 - smoothstep(radius * .25, radius + aa, abs(frac(grid) - center));
                float energy = saturate(radius.x / footprint.x) * saturate(radius.y / footprint.y);
                return shape.x * shape.y * energy * flash;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float held = saturate(input.fade.y);
                float activity = held * (1 - saturate(input.state.x));
                // Breathe around the actual hi-hat axis; never translate the ribbon.
                float center = (_HeadRange.x + _HeadRange.y) * .5;
                float reach = max(center, 1 - center);
                float lateral = input.uv.x - center;
                float width = _HeadRange.z > 0 ? _HeadRange.z : 1;
                float fullRadius = max(reach * width, .0001);
                float fretRadius = min(fullRadius, _HeadRange.w > 0 ? _HeadRange.w * .5 : fullRadius);
                // Waiting tails flare over their entire length. Activation and
                // connected-run carry smoothly widen that shape to full width.
                float expansion = input.state.y > .5 ? 1
                    : lerp(saturate(input.envelope.z), 1, saturate(input.envelope.x));
                float radius = lerp(fretRadius, fullRadius, expansion);
                float wave = sin(50 * (input.uv.y * .055 + _VisualTime * .1)) * .009;
                wave += sin(input.uv.y * 8.5 - _VisualTime * 3.2) * .0035;
                float across = abs(lateral) * width / max(radius, .0001) * (1 + wave * 2 * _MotionStrength * activity);
                // Keep a broad body across the pedal footprint. Secondary's narrow
                // imported alpha formerly collapsed the visible tail to a thin line.
                // Its center provides opacity only; the envelope defines the width.
                half4 silhouette = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, float2(.5, .5), 0);
                // Endings fade flatly across the highway; no circular end cap.
                float diffuse = 1 - smoothstep(.58, 1, across);
                half profile = diffuse * silhouette.r * sqrt(saturate(silhouette.a));
                float edge = smoothstep(0, .07, input.uv.x) * smoothstep(0, .07, 1 - input.uv.x);
                float glint = TransientShimmer(float2(abs(lateral), input.uv.y), _VisualTime) * _PatternStrength * activity;
                half3 body = input.color.rgb * lerp(_GlowLevels.x, _GlowLevels.y, held);
                half3 sparkle = lerp(input.color.rgb, half3(1, 1, 1), .65) * glint;
                // Parabolic wavefronts bow away from the base. Four spatial
                // cycles per quarter give sixteenth spacing; UV4.y is the slow clock.
                float worldX = lateral * width;
                float wavePhase = input.waves.x * 4 + worldX * worldX * 2.2 - input.waves.y;
                float aa = min(fwidth(wavePhase) * .5, .06);
                float bandDistance = abs(frac(wavePhase + .5) - .5);
                // Bell-shaped wings blend across most of the spacing instead
                // of ending at a distinct stripe boundary.
                float bandWidth = .18 + aa;
                float band = exp2(-bandDistance * bandDistance / (bandWidth * bandWidth));
                body *= 1 - band * _WaveStrength * activity;
                float beat = .5 + .5 * cos(_BeatPhase * 6.2831853);
                float pulse = 1 + _PulseStrength * beat * beat * beat * beat * activity;
                float remaining = saturate(input.fade.x);
                // Logarithmic decay keeps the front bright; a soft final third
                // dissipates visibly instead of looking opaque until the last pixel.
                float naturalFade = log2(1 + 15 * remaining) / 4 * smoothstep(0, .35, remaining);
                float fade = input.envelope.y > 1.5 ? 1 : input.envelope.y > .5 ? smoothstep(0, 1, remaining) : naturalFade;
                // Width animates independently; the base stays rectangular with
                // a short, flat, soft front edge rather than a circular fan.
                float startFeather = max(.025, (_HeadRange.w > 0 ? _HeadRange.w : width) * .2);
                float startFade = smoothstep(0, startFeather, input.waves.w);
                float distanceFade = saturate(_FarZ - input.z);
                return half4((body + sparkle) * pulse,
                    profile * edge * fade * startFade * input.color.a * distanceFade);
            }
            ENDHLSL
        }
    }
}
