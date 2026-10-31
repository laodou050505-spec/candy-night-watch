// 坩埚汤药液面：缓慢旋转的漩涡纹 + 随机冒起的气泡圈 + 边缘泛光；自发光，Bloom 下发绿光。
// _Fill（0..1）控制颜色亮度（坩埚里装的敌人越多越亮），_Boil 控制气泡频率（拍锅沿时加快）。
Shader "TrickOrFreeze/Potion"
{
    Properties
    {
        [HDR] _Deep ("深处色", Color) = (0.15, 0.55, 0.25, 1)
        [HDR] _Light ("亮色", Color) = (0.55, 1.6, 0.6, 1)
        [HDR] _Bubble ("气泡色", Color) = (0.9, 2.0, 0.9, 1)
        _Swirl ("漩涡速度", Float) = 0.35
        _Boil ("沸腾", Range(0, 3)) = 1
        _Fill ("满度", Range(0, 1)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Light, _Bubble; float _Swirl, _Boil, _Fill;
            CBUFFER_END
            struct A { float4 p : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h21(i), h21(i + float2(1,0)), f.x), lerp(h21(i + float2(0,1)), h21(i + 1), f.x), f.y); }
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.p = TransformObjectToHClip(i.p.xyz); o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float2 q = (i.uv - 0.5) * 2;                 // -1..1
                float r = length(q), a = atan2(q.y, q.x);
                float t = _Time.y;
                // 漩涡：角度随半径扭转并随时间旋转
                float sw = sin(a * 3 + r * 7 - t * _Swirl * 6) * 0.5 + 0.5;
                float n = vn(q * 4 + float2(t * 0.2, -t * 0.15)) * 0.6 + vn(q * 9 - t * 0.3) * 0.4;
                half3 col = lerp(_Deep.rgb, _Light.rgb, saturate(sw * 0.45 + n * 0.55) * (0.6 + _Fill * 0.8));
                // 气泡：每个格子里周期性鼓起一个小圆环
                float2 g = q * 5; float2 gi = floor(g); float2 gf = frac(g) - 0.5;
                float seed = h21(gi);
                float ph = frac(t * (0.25 + seed * 0.35) * _Boil + seed * 7);
                float2 off = (float2(h21(gi + 3), h21(gi + 7)) - 0.5) * 0.5;
                float br = ph * 0.38;
                float ring = smoothstep(0.035, 0.0, abs(length(gf - off) - br)) * (1 - ph) * step(0.35, seed);
                col += _Bubble.rgb * ring;
                // 边缘暗、中心亮
                col *= lerp(1.15, 0.65, smoothstep(0.5, 1.0, r));
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
