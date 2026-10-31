// 彩色勾线：场景（花园、万圣节布景）里的 ToonLit 材质把描边改成“本体颜色提亮”的同色亮线（ToonLit._ColorLine），
// 参考 10-29 用户给的万圣节卡通插画——南瓜勾橙线、草叶勾绿线、墓碑勾淡紫线，整体更夸张、更有万圣节海报味。
// 小鬼、手套、桌上的玩法物件（糖桶、坩埚、烛台、桌子）保持墨色线：玩法要一眼和背景分开。
// 和小鬼共用的材质不改（以小鬼为准）。
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class ColorLines
    {
        public const float Amount = 0.4f;    // 10-30 用户：描边感要弱一些
        static readonly string[] EnvRoots = { "Garden", "Halloween" };
        static readonly string[] ThinKeys = { "cs_leaf", "cs_stalk", "cs_tie", "gv_fence", "hb_bale", "pp_leaf", "pp_vine", "pg_vine", "pg_leaf", "scr_rope", "br_hoop" };
        static readonly string[] NoOutline = { "gs_tuft", "wl_twig", "hb_twine", "scr_straw", "pg_wire", "iron", "s9_broom", "wb_web" };

        /// <summary>
        /// 灯光减负（10-30 实机 GPU 16.7ms，预算 13.9ms）：每个物体最多受 2 盏逐像素点光，灯多了不止费计算，还会互相抢名额闪烁。
        /// 关掉 ① 离座位 10 米外的点光（物件自发光还在）；② 旁边 1.5 米内已有更亮一盏的点光（蘑菇灯群这种）。桌面 3 米内的灯不动。
        /// </summary>
        [MenuItem("Tools/TrickOrFreeze/Optimize Lights")]
        public static void OptimizeLights()
        {
            var world = GameObject.Find("World"); if (!world) return;
            var eye = world.transform.TransformPoint(new Vector3(0, 1.15f, 0.12f));
            var ls = new List<Light>();
            foreach (var l in world.GetComponentsInChildren<Light>(true)) if (l.type == LightType.Point && l.GetComponentInParent<TrickOrFreeze.UI.CoverStage>(true) == null && l.transform.parent && l.name == "FlickerLight") ls.Add(l);
            // 结果只取决于灯的位置和亮度，和上次运行、遍历顺序无关：先全部打开，再从亮到暗决定去留（只和已经留下的灯比）
            ls.Sort((x, y) => (y.intensity * y.range).CompareTo(x.intensity * x.range));
            var kept = new List<Light>();
            int far = 0, dup = 0;
            foreach (var l in ls)
            {
                float d = Vector3.Distance(l.transform.position, eye);
                bool off = d > 10f;
                if (off) far++;
                else if (d > 3f && kept.Exists(o => Vector3.Distance(o.transform.position, l.transform.position) < 1.5f)) { off = true; dup++; }
                if (!off) kept.Add(l);
                Undo.RecordObject(l, "Optimize Lights");
                if (l.enabled == off) { l.enabled = !off; EditorUtility.SetDirty(l); }
            }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"[Lights] 共 {ls.Count} 盏，关掉远处 {far} 盏、重复 {dup} 盏");
        }

        [MenuItem("Tools/TrickOrFreeze/Apply Color Lines")]
        public static void Apply()
        {
            var world = GameObject.Find("World");
            if (!world) { Debug.LogError("[ColorLines] 场景里没有 World"); return; }
            var keep = new HashSet<Material>();
            // 小鬼预制体、手套、桌上物件用到的材质：保持墨色
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Prefabs/Art" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var n = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!(n.StartsWith("E") && n.Contains("_")) && !n.StartsWith("Desk_") && !n.StartsWith("S1_") && !n.StartsWith("S2_") && !n.StartsWith("S3_")) continue;
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var r in p.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) keep.Add(m);
            }
            foreach (var n in new[] { "Hands", "Desk_Table", "Desk_Bucket", "Desk_Cauldron", "Desk_CandleCluster", "VineRamps" })
            {
                var t = world.transform.Find(n); if (!t) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true)) foreach (var m in r.sharedMaterials) if (m) keep.Add(m);
            }
            var set = new HashSet<Material>();
            foreach (var root in EnvRoots)
            {
                var t = world.transform.Find(root); if (!t) continue;
                foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials)
                        if (m && m.HasProperty("_ColorLine") && !keep.Contains(m)) set.Add(m);
            }
            foreach (var m in set) { m.SetFloat("_ColorLine", Amount); EditorUtility.SetDirty(m); }
            // 细碎物件（10-30 实机：干草垛、玉米秆的细草秆带着亮橙色彩色描边，一转头就闪）：
            // 彩色亮描边改回墨色；最细的那几类（草簇、细枝、麻绳、铁链、细铁丝、稻草、扫帚）不画描边
            int thin = 0, none = 0;
            foreach (var m in set)
            {
                string n = m.name.ToLower();
                bool veryThin = System.Array.Exists(NoOutline, k => n.Contains(k));
                bool isThin = veryThin || System.Array.Exists(ThinKeys, k => n.Contains(k));
                if (!isThin) continue;
                m.SetFloat("_ColorLine", 0f); thin++;
                if (veryThin && m.HasProperty("_OutlineWidth")) { m.SetFloat("_OutlineWidth", 0f); none++; }
                EditorUtility.SetDirty(m);
            }
            Debug.Log($"[ColorLines] 细碎材质 {thin} 个改墨色描边，其中 {none} 个不画描边");
            // 远景不画描边这一遍（10-30 性能：描边在 4~9 米淡出，9 米外根本看不见，但每个物体仍要整个再画一遍——
            // 202 次绘制、几十万三角形白白耗 GPU）。只在“用到它的所有物体都在座位 10 米外”的材质上关掉描边 Pass，近处的不受影响。
            {
                var eye = new Vector3(0, 1.15f, 0.12f);
                var nearest = new Dictionary<Material, float>();
                foreach (var r in world.GetComponentsInChildren<Renderer>(true))
                {
                    float d = Vector3.Distance(eye, world.transform.InverseTransformPoint(r.bounds.ClosestPoint(world.transform.TransformPoint(eye))));
                    foreach (var m in r.sharedMaterials) if (m) nearest[m] = nearest.TryGetValue(m, out var o) ? Mathf.Min(o, d) : d;
                }
                int off = 0, on = 0;
                foreach (var kv in nearest)
                {
                    var m = kv.Key; if (!m.shader || m.shader.name != "TrickOrFreeze/ToonLit") continue;
                    bool far = kv.Value > 8f;           // 描边 7 米外已看不见
                    if (m.GetShaderPassEnabled("SRPDefaultUnlit") == !far) { if (far) off++; continue; }
                    m.SetShaderPassEnabled("SRPDefaultUnlit", !far); EditorUtility.SetDirty(m);
                    if (far) off++; else on++;
                }
                Debug.Log($"[ColorLines] 远景材质 {off} 个关掉描边 Pass（恢复近处 {on} 个）");
            }
            foreach (var m in keep) if (m && m.HasProperty("_ColorLine") && m.GetFloat("_ColorLine") > 0) { m.SetFloat("_ColorLine", 0); EditorUtility.SetDirty(m); }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ColorLines] 彩色勾线 {set.Count} 个材质；保持墨色 {keep.Count} 个");
        }
    }
}
