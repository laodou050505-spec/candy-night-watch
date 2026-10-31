// 万圣夜天空盒：深蓝紫夜空渐变 + 地平线一圈暖色辉光 + 漂移的云（近月处被月光镶银边）+ 闪烁星星 + 天亮过渡（_Dawn 0~1）
// 极光带保留参数（_AuroraStrength 默认 0，庄园花园场景不用）
// 月亮单独用面片（便于随进度移动并做体积光），这里只负责月亮周围的光晕（_MoonDir）。
// 远景：_Backdrop 全景剪影（R 远山远林 / G 近山与城堡教堂风车村屋枯树 / B 亮窗，由 Tools/make_sky_backdrop.py 生成），
//   城堡背后的绿色妖光、远处闪电（带分叉的电光 + 照亮云层）、绕城堡塔楼飞的蝙蝠、山坡上飘的鬼火、偶尔划过的流星。
Shader "TrickOrFreeze/HalloweenSky"
{
    Properties
    {
        _Top ("天顶", Color) = (0.035,0.04,0.12,1)
        _Mid ("中段", Color) = (0.12,0.1,0.3,1)
        _Horizon ("地平线", Color) = (0.3,0.2,0.42,1)
        _HorizonGlow ("地平线辉光", Color) = (0.75,0.38,0.32,1)
        _CloudDark ("云暗部", Color) = (0.07,0.06,0.15,1)
        _CloudLit ("云亮边", Color) = (0.62,0.62,0.85,1)
        _CloudCover ("云量", Range(0,1)) = 0.5
        _DawnTop ("黎明天顶", Color) = (0.36,0.42,0.72,1)
        _DawnHorizon ("黎明地平线", Color) = (1.0,0.66,0.52,1)
        _Dawn ("天亮", Range(0,1)) = 0
        _StarDensity ("星星密度", Range(50, 600)) = 260
        _StarBright ("星星亮度", Range(0, 4)) = 1.6
        _AuroraA ("极光色A", Color) = (0.35,0.95,0.78,1)
        _AuroraB ("极光色B", Color) = (0.68,0.42,1,1)
        _AuroraStrength ("极光强度", Range(0, 2)) = 0
        _MoonDir ("月亮方向", Vector) = (0, 0.32, -1, 0)
        _MoonGlow ("月晕", Color) = (0.86,0.82,1,1)
        _Backdrop ("远景剪影", 2D) = "black" {}
        _BackH0 ("剪影底边高度", Float) = -0.05
        _BackH1 ("剪影顶边高度", Float) = 0.42
        _BackRotate ("剪影旋转(圈)", Float) = 0
        _FarSil ("远层颜色", Color) = (0.17,0.12,0.29,1)
        _NearSil ("近层颜色", Color) = (0.06,0.045,0.11,1)
        _WindowCol ("窗光", Color) = (1.0,0.62,0.22,1)
        _AuraCol ("城堡妖光", Color) = (0.25,0.85,0.45,1)
        _CastleU ("城堡方位(u)", Float) = 0.70
        _Lightning ("闪电强度", Range(0,2)) = 1
        _WispCol ("鬼火", Color) = (0.45,1.0,0.8,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top, _Mid, _Horizon, _DawnTop, _DawnHorizon, _AuroraA, _AuroraB, _MoonGlow, _HorizonGlow, _CloudDark, _CloudLit;
                half _Dawn, _StarDensity, _StarBright, _AuroraStrength, _CloudCover;
                float4 _MoonDir;
                float4 _Backdrop_ST;
                half4 _FarSil, _NearSil, _WindowCol, _AuraCol, _WispCol;
                float _BackH0, _BackH1, _BackRotate, _CastleU, _Lightning;
            CBUFFER_END
            TEXTURE2D(_Backdrop); SAMPLER(sampler_Backdrop);
            struct A { float4 p : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p : SV_POSITION; float3 d : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            V vert(A i)
            {
                // 10-29：游戏天空不再走引擎的天空盒（头显上满天三角形、随转头变形），改画在一个 180 米的天空球上（SkyDome）。
                // 视线方向逐像素由“世界坐标 − 相机位置”算出，和网格怎么分三角形无关；深度钉在最远处，什么都能挡在它前面。
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 wp = TransformObjectToWorld(i.p.xyz);
                o.p = TransformWorldToHClip(wp); o.d = wp;
            #if UNITY_REVERSED_Z
                o.p.z = o.p.w * 1e-6;
            #else
                o.p.z = o.p.w * (1 - 1e-6);
            #endif
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * f * (f * (f * 6 - 15) + 10);   // 五次插值：格子边界处二阶连续，不显方块
                return lerp(lerp(hash21(i), hash21(i + float2(1,0)), f.x), lerp(hash21(i + float2(0,1)), hash21(i + float2(1,1)), f.x), f.y); }

            // 10-30 实机：云里能看到模糊的方块——每层噪声的格子都和坐标轴对齐、叠在同一方向。每层旋转约 37° 再放大，格子互相错开
            float fbm(float2 p) { float a = 0.5, s = 0; const float2x2 R = float2x2(0.8, -0.6, 0.6, 0.8); for (int k = 0; k < 5; k++) { s += a * vnoise(p); p = mul(R, p) * 2.03 + 17.1; a *= 0.5; } return s; }

            // 蝙蝠剪影：p 为以蝙蝠为中心、翼展半宽为 1 的局部坐标；flap -1..1 扇翅
            float batMask(float2 p, float flap)
            {
                float ax = abs(p.x);
                float top = 0.12 + flap * 0.55 * ax - 0.15 * ax * ax;
                float bottom = top - (0.42 * (1 - ax) + 0.1) + 0.13 * abs(sin(ax * 9.42));
                float wing = saturate((top - p.y) * 9) * saturate((p.y - bottom) * 9) * saturate((1 - ax) * 9);
                float body = saturate((0.2 - length(p * float2(1.8, 1.0))) * 12);
                float ears = saturate((0.07 - abs(ax - 0.07)) * 30) * saturate((p.y - 0.12) * 20) * saturate((0.3 - p.y) * 20);
                return saturate(max(max(wing, body), ears));
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.d - GetCameraPositionWS());
                float h = d.y;
                // 渐变
                half3 night = h > 0.15 ? lerp(_Mid.rgb, _Top.rgb, saturate((h - 0.15) / 0.75)) : lerp(_Horizon.rgb, _Mid.rgb, saturate((h + 0.15) / 0.3));
                half3 dawn = lerp(_DawnHorizon.rgb, _DawnTop.rgb, saturate(h * 1.4 + 0.2));
                // 地平线一圈低矮的暖色辉光（远处村镇灯火 / 篝火映天），压在雾色之上
                night += _HorizonGlow.rgb * exp(-pow(max(h, 0) * 9.0, 1.5)) * 0.45 * step(-0.05, h);
                half3 col = lerp(night, dawn, _Dawn);

                // 星星：在方向球面上分格取随机点，闪烁
                float2 uv = float2(atan2(d.x, d.z) / 6.2831853 + 0.5, asin(clamp(d.y, -1, 1)) / 3.1415927 + 0.5);
                float2 g = uv * float2(_StarDensity * 2, _StarDensity);
                float2 cell = floor(g); float2 f = frac(g) - 0.5;
                float r = hash21(cell);
                float2 off = float2(hash21(cell + 7.1), hash21(cell + 3.3)) - 0.5;
                float star = smoothstep(0.08, 0.0, length(f - off * 0.7)) * step(0.86, r);
                float tw = 0.6 + 0.4 * sin(_Time.y * (1.5 + r * 3.0) + r * 40.0);
                col += star * tw * _StarBright * saturate(h * 3) * (1 - _Dawn);

                // 极光：两条缓慢起伏的光带
                float ax = uv.x * 6.2831853;
                float band1 = exp(-pow((h - 0.42 - 0.08 * sin(ax * 2.0 + _Time.y * 0.07) - 0.04 * vnoise(float2(uv.x * 30, _Time.y * 0.1))) * 9.0, 2));
                float band2 = exp(-pow((h - 0.58 - 0.06 * sin(ax * 3.0 - _Time.y * 0.05)) * 11.0, 2));
                float curtain = 0.55 + 0.45 * vnoise(float2(uv.x * 80, h * 6 + _Time.y * 0.2));
                col += (_AuroraA.rgb * band1 + _AuroraB.rgb * band2 * 0.8) * curtain * _AuroraStrength * (1 - _Dawn * 0.8);

                // 月晕
                float md = saturate(dot(d, normalize(_MoonDir.xyz)));
                // 10-30 实机：最外层的宽光晕（pow 2 / pow 8）铺满半边天，被头顶藤架横梁切成一格一格的“模糊方块” → 只留月亮周围一圈
                col += _MoonGlow.rgb * (pow(md, 60) * 0.9 + pow(md, 22) * 0.16) * (1 - _Dawn * 0.6);

                // 云：投到高空平面上的分形噪声，缓慢漂移；近月处镶银边、远处是深色云影
                if (h > 0.02)
                {
                    float2 cp = d.xz / (h + 0.22) * 0.55 + float2(_Time.y * 0.004, _Time.y * 0.0015);
                    float n = fbm(cp * 1.6);
                    // 10-30 实机：云边缘过渡太宽，头显里是一团团糊掉的灰白色块 → 卡通云：边缘收窄成清楚的形状、整体淡一些
                    float cover = smoothstep(1 - _CloudCover + 0.02, 1 - _CloudCover + 0.08, n);
                    float rim = smoothstep(0.0, 0.25, n - (1 - _CloudCover)) * (1 - smoothstep(0.25, 0.6, n - (1 - _CloudCover)));
                    half3 cloud = lerp(_CloudDark.rgb, _CloudLit.rgb, saturate(pow(md, 6) * 1.5 + rim * pow(md, 2) * 0.6));
                    // 云只留在地平线上方一圈（仰角约 3°~25°）：头顶有藤架横梁和灯串，云从横梁缝里露出来，
                    // 被切成一格一格的“模糊方块”（10-30 实机）。头顶只留星空
                    float fade = saturate(h * 6) * (1 - smoothstep(0.22, 0.42, h));
                    col = lerp(col, cloud, cover * fade * 0.55 * (1 - _Dawn * 0.5));
                }

                // ======== 远景 ========
                float t = _Time.y;
                float nt = 1 - _Dawn;
                float au = frac(uv.x + _BackRotate);
                float daC = (frac(au - _CastleU + 0.5) - 0.5) * 6.2831853;        // 与城堡的方位差（弧度）
                // 城堡背后的绿色妖光（缓慢呼吸）
                col += _AuraCol.rgb * exp(-pow(daC / 0.22, 2) - pow((h - 0.16) / 0.16, 2)) * (0.32 + 0.1 * sin(t * 0.6)) * nt;
                // 闪电：每 7~12 秒一次，落在城堡附近；先亮一下、停顿、再亮一下；照亮云层并劈下一道带分叉的电光
                float cyc = floor(t / 9.3); float ph = frac(t / 9.3) * 9.3;
                float strike = step(0.3, hash21(float2(cyc, 3.7)));
                float flash = (exp(-ph * 10) + 0.7 * exp(-max(ph - 0.22, 0) * 14) * step(0.22, ph)) * strike * _Lightning * nt;
                float bu = daC - (hash21(float2(cyc, 1.3)) - 0.5) * 0.5;           // 电光的方位（相对城堡）
                col += half3(0.75, 0.8, 1.0) * flash * exp(-pow(bu / 0.5, 2) - pow((h - 0.3) / 0.18, 2)) * 0.55;
                if (h > 0.05 && h < 0.55)
                {
                    float bx = bu - 0.03 * (vnoise(float2(h * 22, cyc * 7.1)) - 0.5) - 0.012 * (vnoise(float2(h * 90, cyc * 3.3)) - 0.5);
                    float core = smoothstep(0.0022, 0.0, abs(bx)) + 0.35 * exp(-pow(bx / 0.012, 2));
                    float bh = h - 0.3; float fork = bu + 0.04 + bh * 0.25 - 0.015 * (vnoise(float2(h * 40, cyc * 5.0)) - 0.5);
                    core += step(h, 0.32) * step(0.2, h) * (smoothstep(0.0016, 0.0, abs(fork)) + 0.2 * exp(-pow(fork / 0.01, 2)));
                    col += half3(0.85, 0.9, 1.0) * core * saturate(flash * 3) * saturate((0.55 - h) * 8);
                }
                // 剪影：远层（雾里，闪电时被照亮一点）→ 近层 → 亮窗（逐窗轻微闪烁）
                float v = (h - _BackH0) / (_BackH1 - _BackH0);
                if (v < 1.0)
                {
                    float2 buv = float2(au, saturate(v));
                    float2 dx = ddx(buv), dy = ddy(buv);
                    float2 buv2 = float2(frac(au + 0.5), buv.y);                    // 方位接缝处用连续坐标求导，避免一条 mip 竖线
                    float2 dx2 = ddx(buv2), dy2 = ddy(buv2);
                    if (abs(dx2.x) + abs(dy2.x) < abs(dx.x) + abs(dy.x)) { dx = dx2; dy = dy2; }
                    half3 m = SAMPLE_TEXTURE2D_GRAD(_Backdrop, sampler_Backdrop, buv, dx, dy).rgb;
                    half3 farC = lerp(_FarSil.rgb, _Horizon.rgb * 0.9, 0.15) + half3(0.18, 0.2, 0.3) * flash * 0.4;
                    farC = lerp(farC, dawn * 0.75, _Dawn);
                    half3 nearC = lerp(_NearSil.rgb, dawn * 0.35, _Dawn);
                    col = lerp(col, farC, m.r);
                    col = lerp(col, nearC, m.g);
                    float flick = 0.75 + 0.25 * vnoise(float2(au * 1400, t * 1.7));
                    col += _WindowCol.rgb * m.b * flick * 1.4 * nt;
                }
                // 绕城堡塔楼飞的蝙蝠
                [unroll] for (int k = 0; k < 7; k++)
                {
                    float pk = t * (0.11 + 0.015 * k) + k * 0.9;
                    float2 c = float2(0.075 * cos(pk) + 0.02 * sin(pk * 2.3 + k) + 0.012, 0.31 + 0.045 * sin(pk * 1.3 + k * 1.7) + 0.012 * cos(t * 0.9 + k));
                    float sz = 0.0045 + 0.0015 * hash21(float2(k, 9));
                    float2 q = float2(daC - c.x, h - c.y) / sz;
                    if (dot(q, q) < 2.5) col = lerp(col, _NearSil.rgb * 0.8, batMask(q, sin(t * (11 + k) + k * 2)) * nt);
                }
                // 山坡上的鬼火：几团青绿光点慢慢漂、忽明忽暗
                [unroll] for (int w = 0; w < 8; w++)
                {
                    float wu = hash21(float2(w, 4.2)) + 0.004 * sin(t * 0.13 + w);
                    float wh = 0.004 + 0.012 * hash21(float2(w, 7.7)) + 0.004 * sin(t * 0.7 + w * 2.1);
                    float dw = (frac(au - wu + 0.5) - 0.5) * 6.2831853;
                    float r2 = dw * dw + (h - wh) * (h - wh);
                    float pulse = 0.55 + 0.45 * sin(t * (1.3 + 0.2 * w) + w);
                    col += _WispCol.rgb * (exp(-r2 / 0.0000035) * 1.2 + exp(-r2 / 0.00006) * 0.18) * pulse * nt;
                }
                // 流星：约 14 秒一颗，划过高空
                {
                    float sc = floor(t / 14.0); float sp = frac(t / 14.0) * 14.0;
                    if (sp < 0.9 && h > 0.2)
                    {
                        float2 s0 = float2(hash21(float2(sc, 1.1)), 0.45 + 0.3 * hash21(float2(sc, 2.2)));
                        float2 sv = normalize(float2(hash21(float2(sc, 3.3)) > 0.5 ? 1 : -1, -0.45)) * 0.45;
                        float2 head = s0 + sv * sp * float2(1.0 / 6.2831853, 1);
                        float2 rel = float2((frac(au - head.x + 0.5) - 0.5) * 6.2831853, h - head.y);
                        float2 dir = normalize(float2(sv.x, sv.y));
                        float along = dot(rel, -dir); float across = abs(rel.x * dir.y - rel.y * dir.x);
                        float tail = step(0, along) * saturate(1 - along / 0.12) * smoothstep(0.0015, 0.0, across);
                        col += half3(0.9, 0.92, 1.0) * tail * saturate(sp * 6) * saturate((0.9 - sp) * 4) * nt * 1.5;
                    }
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
