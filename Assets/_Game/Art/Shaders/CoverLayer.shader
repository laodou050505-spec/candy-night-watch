// 封面“纸剧场”的一层（远山小镇 / 鬼堡 / 墓园 / 框景大树 / 雾带 / 月亮）：不受光、按贴图透明度混合、不写深度。
// 各层的绘制先后靠 renderQueue 固定（远→近，排在天空盒之后、半透明特效之前），不靠物体中心排序（弧面中心都在玩家身上）。
// _Scroll：雾带缓慢水平流动；_Fade：进封面时淡入。
Shader "TrickOrFreeze/CoverLayer"
{
    Properties
    {
        _BaseMap ("贴图", 2D) = "white" {}
        _Tint ("颜色", Color) = (1, 1, 1, 1)
        _Scroll ("水平流动(每秒)", Float) = 0
        _Fade ("淡入", Range(0, 1)) = 1
        _EdgeFade ("左右两端渐隐(贴片宽度比例)", Range(0, 0.5)) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("混合(目标)", Float) = 10
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-498" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite Off
            Blend SrcAlpha [_DstBlend]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _Tint; float _Scroll; half _Fade; float _DstBlend; float _EdgeFade;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 raw : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap) + float2(_Time.y * _Scroll, 0);
                o.raw = i.uv;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 c = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _Tint;
                c.a *= _Fade;
                if (_EdgeFade > 0) c.a *= smoothstep(0, _EdgeFade, i.raw.x) * smoothstep(0, _EdgeFade, 1 - i.raw.x);
                return c;
            }
            ENDHLSL
        }
    }
}
