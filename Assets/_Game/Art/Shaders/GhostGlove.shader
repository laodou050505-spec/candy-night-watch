// 教学示范手（幽灵手套）：半透明青色，边缘（菲涅尔）发亮，整体轻轻呼吸。
// 先写一遍深度（不出颜色），再用普通透明混合画正面——同一部件内部的背面不会透出来；
// 不用加色混合：加在亮橙色桌面上会发白，看不出是只手。
Shader "TrickOrFreeze/GhostGlove"
{
    Properties
    {
        [HDR] _Color ("主体色", Color) = (0.22, 0.78, 0.95, 1)
        [HDR] _Rim ("边缘光", Color) = (0.85, 1.35, 1.45, 1)
        _Alpha ("整体透明度", Range(0, 1)) = 0.85
        _Fade ("淡入淡出", Range(0, 1)) = 1
        [HDR] _EmissionColor ("捏合/托灯发光（HandView 用 PropertyBlock 写）", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            ColorMask 0
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target { return 0; }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color, _Rim, _EmissionColor; half _Alpha, _Fade;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.wp = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 v = normalize(GetWorldSpaceViewDir(i.wp));
                float f = 1 - saturate(dot(normalize(i.n), v));
                f = f * f;
                half breathe = 0.85 + 0.15 * sin(_Time.y * 3.2);
                // 朝上的面略亮（顶光），让手指的体积看得出来
                half top = saturate(normalize(i.n).y * 0.5 + 0.5);
                half3 col = _Color.rgb * (0.7 + 0.35 * top) + _Rim.rgb * f * 1.2 + _EmissionColor.rgb * 2;
                half glow = saturate(dot(_EmissionColor.rgb, half3(0.33, 0.33, 0.33)));
                half a = saturate((0.42 + f * 0.6 + glow) * _Alpha * breathe) * _Fade;
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
