// 甩成星星的敌人变成夜空里的星星（10-31：原来这里是空的占位，玩家只看得到飞出去的拖尾，什么星星都没有）。
//   1. 原地“啵”地冒出一颗卡通五角星（深紫描边 + 按敌人种类着色的星身 + 背后一团柔光），转着弹一下；
//   2. 沿弧线飞上铁拱门上方的夜空，越飞越远（保持看起来大约 2° 大小）；
//   3. 停在天上一闪一闪，这一夜甩出去的都留在那儿，攒成一片；新的一夜清空。
// 头显上关了后期（没有 Bloom），“亮”靠星身自发光颜色 + 叠加柔光。
using System.Collections.Generic;
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public class SkyStars : MonoBehaviour
    {
        public Material starMat;   // 透明、读顶点色（URP Particles/Unlit），星身和描边共用
        public Material haloMat;   // 叠加柔光（SoftDot）
        public Transform worldRoot;
        [System.NonSerialized] public SolidGrid grid;   // 场景占用网格（GameLoop 运行时给）：往上飞时从椽子缝里穿出去，不穿模
        public Camera cam;
        public int maxStars = 30;

        class Star
        {
            public Transform root, body, halo; public Renderer haloR;
            public Vector3 from, ctrl, to; public float t, phase, sizeTo; public Color color;
            public List<Vector3> way;   // 世界坐标：先找到能竖直穿出藤架的空隙，再升上去，最后弧线飞到天上
        }
        readonly List<Star> stars = new List<Star>();
        [System.NonSerialized] Mesh fillMesh, rimMesh;
        [System.NonSerialized] Material rimMatInst, fillMatInst;
        [System.NonSerialized] MaterialPropertyBlock mpb;
        static readonly int ID_Base = Shader.PropertyToID("_BaseColor");
        const float POP = 0.35f, RISE = 1.9f, SIZE0 = 0.09f;

        static Color KindColor(string kind)
        {
            switch (kind)
            {
                case KindIds.Bat: return new Color(0.82f, 0.62f, 1f);
                case KindIds.Pumpkin: return new Color(1f, 0.64f, 0.22f);
                case KindIds.Ghost: case KindIds.Ghostling: return new Color(0.62f, 0.96f, 1f);
                case KindIds.Cat: return new Color(0.86f, 1f, 0.5f);
                case KindIds.Spider: return new Color(0.96f, 0.74f, 1f);
                case KindIds.Frank: return new Color(1f, 0.62f, 0.8f);
                default: return new Color(1f, 0.92f, 0.62f);   // 小骷髅：骨白偏金
            }
        }

        void EnsureAssets()
        {
            if (fillMesh && rimMesh && mpb != null && (rimMatInst || !starMat)) return;
            fillMesh = StarMesh(1f, 0.45f, 0f, true);
            rimMesh = StarMesh(1.24f, 0.6f, 0f, false);
            if (starMat)
            {
                rimMatInst = new Material(starMat) { renderQueue = starMat.renderQueue };
                fillMatInst = new Material(starMat) { renderQueue = starMat.renderQueue + 1 };   // 星身画在描边上面
            }
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// 卡通五角星：尖角和凹角之间是直边（以前按极坐标插值半径，边会鼓出来，看着像花瓣）；
        /// 中心亮、尖角略暗（顶点色）。shaded=false 时整颗同色（描边，由调用方着色）
        /// </summary>
        static Mesh StarMesh(float outer, float inner, float unused, bool shaded)
        {
            var ring = new List<Vector3>();
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2 + i * Mathf.PI * 2 / 10;
                float r = i % 2 == 0 ? outer : inner;
                ring.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0));
            }
            var vs = new List<Vector3> { Vector3.zero }; var cs = new List<Color> { Color.white }; var tri = new List<int>();
            foreach (var p in ring) { vs.Add(p); float d = p.magnitude / outer; cs.Add(shaded ? Color.Lerp(Color.white, new Color(0.8f, 0.8f, 0.8f), d) : Color.white); }
            for (int i = 0; i < ring.Count; i++) { tri.Add(0); tri.Add(1 + (i + 1) % ring.Count); tri.Add(1 + i); }
            var m = new Mesh { name = shaded ? "StarFill" : "StarRim" };
            m.SetVertices(vs); m.SetColors(cs); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        }

        /// <summary>规则层坐标 → 世界坐标</summary>
        Vector3 W(Vector3 p) => worldRoot ? worldRoot.TransformPoint(p) : p;

        /// <summary>天上的位置：铁拱门上方（玩家正前方 ±45°、仰角 22°~40°、28 米远），和已有的星星至少隔开 5°</summary>
        Vector3 SkySlot()
        {
            var eye = new Vector3(0, 1.15f, 0.12f);
            Vector3 best = Vector3.zero; float bestSep = -1;
            for (int tries = 0; tries < 24; tries++)
            {
                float az = Random.Range(-45f, 45f), el = Random.Range(22f, 40f);
                var dir = Quaternion.Euler(-el, 180f + az, 0) * Vector3.forward;
                var p = eye + dir * 28f;
                float sep = 180f;
                foreach (var s in stars) sep = Mathf.Min(sep, Vector3.Angle(dir, s.to - W(eye)));
                if (sep > bestSep) { bestSep = sep; best = p; }
                if (sep >= 5f) break;
            }
            return W(best);
        }

        public void AddStar(string kind) => AddStar(kind, W(new Vector3(0, 2.0f, -3.5f)));

        public void AddStar(string kind, Vector3 worldPos)
        {
            EnsureAssets();
            if (stars.Count >= maxStars) { var old = stars[0]; stars.RemoveAt(0); if (old.root) Destroy(old.root.gameObject); }
            var s = new Star { color = KindColor(kind), phase = Random.value * 6.28f, from = worldPos };
            s.to = SkySlot();
            s.ctrl = Vector3.Lerp(s.from, s.to, 0.35f) + Vector3.up * 2.5f;
            s.way = ClimbOut(worldPos);
            s.sizeTo = Vector3.Distance(cam ? cam.transform.position : W(new Vector3(0, 1.15f, 0.12f)), s.to) * 0.055f;   // 约 3° 大（头显里太小看不清）
            var root = new GameObject("SkyStar_" + kind).transform;
            root.SetParent(transform, false); root.position = worldPos;
            s.root = root;
            s.halo = MakePart(root, "Halo", null, haloMat, 0.002f).transform;
            s.haloR = s.halo.GetComponent<Renderer>();
            s.halo.localScale = Vector3.one * 3.2f;
            s.body = new GameObject("Body").transform; s.body.SetParent(root, false);
            var rim = MakePart(s.body, "Rim", rimMesh, rimMatInst, 0.001f);
            var fill = MakePart(s.body, "Fill", fillMesh, fillMatInst, 0f);
            // 描边深紫、星身按种类着色（自发光一样的亮色）
            rim.GetPropertyBlock(mpb); mpb.SetColor(ID_Base, new Color(0.18f, 0.1f, 0.28f, 1f)); rim.SetPropertyBlock(mpb);
            fill.GetPropertyBlock(mpb); mpb.SetColor(ID_Base, Color.Lerp(s.color, Color.white, 0.1f)); fill.SetPropertyBlock(mpb);
            root.localScale = Vector3.zero;
            stars.Add(s);
        }

        Renderer MakePart(Transform parent, string name, Mesh mesh, Material mat, float back)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, 0, back);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh ? mesh : QuadMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return mr;
        }

        static Mesh quad;
        static Mesh QuadMesh()
        {
            if (quad) return quad;
            quad = new Mesh { name = "StarHaloQuad" };
            quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0) });
            quad.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            quad.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            quad.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            quad.RecalculateBounds();
            return quad;
        }

        /// <summary>
        /// 从冒出星星的地方钻出藤架：在附近找一根能一路竖直升到藤架顶上（3.4 米）都不碰任何摆件的空气柱，
        /// 先平移过去再竖直升上去（世界坐标）。没有网格时直接往上
        /// </summary>
        List<Vector3> ClimbOut(Vector3 worldPos)
        {
            var w = new List<Vector3> { worldPos };
            var p0 = worldRoot ? worldRoot.InverseTransformPoint(worldPos) : worldPos;
            const float R = 0.08f, TOP = 3.4f;
            if (grid == null) { w.Add(W(new Vector3(p0.x, TOP, p0.z))); return w; }
            for (float r = 0f; r <= 1.6f; r += 0.05f)
            {
                int n = r < 0.01f ? 1 : Mathf.CeilToInt(2 * Mathf.PI * r / 0.05f);
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2 / n;
                    var q = new Vector3(p0.x + Mathf.Cos(a) * r, p0.y, p0.z + Mathf.Sin(a) * r);
                    var up = new Vector3(q.x, TOP, q.z);
                    if (!grid.SegmentFree(p0, q, R) || !grid.SegmentFree(q, up, R)) continue;
                    if (r > 0.01f) w.Add(W(q));
                    w.Add(W(up));
                    return w;
                }
            }
            w.Add(W(new Vector3(p0.x, TOP, p0.z)));
            return w;
        }

        static Vector3 AlongWay(List<Vector3> way, float k)
        {
            float total = 0; for (int i = 1; i < way.Count; i++) total += Vector3.Distance(way[i - 1], way[i]);
            float d = k * total;
            for (int i = 1; i < way.Count; i++)
            {
                float l = Vector3.Distance(way[i - 1], way[i]);
                if (d <= l || i == way.Count - 1) return Vector3.Lerp(way[i - 1], way[i], l > 1e-5f ? Mathf.Clamp01(d / l) : 1f);
                d -= l;
            }
            return way[way.Count - 1];
        }

        /// <summary>新的一夜：清空夜空里的星星</summary>
        public void Clear()
        {
            foreach (var s in stars) if (s.root) { if (Application.isPlaying) Destroy(s.root.gameObject); else DestroyImmediate(s.root.gameObject); }
            stars.Clear();
        }

        void Update() => Tick(Time.deltaTime, Time.time);

        /// <summary>推进动画（编辑器预览也用它）</summary>
        public void Tick(float dt, float time)
        {
            if (stars.Count == 0) return;
            var camP = cam ? cam.transform.position : Vector3.zero;
            foreach (var s in stars)
            {
                if (!s.root) continue;
                s.t += dt;
                float size;
                if (s.t < POP)
                {
                    // 原地冒出来：弹一下（0 → 1.3 → 1）
                    float k = s.t / POP;
                    size = SIZE0 * (k < 0.6f ? Mathf.SmoothStep(0, 1.3f, k / 0.6f) : Mathf.Lerp(1.3f, 1f, (k - 0.6f) / 0.4f));
                    s.root.position = s.from;
                }
                else if (s.t < POP + RISE)
                {
                    // 先沿防穿模路点钻出藤架（前 35% 时间），再弧线飞上夜空；大小按对数插值，飞远了看起来大小不变
                    float k = Mathf.SmoothStep(0, 1, (s.t - POP) / RISE);
                    const float OUT = 0.35f;
                    if (k < OUT) s.root.position = AlongWay(s.way, k / OUT);
                    else
                    {
                        float q = (k - OUT) / (1 - OUT);
                        var top = s.way[s.way.Count - 1];
                        var c2 = Vector3.Lerp(top, s.to, 0.3f) + Vector3.up * 1.5f;
                        var a = Vector3.Lerp(top, c2, q); var b = Vector3.Lerp(c2, s.to, q);
                        s.root.position = Vector3.Lerp(a, b, q);
                    }
                    size = Mathf.Exp(Mathf.Lerp(Mathf.Log(SIZE0), Mathf.Log(s.sizeTo), k));
                }
                else
                {
                    s.root.position = s.to;
                    size = s.sizeTo * (1f + 0.12f * Mathf.Sin(time * 3f + s.phase));   // 一闪一闪
                }
                s.root.localScale = Vector3.one * size;
                // 永远正对着玩家；星身慢慢转
                s.root.rotation = Quaternion.LookRotation(s.root.position - camP, Vector3.up);
                s.body.localRotation = Quaternion.Euler(0, 0, s.t < POP + RISE ? s.t * 260f : Mathf.Sin(time * 0.8f + s.phase) * 12f);
                if (s.haloR)
                {
                    float glow = s.t < POP ? 1.2f : 0.75f + 0.25f * Mathf.Sin(time * 3f + s.phase);
                    s.haloR.GetPropertyBlock(mpb); mpb.SetColor(ID_Base, new Color(s.color.r, s.color.g, s.color.b, 0.8f * glow)); s.haloR.SetPropertyBlock(mpb);
                }
            }
        }
    }
}
