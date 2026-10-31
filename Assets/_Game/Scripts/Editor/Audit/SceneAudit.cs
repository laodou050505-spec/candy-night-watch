// 场景量化审计（菜单 Tools/TrickOrFreeze/Audit Scene）：
//  1. 每个“摆件”（World 下带 LODGroup 或 MeshRenderer 的预制体实例，按根对象分组）用 LOD0 网格建临时 MeshCollider（在隔离层），
//     两两用 Physics.ComputePenetration 求穿插深度；同一挂接关系（父子）跳过。穿插深度 > 1.5cm 记为问题。
//  2. 支撑检测：从物体底部 8 个采样点向下射线，看底面以下 3cm 内是否有别的物体（地面/桌面/护边/岛）；
//     底面悬空 > 3cm 且不在“允许悬空”名单（灯笼、旗串、蛛网、烛海、飞行物）里记为问题。
//  3. 对每个摆件从 6 个方向拍 512×512 近景（物体包围盒自动取景），存 Logs/audit/<序号>_<名字>_<方向>.png。
//  结果写 Logs/audit/scene_audit.json。
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class SceneAudit
    {
        const int Layer = 31;
        static readonly string[] FloatOk = { "BatLantern", "Bunting", "Deco_Web", "Deco_Spider", "Candle_", "CandleSea", "Bat", "Ghost", "Witch", "Moon", "L2_RoseWindow", "Isle_", "Flock", "Chain", "Silk", "Glow" };

        class Item { public string name; public GameObject root; public List<MeshCollider> cols = new List<MeshCollider>(); public Bounds b; }

        [MenuItem("Tools/TrickOrFreeze/Audit Scene")]
        public static void Run() => Run(true);

        public static string Run(bool shots)
        {
            var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "audit");
            Directory.CreateDirectory(outDir);
            var world = GameObject.Find("World");
            var items = Collect(world.transform);
            var tmp = new GameObject("__audit_cols");
            // 建碰撞体
            foreach (var it in items)
            {
                foreach (var mf in it.root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (!IsLod0OrPlain(mf.transform, it.root.transform) || !mf.sharedMesh) continue;
                    var r = mf.GetComponent<Renderer>(); if (!r || !r.enabled) continue;
                    var g = new GameObject(it.name + "_col"); g.layer = Layer; g.transform.SetParent(tmp.transform, false);
                    g.transform.position = mf.transform.position; g.transform.rotation = mf.transform.rotation; g.transform.localScale = mf.transform.lossyScale;
                    var mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; mc.convex = false;
                    it.cols.Add(mc);
                    if (it.b.size == Vector3.zero) it.b = r.bounds; else it.b.Encapsulate(r.bounds);
                }
            }
            items = items.Where(i => i.cols.Count > 0).ToList();
            Physics.SyncTransforms();
            bool oldBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;   // 内部判定与深度都需要能打到背面
            var sb = new StringBuilder(); sb.Append("{\"overlaps\":[");
            int no = 0;
            // 穿插：非凸 MeshCollider 之间不能 ComputePenetration，改用“顶点在对方内部”采样：对 A 的顶点做 ClosestPoint 测试（凸包外判）+ 射线奇偶法
            for (int i = 0; i < items.Count; i++)
                for (int j = i + 1; j < items.Count; j++)
                {
                    var A = items[i]; var B = items[j];
                    if (!A.b.Intersects(B.b)) continue;
                    if (Related(A.root.transform, B.root.transform)) continue;
                    float dAB = MaxInside(A, B), dBA = MaxInside(B, A);
                    float d = Mathf.Max(dAB, dBA);
                    if (d > 0.015f)
                    {
                        if (no++ > 0) sb.Append(',');
                        sb.Append($"{{\"a\":\"{Esc(A.name)}\",\"b\":\"{Esc(B.name)}\",\"depth\":{d:F3},\"at\":\"{Fmt(A.b.center)}|{Fmt(B.b.center)}\"}}");
                    }
                }
            sb.Append("],\"floating\":[");
            int nf = 0;
            foreach (var it in items)
            {
                if (FloatOk.Any(k => it.name.Contains(k)) || PathOf(it.root.transform).Contains("CandleSea")) continue;
                float gap = SupportGap(it, items);
                if (gap > 0.03f)
                {
                    if (nf++ > 0) sb.Append(',');
                    sb.Append($"{{\"name\":\"{Esc(it.name)}\",\"gap\":{gap:F3},\"bottom\":{it.b.min.y:F3},\"at\":\"{Fmt(it.b.center)}\"}}");
                }
            }
            sb.Append("],\"items\":[");
            for (int k = 0; k < items.Count; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append($"{{\"i\":{k},\"name\":\"{Esc(items[k].name)}\",\"path\":\"{Esc(PathOf(items[k].root.transform))}\",\"min\":\"{Fmt(items[k].b.min)}\",\"max\":\"{Fmt(items[k].b.max)}\"}}");
            }
            sb.Append("]}");
            Physics.queriesHitBackfaces = oldBack;
            File.WriteAllText(Path.Combine(outDir, "scene_audit.json"), sb.ToString());
            if (shots) Shots(items, outDir);
            Object.DestroyImmediate(tmp);
            return $"items={items.Count} overlaps={no} floating={nf}";
        }

        static List<Item> Collect(Transform world)
        {
            var list = new List<Item>();
            void Walk(Transform t)
            {
                bool isInst = PrefabUtility.IsOutermostPrefabInstanceRoot(t.gameObject) || (t.GetComponent<MeshRenderer>() && !PrefabUtility.IsPartOfPrefabInstance(t.gameObject) && t.GetComponent<MeshFilter>());
                if (isInst && t.gameObject.activeInHierarchy)
                {
                    list.Add(new Item { name = t.name + "#" + list.Count, root = t.gameObject });
                    // 预制体内部若嵌套别的预制体（比如岛上建筑在 Isle 根下，是兄弟不是子），继续往下找子预制体
                    foreach (Transform c in t) if (PrefabUtility.IsOutermostPrefabInstanceRoot(c.gameObject)) Walk(c);
                    return;
                }
                foreach (Transform c in t) Walk(c);
            }
            Walk(world);
            return list;
        }

        static bool IsLod0OrPlain(Transform t, Transform root)
        {
            for (var p = t; p && p != root.parent; p = p.parent)
            {
                if (p.name == "LOD1" || p.name == "LOD2") return false;
                if (p != root && p != t && PrefabUtility.IsOutermostPrefabInstanceRoot(p.gameObject)) return false;   // 属于嵌套的子摆件
            }
            return true;
        }

        static bool Related(Transform a, Transform b) => a.IsChildOf(b) || b.IsChildOf(a);

        // A 的顶点有多少深入 B 内部：对每个采样顶点，向 +Y 和 -Y 各打一条射线统计与 B 的交点奇偶（只用 B 的碰撞体），
        // 两个方向都为奇数才算在内部；深度用到 B 表面的最近距离（向六个方向射线取最短命中）估计
        static float MaxInside(Item A, Item B)
        {
            float best = 0;
            var bset = new HashSet<Collider>(B.cols);
            foreach (var col in A.cols)
            {
                var m = col.sharedMesh; var vs = m.vertices; var tr = col.transform;
                int step = Mathf.Max(1, vs.Length / 1500);
                for (int i = 0; i < vs.Length; i += step)
                {
                    var p = tr.TransformPoint(vs[i]);
                    if (!B.b.Contains(p)) continue;
                    if (!Inside(p, bset)) continue;
                    float d = float.MaxValue;
                    foreach (var dir in Dirs)
                        foreach (var c in B.cols)
                            if (c.Raycast(new Ray(p, dir), out var h, 2f)) d = Mathf.Min(d, h.distance);
                    if (d < 2f) best = Mathf.Max(best, d);
                }
            }
            return best;
        }

        static readonly Vector3[] Dirs = { Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back };

        static bool Inside(Vector3 p, HashSet<Collider> cols)
        {
            int votes = 0;
            foreach (var dir in new[] { Vector3.up, Vector3.down, new Vector3(0.37f, 0.11f, 0.92f).normalized })
            {
                int hits = 0;
                foreach (var c in cols)
                {
                    // 射线从远处射向 p，统计穿过的面数（MeshCollider 只报第一次，所以分段推进）
                    var o = p + dir * 50f; var d = -dir; float left = 50f;
                    for (int k = 0; k < 64 && left > 1e-4f; k++)
                    {
                        if (!c.Raycast(new Ray(o, d), out var h, left)) break;
                        hits++; o = h.point + d * 1e-4f; left -= h.distance + 1e-4f;
                    }
                }
                if ((hits & 1) == 1) votes++;
            }
            return votes >= 2;
        }

        static float SupportGap(Item it, List<Item> all)
        {
            var b = it.b; float minGap = float.MaxValue;
            var others = all.Where(o => o != it && !Related(o.root.transform, it.root.transform)).SelectMany(o => o.cols).ToList();
            // 底部采样：取底面 5% 高度内的顶点
            var pts = new List<Vector3>();
            foreach (var col in it.cols)
            {
                var vs = col.sharedMesh.vertices; var tr = col.transform; int step = Mathf.Max(1, vs.Length / 4000);
                for (int i = 0; i < vs.Length; i += step) { var w = tr.TransformPoint(vs[i]); if (w.y < b.min.y + Mathf.Max(0.02f, b.size.y * 0.04f)) pts.Add(w); }
            }
            if (pts.Count == 0) return 0;
            foreach (var p in pts.Take(400))
                foreach (var c in others)
                    if (c.Raycast(new Ray(p + Vector3.up * 0.02f, Vector3.down), out var h, 1f)) minGap = Mathf.Min(minGap, h.distance - 0.02f);
            return minGap == float.MaxValue ? 9.99f : minGap;
        }

        // 近景拍摄：每种摆件取第一个实例；挂件/桥/岛/灯/树/烛台等“位置关系”要紧的全部实例都拍；烛海只拍 3 支
        public static string ShotsOnly()
        {
            var outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "audit", "shots");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);
            var world = GameObject.Find("World");
            var items = Collect(world.transform);
            foreach (var it in items)
            {
                foreach (var r in it.root.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    if (!IsLod0OrPlain(r.transform, it.root.transform)) continue;
                    if (it.b.size == Vector3.zero) it.b = r.bounds; else it.b.Encapsulate(r.bounds);
                }
            }
            string Base(string n) { int k = n.IndexOf('#'); var b = k > 0 ? n.Substring(0, k) : n; int sp = b.IndexOf(' '); return sp > 0 ? b.Substring(0, sp) : b; }
            string[] all = { "Bridge", "L7_FloatIsland", "BatLantern", "Bunting", "Deco_Web", "H5_Skull", "H1_Jack", "H2_Spooky", "L1_Arch", "L8_Rope", "Chain" };
            var seen = new Dictionary<string, int>();
            var pick = new List<Item>();
            foreach (var it in items)
            {
                if (it.b.size == Vector3.zero || it.b.size.magnitude > 40) continue;
                var b = Base(it.name);
                seen.TryGetValue(b, out int c); seen[b] = c + 1;
                bool keep = c == 0 || all.Any(a => b.StartsWith(a)) || (b.StartsWith("Candle_Float") && c < 3);
                if (keep) pick.Add(it);
            }
            Shots(pick, outDir);
            return $"picked={pick.Count}";
        }

        static void Shots(List<Item> items, string outDir)
        {
            var cg = new GameObject("__audit_cam"); var cam = cg.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox; cam.fieldOfView = 40; cam.nearClipPlane = 0.01f; cam.farClipPlane = 200;
            var rt = new RenderTexture(768, 768, 24) { antiAliasing = 4 }; cam.targetTexture = rt;
            var tex = new Texture2D(768, 768, TextureFormat.RGB24, false);
            var ud = cg.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); ud.renderPostProcessing = true;
            var dirs = new (string, Vector3)[] { ("in", Vector3.zero), ("out", Vector3.zero), ("side", Vector3.zero), ("top", new Vector3(0.05f, 1, -0.2f)), ("low", Vector3.zero) };
            for (int k = 0; k < items.Count; k++)
            {
                var it = items[k]; if (it.b.size.magnitude > 30) continue;
                float rad = it.b.extents.magnitude;
                // 方向按“从场景中心看过去”定义：in = 玩家一侧看向物体（玩家实际看到的面），out = 背面，side = 切向侧面，low = 低角度仰视（看底面是否悬空/穿地）
                var toC = new Vector3(-it.b.center.x, 0, -it.b.center.z); if (toC.sqrMagnitude < 1e-4f) toC = Vector3.back; toC.Normalize();
                var tan = Vector3.Cross(Vector3.up, toC);
                foreach (var (tag, d0) in dirs)
                {
                    var d = tag == "in" ? toC + Vector3.up * 0.3f : tag == "out" ? -toC + Vector3.up * 0.3f : tag == "side" ? tan + Vector3.up * 0.15f : tag == "low" ? toC * 0.8f + tan * 0.4f - Vector3.up * 0.25f : d0;
                    float dist = Mathf.Max(rad * 2.6f, 0.25f);
                    cam.transform.position = it.b.center + d.normalized * dist;
                    cam.transform.LookAt(it.b.center);
                    cam.Render(); RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 768, 768), 0, 0); tex.Apply();
                    File.WriteAllBytes(Path.Combine(outDir, $"{k:D3}_{San(it.name)}_{tag}.png"), tex.EncodeToPNG());
                }
            }
            RenderTexture.active = null; Object.DestroyImmediate(cg);
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
        static string San(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        static string Fmt(Vector3 v) => $"{v.x:F2},{v.y:F2},{v.z:F2}";
    }
}
