// 月亮圆盘：面片上画一个软边圆 + 程序化月海斑纹 + 边缘提亮，外圈一层淡光晕。不透明部分写深度，光晕透明混合。
Shader "TrickOrFreeze/MoonDisc"
{
    Properties
    {
        [HDR] _Color ("月面色", Color) = (1.15, 1.08, 0.92, 1)
        _Maria ("月海色", Color) = (0.82, 0.76, 0.86, 1)
        [HDR] _Halo ("光晕色", Color) = (0.75, 0.7, 1.0, 1)
        _HaloSize ("光晕范围", Range(0, 0.5)) = 0.38
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Maria, _Halo; float _HaloSize;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + 1), f.x), f.y); }
            half4 frag(V i) : SV_Target
            {
                // 面片 uv 0..1，月盘半径占 0.5-_HaloSize
                float2 p = (i.uv - 0.5) * 2;           // -1..1
                float r = length(p);
                float R = 1 - _HaloSize * 2;            // 月盘半径（在 -1..1 空间）
                float disc = smoothstep(R, R - 0.012, r);
                float2 q = p / R;
                float n = vnoise(q * 3.1 + 7) * 0.6 + vnoise(q * 7.3 + 3) * 0.3 + vnoise(q * 15 + 1) * 0.1;
                float maria = smoothstep(0.55, 0.72, n);
                half3 col = lerp(_Color.rgb, _Maria.rgb * _Color.rgb, maria * 0.55);
                col *= 1 + 0.12 * saturate(r / R);      // 边缘略亮（卡通月亮）
                float halo = saturate(1 - (r - R) / (1 - R)); halo = halo * halo * (1 - disc);
                half4 o = half4(col, disc);
                o.rgb = lerp(_Halo.rgb, o.rgb, disc);
                o.a = max(disc, halo * 0.55 * _Halo.a);
                return o;
            }
            ENDHLSL
        }
    }
}
