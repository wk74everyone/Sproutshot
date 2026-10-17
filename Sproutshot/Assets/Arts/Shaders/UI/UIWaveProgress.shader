Shader "Sproutshot/UI/Wave Progress Bar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)

        [Header(Progress)]
        [Range(0, 1)] _Progress ("Progress", Float) = 0.5
        [Enum(Left To Right, 0, Right To Left, 1)] _Direction ("Direction", Int) = 0

        [Header(Wave)]
        [Range(0, 0.15)] _WaveAmplitude ("Wave Amplitude", Float) = 0.025
        [Range(0.5, 12)] _WaveFrequency ("Wave Count", Float) = 3
        [Range(0, 5)] _WaveSpeed ("Wave Speed", Float) = 1
        [Range(0.0001, 0.02)] _EdgeSoftness ("Edge Softness", Float) = 0.002

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
            "RenderPipeline" = "UniversalPipeline"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Wave Progress"

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct Attributes
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 positionOS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;
            float _Progress;
            int _Direction;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;
            float _EdgeSoftness;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionOS = input.vertex;
                output.positionCS = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                fixed4 color = (tex2D(_MainTex, input.uv) + _TextureSampleAdd) * input.color;
                float progress = saturate(_Progress);

                // Convert both fill directions to a common 0 -> 1 progress axis.
                float progressAxis = _Direction == 0 ? input.uv.x : 1.0 - input.uv.x;

                // Two harmonics keep the moving edge organic without requiring a noise texture.
                float phase = (input.uv.y * _WaveFrequency + _Time.y * _WaveSpeed) * UNITY_TWO_PI;
                float wave = sin(phase) + sin(phase * 1.73 - _Time.y * _WaveSpeed * 2.11) * 0.25;
                wave *= 0.8;

                // Fade the displacement near the two endpoints so progress stays in its bounds.
                float endpointFade = saturate(min(progress, 1.0 - progress) / max(_WaveAmplitude, 0.0001));
                float front = progress + wave * _WaveAmplitude * endpointFade;
                float antialiasWidth = max(fwidth(progressAxis), _EdgeSoftness);
                float fillMask = 1.0 - smoothstep(front - antialiasWidth, front + antialiasWidth, progressAxis);

                // Guarantee exact empty/full output at the endpoints.
                fillMask = progress <= 0.00001 ? 0.0 : fillMask;
                fillMask = progress >= 0.99999 ? 1.0 : fillMask;

                color.a *= fillMask;

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.positionOS.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                // UI uses premultiplied alpha with Blend One OneMinusSrcAlpha.
                color.rgb *= color.a;
                return color;
            }
            ENDCG
        }
    }

    Fallback "UI/Default"
}
