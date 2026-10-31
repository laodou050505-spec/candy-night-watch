// 手套主体的距离场与表面提取（HandView 第五版，10-30）：
//   手掌 = 沿手腕→指根的圆角扁块（宽度从手腕收窄到四指跨度、手背厚掌心薄）+ 小指一侧的掌根肉垫（小鱼际）；
//   每根手指 = 三节圆锥台（关节处是球，节与节连成光滑软管），拇指根粗、和手掌大幅度平滑融合 → 大鱼际；
//   手指各自和手掌平滑融合（指根有一点指蹼），手指之间不互相融合（不会粘成一片）。
//   表面用 Surface Nets 提取（每个跨表面的格子放一个顶点，按距离场梯度投回表面），粗格子先筛掉离表面远的区域。
//   蒙皮权重：每个顶点先判断属于哪根手指（或手掌），只在这根手指的三节骨骼和手掌骨之间按到各节的距离平滑分配，
//   相邻手指互不牵连；关节两侧约 1 厘米内平滑过渡。
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public partial class HandView
    {
        class GloveSdf
        {
            const float CELL = 0.0019f;          // 表面网格的格子边长（约 2 毫米）
            const float K_FINGER = 0.006f, K_THUMB = 0.016f, K_HYPO = 0.012f, K_SEG = 0.002f;
            readonly Vector3 W, S, U, F; readonly float L, sMid;
            readonly float zStart, zEnd, spanW;
            readonly Vector3[,] A = new Vector3[5, 3], B = new Vector3[5, 3];
            readonly float[,] RA = new float[5, 3], RB = new float[5, 3];
            readonly Vector3 hypoA, hypoB; readonly float hypoR0, hypoR1;
            readonly Vector3 lo, hi;

            public GloveSdf(Vector3[] j, bool right, float[] mcpS, float sMin, float sMax, Vector3 wrist, Vector3 s, Vector3 u, Vector3 f, float len,
                            int[][] chains, float[] rootR, float[] tipR)
            {
                W = wrist; S = s; U = u; F = f; L = len;
                sMid = (sMin + sMax) * 0.5f; spanW = (sMax - sMin) * 0.5f + 0.013f;   // 手指改细后手掌跟着收窄
                zStart = -0.22f * L; zEnd = 0.97f * L;
                for (int fi = 0; fi < 5; fi++)
                {
                    var c = chains[fi];
                    float total = 0; for (int k = 0; k < 3; k++) total += Vector3.Distance(j[c[k]], j[c[k + 1]]);
                    float acc = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        float l = Vector3.Distance(j[c[k]], j[c[k + 1]]);
                        float u0 = acc / total, u1 = (acc + l) / total; acc += l;
                        float r0 = Mathf.Lerp(rootR[fi], tipR[fi], u0), r1 = Mathf.Lerp(rootR[fi], tipR[fi], u1);
                        if (k == 0) r0 *= fi == 0 ? 1.3f : 1.04f;                 // 指根（拇指根）略粗
                        if (k > 0) r0 *= 0.965f;                                    // 指节处微微收窄，像布料裹着关节
                        if (k < 2) r1 *= 0.965f;
                        A[fi, k] = j[c[k]]; B[fi, k] = j[c[k + 1]]; RA[fi, k] = r0; RB[fi, k] = r1 * (k == 2 ? 1.06f : 1f);
                    }
                }
                // 小鱼际：小指一侧掌根的一道肉垫，掌心那一侧略低
                float sLittle = mcpS[3], side = Mathf.Sign(sLittle - sMid);
                hypoA = W + F * (0.12f * L) + S * (sMid + side * 0.024f) - U * 0.005f;
                hypoB = W + F * (0.72f * L) + S * (sLittle - side * 0.002f) - U * 0.006f;
                hypoR0 = 0.013f; hypoR1 = 0.0115f;
                // 包围盒
                lo = Vector3.one * 1e9f; hi = -lo;
                foreach (var p in j) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
                var back = W + F * zStart;
                lo = Vector3.Min(lo, back); hi = Vector3.Max(hi, back);
                lo -= Vector3.one * 0.04f; hi += Vector3.one * 0.04f;
            }

            static float SMin(float a, float b, float k)
            {
                float h = Mathf.Max(k - Mathf.Abs(a - b), 0) / k;
                return Mathf.Min(a, b) - h * h * k * 0.25f;
            }

            // 圆锥台（两端是半径不同的球）：Inigo Quilez 的 sdRoundCone（任意两点）
            static float RoundCone(Vector3 p, Vector3 a, Vector3 b, float r1, float r2)
            {
                var ba = b - a; float l2 = Vector3.Dot(ba, ba); if (l2 < 1e-10f) return (p - a).magnitude - Mathf.Max(r1, r2);
                float rr = r1 - r2, a2 = l2 - rr * rr, il2 = 1f / l2;
                var pa = p - a; float y = Vector3.Dot(pa, ba), z = y - l2;
                var xv = pa * l2 - ba * y; float x2 = Vector3.Dot(xv, xv), y2 = y * y * l2, z2 = z * z * l2;
                float k = Mathf.Sign(rr) * rr * rr * x2;
                if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - r2;
                if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - r1;
                return (Mathf.Sqrt(x2 * a2 * il2) + y * rr) * il2 - r1;
            }

            /// <summary>手掌：圆角扁块（截面是圆角矩形，四周再整体倒 8 毫米圆角）+ 小鱼际</summary>
            public float Palm(Vector3 p)
            {
                var d = p - W;
                float z = Vector3.Dot(d, F);
                float t = Mathf.Clamp(z / L, -0.22f, 0.97f);
                float hw = Mathf.Lerp(0.031f, spanW, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.1f, 0.85f, t)));
                float th = Mathf.Lerp(0.017f, 0.021f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-0.2f, 0.6f, t)));
                float sc = sMid * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.1f, 0.9f, t));
                float x = Vector3.Dot(d, S) - sc, y = Vector3.Dot(d, U) - 0.002f;
                float thy = th * (y > 0 ? 1.12f : 0.88f);                          // 手背鼓、掌心平
                const float re = 0.008f;
                float rc = Mathf.Max(thy * 0.85f - re, 0.001f);
                float qx = Mathf.Abs(x) - (hw - re - rc), qy = Mathf.Abs(y) - (thy - re - rc);
                float d2 = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - rc;
                float dz = Mathf.Max(zStart + re - z, z - (zEnd - re));
                float box = new Vector2(Mathf.Max(d2, 0), Mathf.Max(dz, 0)).magnitude + Mathf.Min(Mathf.Max(d2, dz), 0) - re;
                return SMin(box, RoundCone(p, hypoA, hypoB, hypoR0, hypoR1), K_HYPO);
            }

            public float Finger(int f, Vector3 p)
            {
                float d = RoundCone(p, A[f, 0], B[f, 0], RA[f, 0], RB[f, 0]);
                d = SMin(d, RoundCone(p, A[f, 1], B[f, 1], RA[f, 1], RB[f, 1]), K_SEG);
                return SMin(d, RoundCone(p, A[f, 2], B[f, 2], RA[f, 2], RB[f, 2]), K_SEG);
            }

            public float Eval(Vector3 p)
            {
                // 手指之间不融合（10-30 用户：手指粘在一起），每根手指只和手掌圆滑相接
                float palm = Palm(p), d = palm;
                for (int f = 0; f < 5; f++) d = Mathf.Min(d, SMin(palm, Finger(f, p), f == 0 ? K_THUMB : K_FINGER));
                return d;
            }

            public Vector3 Normal(Vector3 p)
            {
                const float e = 0.0004f;
                var n = new Vector3(Eval(p + Vector3.right * e) - Eval(p - Vector3.right * e),
                                    Eval(p + Vector3.up * e) - Eval(p - Vector3.up * e),
                                    Eval(p + Vector3.forward * e) - Eval(p - Vector3.forward * e));
                return n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
            }

            /// <summary>从 p 沿 dir 打到表面（球追踪）</summary>
            public Vector3 Project(Vector3 p, Vector3 dir)
            {
                for (int i = 0; i < 64; i++) { float d = Eval(p); if (Mathf.Abs(d) < 1e-5f) break; p += dir * d; }
                return p;
            }

            /// <summary>Surface Nets 提取表面，顶点、法线、蒙皮权重写进 mb，三角形写进 tris</summary>
            public void Mesh(MB mb, List<int> tris)
            {
                int nx = Mathf.CeilToInt((hi.x - lo.x) / CELL) + 1, ny = Mathf.CeilToInt((hi.y - lo.y) / CELL) + 1, nz = Mathf.CeilToInt((hi.z - lo.z) / CELL) + 1;
                var v = new float[nx * ny * nz];
                int Idx(int x, int y, int z) => (z * ny + y) * nx + x;
                Vector3 P(int x, int y, int z) => lo + new Vector3(x, y, z) * CELL;
                // 粗筛：每 6 格采一个粗点，离表面远的整块直接取粗值（距离场 1-Lipschitz，远处的符号不会变）
                const int C = 6; float far = C * CELL * 0.9f + 0.002f;
                int cx = (nx + C - 1) / C + 1, cy = (ny + C - 1) / C + 1, cz = (nz + C - 1) / C + 1;
                var coarse = new float[cx * cy * cz];
                for (int z = 0; z < cz; z++) for (int y = 0; y < cy; y++) for (int x = 0; x < cx; x++)
                    coarse[(z * cy + y) * cx + x] = Eval(P(x * C, y * C, z * C));
                for (int z = 0; z < nz; z++) for (int y = 0; y < ny; y++) for (int x = 0; x < nx; x++)
                {
                    int bx = Mathf.Min((x + C / 2) / C, cx - 1), by = Mathf.Min((y + C / 2) / C, cy - 1), bz = Mathf.Min((z + C / 2) / C, cz - 1);
                    float cv = coarse[(bz * cy + by) * cx + bx];
                    v[Idx(x, y, z)] = Mathf.Abs(cv) > far ? cv : Eval(P(x, y, z));
                }
                // 每个跨表面的格子一个顶点
                var cellV = new Dictionary<int, int>();
                int[,] E = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
                var corner = new float[8]; var cp = new Vector3[8];
                for (int z = 0; z < nz - 1; z++) for (int y = 0; y < ny - 1; y++) for (int x = 0; x < nx - 1; x++)
                {
                    int neg = 0;
                    for (int c = 0; c < 8; c++)
                    {
                        int dx = c & 1, dy = (c >> 1) & 1, dz = (c >> 2) & 1;
                        corner[c] = v[Idx(x + dx, y + dy, z + dz)]; cp[c] = P(x + dx, y + dy, z + dz);
                        if (corner[c] < 0) neg++;
                    }
                    if (neg == 0 || neg == 8) continue;
                    var sum = Vector3.zero; int cnt = 0;
                    for (int e = 0; e < 12; e++)
                    {
                        float a = corner[E[e, 0]], b = corner[E[e, 1]];
                        if ((a < 0) == (b < 0)) continue;
                        sum += Vector3.Lerp(cp[E[e, 0]], cp[E[e, 1]], a / (a - b)); cnt++;
                    }
                    var p = sum / cnt;
                    for (int it = 0; it < 3; it++) { float d = Eval(p); if (Mathf.Abs(d) < 2e-5f) break; p -= Normal(p) * d; }
                    var n = Normal(p);
                    cellV[Idx(x, y, z)] = mb.v.Count;
                    mb.nextUV = Vector2.zero;
                    mb.Add(p, n, Weights(p));
                }
                // 每条跨表面的格边：周围 4 个格子的顶点连成一个四边形（按距离场梯度定绕序）
                for (int z = 1; z < nz - 1; z++) for (int y = 1; y < ny - 1; y++) for (int x = 1; x < nx - 1; x++)
                {
                    float a = v[Idx(x, y, z)];
                    for (int ax = 0; ax < 3; ax++)
                    {
                        int x2 = x + (ax == 0 ? 1 : 0), y2 = y + (ax == 1 ? 1 : 0), z2 = z + (ax == 2 ? 1 : 0);
                        if (x2 >= nx || y2 >= ny || z2 >= nz) continue;
                        float b = v[Idx(x2, y2, z2)];
                        if ((a < 0) == (b < 0)) continue;
                        // 这条边周围的 4 个格子（格子下标 = 格子最小角）
                        int q0, q1, q2, q3;
                        if (ax == 0) { q0 = Idx(x, y - 1, z - 1); q1 = Idx(x, y, z - 1); q2 = Idx(x, y, z); q3 = Idx(x, y - 1, z); }
                        else if (ax == 1) { q0 = Idx(x - 1, y, z - 1); q1 = Idx(x - 1, y, z); q2 = Idx(x, y, z); q3 = Idx(x, y, z - 1); }
                        else { q0 = Idx(x - 1, y - 1, z); q1 = Idx(x, y - 1, z); q2 = Idx(x, y, z); q3 = Idx(x - 1, y, z); }
                        if (!cellV.TryGetValue(q0, out var i0) || !cellV.TryGetValue(q1, out var i1) || !cellV.TryGetValue(q2, out var i2) || !cellV.TryGetValue(q3, out var i3)) continue;
                        // 绕序：Unity 正面顺时针（从外往里看），四边形法线要和梯度（指向外）同向
                        var nq = Vector3.Cross(mb.v[i1] - mb.v[i0], mb.v[i2] - mb.v[i0]);
                        var g = mb.n[i0] + mb.n[i1] + mb.n[i2] + mb.n[i3];
                        bool flip = Vector3.Dot(nq, g) < 0;
                        if (flip) { var t = i1; i1 = i3; i3 = t; }
                        // 沿较短的对角线切成两个三角形
                        if ((mb.v[i0] - mb.v[i2]).sqrMagnitude < (mb.v[i1] - mb.v[i3]).sqrMagnitude) { Tri(tris, i0, i1, i2); Tri(tris, i0, i2, i3); }
                        else { Tri(tris, i0, i1, i3); Tri(tris, i1, i2, i3); }
                    }
                }
            }

            /// <summary>蒙皮权重：归属的那根手指的三节 + 手掌，按到各部分的距离平滑分配</summary>
            BoneWeight Weights(Vector3 p)
            {
                float palm = Palm(p);
                int owner = -1; float best = palm;
                var fd = new float[5];
                for (int f = 0; f < 5; f++) { fd[f] = Finger(f, p); if (fd[f] < best) { best = fd[f]; owner = f; } }
                // 手掌上的顶点：也要跟最近的那根手指走一点（指根附近弯指时掌缘跟着卷）
                int fo = owner;
                if (fo < 0) { float b2 = 1e9f; for (int f = 0; f < 5; f++) if (fd[f] < b2) { b2 = fd[f]; fo = f; } }
                var seg = new float[3];
                for (int k = 0; k < 3; k++) seg[k] = Mathf.Max(0, RoundCone(p, A[fo, k], B[fo, k], RA[fo, k], RB[fo, k]));
                float dp = Mathf.Max(0, palm);
                float dmin = Mathf.Min(dp, Mathf.Min(seg[0], Mathf.Min(seg[1], seg[2])));
                const float sigF = 0.0035f;
                float sigP = fo == 0 ? 0.008f : 0.006f;
                float wp = Mathf.Exp(-(dp - dmin) / sigP);
                float w0 = Mathf.Exp(-(seg[0] - dmin) / sigF), w1 = Mathf.Exp(-(seg[1] - dmin) / sigF), w2 = Mathf.Exp(-(seg[2] - dmin) / sigF);
                int b0 = 1 + fo * 3;
                return BW((0, wp), (b0, w0), (b0 + 1, w1), (b0 + 2, w2));
            }
        }
    }
}
