Shader "Sproutshot/2D/Character Buff"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        [HDR] _BurnColor ("Burn", Color) = (2.8,.46,.045,1)
        [HDR] _FrostColor ("Frost", Color) = (.18,1.3,2.4,1)
        [HDR] _PoisonColor ("Poison", Color) = (.45,2.1,.12,1)
        [HDR] _ShockColor ("Shock", Color) = (1.4,.45,2.8,1)
        _EffectSpeed ("Animation Speed", Range(0,3)) = 1
        _Glow ("Glow", Range(0,3)) = .8
        _MixBoost ("Combined Buff Energy", Range(0,2)) = .8
        [PerRendererData] _BuffState ("Burn / Frost / Poison / Shock", Vector) = (0,0,0,0)
        [PerRendererData] _BuffBounds ("Local Bounds", Vector) = (0,0,1,1)
        [PerRendererData] _BuffUVRect ("Atlas UV Bounds", Vector) = (0,0,1,1)
        [PerRendererData] _BuffSeed ("Animation Phase", Float) = 0
        [PerRendererData] _FlashColor ("Flash Color", Color) = (1,1,1,1)
        [PerRendererData] _FlashAmount ("Flash Amount", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "Character Buff Unlit"
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _MainTex_TexelSize;
                half4 _Color, _BurnColor, _FrostColor, _PoisonColor, _ShockColor;
                float _EffectSpeed, _Glow, _MixBoost;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(CharacterEffects)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BuffState)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BuffBounds)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BuffUVRect)
                UNITY_DEFINE_INSTANCED_PROP(float, _BuffSeed)
                UNITY_DEFINE_INSTANCED_PROP(float4, _FlashColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _FlashAmount)
            UNITY_INSTANCING_BUFFER_END(CharacterEffects)
            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float2 localUV : TEXCOORD1;
                float4 state : TEXCOORD2;
                float4 uvRect : TEXCOORD3;
                half4 flash : TEXCOORD4;
                float seed : TEXCOORD5;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                // 先取原始局部坐标，外观镜像和图集打包不会改变纹理效果的尺寸。
                float4 bounds = UNITY_ACCESS_INSTANCED_PROP(CharacterEffects, _BuffBounds);
                o.localUV = (v.positionOS.xy - bounds.xy) / max(bounds.zw, .0001) + .5;
                o.state = UNITY_ACCESS_INSTANCED_PROP(CharacterEffects, _BuffState);
                o.uvRect = UNITY_ACCESS_INSTANCED_PROP(CharacterEffects, _BuffUVRect);
                o.seed = UNITY_ACCESS_INSTANCED_PROP(CharacterEffects, _BuffSeed);
                o.flash = half4(UNITY_ACCESS_INSTANCED_PROP(CharacterEffects, _FlashColor).rgb,
                    UNITY_ACCESS_INSTANCED_PROP(CharacterEffects, _FlashAmount));
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }
            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(cell), Hash(cell+float2(1,0)), f.x),
                    lerp(Hash(cell+float2(0,1)), Hash(cell+1), f.x), f.y);
            }
            half AlphaAt(float2 uv, float4 uvRect)
            {
                float inside = step(uvRect.x, uv.x) * step(uv.x, uvRect.z) *
                    step(uvRect.y, uv.y) * step(uv.y, uvRect.w);
                float2 safeUV = clamp(uv, uvRect.xy + abs(_MainTex_TexelSize.xy)*.5,
                    uvRect.zw - abs(_MainTex_TexelSize.xy)*.5);
                return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, safeUV).a * inside;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                half4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * i.color;
                clip(source.a - .001h);
                float4 w = saturate(i.state);
                float total = dot(w, float4(1,1,1,1));
                // 无 Buff 时跳过所有噪声和轮廓采样；动画由 GPU 时间推进，无需逐帧写材质。
                [branch] if (total > .001)
                {
                    float2 p = i.localUV;
                    float t = _Time.y * _EffectSpeed;
                    float seed = i.seed;
                    float n = Noise(p*8 + float2(seed, -t*1.8));
                    float fine = Noise(p*19 + float2(-t*.4, seed));
                    float2 texel = abs(_MainTex_TexelSize.xy)*1.5;
                    float neighbor = min(min(AlphaAt(i.uv+float2(texel.x,0),i.uvRect), AlphaAt(i.uv-float2(texel.x,0),i.uvRect)),
                        min(AlphaAt(i.uv+float2(0,texel.y),i.uvRect), AlphaAt(i.uv-float2(0,texel.y),i.uvRect)));
                    float rim = saturate(1-neighbor);
                    // 燃烧：向上流动的火舌和间歇火星。
                    float flame = smoothstep(.32,.82,n + (1-p.y)*.3);
                    float ember = pow(saturate(fine), 12)*4;
                    float fire = flame*(.25+.75*(1-p.y)) + ember;
                    // 冰霜：固定晶面裂纹，扫光沿冰面流动。
                    float crack = 1-smoothstep(.025,.095,abs(sin(p.x*26 + sin(p.y*17+seed)*2)));
                    crack = max(crack, (1-smoothstep(.025,.07,abs(sin((p.x+p.y)*23+seed))))*.8);
                    float glint = pow(saturate(.5+.5*sin((p.x-p.y)*10-t*2)),12);
                    float ice = .16+crack*.55+glint*.4;
                    // 中毒：漂浮的气泡环和流动毒液。
                    float2 bubbleCell = floor(p*7+float2(0,-t*.5));
                    float2 bubble = frac(p*7+float2(0,-t*.5))-.5;
                    float bubbleRadius = .12+Hash(bubbleCell+seed)*.18;
                    float bubbles = 1-smoothstep(.025,.065,abs(length(bubble)-bubbleRadius));
                    float poison = bubbles*.55 + smoothstep(.4,.8,fine)*.3;
                    // 雷电：不断改道的细电弧和快速脉冲。
                    float boltX = .5 + (Noise(float2(floor(p.y*26),floor(t*12)+seed))-.5)*.65;
                    float arc = 1-smoothstep(.008,.035,abs(p.x-boltX));
                    float shock = arc*(.7+.3*sin(t*29+seed)) + glint*.18;
                    float pairs = max(0,(total*total-dot(w,w))*.5);
                    float mix = saturate(pairs)*_MixBoost;
                    float weave = pow(saturate(.5+.5*sin((p.x+p.y)*28-t*5+n*5)),8);
                    half3 tint = (_BurnColor.rgb*w.x + _FrostColor.rgb*w.y + _PoisonColor.rgb*w.z + _ShockColor.rgb*w.w)/max(total,.001);
                    half3 energy = _BurnColor.rgb*w.x*fire + _FrostColor.rgb*w.y*ice +
                        _PoisonColor.rgb*w.z*poison + _ShockColor.rgb*w.w*shock;
                    // 多元素增强细节，而非简单相加到全白，保留角色底图的辨识度。
                    energy /= max(1, total*.8);
                    // 冰火叠加有蒸汽闪光；多元素叠加有彩色交织光纹和更强的内轮廓。
                    half3 spectrum = .55+.45*cos(float3(0,2.094,4.188)+t*2+p.y*9+seed);
                    float steam = w.x*w.y*smoothstep(.4,.85,n)*(.5+.5*sin(t*4+p.x*12));
                    source.rgb = lerp(source.rgb, source.rgb*.55 + tint*.22, saturate(total*.22));
                    source.rgb += _Glow*(energy*.45 + tint*rim*(.22+mix*.18) +
                        spectrum*mix*weave*.45 + half3(.7,.85,1)*steam*.25);
                }
                // 受伤闪色和攻击预警始终可见，不被 Buff 混合覆盖。
                source.rgb = lerp(source.rgb, i.flash.rgb, saturate(i.flash.a));
                return source;
            }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
