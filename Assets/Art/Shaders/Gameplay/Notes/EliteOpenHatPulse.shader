Shader "YARG/Elite Open Hat Pulse"
{
    Properties
    {
        _FloorGain ("Minimum fraction of well brightness", Range(0, 1)) = 1
        _Disc ("Local XZ center and inverse radii", Vector) = (0, 0, 1, 1)
        [Enum(Ghost,0,Neutral,1,Accent,2)] _GlowRole ("Glow level", Float) = 1
        _PulsesPerBeat ("Pulses per quarter note", Float) = 1
        _WholeSurface ("Pulse the entire well together", Float) = 0
        _UseLidShadow ("Use baked lid shadow", Float) = 0
        _SweepDirection ("Clockwise sweep (negative for lefty)", Float) = 0
        _PreviewBeatRate ("Layout preview beats per second", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "DisableBatching"="True" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            // Scale the lit backing and moving noise together, before bloom.
            Blend DstColor Zero
            ColorMask RGB
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Art/Shaders/highways.hlsl"
            #include "Assets/Art/Shaders/Gameplay/Notes/EliteFlamGlowRipple.hlsl"

            // Relative adjustments from Experimental settings: ghost, neutral, accent, minimum.
            float4 _YargOpenHatGlowAdjustments;
            // x adjusts the authored floor, y adjusts the authored 50% shadow peak.
            float4 _YargOpenHatShadowAdjustments;
            CBUFFER_START(UnityPerMaterial)
                float4 _Disc;
                float _FloorGain;
                float _PulsesPerBeat;
                float _GlowRole;
                float _WholeSurface;
                float _SweepDirection;
                float _UseLidShadow;
                float _PreviewBeatRate;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 disc : TEXCOORD0;
                float phase : TEXCOORD1;
                float acrossBeam : TEXCOORD2;
                float2 glowRange : TEXCOORD3;
            };

            Varyings vert(float3 positionOS : POSITION, half4 lidLight : COLOR)
            {
                Varyings output;
                output.positionCS = YargTransformWorldToHClip(TransformObjectToWorld(positionOS));
                output.disc = (positionOS.xz - _Disc.xy) * _Disc.zw;
                output.phase = YargHatPulsePhase(_PulsesPerBeat, _PreviewBeatRate);
                float peak = 1.0 + _YargOpenHatGlowAdjustments[(int)_GlowRole];
                float floorGain = _FloorGain * (1.0 + _YargOpenHatGlowAdjustments.w);
                output.glowRange = float2(min(peak, floorGain), peak);
                if (_UseLidShadow > 0.5)
                {
                    float2 shadedRange = (0.2 * peak).xx;
                    if (_PulsesPerBeat > 0)
                    {
                        float shadedPeak = peak * 0.5 * (1.0 + _YargOpenHatShadowAdjustments.y);
                        float shadedFloor = _FloorGain * (1.0 + _YargOpenHatShadowAdjustments.x);
                        shadedRange = float2(min(shadedPeak, shadedFloor), shadedPeak);
                    }
                    // The baked RGB masks follow the three lid sizes; their edges soften both endpoints.
                    output.glowRange = lerp(shadedRange, output.glowRange, lidLight[(int)_GlowRole]);
                }
                output.acrossBeam = 0;
                if (_SweepDirection != 0)
                {
                    // A diameter repeats after half a turn. One bright pass per selected pulse.
                    float s, c;
                    sincos(PI * output.phase, s, c);
                    output.acrossBeam = dot(output.disc, float2(s * _SweepDirection, c));
                }
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float glow = 1;
                float phase = input.phase;
                if (_SweepDirection != 0 && phase >= 0)
                {
                    // cos² of the angle to the rotating diameter, with a bright common hub.
                    float radiusSq = dot(input.disc, input.disc);
                    glow = saturate(1.0 - input.acrossBeam * input.acrossBeam / max(radiusSq, 1e-6));
                }
                else if (phase >= 0 && _PulsesPerBeat > 0)
                {
                    // Mirror the same eased curve on both halves of the beat.
                    glow = _WholeSurface > 0.5 ? YargWholeSurfaceGlow(phase)
                        : YargQuarterNoteGlow(length(input.disc), phase);
                }
                return lerp(input.glowRange.x, input.glowRange.y, glow).xxxx;
            }
            ENDHLSL
        }
    }
}
