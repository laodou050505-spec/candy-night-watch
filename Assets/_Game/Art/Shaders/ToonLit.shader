// 《一二三·木头鬼》卡通着色器（URP）
//  - 2~3 阶色带（可调软硬），暗部偏紫（_ShadowTint），与 Tripo 手绘贴图相乘
//  - 月光青边缘光 + 南瓜暖色自发光（遮罩 = 贴图亮度或独立通道）
//  - 冰霜（定身）：菲涅尔 + 噪声冰晶，_Frost 0~1
//  - 黑暗剪影：_Silhouette 0~1（黑猫/熄灯夜）
//  - 蓄力金光：_Charge 0~1
//  - 外描边：第二个 Pass 背面法线外扩，宽度按屏幕距离补偿（远近粗细一致）
Shader "TrickOrFreeze/ToonLit"
{
    Properties
    {
        _BaseMap ("贴图", 2D) = "white" {}
        _BaseColor ("颜色", Color) = (1,1,1,1)
        _ShadowTint ("暗部色", Color) = (0.24,0.23,0.42,1)
        _MidTint ("中间调", Color) = (0.7,0.72,0.88,1)
        _Steps ("色带阈值(暗/中)", Vector) = (0.18, 0.55, 0, 0)
        _Soft ("色带过渡", Range(0.001, 0.3)) = 0.06
        _RimColor ("边缘光", Color) = (0.62,0.91,1,1)
        _RimPower ("边缘光宽度", Range(0.5, 8)) = 3.2
        _RimStrength ("边缘光强度", Range(0, 2)) = 0.55
        [HDR] _EmissionColor ("自发光", Color) = (0,0,0,1)
        _EmissionMap ("自发光遮罩（不贴=全亮，由自发光颜色决定）", 2D) = "white" {}
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除(手套用双面)", Float) = 2
        _OutlineColor ("描边颜色", Color) = (0.165,0.094,0.22,1)
        _OutlineWidth ("描边宽度", Range(0, 0.02)) = 0.0045
        _OutlinePush ("描边后推（米）", Range(0, 0.02)) = 0
        _Frost ("冰霜", Range(0,1)) = 0
        _FrostColor ("冰霜色", Color) = (0.75,0.92,1,1)
        _Silhouette ("剪影", Range(0,1)) = 0
        _Charge ("蓄力光", Range(0,1)) = 0
        [Toggle] _Cutout ("透明裁剪", Float) = 0
        _Cutoff ("裁剪阈值", Range(0,1)) = 0.5
        _Alpha ("整体透明度", Range(0,1)) = 1
        _Wiggle ("扭动", Range(0,1)) = 0
        _WigglePhase ("扭动相位", Float) = 0
        _WiggleHeight ("模型高度(扭动参考)", Float) = 0.2
        _Stride ("迈腿幅度", Range(0,1)) = 0
        _StridePhase ("迈腿相位", Float) = 0
        _StrideHip ("髋高(模型空间米)", Float) = 0.07
        _StrideFwd ("模型正面(模型空间 xz)", Vector) = (-1, 0, 0, 0)
        _StrideQuad ("四足(对角步)", Float) = 0
        _Flap ("扇翅幅度(弧度)", Float) = 0
        _FlapPhase ("扇翅相位", Float) = 0
        _FlapBias ("翅膀抬起角(弧度)", Float) = 0
        _FlapSpan ("半翼展(模型空间,沿Z)", Float) = 0.1
        _FlapRoot ("翼根离中线(模型空间，身体外侧)", Float) = 0.042
        _FlapPivotY ("肩高(模型空间)", Float) = 0.055
        _ColorLine ("彩色勾线(0=墨色 1=本色提亮)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _ShadowTint, _MidTint, _RimColor, _EmissionColor, _OutlineColor, _FrostColor;
            float4 _Steps;
            half _Soft, _RimPower, _RimStrength, _OutlineWidth, _Frost, _Silhouette, _Charge, _Cutout, _Cutoff, _Alpha;
            float _OutlinePush;
            float _Wiggle, _WigglePhase, _WiggleHeight;
            float _Stride, _StridePhase, _StrideHip, _StrideQuad; float4 _StrideFwd;
            float _Flap, _FlapPhase, _FlapBias, _FlapSpan, _FlapRoot, _FlapPivotY, _ColorLine;
        CBUFFER_END

        // 顶点扭动：离模型中心越远（四肢、翅膀、帽尖、衣摆）摆得越大，像在蹬腿/扑腾。_Wiggle=0 时不动。
        // 扇翅（蝙蝠）：翼展沿模型 Z。翼根外侧的顶点绕肩部（翼根处、肩高）在 YZ 平面里转动，越往翼尖转得越多（翅膀有点弯），
        // 身体不动。角度 = sin(相位)*幅度 + 抬起角。_Flap=0 且 _FlapBias=0 时不变形。
        float3 FlapOS(float3 p)
        {
            if (abs(_Flap) + abs(_FlapBias) <= 0.0001) return p;
            float az = abs(p.z);
            float d = az - _FlapRoot;
            if (d <= 0) return p;
            float tip = saturate(d / max(_FlapSpan - _FlapRoot, 1e-4));
            float ang = (sin(_FlapPhase) * _Flap + _FlapBias) * (0.55 + 0.45 * tip);
            float c = cos(ang), sn = sin(ang);
            float dy = p.y - _FlapPivotY;
            float nz = d * c - dy * sn;
            float ny = d * sn + dy * c;
            p.z = sign(p.z) * (_FlapRoot + nz);
            p.y = _FlapPivotY + ny;
            return p;
        }

        // 迈腿（10-29 用户：走路要有简单的走路动作）：单网格模型没有骨骼，按顶点高度把髋以下当成腿，
        // 左右两侧（四足按对角）交替绕髋前后摆，往前摆的那条腿抬起一点；身体正中（两腿之间）平滑过渡、不动。
        float3 StrideOS(float3 p)
        {
            if (_Stride <= 0.001) return p;
            float hip = max(_StrideHip, 0.005);
            float below = hip - p.y;
            if (below <= 0) return p;
            float2 f = normalize(_StrideFwd.xy + float2(1e-5, 0));
            float2 lat = float2(-f.y, f.x);
            float fw = dot(p.xz, f), lt = dot(p.xz, lat);
            float side = tanh(lt / 0.01);
            float sel = _StrideQuad > 0.5 ? side * tanh(fw / 0.01) : side;
            float ang = sin(_StridePhase) * sel * _Stride * 0.5;          // 最大约 ±29°
            float legK = saturate(below / hip);
            p.xz += f * (below * sin(ang));
            p.y += below * (1 - cos(ang)) + legK * legK * saturate(cos(_StridePhase) * sel) * _Stride * hip * 0.22;   // 往前迈的腿抬脚
            return p;
        }

        float3 WiggleOS(float3 p)
        {
            p = StrideOS(FlapOS(p));
            if (_Wiggle <= 0.001) return p;
            float h = max(_WiggleHeight, 0.001);
            float2 xz = p.xz;
            float r = length(xz) / h;                     // 水平离中轴的距离（相对身高）
            float y = p.y / h;
            float amp = _Wiggle * saturate(r * 1.6 - 0.15) * 0.12 * h;
            float side = sign(p.x + 1e-5);
            float w = sin(_WigglePhase * 2.3 + y * 6.0 + side * 1.7);
            p.y += w * amp;
            p.xz += normalize(xz + 1e-5) * cos(_WigglePhase * 2.3 + y * 4.0) * amp * 0.5;
            return p;
        }

        // 便宜的 3D 噪声（冰晶纹）
        float hash31(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
        float noise3(float3 x)
        {
            float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(hash31(i), hash31(i + float3(1,0,0)), f.x), lerp(hash31(i + float3(0,1,0)), hash31(i + float3(1,1,0)), f.x), f.y),
                        lerp(lerp(hash31(i + float3(0,0,1)), hash31(i + float3(1,0,1)), f.x), lerp(hash31(i + float3(0,1,1)), hash31(i + float3(1,1,1)), f.x), f.y), f.z);
        }
        ENDHLSL

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2;
                float3 positionOS : TEXCOORD3; half fog : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                v.positionOS.xyz = WiggleOS(v.positionOS.xyz);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionOS = v.positionOS.xyz;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half band(half ndl)
            {
                // 三阶：暗 / 中 / 亮
                half a = smoothstep(_Steps.x - _Soft, _Steps.x + _Soft, ndl);
                half b = smoothstep(_Steps.y - _Soft, _Steps.y + _Soft, ndl);
                return a * 0.5 + b * 0.5;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                if (_Cutout > 0.5) clip(tex.a - _Cutoff);
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light main = GetMainLight(shadowCoord);
                half ndl = saturate(dot(n, main.direction) * 0.5 + 0.5) * lerp(0.3, 1.0, main.shadowAttenuation);
                half k = band(ndl);
                half3 tint = lerp(_ShadowTint.rgb, lerp(_MidTint.rgb, 1.0.xxx, saturate(k * 2 - 1)), saturate(k * 2));
                half3 col = tex.rgb * tint * main.color;

                // 附加光（南瓜灯、掌心灯、蜡烛）：同样做色带，暖色叠加
                // Forward+（集群光源）下 GetAdditionalLightsCount() 恒为 0，必须用 LIGHT_LOOP_BEGIN 宏遍历；普通 Forward 下宏退化为 for 循环
                #if defined(_ADDITIONAL_LIGHTS) || USE_CLUSTER_LIGHT_LOOP
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                    uint pixelLightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(pixelLightCount)
                        Light l = GetAdditionalLight(lightIndex, i.positionWS, half4(1, 1, 1, 1));
                        // 真兰伯特 + 两阶色带：灯照到的一面亮、背面暗，距离衰减形成一圈圈暖色光池
                        half d = saturate(dot(n, l.direction));
                        half s = (smoothstep(0.05, 0.05 + _Soft * 2, d) * 0.55 + smoothstep(0.45, 0.45 + _Soft * 2, d) * 0.45) * l.distanceAttenuation * l.shadowAttenuation;
                        col += tex.rgb * l.color * s;
                    LIGHT_LOOP_END
                }
                #endif

                // 环境：天空球色（来自 SH），保证暗部不死黑
                col += tex.rgb * SampleSH(n) * 0.22;

                // 边缘光
                half rim = pow(1.0 - saturate(dot(n, v)), _RimPower) * _RimStrength;
                col += _RimColor.rgb * rim * (0.6 + 0.4 * k);

                // 自发光
                half3 em = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionColor.rgb + _EmissionColor.rgb * step(0.999, _EmissionColor.a) * 0;
                col += em;

                // 冰霜：冷色叠层 + 菲涅尔亮边 + 冰晶纹
                if (_Frost > 0.001)
                {
                    float crystal = smoothstep(0.62, 0.9, noise3(i.positionOS * 38.0)) * 0.6 + smoothstep(0.7, 0.95, noise3(i.positionOS * 90.0)) * 0.4;
                    half fres = pow(1.0 - saturate(dot(n, v)), 2.0);
                    half3 frost = lerp(col, _FrostColor.rgb * (0.75 + 0.25 * k), 0.55) + _FrostColor.rgb * (fres * 0.8 + crystal * 0.5);
                    col = lerp(col, frost, _Frost);
                }
                // 蓄力金光
                if (_Charge > 0.001)
                {
                    half fres = pow(1.0 - saturate(dot(n, v)), 1.5);
                    col += half3(1.0, 0.78, 0.3) * _Charge * (0.35 + fres * 1.2);
                }
                // 剪影：压暗到深紫，只保留发光部分
                col = lerp(col, half3(0.05, 0.035, 0.09) + em, _Silhouette * 0.95);

                col = MixFog(col, i.fog);
                return half4(col, _Alpha);
            }
            ENDHLSL
        }

        // 外描边
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half fog : TEXCOORD0; float2 uv : TEXCOORD1; half fade : TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                v.positionOS.xyz = WiggleOS(v.positionOS.xyz);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(v.normalOS));
                // 宽度随距离补偿：近处不粗（3 米内按距离加宽）；3~7 米渐隐、更远不画——
                // 远处描边只剩一两个像素宽，头显里会一直“爬动”闪烁（10-28 实机反馈；10-30 干草垛闪烁后再提前）
                float dist = distance(posWS, GetCameraPositionWS());
                float fade = 1.0 - smoothstep(3.0, 7.0, dist);
                // 10-30 用户：Scene 窗口里描边感弱、看着舒服；头显里 0.26° 宽约 5 像素太重 → 整体减到 6 成，3~7 米淡出
                float w = _OutlineWidth * 0.6 * clamp(dist, 0.35, 3.0) * fade;
                posWS += nWS * w;
                // 后推：描边壳沿视线往后挪一点，只在轮廓处露出来；手指缝这类很窄的凹槽里，壳不会从另一侧的表面穿出来变成断续黑线（10-30）
                posWS += normalize(posWS - GetCameraPositionWS()) * (_OutlinePush * fade);
                o.positionCS = TransformWorldToHClip(posWS);
                // 远处不外扩（w=0：描边壳和本体重合，背面被正面挡住），片元里再按 fade 丢弃。
                // 10-29 实机：以前把远处顶点塌到屏幕中心 (0,0,0,1)，近远混合的三角形被拉成从屏幕中心放射的长条，
                // 满天彩色三角扇面（用户以为是天空盒坏了）
                o.fade = fade;
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.uv = v.uv;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                if (_OutlineWidth <= 0.00001 || i.fade < 0.02) discard;
                half3 c = _OutlineColor.rgb;
                // 彩色勾线（环境物件）：取本体颜色，按最亮通道归一再提纯，得到同色系的亮线——南瓜勾橙线、草叶勾绿线
                // （参考卡通万圣节插画里霓虹似的彩色描边）；角色、手保持墨色线，和背景分开
                if (_ColorLine > 0.001)
                {
                    half3 alb = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
                    half m = max(max(alb.r, alb.g), max(alb.b, 1e-3));
                    half3 vivid = pow(saturate(alb / m), 1.8) * 0.92 + 0.04;
                    c = lerp(c, vivid, _ColorLine);
                }
                c = lerp(c, half3(0.55, 0.78, 0.95), _Frost * 0.7);
                c = lerp(c, half3(1.0, 0.75, 0.25), _Charge);
                c = MixFog(c, i.fog);
                return half4(c, _Alpha);
            }
            ENDHLSL
        }

        // 阴影投射 + 深度
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Universal Render Pipeline/Lit"
}
