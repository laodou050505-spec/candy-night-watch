// 云海：大平面，顶点按噪声起伏成棉花糖云团，颜色随高度在紫与粉之间渐变；远处与天空融合（软边、雾）
Shader "TrickOrFreeze/CloudSea"
{
    Properties
    {
        _Low ("云底色", Color) = (0.32,0.2,0.52,1)
        _High ("云顶色", Color) = (0.78,0.62,0.95,1)
        _DawnLow ("黎明云底", Color) = (0.82,0.48,0.55,1)
        _DawnHigh ("黎明云顶", Color) = (1.0,0.82,0.72,1)
        _Dawn ("天亮", Range(0,1)) = 0
        _Height ("起伏高度", Float) = 2.2
        _Scale ("云团尺度", Float) = 0.045
        _Speed ("流动速度", Float) = 0.015
        _FadeStart ("淡出起点", Float) = 60
        _FadeEnd ("淡出终点", Float) = 160
        _RimColor ("月光边", Color) = (0.62,0.9,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-50" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Low, _High, _DawnLow, _DawnHigh, _RimColor;
                float _Dawn, _Height, _Scale, _Speed, _FadeStart, _FadeEnd;
            CBUFFER_END
            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1,0)), f.x), lerp(hash21(i + float2(0,1)), hash21(i + float2(1,1)), f.x), f.y); }
            float fbm(float2 p) { float s = 0, a = 0.55; for (int k = 0; k < 4; k++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }
            // 圆鼓鼓的云团：把噪声做“膨胀”变形，顶部更圆
            float puff(float2 p) { float n = fbm(p); return pow(saturate(n * 1.35 - 0.15), 0.7); }

            struct A { float4 p : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p : SV_POSITION; float3 w : TEXCOORD0; float h : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 w = TransformObjectToWorld(i.p.xyz);
                float2 q = w.xz * _Scale + float2(_Time.y * _Speed, _Time.y * _Speed * 0.6);
                float h = puff(q);
                w.y += h * _Height;
                o.w = w; o.h = h;
                o.p = TransformWorldToHClip(w);
                o.fog = ComputeFogFactor(o.p.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 q = i.w.xz * _Scale + float2(_Time.y * _Speed, _Time.y * _Speed * 0.6);
                float e = 0.6;
                float hx = puff(q + float2(e * _Scale, 0)) - puff(q - float2(e * _Scale, 0));
                float hz = puff(q + float2(0, e * _Scale)) - puff(q - float2(0, e * _Scale));
                float3 n = normalize(float3(-hx * _Height * 0.8, 1.0 * e * 2, -hz * _Height * 0.8));
                Light L = GetMainLight();
                half ndl = saturate(dot(n, L.direction) * 0.5 + 0.5);
                half k = smoothstep(0.35, 0.65, ndl) * 0.6 + i.h * 0.4;
                half3 low = lerp(_Low.rgb, _DawnLow.rgb, _Dawn), high = lerp(_High.rgb, _DawnHigh.rgb, _Dawn);
                half3 col = lerp(low, high, saturate(k));
                float3 v = normalize(GetWorldSpaceViewDir(i.w));
                col += _RimColor.rgb * pow(1 - saturate(dot(n, v)), 3) * 0.35 * (1 - _Dawn * 0.5);
                float dist = length(i.w.xz - GetCameraPositionWS().xz);
                float a = 1 - smoothstep(_FadeStart, _FadeEnd, dist);
                col = MixFog(col, i.fog);
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
