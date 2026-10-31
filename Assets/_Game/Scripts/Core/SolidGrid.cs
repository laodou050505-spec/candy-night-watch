// 场景占用网格（10-31，用户：吹飞可以、回来可以，但一定不能穿模）。
// 规则层本来不知道花园里摆了什么（墓碑、立柱、南瓜堆……），只能靠写死的绕行。现在由编辑器把静态摆件烘焙成 5 厘米的体素
// （Editor/SolidGridBaker.cs → Resources/solid_grid.bytes），规则层规划路线时对照它：
//   - 吹飞 / 甩星星：飞行曲线逐点检查，撞上之前就结束（表现层缩小消失）；
//   - 地面行走：线段穿过摆件时用 A* 在“地面占用图”上绕行；
//   - 飞行：线段穿过摆件时在粗网格上做 3D A*；
//   - 被扔飞：撞上摆件就弹回，落点在摆件里就挪到最近的空地。
// 网格为 null（测试、没烘焙）时一切照旧。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public class SolidGrid
    {
        public Vector3 min;
        public float cell;
        public int nx, ny, nz;
        byte[] bits;
        byte[] dist;   // 每个体素中心到最近实体体素中心的距离（厘米，最大 255）；编辑器用精确欧氏距离变换算好

        // 地面占用图：身体高度范围（离地 0.02~0.35 米）里有东西的格子，再按小鬼半径外扩
        // 两套：普通小鬼外扩 GROUND_PAD，缝合熊这种大个子外扩 GROUND_PAD_BIG
        bool[] ground, groundBig;
        bool[] mainGround, mainGroundBig;   // 和桌子周围连通的那一大片空地（地上的小坑、摆件之间封死的缝隙都不算能站的地方）
        public const float GROUND_TOP = 0.45f, GROUND_PAD = 0.11f, GROUND_PAD_BIG = 0.27f;
        // 3D 粗网格（飞行寻路用）：边长 COARSE 个细格合成一格，外扩 COARSE_PAD 格（≥ 蝙蝠身体半径 + 余量）
        const int COARSE = 2, COARSE_PAD = 2;
        int cx, cy, cz;
        bool[] coarse;

        public static SolidGrid FromBytes(byte[] data)
        {
            if (data == null || data.Length < 32) return null;
            int o = 0;
            int magic = BitConverter.ToInt32(data, o); o += 4;
            if (magic != 0x32444C53) return null;   // "SLD2"：占用位 + 距离场
            var g = new SolidGrid();
            g.min = new Vector3(BitConverter.ToSingle(data, o), BitConverter.ToSingle(data, o + 4), BitConverter.ToSingle(data, o + 8)); o += 12;
            g.cell = BitConverter.ToSingle(data, o); o += 4;
            g.nx = BitConverter.ToInt32(data, o); g.ny = BitConverter.ToInt32(data, o + 4); g.nz = BitConverter.ToInt32(data, o + 8); o += 12;
            int n = (g.nx * g.ny * g.nz + 7) / 8;
            if (data.Length < o + n) return null;
            g.bits = new byte[n];
            Buffer.BlockCopy(data, o, g.bits, 0, n); o += n;
            int nd = g.nx * g.ny * g.nz;
            if (data.Length < o + nd) return null;
            g.dist = new byte[nd];
            Buffer.BlockCopy(data, o, g.dist, 0, nd);
            g.BuildDerived();
            return g;
        }

        public static byte[] ToBytes(Vector3 min, float cell, int nx, int ny, int nz, byte[] bits, byte[] distCm)
        {
            var outB = new byte[32 + bits.Length + distCm.Length];
            int o = 0;
            void I(int v) { BitConverter.GetBytes(v).CopyTo(outB, o); o += 4; }
            void F(float v) { BitConverter.GetBytes(v).CopyTo(outB, o); o += 4; }
            I(0x32444C53); F(min.x); F(min.y); F(min.z); F(cell); I(nx); I(ny); I(nz);
            Buffer.BlockCopy(bits, 0, outB, o, bits.Length); o += bits.Length;
            Buffer.BlockCopy(distCm, 0, outB, o, distCm.Length);
            return outB;
        }

        int Idx(int x, int y, int z) => (z * ny + y) * nx + x;
        public bool Cell(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) return false;
            int i = Idx(x, y, z);
            return (bits[i >> 3] & (1 << (i & 7))) != 0;
        }

        public bool Solid(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - min.x) / cell), y = Mathf.FloorToInt((p.y - min.y) / cell), z = Mathf.FloorToInt((p.z - min.z) / cell);
            return Cell(x, y, z);
        }

        /// <summary>到最近实体的距离（米）；网格外按无穷远</summary>
        public float Dist(Vector3 p)
        {
            int x = Mathf.FloorToInt((p.x - min.x) / cell), y = Mathf.FloorToInt((p.y - min.y) / cell), z = Mathf.FloorToInt((p.z - min.z) / cell);
            if (x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz) return 9f;
            return dist[Idx(x, y, z)] * 0.01f;
        }

        /// <summary>以 c 为中心、半径 r 的球碰不碰实体（查距离场，精确到体素；按体素中心算，多留半格余量）</summary>
        public bool SolidSphere(Vector3 c, float r) => Dist(c) < r + cell * 0.5f;

        /// <summary>线段 a→b（身体中心）全程不碰实体；返回第一次碰到的比例（0~1），没碰到返回 -1</summary>
        public float FirstHit(Vector3 a, Vector3 b, float r)
        {
            float len = Vector3.Distance(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / (cell * 0.8f)));
            float pad = len / n * 0.5f;   // 两个采样点之间的点离采样点最多半个步长：半径多加半步，不漏
            for (int i = 0; i <= n; i++)
            {
                float k = (float)i / n;
                if (SolidSphere(Vector3.Lerp(a, b, k), r + pad)) return k;
            }
            return -1;
        }
        public bool SegmentFree(Vector3 a, Vector3 b, float r) => FirstHit(a, b, r) < 0;

        // ---------- 地面 ----------
        int gx, gz;
        void BuildDerived()
        {
            gx = nx; gz = nz;
            var raw = new bool[gx * gz];
            int y0 = Mathf.Max(0, Mathf.FloorToInt((0.02f - min.y) / cell)), y1 = Mathf.Min(ny - 1, Mathf.FloorToInt((GROUND_TOP - min.y) / cell));
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                    for (int y = y0; y <= y1; y++)
                        if (Cell(x, y, z)) { raw[z * gx + x] = true; break; }
            ground = Dilate(raw, Mathf.CeilToInt(GROUND_PAD / cell));
            groundBig = Dilate(raw, Mathf.CeilToInt(GROUND_PAD_BIG / cell));
            mainGround = Reach(ground);
            mainGroundBig = Reach(groundBig);
            // 粗 3D 网格（飞行寻路）
            cx = (nx + COARSE - 1) / COARSE; cy = (ny + COARSE - 1) / COARSE; cz = (nz + COARSE - 1) / COARSE;
            var c0 = new bool[cx * cy * cz];
            for (int z = 0; z < nz; z++)
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx; x++)
                        if (Cell(x, y, z)) c0[((z / COARSE) * cy + y / COARSE) * cx + x / COARSE] = true;
            coarse = new bool[c0.Length];
            for (int z = 0; z < cz; z++)
                for (int y = 0; y < cy; y++)
                    for (int x = 0; x < cx; x++)
                    {
                        if (!c0[(z * cy + y) * cx + x]) continue;
                        for (int dz = -COARSE_PAD; dz <= COARSE_PAD; dz++)
                            for (int dy = -COARSE_PAD; dy <= COARSE_PAD; dy++)
                                for (int dx = -COARSE_PAD; dx <= COARSE_PAD; dx++)
                                {
                                    int xx = x + dx, yy = y + dy, zz = z + dz;
                                    if (xx >= 0 && yy >= 0 && zz >= 0 && xx < cx && yy < cy && zz < cz) coarse[(zz * cy + yy) * cx + xx] = true;
                                }
                    }
        }

        bool[] Dilate(bool[] raw, int pad)
        {
            var outA = new bool[gx * gz];
            for (int z = 0; z < gz; z++)
                for (int x = 0; x < gx; x++)
                {
                    if (!raw[z * gx + x]) continue;
                    for (int dz = -pad; dz <= pad; dz++)
                        for (int dx = -pad; dx <= pad; dx++)
                        {
                            if (dx * dx + dz * dz > pad * pad) continue;
                            int xx = x + dx, zz = z + dz;
                            if (xx >= 0 && zz >= 0 && xx < gx && zz < gz) outA[zz * gx + xx] = true;
                        }
                }
            return outA;
        }

        // 连通区域：从桌子一圈外的地面泛洪，能走到的才是“能站的地”
        bool[] Reach(bool[] blocked)
        {
            var main = new bool[gx * gz];
            var q = new Queue<int>();
            for (int a = 0; a < 360; a += 10)
                foreach (float rr in new[] { 0.35f, 0.6f, 0.9f })
                {
                    var p = Layout.TABLE.center + new Vector3(Mathf.Sin(a * Mathf.Deg2Rad), 0, Mathf.Cos(a * Mathf.Deg2Rad)) * (Layout.TABLE.radius + rr);
                    int x = Mathf.FloorToInt((p.x - min.x) / cell), z = Mathf.FloorToInt((p.z - min.z) / cell);
                    if (x < 0 || z < 0 || x >= gx || z >= gz) continue;
                    int i = z * gx + x;
                    if (blocked[i] || main[i]) continue;
                    main[i] = true; q.Enqueue(i);
                }
            while (q.Count > 0)
            {
                int i = q.Dequeue(); int x = i % gx, z = i / gx;
                for (int d = 0; d < 4; d++)
                {
                    int xx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), zz = z + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (xx < 0 || zz < 0 || xx >= gx || zz >= gz) continue;
                    int j = zz * gx + xx;
                    if (blocked[j] || main[j]) continue;
                    main[j] = true; q.Enqueue(j);
                }
            }
            return main;
        }

        public bool GroundBlocked(Vector3 p, bool big = false)
        {
            int x = Mathf.FloorToInt((p.x - min.x) / cell), z = Mathf.FloorToInt((p.z - min.z) / cell);
            if (x < 0 || z < 0 || x >= gx || z >= gz) return false;
            return (big ? groundBig : ground)[z * gx + x];
        }

        bool GroundSegFree(Vector3 a, Vector3 b, bool big)
        {
            float len = Vec.DistXZ(a, b);
            int n = Mathf.Max(1, Mathf.CeilToInt(len / (cell * 0.7f)));
            for (int i = 0; i <= n; i++) if (GroundBlocked(Vector3.Lerp(a, b, (float)i / n), big)) return false;
            return true;
        }

        /// <summary>这里是不是能站的地（空着、并且和桌子周围的大片空地连通）</summary>
        public bool GroundOk(Vector3 p, bool big = false)
        {
            int x = Mathf.FloorToInt((p.x - min.x) / cell), z = Mathf.FloorToInt((p.z - min.z) / cell);
            if (x < 0 || z < 0 || x >= gx || z >= gz) return true;
            return (big ? mainGroundBig : mainGround)[z * gx + x];
        }

        /// <summary>最近的能站的地（空着且和桌子周围连通；螺旋向外找，最多 maxR 米）</summary>
        public Vector3 NearestFreeGround(Vector3 p, float maxR = 1.5f, bool big = false)
        {
            if (GroundOk(p, big)) return p;
            for (float r = cell; r <= maxR; r += cell)
            {
                int steps = Mathf.Max(8, Mathf.CeilToInt(2 * Mathf.PI * r / cell));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2 / steps;
                    var q = new Vector3(p.x + Mathf.Cos(a) * r, p.y, p.z + Mathf.Sin(a) * r);
                    if (GroundOk(q, big)) return q;
                }
            }
            return p;
        }

        /// <summary>地面上 a→b 的绕行折线（不含 a，含 b）；直线可走返回 null。A* 8 邻接 + 视线拉直</summary>
        public List<Vector3> GroundDetour(Vector3 a, Vector3 b, bool big = false)
        {
            var G = big ? groundBig : ground;
            // 起点/终点就在摆件边上（出生点、桌腿下）时，先从最近的空地出发 / 到达
            var sa = NearestFreeGround(a, 0.8f, big); var sb = NearestFreeGround(b, 0.8f, big);
            // 终点本身容不下这个身体（出生点紧贴南瓜堆，熊回去时会蹭进去）：就停在最近容得下的地方
            bool moved = !GroundOk(b, big);
            if (moved) b = sb;
            if (GroundSegFree(sa, sb, big) && GroundSegFree(a, sa, big) && GroundSegFree(sb, b, big)) return moved ? new List<Vector3> { b } : null;
            int ax = Mathf.Clamp(Mathf.FloorToInt((sa.x - min.x) / cell), 0, gx - 1), az = Mathf.Clamp(Mathf.FloorToInt((sa.z - min.z) / cell), 0, gz - 1);
            int bx = Mathf.Clamp(Mathf.FloorToInt((sb.x - min.x) / cell), 0, gx - 1), bz = Mathf.Clamp(Mathf.FloorToInt((sb.z - min.z) / cell), 0, gz - 1);
            var raw = AStar2D(ax, az, bx, bz, G);
            if (raw == null) return null;
            var pts = new List<Vector3>();
            foreach (var (x, z) in raw) pts.Add(new Vector3(min.x + (x + 0.5f) * cell, a.y, min.z + (z + 0.5f) * cell));
            // 拉直：能直接看到的点之间去掉中间点
            var outL = new List<Vector3>();
            var cur = a;
            int k = 0;
            while (k < pts.Count)
            {
                int far = k;
                for (int j = pts.Count - 1; j > k; j--) if (GroundSegFree(cur, pts[j], big)) { far = j; break; }
                outL.Add(pts[far]); cur = pts[far]; k = far + 1;
            }
            outL.Add(b);
            return outL;
        }

        List<(int, int)> AStar2D(int ax, int az, int bx, int bz, bool[] ground)
        {
            int N = gx * gz;
            var gScore = new float[N]; var from = new int[N]; var closed = new bool[N];
            for (int i = 0; i < N; i++) { gScore[i] = float.PositiveInfinity; from[i] = -1; }
            int start = az * gx + ax, goal = bz * gx + bx;
            var open = new SortedSet<(float f, int id)>();
            gScore[start] = 0; open.Add((H(ax, az, bx, bz), start));
            int iter = 0;
            while (open.Count > 0 && iter++ < 200000)
            {
                var cur = open.Min; open.Remove(cur);
                int id = cur.id; if (closed[id]) continue; closed[id] = true;
                if (id == goal) break;
                int x = id % gx, z = id / gx;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        int xx = x + dx, zz = z + dz;
                        if (xx < 0 || zz < 0 || xx >= gx || zz >= gz) continue;
                        int nid = zz * gx + xx;
                        if (closed[nid] || (ground[nid] && nid != goal)) continue;
                        if (dx != 0 && dz != 0 && (ground[z * gx + xx] || ground[zz * gx + x])) continue;   // 不切角
                        float ng = gScore[id] + ((dx != 0 && dz != 0) ? 1.4142f : 1f);
                        if (ng < gScore[nid]) { gScore[nid] = ng; from[nid] = id; open.Add((ng + H(xx, zz, bx, bz), nid)); }
                    }
            }
            if (from[goal] < 0 && goal != start) return null;
            var path = new List<(int, int)>();
            for (int c = goal; c >= 0; c = from[c]) { path.Add((c % gx, c / gx)); if (c == start) break; }
            path.Reverse();
            return path;
        }
        static float H(int x, int z, int bx, int bz) { float dx = Mathf.Abs(x - bx), dz = Mathf.Abs(z - bz); return Mathf.Max(dx, dz) + 0.4142f * Mathf.Min(dx, dz); }

        // ---------- 空中 ----------
        bool CoarseBlocked(int x, int y, int z) => x < 0 || y < 0 || z < 0 || x >= cx || y >= cy || z >= cz || coarse[(z * cy + y) * cx + x];

        /// <summary>空中 a→b 的绕行折线（不含 a，含 b）；直线可走返回 null；找不到返回 null（调用方保持原样）</summary>
        public List<Vector3> AirDetour(Vector3 a, Vector3 b, float r, float maxY)
        {
            if (SegmentFree(a, b, r)) return null;
            float cs = cell * COARSE;
            (int, int, int) C(Vector3 p) => (Mathf.Clamp(Mathf.FloorToInt((p.x - min.x) / cs), 0, cx - 1), Mathf.Clamp(Mathf.FloorToInt((p.y - min.y) / cs), 0, cy - 1), Mathf.Clamp(Mathf.FloorToInt((p.z - min.z) / cs), 0, cz - 1));
            // 起点贴着摆件（落在摆件顶上、地上紧挨着摆件）：先竖直升起，直到离开外扩区，再从那里寻路（不贴着摆件横着蹭出去）
            Vector3 lift = a;
            {
                var (x0, y0, z0) = C(a);
                if (CoarseBlocked(x0, y0, z0))
                    for (float dy = 0.05f; dy <= 1.2f; dy += 0.05f)
                    {
                        var q = a + Vector3.up * dy;
                        var (qx, qy, qz) = C(q);
                        if (!SegmentFree(a, q, r)) break;
                        if (!CoarseBlocked(qx, qy, qz)) { lift = q; break; }
                    }
            }
            if (lift != a)
            {
                var rest = AirDetour(lift, b, r, maxY);
                var l2 = new List<Vector3> { lift };
                if (rest != null) l2.AddRange(rest); else l2.Add(b);
                return l2;
            }
            var (ax, ay, az) = C(a); var (bx, by, bz) = C(b);
            int ymax = Mathf.Min(cy - 1, Mathf.FloorToInt((maxY - min.y) / cs));
            int N = cx * cy * cz;
            int Id(int x, int y, int z) => (z * cy + y) * cx + x;
            var gScore = new Dictionary<int, float>(); var from = new Dictionary<int, int>(); var closed = new HashSet<int>();
            int start = Id(ax, ay, az), goal = Id(bx, by, bz);
            var open = new SortedSet<(float f, int id)>();
            gScore[start] = 0; open.Add((0, start));
            int iter = 0; bool found = false;
            while (open.Count > 0 && iter++ < 60000)
            {
                var cur = open.Min; open.Remove(cur);
                int id = cur.id; if (closed.Contains(id)) continue; closed.Add(id);
                if (id == goal) { found = true; break; }
                int x = id % cx, y = (id / cx) % cy, z = id / (cx * cy);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;
                            int xx = x + dx, yy = y + dy, zz = z + dz;
                            if (yy > ymax) continue;
                            int nid = Id(Mathf.Clamp(xx, 0, cx - 1), Mathf.Clamp(yy, 0, cy - 1), Mathf.Clamp(zz, 0, cz - 1));
                            if (xx < 0 || yy < 0 || zz < 0 || xx >= cx || yy >= cy || zz >= cz) continue;
                            if (closed.Contains(nid)) continue;
                            bool nearEnds = Mathf.Abs(xx - bx) <= 2 && Mathf.Abs(yy - by) <= 2 && Mathf.Abs(zz - bz) <= 2;   // 终点（糖桶、出生点）附近允许进外扩区
                            if (CoarseBlocked(xx, yy, zz) && !nearEnds) continue;
                            float step = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
                            float ng = gScore[id] + step;
                            if (!gScore.TryGetValue(nid, out var og) || ng < og)
                            {
                                gScore[nid] = ng; from[nid] = id;
                                float h = Mathf.Sqrt((xx - bx) * (xx - bx) + (yy - by) * (yy - by) + (zz - bz) * (zz - bz));
                                open.Add((ng + h, nid));
                            }
                        }
            }
            if (!found)
            {
                // 兜底：先竖直升到安全高度，再水平飞到终点正上方，再下降（每段都要畅通，否则放弃）
                for (float hy = Mathf.Max(a.y, b.y) + 0.3f; hy <= maxY; hy += 0.2f)
                {
                    var p1 = new Vector3(a.x, hy, a.z); var p2 = new Vector3(b.x, hy, b.z);
                    if (SegmentFree(a + Vector3.up * 0.15f, p1, r) && SegmentFree(p1, p2, r) && SegmentFree(p2, b + Vector3.up * 0.15f, r)) return new List<Vector3> { p1, p2, b };
                }
                return null;
            }
            var pts = new List<Vector3>();
            for (int c = goal; ; c = from[c])
            {
                int x = c % cx, y = (c / cx) % cy, z = c / (cx * cy);
                pts.Add(new Vector3(min.x + (x + 0.5f) * cs, min.y + (y + 0.5f) * cs, min.z + (z + 0.5f) * cs));
                if (c == start || !from.ContainsKey(c)) break;
            }
            pts.Reverse();
            if (pts.Count > 0) pts.RemoveAt(0);
            var outL = new List<Vector3>();
            var curP = a; int k = 0;
            while (k < pts.Count)
            {
                int far = k;
                for (int j = pts.Count - 1; j > k; j--) if (SegmentFree(curP, pts[j], r)) { far = j; break; }
                outL.Add(pts[far]); curP = pts[far]; k = far + 1;
            }
            outL.Add(b);
            return outL;
        }
    }
}
