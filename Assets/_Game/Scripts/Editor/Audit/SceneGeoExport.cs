// 把场景几何导出给 Blender 做精确相交审计（Tools/scene_interference.py）：
//   Logs/audit/geo/meshes.bin  : 每个唯一网格一段 [int nv][int nt][nv*3 float 本地顶点][nt*3 int 索引]
//   Logs/audit/geo/items.json  : 每个渲染体 {item, path, mesh(索引), m(4x4 本地到世界，行主序)}
// 只导出 LOD0（或无 LODGroup 的网格）；粒子、天空、云海、月亮面片不导出。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class SceneGeoExport
    {
        [MenuItem("Tools/TrickOrFreeze/Export Scene Geometry")]
        public static string Run()
        {
            var dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "audit", "geo");
            Directory.CreateDirectory(dir);
            var world = GameObject.Find("World");
            var meshIndex = new Dictionary<Mesh, int>();
            var meshes = new List<Mesh>();
            var sb = new StringBuilder("[");
            int n = 0;
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>(false))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (!r || !r.enabled || !mf.sharedMesh) continue;
                if (IsHigherLod(mf.transform)) continue;
                string nm = mf.gameObject.name;
                if (nm == "CloudSea" || nm == "Moon") continue;
                var item = ItemRoot(mf.transform, world.transform);
                if (!meshIndex.TryGetValue(mf.sharedMesh, out int mi)) { mi = meshes.Count; meshIndex[mf.sharedMesh] = mi; meshes.Add(mf.sharedMesh); }
                var m = mf.transform.localToWorldMatrix;
                if (n++ > 0) sb.Append(',');
                sb.Append("{\"item\":\"").Append(Esc(item.name + "@" + item.GetInstanceIDSafe())).Append("\",\"path\":\"").Append(Esc(PathOf(mf.transform))).Append("\",\"mesh\":").Append(mi).Append(",\"m\":[");
                for (int i = 0; i < 16; i++) { if (i > 0) sb.Append(','); sb.Append(m[i / 4, i % 4].ToString("R", System.Globalization.CultureInfo.InvariantCulture)); }
                sb.Append("]}");
            }
            sb.Append(']');
            File.WriteAllText(Path.Combine(dir, "items.json"), sb.ToString());
            using (var bw = new BinaryWriter(File.Open(Path.Combine(dir, "meshes.bin"), FileMode.Create)))
            {
                bw.Write(meshes.Count);
                foreach (var me in meshes)
                {
                    var vs = me.vertices; var ts = me.triangles;
                    bw.Write(vs.Length); bw.Write(ts.Length / 3);
                    foreach (var v in vs) { bw.Write(v.x); bw.Write(v.y); bw.Write(v.z); }
                    foreach (var t in ts) bw.Write(t);
                }
            }
            return $"renderers={n} uniqueMeshes={meshes.Count}";
        }

        static string GetInstanceIDSafe(this Transform t) => Mathf.Abs(t.GetHashCode() % 100000).ToString();

        static bool IsHigherLod(Transform t)
        {
            for (var p = t; p; p = p.parent) if (p.name == "LOD1" || p.name == "LOD2") return true;
            return false;
        }

        // 摆件根：最外层预制体实例根；没有则用直接挂在 World/Halloween/CentralIsle 等分组下的那一层
        static Transform ItemRoot(Transform t, Transform world)
        {
            Transform best = null;
            for (var p = t; p && p != world; p = p.parent)
                if (PrefabUtility.IsOutermostPrefabInstanceRoot(p.gameObject)) { best = p; }
            if (best) return best;
            var q = t;
            while (q.parent && q.parent != world && q.parent.name != "Halloween" && q.parent.name != "CentralIsle") q = q.parent;
            return q;
        }

        static string PathOf(Transform t) { var s = t.name; while (t.parent) { t = t.parent; s = t.name + "/" + s; } return s; }
        static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
