Shader "Sproutshot/2D/Natural Map Ground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Primary Ground", 2D) = "white" {}
        _SecondaryTex ("Secondary Ground", 2D) = "white" {}
        _FieldTex ("Terrain Blend", 2D) = "black" {}
        _MapSize ("Map Size", Vector) = (1,1,0,0)
        _GroundSize ("Painted Ground Size", Vector) = (1,1,0,0)
        _TileSize ("Texture Repeat Size", Vector) = (1,1,0,0)
        _PrimaryRect ("Primary Texture Rect", Vector) = (0,0,1,1)
        _SecondaryRect ("Secondary Texture Rect", Vector) = (0,0,1,1)
        _Color ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "Continuous Natural Ground"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes { float3 positionOS:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionHCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float2 fieldUV:TEXCOORD1; float2 worldXY:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_SecondaryTex); SAMPLER(sampler_SecondaryTex);
            TEXTURE2D(_FieldTex); SAMPLER(sampler_FieldTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MapSize, _GroundSize, _TileSize, _PrimaryRect, _SecondaryRect;
                half4 _Color;
            CBUFFER_END
            float Hash21(float2 p) { p=frac(p*float2(123.34,345.45)); p+=dot(p,p+34.345); return frac(p.x*p.y); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash21(i),Hash21(i+float2(1,0)),f.x),lerp(Hash21(i+float2(0,1)),Hash21(i+1),f.x),f.y);
            }
            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                input.positionOS=UnityFlipSprite(input.positionOS,unity_SpriteProps.xy);
                float3 world=TransformObjectToWorld(input.positionOS);
                o.positionHCS=TransformWorldToHClip(world);
                float2 normalizedUV=(input.uv-_PrimaryRect.xy)/max(_PrimaryRect.zw,float2(.001,.001));
                float2 groundLocal=(normalizedUV-.5)*_GroundSize.xy;
                o.uv=normalizedUV*_GroundSize.xy/max(_TileSize.xy,float2(.001,.001));
                o.fieldUV=groundLocal/max(_MapSize.xy,float2(.001,.001))+.5;
                o.worldXY=world.xy; o.color=input.color*_Color*unity_SpriteColor;
                return o;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 tileUV=frac(input.uv);
                half4 primary=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,_PrimaryRect.xy+tileUV*_PrimaryRect.zw);
                float2 secondaryUV=_SecondaryRect.xy+tileUV*_SecondaryRect.zw;
                half4 secondary=SAMPLE_TEXTURE2D(_SecondaryTex,sampler_SecondaryTex,secondaryUV);
                half field=SAMPLE_TEXTURE2D(_FieldTex,sampler_FieldTex,saturate(input.fieldUV)).r;
                float boundaryNoise=Noise(input.worldXY*1.25)*.16-.08;
                float blend=smoothstep(.18,.76,saturate(field+boundaryNoise));
                half3 ground=lerp(primary.rgb,secondary.rgb,blend);
                return half4(ground,1)*input.color;
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
