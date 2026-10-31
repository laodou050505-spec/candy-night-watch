// 把花园里的静态摆件烘焙成 5 厘米体素占用网格（10-31，防穿模），给规则层规划吹飞、扔飞、行走、飞行路线用。
// 菜单 Tools/TrickOrFreeze/Bake Solid Grid；Rebuild Gameplay (keep scene) 也会自动重烘（用户挪了摆件后要重跑）。
// 计入：World 下的摆件、藤架、围墙、桌腿；不计入：玩法物件（糖桶、桌上坩埚、烛台、桌面——规则层另有处理）、
//      藤蔓坡道（小鬼本来就在上面爬）、地面、天空、动画/粒子/透明物体。
// 先逐三角形做三角形-立方体相交，再从网格边界泛洪，把封闭模型的内部也标成实体（落点在模型肚子里也算）。
using System.Collections.Generic;
using System.IO;
using TrickOrFreeze.Core;
using UnityEditor;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class SolidGridBaker
    {
        public static readonly Vector3 MIN = new Vector3(-6.4f, 0f, -5.4f);
        public const float CELL = 0.05f;
        public const int NX = 256, NY = 68, NZ = 212;
        static readonly string[] SkipRoots = { "Desk_Bucket", "Desk_Cauldron", "Desk_CandleCluster", "VineRamps", "Hands", "Game", "SkyDome", "Moon", "Deco_Ground", "Deco_GroundDetail" };

        [MenuItem("Tools/TrickOrFreeze/Bake Solid Grid")]
        public static void Bake()
        {
            var world = GameObject.Find("World");
            if (!world) { Debug.LogError("[SolidGrid] 场景里没有 World"); return; }
            var solid = new bool[NX * NY * NZ];
            int meshes = 0, tris = 0;
            var used = new Dictionary<string, int>();
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (!mr || !mr.enabled || !mf.sharedMesh) continue;
                if (Skip(world.transform, mf.transform, mr)) continue;
                var b = mr.bounds;
                if (b.size.magnitude > 40f) continue;
                if (b.size.y < 0.06f && b.max.y < 0.12f) continue;   // 贴地的落叶、贴花
                if (b.max.x < MIN.x || b.min.x > MIN.x + NX * CELL || b.max.z < MIN.z || b.min.z > MIN.z + NZ * CELL || b.min.y > MIN.y + NY * CELL) continue;
                var mesh = mf.sharedMesh;
                if (!mesh.isReadable && !AssetDatabase.Contains(mesh)) continue;
                var vs = mesh.vertices; var ix = mesh.triangles;
                var m = mf.transform.localToWorldMatrix;
                var toW = world.transform.worldToLocalMatrix;   // 规则层坐标 = World 的局部坐标
                var w = new Vector3[vs.Length];
                for (int i = 0; i < vs.Length; i++) w[i] = toW.MultiplyPoint3x4(m.MultiplyPoint3x4(vs[i]));
                for (int t = 0; t < ix.Length; t += 3) Raster(solid, w[ix[t]], w[ix[t + 1]], w[ix[t + 2]]);
                meshes++; tris += ix.Length / 3;
                string top = TopName(world.transform, mf.transform);
                used[top] = used.TryGetValue(top, out var c) ? c + 1 : 1;
            }
            int surface = 0; foreach (var s in solid) if (s) surface++;
            FillInterior(solid);
            int total = 0; foreach (var s in solid) if (s) total++;
            var bits = new byte[(solid.Length + 7) / 8];
            for (int i = 0; i < solid.Length; i++) if (solid[i]) bits[i >> 3] |= (byte)(1 << (i & 7));
            var dir = Path.Combine(Application.dataPath, "Resources");
            Directory.CreateDirectory(dir);
            var distCm = DistanceField(solid);
            File.WriteAllBytes(Path.Combine(dir, "solid_grid.bytes"), SolidGrid.ToBytes(MIN, CELL, NX, NY, NZ, bits, distCm));
            AssetDatabase.ImportAsset("Assets/Resources/solid_grid.bytes");
            WritePreview(solid, "/tmp/solid_grid_top.png");
            var list = new List<string>(); foreach (var kv in used) list.Add(kv.Key + "×" + kv.Value);
            list.Sort();
            Debug.Log($"[SolidGrid] {meshes} 个网格、{tris} 个三角形 → 表面 {surface} 格，填实后 {total} 格；计入：{string.Join("，", list)}");
        }

        static string TopName(Transform world, Transform t)
        {
            while (t.parent && t.parent != world && t.parent.parent != world) t = t.parent;
            return t.name;
        }

        static bool Skip(Transform world, Transform t, Renderer r)
        {
            if (r.GetComponentInParent<TrickOrFreeze.UI.CoverStage>(true)) return true;
            if (r.name == "LOD1" || r.name == "LOD2" || r.name == "LOD3") return true;   // 只用最精细的 LOD0
            // 只看物体自己和直接父物体上的动画/环境组件（Halloween 这类大分组的根上挂着地雾、粒子，不能因此跳过整组）
            for (var p = r.transform; p && p != world && p.parent != world; p = p.parent)
            {
                if (p != r.transform && p != r.transform.parent) break;
                if (p.GetComponent<Animator>() || p.GetComponent<ParticleSystem>()) return true;
                foreach (var mb in p.GetComponents<MonoBehaviour>())
                    if (mb && mb.GetType().FullName.Contains(".Ambient") && !mb.GetType().Name.Contains("Glow")) return true;
            }
            foreach (var m in r.sharedMaterials)
            {
                if (!m) continue;
                if (m.renderQueue >= 2900) return true;   // 透明（光晕、雾、蛛网）
                string sn = m.shader ? m.shader.name : "";
                if (sn.Contains("Sky") || sn.Contains("Particle") || sn.Contains("Halo") || sn.Contains("Fog") || sn.Contains("Ghost")) return true;
            }
            for (var p = t; p && p != world; p = p.parent)
            {
                foreach (var s in SkipRoots) if (p.name == s && (p.parent == world || s.StartsWith("Deco_"))) return true;
                if (p.parent && p.parent.name == "Desk_Table" && p.parent.parent == world && (p.name == "Top" || p.name == "Rim")) return true;   // 桌面由规则层处理（桌腿计入）
            }
            return false;
        }

        // ---------- 三角形体素化 ----------
        static void Raster(bool[] solid, Vector3 a, Vector3 b, Vector3 c)
        {
            var lo = Vector3.Min(a, Vector3.Min(b, c)); var hi = Vector3.Max(a, Vector3.Max(b, c));
            int x0 = Mathf.Max(0, Mathf.FloorToInt((lo.x - MIN.x) / CELL)), x1 = Mathf.Min(NX - 1, Mathf.FloorToInt((hi.x - MIN.x) / CELL));
            int y0 = Mathf.Max(0, Mathf.FloorToInt((lo.y - MIN.y) / CELL)), y1 = Mathf.Min(NY - 1, Mathf.FloorToInt((hi.y - MIN.y) / CELL));
            int z0 = Mathf.Max(0, Mathf.FloorToInt((lo.z - MIN.z) / CELL)), z1 = Mathf.Min(NZ - 1, Mathf.FloorToInt((hi.z - MIN.z) / CELL));
            if (x0 > x1 || y0 > y1 || z0 > z1) return;
            var half = Vector3.one * (CELL * 0.5f);
            for (int z = z0; z <= z1; z++)
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = (z * NY + y) * NX + x;
                        if (solid[i]) continue;
                        var ctr = MIN + new Vector3((x + 0.5f) * CELL, (y + 0.5f) * CELL, (z + 0.5f) * CELL);
                        if (TriBox(ctr, half, a, b, c)) solid[i] = true;
                    }
        }

        // Akenine-Möller 三角形-AABB 分离轴测试
        static bool TriBox(Vector3 c, Vector3 h, Vector3 a, Vector3 b, Vector3 d)
        {
            Vector3 v0 = a - c, v1 = b - c, v2 = d - c;
            Vector3 e0 = v1 - v0, e1 = v2 - v1, e2 = v0 - v2;
            if (!Axis(new Vector3(0, -e0.z, e0.y), v0, v1, v2, h) || !Axis(new Vector3(0, -e1.z, e1.y), v0, v1, v2, h) || !Axis(new Vector3(0, -e2.z, e2.y), v0, v1, v2, h)) return false;
            if (!Axis(new Vector3(e0.z, 0, -e0.x), v0, v1, v2, h) || !Axis(new Vector3(e1.z, 0, -e1.x), v0, v1, v2, h) || !Axis(new Vector3(e2.z, 0, -e2.x), v0, v1, v2, h)) return false;
            if (!Axis(new Vector3(-e0.y, e0.x, 0), v0, v1, v2, h) || !Axis(new Vector3(-e1.y, e1.x, 0), v0, v1, v2, h) || !Axis(new Vector3(-e2.y, e2.x, 0), v0, v1, v2, h)) return false;
            if (Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x)) < -h.x || Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)) > h.x) return false;
            if (Mathf.Max(v0.y, Mathf.Max(v1.y, v2.y)) < -h.y || Mathf.Min(v0.y, Mathf.Min(v1.y, v2.y)) > h.y) return false;
            if (Mathf.Max(v0.z, Mathf.Max(v1.z, v2.z)) < -h.z || Mathf.Min(v0.z, Mathf.Min(v1.z, v2.z)) > h.z) return false;
            var n = Vector3.Cross(e0, e1);
            return Axis(n, v0, v1, v2, h);
        }
        static bool Axis(Vector3 ax, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 h)
        {
            if (ax.sqrMagnitude < 1e-12f) return true;
            float p0 = Vector3.Dot(v0, ax), p1 = Vector3.Dot(v1, ax), p2 = Vector3.Dot(v2, ax);
            float r = h.x * Mathf.Abs(ax.x) + h.y * Mathf.Abs(ax.y) + h.z * Mathf.Abs(ax.z);
            return !(Mathf.Min(p0, Mathf.Min(p1, p2)) > r || Mathf.Max(p0, Mathf.Max(p1, p2)) < -r);
        }

        // ---------- 填实内部：从网格四周（不含地面那一面）泛洪，没泛到的空格就是封闭模型的肚子 ----------
        static void FillInterior(bool[] solid)
        {
            var outside = new bool[solid.Length];
            var q = new Queue<int>();
            void Seed(int x, int y, int z) { int i = (z * NY + y) * NX + x; if (!solid[i] && !outside[i]) { outside[i] = true; q.Enqueue(i); } }
            for (int z = 0; z < NZ; z++) for (int y = 0; y < NY; y++) { Seed(0, y, z); Seed(NX - 1, y, z); }
            for (int x = 0; x < NX; x++) for (int y = 0; y < NY; y++) { Seed(x, y, 0); Seed(x, y, NZ - 1); }
            for (int z = 0; z < NZ; z++) for (int x = 0; x < NX; x++) Seed(x, NY - 1, z);
            // 地面那一层（y=0）也算外面：贴地的空隙（桌下、摆件之间）都和外界连通
            for (int z = 0; z < NZ; z++) for (int x = 0; x < NX; x++) Seed(x, 0, z);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                int x = i % NX, y = (i / NX) % NY, z = i / (NX * NY);
                void Try(int xx, int yy, int zz)
                {
                    if (xx < 0 || yy < 0 || zz < 0 || xx >= NX || yy >= NY || zz >= NZ) return;
                    int j = (zz * NY + yy) * NX + xx;
                    if (solid[j] || outside[j]) return;
                    outside[j] = true; q.Enqueue(j);
                }
                Try(x + 1, y, z); Try(x - 1, y, z); Try(x, y + 1, z); Try(x, y - 1, z); Try(x, y, z + 1); Try(x, y, z - 1);
            }
            for (int i = 0; i < solid.Length; i++) if (!outside[i]) solid[i] = true;
        }

        // ---------- 精确欧氏距离变换（Felzenszwalb-Huttenlocher，逐轴一维平方距离），结果按厘米存一个字节（最大 2.55 米） ----------
        static byte[] DistanceField(bool[] solid)
        {
            const float INF = 1e12f;
            var d = new float[solid.Length];
            for (int i = 0; i < d.Length; i++) d[i] = solid[i] ? 0f : INF;
            int maxN = Mathf.Max(NX, Mathf.Max(NY, NZ));
            var f = new float[maxN]; var outA = new float[maxN]; var v = new int[maxN]; var zb = new float[maxN + 1];
            void Pass(int n, System.Func<int, int> idx)
            {
                for (int q = 0; q < n; q++) f[q] = d[idx(q)];
                int k = 0; v[0] = 0; zb[0] = -INF; zb[1] = INF;
                for (int q = 1; q < n; q++)
                {
                    float s2 = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]);
                    while (s2 <= zb[k]) { k--; s2 = ((f[q] + q * q) - (f[v[k]] + v[k] * v[k])) / (2f * q - 2f * v[k]); }
                    k++; v[k] = q; zb[k] = s2; zb[k + 1] = INF;
                }
                k = 0;
                for (int q = 0; q < n; q++) { while (zb[k + 1] < q) k++; outA[q] = (q - v[k]) * (q - v[k]) + f[v[k]]; }
                for (int q = 0; q < n; q++) d[idx(q)] = outA[q];
            }
            for (int z = 0; z < NZ; z++) for (int y = 0; y < NY; y++) { int zz = z, yy = y; Pass(NX, x => (zz * NY + yy) * NX + x); }
            for (int z = 0; z < NZ; z++) for (int x = 0; x < NX; x++) { int zz = z, xx = x; Pass(NY, y => (zz * NY + y) * NX + xx); }
            for (int y = 0; y < NY; y++) for (int x = 0; x < NX; x++) { int yy = y, xx = x; Pass(NZ, z => (z * NY + yy) * NX + xx); }
            var b = new byte[d.Length];
            for (int i = 0; i < d.Length; i++) b[i] = (byte)Mathf.Min(255, Mathf.FloorToInt(Mathf.Sqrt(d[i]) * CELL * 100f));
            return b;
        }

        // 俯视预览：每列最高的实体格按高度着色（自查摆件位置和场景对不对得上）
        static void WritePreview(bool[] solid, string path)
        {
            var tex = new Texture2D(NX, NZ, TextureFormat.RGB24, false);
            for (int z = 0; z < NZ; z++)
                for (int x = 0; x < NX; x++)
                {
                    int top = -1;
                    for (int y = NY - 1; y >= 0; y--) if (solid[(z * NY + y) * NX + x]) { top = y; break; }
                    Color col = top < 0 ? new Color(0.08f, 0.08f, 0.1f) : Color.HSVToRGB(0.66f - 0.66f * top / (float)NY, 0.8f, 0.95f);
                    tex.SetPixel(x, z, col);
                }
            // 桌子轮廓、座位、出生点
            void Dot(Vector3 p, Color c, int r) { int px = Mathf.RoundToInt((p.x - MIN.x) / CELL), pz = Mathf.RoundToInt((p.z - MIN.z) / CELL); for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) if (dx * dx + dz * dz <= r * r) tex.SetPixel(px + dx, pz + dz, c); }
            for (int a = 0; a < 360; a += 3) Dot(Layout.TABLE.center + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0, Mathf.Sin(a * Mathf.Deg2Rad)) * Layout.TABLE.radius, Color.white, 0);
            Dot(new Vector3(0, 0, 0.12f), Color.green, 3);
            foreach (var s in Game.SPAWNS) Dot(s.pos, Color.magenta, 2);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
