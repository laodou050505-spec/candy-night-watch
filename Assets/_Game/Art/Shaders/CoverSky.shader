// 封面夜空（罩住整个封面的大球，看内表面）：程序生成，不用贴图——
//   竖直渐变：头顶深靛 → 中段梅紫 → 地平线一道暖橙（万圣节海报那种“橙紫撞色”的天）；
//   月亮方向一大圈暖色辉光；星星按方向网格哈希撒点、各自慢慢闪；地平线以下压暗成深紫。
Shader "TrickOrFreeze/CoverSky"
{
    Properties
    {
        _Top ("头顶", Color) = (0.035, 0.02, 0.09, 1)
        _Mid ("中段", Color) = (0.2, 0.08, 0.3, 1)
        _Horizon ("地平线", Color) = (0.95, 0.42, 0.2, 1)
        _Ground ("地平线以下", Color) = (0.06, 0.03, 0.11, 1)
        _MoonDir ("月亮方向(世界)", Vector) = (-0.27, 0.38, -0.88, 0)
        _MoonGlow ("月亮辉光", Color) = (1, 0.62, 0.42, 1)
        _Stars ("星星亮度", Range(0, 2)) = 1
        _Fade ("淡入", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-499" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Mid, _Horizon, _Ground, _MoonGlow; float4 _MoonDir; half _Stars, _Fade;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 wp : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wp = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.wp);
                return o;
            }
            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.wp - GetCameraPositionWS());
                float y = d.y;
                half3 c = lerp(_Mid.rgb, _Top.rgb, smoothstep(0.05, 0.75, y));
                // 地平线暖橙：越靠月亮那一侧越浓
                float3 md = normalize(_MoonDir.xyz);
                float side = saturate(dot(normalize(float3(d.x, 0, d.z) + 1e-5), normalize(float3(md.x, 0, md.z) + 1e-5)) * 0.5 + 0.5);
                float hz = exp(-abs(y - 0.02) * 9) * (0.45 + 0.55 * side * side);
                c = lerp(c, _Horizon.rgb, saturate(hz));
                // 月亮辉光
                float m = saturate(dot(d, md));
                c += _MoonGlow.rgb * (pow(m, 60) * 0.55 + pow(m, 12) * 0.22);
                // 星星：方位/仰角网格里随机撒点，地平线附近和月亮旁边少一些
                float2 sp = float2(atan2(d.x, d.z) * 60, asin(clamp(y, -1, 1)) * 60);
                float2 cell = floor(sp); float2 f = frac(sp) - 0.5;
                float h = hash(cell);
                float2 off = float2(hash(cell + 3.1), hash(cell + 7.7)) - 0.5;
                float star = step(0.93, h) * smoothstep(0.09, 0.0, length(f - off * 0.6));
                float tw = 0.6 + 0.4 * sin(_Time.y * (1.5 + h * 3) + h * 40);
                c += star * tw * _Stars * saturate(y * 4) * (1 - pow(m, 6)) * half3(1, 0.95, 0.85);
                // 地平线以下
                c = lerp(c, _Ground.rgb, smoothstep(0.0, -0.12, y));
                return half4(c * _Fade, 1);
            }
            ENDHLSL
        }
    }
}
