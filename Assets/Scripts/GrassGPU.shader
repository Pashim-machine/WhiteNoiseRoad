Shader "Custom/GrassGPUInstanced"
{
    Properties
    {
        _BaseMap ("Albedo (RGB) Alpha (A)", 2D) = "white" {}
        _Color ("Color Tint", Color) = (1,1,1,1)
        _AlphaClip ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
        }
        LOD 100
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "Forward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            float4 _Color;
            float _AlphaClip;

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings o = (Varyings)0;

                // Используем классический инстансинг: матрица из unity_ObjectToWorld
                float4 worldPos = mul(unity_ObjectToWorld, input.vertex);

                o.positionCS = TransformWorldToHClip(worldPos.xyz);
                o.uv = input.uv;
                return o;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                clip(texColor.a - _AlphaClip);
                return texColor * _Color;
            }
            ENDHLSL
        }
    }
}