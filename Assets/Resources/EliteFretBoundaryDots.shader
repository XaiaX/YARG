Shader "YARG/Elite Fret Boundary Dots"
{
    Properties { [HideInInspector] _Diamond ("Diamond", Float) = 0 }
    SubShader
    {
        // Between the highway surface (2950) and the translucent kick fill (2952).
        Tags { "RenderType"="Transparent" "Queue"="Transparent-49" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/Art/Shaders/highways.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Diamond;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 dotPosition : TEXCOORD0;
                half3 color : COLOR;
            };

            Varyings vert(float3 positionOS : POSITION, float2 dotPosition : TEXCOORD0, half4 tint : COLOR)
            {
                Varyings output;
                output.positionCS = YargTransformWorldToHClip(TransformObjectToWorld(positionOS));
                output.dotPosition = dotPosition;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                tint.rgb = SRGBToLinear(tint.rgb);
                #endif
                output.color = lerp(half3(.74, .78, .80), tint.rgb, tint.a);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float distanceFromCenter = _Diamond > .5
                    ? 1.25 * abs(input.dotPosition.x) + abs(input.dotPosition.y)
                    : length(input.dotPosition);
                float edgeWidth = max(fwidth(distanceFromCenter), .001);
                half opacity = .62 * (1.0 - smoothstep(1.0 - edgeWidth, 1.0, distanceFromCenter));
                return half4(input.color, opacity);
            }
            ENDHLSL
        }
    }
}
