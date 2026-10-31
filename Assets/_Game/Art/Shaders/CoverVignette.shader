// 封面“身后渐黑”罩子：一个罩住头部的球（看它的内表面），按视线方向和封面正前方的夹角决定黑度——
//   正前方 ±_Clear 度内完全透明（庄园花园、漂浮小布景、标题按钮都清清楚楚），
//   往两侧慢慢变暗，到 ±_Black 度以后全黑；没有硬边，转头时是平滑过渡（10-28 反馈：不要一刀切）。
// 另外地平线以下再压暗一点，让视线自然落在前方的庄园和标题上。
// 最后绘制、不写深度、ZTest Always：罩住它后面的一切；正前方透明，所以前面的东西不受影响。
Shader "TrickOrFreeze/CoverVignette"
{
    Properties
    {
        _Forward ("封面正前方(世界)", Vector) = (0, 0, -1, 0)
        _Clear ("全透明半角(度)", Range(0, 180)) = 70
        _Black ("全黑半角(度)", Range(0, 180)) = 150
        _Floor ("地面压暗", Range(0, 1)) = 0.35
        _Tint ("黑色调", Color) = (0.012, 0.008, 0.025, 1)
        _Fade ("整体强度", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+60" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Cull Front
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Forward; half4 _Tint; half _Clear, _Black, _Floor, _Fade;
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
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.wp - GetCameraPositionWS());
                float3 f = normalize(float3(_Forward.x, 0, _Forward.z));
                // 只按水平方位角算（抬头低头不会忽明忽暗）
                float3 dh = normalize(float3(d.x, 0, d.z) + 1e-5);
                float ang = degrees(acos(clamp(dot(dh, f), -1, 1)));
                float a = smoothstep(_Clear, _Black, ang);
                a = a * a * (3 - 2 * a);                       // 再柔一点
                float below = saturate(-d.y * 1.6) * _Floor;   // 往下看：地面略暗
                a = saturate(max(a, below));
                return half4(_Tint.rgb, a * _Fade);
            }
            ENDHLSL
        }
    }
}
