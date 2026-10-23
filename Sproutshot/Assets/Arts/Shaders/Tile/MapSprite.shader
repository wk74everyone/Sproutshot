Shader "Sproutshot/2D/Map Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [PerRendererData] _FlashColor ("Flash Color", Color) = (1, 1, 1, 1)
        [PerRendererData] _FlashAmount ("Flash Amount", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "Map Sprite Unlit"
            Tags { "LightMode" = "Universal2D" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                half4 flashColor : TEXCOORD1;
                half flashAmount : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(EnemyFlash)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FlashColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _FlashAmount)
            UNITY_INSTANCING_BUFFER_END(EnemyFlash)

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                SetUpSpriteInstanceProperties();
                input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
                float3 worldPosition = TransformObjectToWorld(input.positionOS);
                output.positionHCS = TransformWorldToHClip(worldPosition);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.color = input.color * _Color * unity_SpriteColor;
                output.flashColor = UNITY_ACCESS_INSTANCED_PROP(EnemyFlash, _FlashColor);
                output.flashAmount = UNITY_ACCESS_INSTANCED_PROP(EnemyFlash, _FlashAmount);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
                clip(source.a - 0.001h);
                source.rgb = lerp(source.rgb, input.flashColor.rgb, saturate(input.flashAmount));

                return source;
            }
            ENDHLSL
        }
    }

    Fallback "Sprites/Default"
}
