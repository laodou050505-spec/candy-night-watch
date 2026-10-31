// 封面专用背景“月下纸剧场”（10-29 用户：封面背景要更高级，不要和游戏场景一样）。
// 贴图由 Tools/make_cover_backdrop.py 代码绘制；这里把它们搭成以玩家为圆心的几层弧面（一整圈 360°）：
//   夜空大球（CoverSky，程序生成）→ 月亮光晕 → 月亮 → 远山小镇 → 雾 → 鬼堡 → 雾 → 墓园 → 雾 → 两侧框景大树
// 绘制顺序用 renderQueue 固定（远→近）：2501~2511——在 URP 画完天空盒之后（≤2500 会被天空盒盖掉），又在半透明特效（蜡烛光晕、UI，3000）之前。
// 本地坐标同封面：玩家在原点、眼高 1.15、面朝 -Z；方位角 0 = 正前方，负 = 玩家左手（+X）。
using UnityEditor;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class CoverBackdrop
    {
        const string Dir = "Assets/_Game/Art/Cover";
        public const float MoonAz = -16f, MoonEl = 24f;      // 和 make_cover_backdrop.py 的 MOON_ANG 对应：月亮正好在鬼堡上方
        const float Arc = 360f, Eye = 1.15f;     // 和 make_cover_backdrop.py 的 ARC 一致：一整圈（10-29 用户：身后也要正常显示）

        struct Layer { public string tex, name; public float r, y0, h, uTile, scroll; public int queue; public Color tint; }

        [MenuItem("Tools/TrickOrFreeze/Rebuild Cover Backdrop")]
        public static void RebuildInScene()
        {
            var cs = Object.FindFirstObjectByType<TrickOrFreeze.UI.CoverStage>(FindObjectsInactive.Include);
            if (!cs || !cs.stage) { Debug.LogError("[CoverBackdrop] 场景里没有 CoverStage"); return; }
            Build(cs.stage);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(cs.gameObject.scene);
        }

        public static Transform Build(Transform stage)
        {
            var old = stage.Find("Backdrop"); if (old) Object.DestroyImmediate(old.gameObject);
            ImportTextures();
            AssetDatabase.DeleteAsset($"{Dir}/Meshes");   // 弧面网格每次按当前参数重新生成，旧的不留
            var root = new GameObject("Backdrop").transform; root.SetParent(stage, false);

            // 夜空
            var sky = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(sky.GetComponent<Collider>());
            sky.name = "Sky"; sky.transform.SetParent(root, false); sky.transform.localPosition = new Vector3(0, Eye, 0); sky.transform.localScale = Vector3.one * 300f;
            var skyMat = Mat("CoverSky", "TrickOrFreeze/CoverSky", null, 2501);
            skyMat.SetVector("_MoonDir", Dir3(MoonAz, MoonEl));
            skyMat.SetColor("_Ground", new Color(0.12f, 0.07f, 0.2f));   // 地平线以下是深紫（脚下雾海的缝里不发黑）
            Setup(sky.GetComponent<Renderer>(), skyMat);

            // 月亮 + 光晕（面片朝玩家）
            float moonDist = 120f, moonSize = 2 * moonDist * Mathf.Tan(8.5f * Mathf.Deg2Rad);
            Card(root, "MoonGlow", "Cover_MoonGlow", Dir3(MoonAz, MoonEl) * (moonDist + 2) + Vector3.up * Eye, moonSize * 3.2f, 2502, new Color(1, 1, 1, 0.75f), additive: true);
            Card(root, "Moon", "Cover_Moon", Dir3(MoonAz, MoonEl) * moonDist + Vector3.up * Eye, moonSize, 2503, Color.white, additive: false);
            // 月前飘过的薄云（10-29 用户：月光看着假）：一条宽云带，比月亮略近，缓缓横向漂移；左右两端渐隐
            {
                var cd = Dir3(MoonAz, MoonEl - 2.5f);
                var cq = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(cq.GetComponent<Collider>());
                cq.name = "MoonClouds"; cq.transform.SetParent(root, false);
                cq.transform.localPosition = cd * 105f + Vector3.up * Eye;
                cq.transform.localRotation = Quaternion.LookRotation(cd, Vector3.up);
                cq.transform.localScale = new Vector3(moonSize * 4.2f, moonSize * 1.05f, 1);
                var cm = Mat("Cover_MoonClouds", "TrickOrFreeze/CoverLayer", "Cover_MoonClouds", 2504);
                cm.SetFloat("_Scroll", 0.006f); cm.SetFloat("_EdgeFade", 0.28f); cm.SetColor("_Tint", new Color(1, 1, 1, 0.95f));
                cm.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                Setup(cq.GetComponent<Renderer>(), cm);
            }

            // 各层弧面（半径、底边高度、高度都按眼睛看过去的仰角算好：远层地平线略高、近层略低，层层叠出纵深）
            Layer[] layers =
            {
                new Layer { name = "Far",     tex = "Cover_Far",   r = 70, y0 = -6,   h = 24, uTile = 1, queue = 2505, tint = Color.white },
                new Layer { name = "FogFar",  tex = "Cover_Fog",   r = 52, y0 = -5,   h = 9,  uTile = 5, queue = 2506, tint = new Color(1, 0.86f, 1, 0.42f), scroll = 0.004f },
                new Layer { name = "Mid",     tex = "Cover_Mid",   r = 34, y0 = -12,  h = 32, uTile = 1, queue = 2507, tint = Color.white },
                new Layer { name = "FogMid",  tex = "Cover_Fog",   r = 24, y0 = -5,   h = 6,  uTile = 4, queue = 2508, tint = new Color(0.9f, 0.8f, 1, 0.38f), scroll = -0.006f },
                new Layer { name = "Near",    tex = "Cover_Near",  r = 15, y0 = -5,   h = 9,  uTile = 1, queue = 2509, tint = Color.white },
                new Layer { name = "FogNear", tex = "Cover_Fog",   r = 11, y0 = -3.4f, h = 3.2f, uTile = 4, queue = 2510, tint = new Color(0.8f, 0.72f, 1, 0.32f), scroll = 0.008f },
                new Layer { name = "Frame",   tex = "Cover_Frame", r = 8,  y0 = -3,   h = 15, uTile = 1, queue = 2511, tint = Color.white },
            };
            // 贴图上方的空白已在生成时裁掉（cover_layers.json 记着保留比例），弧面高度同比缩短，画面位置不变、少画一大片透明
            var keep = new System.Collections.Generic.Dictionary<string, float>();
            var metaPath = $"{Dir}/cover_layers.json";
            if (System.IO.File.Exists(metaPath))
                foreach (System.Text.RegularExpressions.Match mm in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(metaPath), "\"(\\w+)\":\\s*([0-9.]+)"))
                    keep[mm.Groups[1].Value] = float.Parse(mm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var L in layers)
            {
                var go = new GameObject("Layer_" + L.name); go.transform.SetParent(root, false);
                float hk = L.h * (keep.TryGetValue(L.tex, out var k) ? k : 1f);
                go.AddComponent<MeshFilter>().sharedMesh = ArcMesh(L.r, L.y0, hk, L.uTile);
                var m = Mat("Cover_" + L.name, "TrickOrFreeze/CoverLayer", L.tex, L.queue);
                m.SetColor("_Tint", L.tint); m.SetFloat("_Scroll", L.scroll);
                Setup(go.AddComponent<MeshRenderer>(), m);
            }
            // 脚下的雾海：低头看到的是一片流动的紫雾（像浮在雾海上空），不是黑的
            var fl = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(fl.GetComponent<Collider>());
            fl.name = "FloorMist"; fl.transform.SetParent(root, false);
            fl.transform.localPosition = new Vector3(0, -1.4f, 0); fl.transform.localRotation = Quaternion.Euler(90, 0, 0); fl.transform.localScale = Vector3.one * 24f;
            var fm = Mat("Cover_FloorMist", "TrickOrFreeze/CoverLayer", "Cover_Floor", 2512);
            fm.SetColor("_Tint", new Color(1, 1, 1, 0.85f));
            Setup(fl.GetComponent<Renderer>(), fm);
            fl.AddComponent<TrickOrFreeze.Ambient.SlowSpin>().degPerSec = 1.2f;
            Debug.Log("[CoverBackdrop] 封面纸剧场背景已搭好");
            return root;
        }

        /// <summary>方位角（度，负 = 玩家左手 +X）+ 仰角 → 单位方向（封面本地）</summary>
        public static Vector3 Dir3(float az, float el)
        {
            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
            return new Vector3(-Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), -Mathf.Cos(a) * Mathf.Cos(e));
        }

        static Mesh ArcMesh(float r, float y0, float h, float uTile)
        {
            string path = $"{Dir}/Meshes/Arc_{r}_{y0}_{h}_{uTile}.asset".Replace("-", "m");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh) return mesh;
            System.IO.Directory.CreateDirectory($"{Dir}/Meshes");
            int seg = 180;
            var v = new Vector3[(seg + 1) * 2]; var uv = new Vector2[v.Length]; var tri = new int[seg * 6];
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg, az = -Arc / 2 + Arc * u, a = az * Mathf.Deg2Rad;
                var p = new Vector3(-Mathf.Sin(a) * r, 0, -Mathf.Cos(a) * r);
                v[i * 2] = p + Vector3.up * y0; v[i * 2 + 1] = p + Vector3.up * (y0 + h);
                uv[i * 2] = new Vector2(u * uTile, 0); uv[i * 2 + 1] = new Vector2(u * uTile, 1);
                if (i < seg) { int k = i * 6, b = i * 2; tri[k] = b; tri[k + 1] = b + 1; tri[k + 2] = b + 2; tri[k + 3] = b + 1; tri[k + 4] = b + 3; tri[k + 5] = b + 2; }
            }
            mesh = new Mesh { name = "CoverArc", vertices = v, uv = uv, triangles = tri };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static void Card(Transform root, string name, string tex, Vector3 pos, float size, int queue, Color tint, bool additive)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = name; q.transform.SetParent(root, false);
            q.transform.localPosition = pos; q.transform.localScale = Vector3.one * size;
            q.transform.localRotation = Quaternion.LookRotation(pos - Vector3.up * Eye, Vector3.up);   // Quad 正面朝 -forward = 朝玩家
            var m = Mat("Cover_" + name, "TrickOrFreeze/CoverLayer", tex, queue);
            m.SetColor("_Tint", tint);
            m.SetFloat("_DstBlend", additive ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            Setup(q.GetComponent<Renderer>(), m);
        }

        static void Setup(Renderer r, Material m)
        {
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        static Material Mat(string name, string shader, string tex, int queue)
        {
            System.IO.Directory.CreateDirectory($"{Dir}/Materials");
            string path = $"{Dir}/Materials/{name}.mat";
            var sh = Shader.Find(shader);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            if (tex != null) m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{tex}.png"));
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void ImportTextures()
        {
            AssetDatabase.ImportAsset("Assets/_Game/Art/Shaders/CoverLayer.shader");
            AssetDatabase.ImportAsset("Assets/_Game/Art/Shaders/CoverSky.shader");
            foreach (var n in new[] { "Cover_Far", "Cover_Mid", "Cover_Near", "Cover_Frame", "Cover_Fog", "Cover_Moon", "Cover_MoonGlow", "Cover_Floor", "Cover_MoonClouds" })
            {
                string p = $"{Dir}/{n}.png";
                AssetDatabase.ImportAsset(p);
                var ti = AssetImporter.GetAtPath(p) as TextureImporter; if (!ti) { Debug.LogError("[CoverBackdrop] 缺少贴图 " + p); continue; }
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true; ti.wrapModeU = n == "Cover_Fog" || n == "Cover_MoonClouds" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; ti.wrapModeV = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4; ti.maxTextureSize = 8192;   // 弧面贴图宽 5120
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
                var a = ti.GetPlatformTextureSettings("Android");
                a.overridden = true; a.maxTextureSize = 8192; a.format = TextureImporterFormat.ASTC_6x6; ti.SetPlatformTextureSettings(a);
                ti.SaveAndReimport();
            }
        }
    }
}
