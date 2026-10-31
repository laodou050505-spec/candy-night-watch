// 美术导入：把 Assets/_Game/Art/Models/<名称>/ 下 Blender 导出的 LOD0/1/2.fbx + albedo.png
// 组装成带 LODGroup 的预制体（Assets/_Game/Prefabs/Art/<名称>.prefab），材质用 TrickOrFreeze/ToonLit。
// 菜单：Tools/TrickOrFreeze/Import Art
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class ArtImport
    {
        const string ModelRoot = "Assets/_Game/Art/Models";
        const string PrefabRoot = "Assets/_Game/Prefabs/Art";
        const string MatRoot = "Assets/_Game/Art/Materials";

        // 每类资产的卡通参数：描边宽度、边缘光强度、自发光（窗户/南瓜灯的亮部由贴图亮度驱动）
        static (float outline, float rim, Color emis) Style(string name)
        {
            if (name.StartsWith("E")) return (0.0035f, 0.55f, Color.black);           // 敌人：描边清晰
            if (name == "L3_HauntedManor" || name == "L4_WitchCottage") return (0.0022f, 0.35f, new Color(1f, 0.62f, 0.25f) * 3f);
            if (name == "L10_CandleCluster") return (0.0015f, 0.3f, new Color(1f, 0.85f, 0.6f) * 0.5f);
            if (name == "L9_MushroomLamp") return (0.002f, 0.4f, new Color(0.45f, 0.95f, 0.9f) * 0.6f);
            if (name == "H1_JackLanternBig") return (0.0026f, 0.35f, Color.black);   // 雕脸的光由内部发光芯提供（SceneBuilder）
            if (name == "H2_SpookyTree") return (0.0022f, 0.3f, new Color(1f, 0.5f, 0.15f) * 0.8f);
            if (name == "H5_SkullCandelabra") return (0.0024f, 0.35f, new Color(1f, 0.85f, 0.55f) * 0.6f);
            if (name == "H6_BatLantern") return (0.0024f, 0.35f, new Color(1f, 0.82f, 0.4f) * 2.2f);
            if (name == "H8_PotionShelf") return (0.0024f, 0.35f, new Color(0.6f, 1f, 0.6f) * 0.5f);
            if (name == "S2_CandyBucket") return (0.003f, 0.45f, new Color(1f, 0.55f, 0.15f) * 0.35f);
            if (name.StartsWith("L")) return (0.0018f, 0.3f, Color.black);           // 远景：描边细
            return (0.0028f, 0.4f, Color.black);
        }

        [MenuItem("Tools/TrickOrFreeze/Import Art")]
        public static void ImportAll()
        {
            Directory.CreateDirectory(PrefabRoot);
            Directory.CreateDirectory(MatRoot);
            var shader = Shader.Find("TrickOrFreeze/ToonLit");
            if (shader == null) { Debug.LogError("[ArtImport] 找不到 TrickOrFreeze/ToonLit"); return; }
            int n = 0;
            foreach (var dir in Directory.GetDirectories(ModelRoot))
            {
                string name = Path.GetFileName(dir);
                if (name == "Sanctum") { ImportSanctum(dir, shader); continue; }
                if (name == "Deco") { ImportDeco(dir, shader); continue; }
                if (name == "Assemblies") { foreach (var ad in Directory.GetDirectories(dir)) ImportAssembly(ad, shader); continue; }
                string lod0 = $"{dir}/{name}_LOD0.fbx";
                if (!File.Exists(lod0)) continue;
                SetupModelImporter(lod0); SetupModelImporter($"{dir}/{name}_LOD1.fbx"); SetupModelImporter($"{dir}/{name}_LOD2.fbx");
                string tex = $"{dir}/{name}_albedo.png";
                SetupTexture(tex);
                var mat = GetOrCreateMat($"{MatRoot}/{name}.mat", shader);
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex));
                var st = Style(name);
                mat.SetFloat("_OutlineWidth", st.outline);
                mat.SetFloat("_RimStrength", st.rim);
                if (st.emis.maxColorComponent > 0)
                {
                    // 自发光遮罩：有 _emis.png（Tools/emission_mask.py 只保留高亮暖色）就用它，否则用贴图本身
                    string emisPath = $"{dir}/{name}_emis.png";
                    if (File.Exists(emisPath)) SetupTexture(emisPath);
                    var emisTex = AssetDatabase.LoadAssetAtPath<Texture2D>(File.Exists(emisPath) ? emisPath : tex);
                    mat.SetTexture("_EmissionMap", emisTex);
                    mat.SetColor("_EmissionColor", st.emis);
                }
                EditorUtility.SetDirty(mat);
                BuildLodPrefab(name, dir, mat);
                n++;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ArtImport] 生成 {n} 个预制体");
        }

        // meshLods：组装体（没有 LODGroup 的程序化/拼装件）让 Unity 6 自动生成 Mesh LOD，远处按屏幕占比自动用简化网格
        static void SetupModelImporter(string path, bool meshLods = false)
        {
            AssetDatabase.ImportAsset(path);
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi == null) return;
            bool dirty = false;
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
            if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
            if (mi.animationType != ModelImporterAnimationType.None) { mi.animationType = ModelImporterAnimationType.None; dirty = true; }
            if (mi.importCameras || mi.importLights) { mi.importCameras = false; mi.importLights = false; dirty = true; }
            if (mi.meshCompression != ModelImporterMeshCompression.Low) { mi.meshCompression = ModelImporterMeshCompression.Low; dirty = true; }
            if (mi.globalScale != 1f) { mi.globalScale = 1f; dirty = true; }
            if (mi.useFileScale != true) { mi.useFileScale = true; dirty = true; }
            if (mi.generateMeshLods != meshLods) { mi.generateMeshLods = meshLods; dirty = true; }
            if (dirty) mi.SaveAndReimport();
        }

        static void SetupTexture(string path)
        {
            if (!File.Exists(path)) return;
            AssetDatabase.ImportAsset(path);
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) return;
            ti.sRGBTexture = true;
            ti.mipmapEnabled = true;
            ti.anisoLevel = 4;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            var android = ti.GetPlatformTextureSettings("Android");
            android.overridden = true; android.maxTextureSize = 2048; android.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(android);
            ti.SaveAndReimport();
        }

        static Material GetOrCreateMat(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            else m.shader = shader;
            // 统一的夜景色调：暗部深蓝紫、中间调偏冷，亮部保持贴图本色（暖色光池才能跳出来）
            if (m.HasProperty("_ShadowTint")) { m.SetColor("_ShadowTint", new Color(0.24f, 0.23f, 0.42f)); m.SetColor("_MidTint", new Color(0.7f, 0.72f, 0.88f)); }
            if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", Mathf.Min(m.GetFloat("_RimStrength"), 0.35f));
            return m;
        }

        static void BuildLodPrefab(string name, string dir, Material mat)
        {
            var root = new GameObject(name);
            var renderers = new Renderer[3][];
            for (int i = 0; i < 3; i++)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{dir}/{name}_LOD{i}.fbx");
                if (fbx == null) { renderers[i] = new Renderer[0]; continue; }
                var go = (GameObject)Object.Instantiate(fbx, root.transform);
                go.name = $"LOD{i}";
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    r.sharedMaterial = mat;
                    r.shadowCastingMode = i == 0 ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                renderers[i] = go.GetComponentsInChildren<Renderer>();
            }
            var lg = root.AddComponent<LODGroup>();
            // 敌人小，切换阈值按屏幕占比给得低一些（近处才看得到细节）
            bool small = name.StartsWith("E") || name.StartsWith("S1") || name.StartsWith("S2") || name.StartsWith("S3") || name.StartsWith("S10") || name.StartsWith("S11");
            float[] h = small ? new[] { 0.06f, 0.02f, 0.004f } : new[] { 0.25f, 0.08f, 0.01f };
            lg.SetLODs(Enumerable.Range(0, 3).Select(i => new LOD(h[i], renderers[i])).ToArray());
            lg.RecalculateBounds();
            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabRoot}/{name}.prefab");
            Object.DestroyImmediate(root);
        }

        // 程序化建模的圣坛件：每件一个材质（纯色卡通，顶点色/噪声在着色器里）
        // 程序化万圣节装饰：每个 FBX 三个材质槽（基础/副色/主色），各配一个纯色 ToonLit 材质
        static void ImportDeco(string dir, Shader shader)
        {
            var specs = new (string file, Color a, Color b, Color c, float outline, Color emis)[]
            {
                ("Deco_Web", new Color(0.9f, 0.92f, 1f), Color.white, Color.white, 0f, new Color(0.55f, 0.6f, 0.9f) * 0.35f),
                ("Deco_Bunting", new Color(0.32f, 0.22f, 0.2f), new Color(0.16f, 0.1f, 0.22f), new Color(1f, 0.52f, 0.12f), 0.0015f, Color.black),
                ("Deco_Spider", new Color(0.16f, 0.1f, 0.22f), new Color(0.1f, 0.06f, 0.12f), Color.white, 0.002f, Color.black),
            };
            foreach (var sp in specs)
            {
                string fbxPath = $"{dir}/{sp.file}.fbx";
                if (!File.Exists(fbxPath)) continue;
                SetupModelImporter(fbxPath);
                var cols = new[] { sp.a, sp.b, sp.c };
                var mats = new Material[3];
                for (int i = 0; i < 3; i++)
                {
                    var m = GetOrCreateMat($"{MatRoot}/{sp.file}_{i}.mat", shader);
                    m.SetColor("_BaseColor", cols[i]);
                    m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                    m.SetFloat("_OutlineWidth", sp.outline);
                    m.SetColor("_EmissionColor", i == 0 ? sp.emis : (sp.file == "Deco_Bunting" && i == 2 ? new Color(1f, 0.45f, 0.1f) * 0.25f : Color.black));
                    EditorUtility.SetDirty(m);
                    mats[i] = m;
                }
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var go = (GameObject)Object.Instantiate(fbx);
                go.name = sp.file;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    int k = r.sharedMaterials.Length;
                    var arr = new Material[k];
                    for (int i = 0; i < k; i++) arr[i] = mats[Mathf.Min(i, 2)];
                    r.sharedMaterials = arr;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/{sp.file}.prefab");
                Object.DestroyImmediate(go);
            }
        }

        // 分部件装配体：<名>.fbx（每个部件实例是一个子物体）+ <名>_map.txt（实例名→部件名）+ 各部件贴图 <部件>_albedo.png
        // 每个部件一个材质：有贴图用贴图，程序化部件按 PartLook 给颜色/特殊着色器
        static (Color col, Color emis, float outline, string shader) PartLook(string part, string inst)
        {
            switch (part)
            {
                case "Bucket_Handle": case "Cauldron_Ring": return (new Color(0.13f, 0.11f, 0.16f), Color.black, 0.0015f, null);
                case "Candle_Dish": return (new Color(0.52f, 0.48f, 0.58f), Color.black, 0.0015f, null);
                case "Candle_Float": return (new Color(1f, 0.88f, 0.7f), new Color(1f, 0.6f, 0.28f) * 0.55f, 0f, null);
                case "Ct_Window":
                case "Ms_Window":
                case "Mn_Window": return (new Color(1f, 0.85f, 0.45f), new Color(1f, 0.7f, 0.25f) * 1.4f, 0f, null);
                case "Ct_MushCap": return (Color.white, new Color(0.6f, 0.3f, 1f) * 0.5f, 0.002f, null);
                case "Pg_Bulb": return (new Color(1f, 0.62f, 0.25f), new Color(1f, 0.45f, 0.1f) * 1.1f, 0f, null);
                case "Pg_BulbP": return (new Color(0.8f, 0.5f, 1f), new Color(0.55f, 0.25f, 1f) * 1.1f, 0f, null);
                case "Fp_Fire": return (new Color(1f, 0.7f, 0.25f), new Color(1f, 0.45f, 0.1f) * 2.2f, 0f, null);
                case "Fp_Ember": return (Color.white, new Color(1f, 0.35f, 0.08f) * 1.3f, 0f, null);
                case "Wl_Glow": return (new Color(1f, 0.8f, 0.3f), new Color(1f, 0.62f, 0.15f) * 2.6f, 0f, null);
                case "Gh_Glass": return (Color.white, Color.white * 0.75f, 0.002f, null);
                case "Gh_Door": return (Color.white, Color.white * 0.95f, 0f, null);
                case "Mz_Door": return (Color.white, new Color(0.3f, 1f, 0.55f) * 0.7f, 0f, null);
                case "Ft_Water": return (Color.white, Color.white * 0.45f, 0f, null);
                case "Lp_Glass": return (new Color(1f, 0.88f, 0.6f), new Color(1f, 0.75f, 0.4f) * 1.8f, 0f, null);
                case "Gr_Lawn": case "Gr_Patio": case "Gr_Path": return (Color.white, Color.black, 0f, null);
                case "Wb_Web": return (new Color(0.85f, 0.85f, 0.95f), new Color(0.5f, 0.5f, 0.65f) * 0.25f, 0f, null);
                case "Ln_Glass": return (new Color(1f, 0.8f, 0.45f), new Color(1f, 0.6f, 0.22f) * 1.5f, 0f, null);
                case "Top_Wood": return (new Color(0.92f, 0.74f, 0.6f), Color.black, 0.002f, null);
                case "Candle_Flame": return (new Color(1f, 0.72f, 0.3f), new Color(1f, 0.5f, 0.12f) * 2.2f, 0f, null);
                case "Potion_Surface": return (Color.white, Color.black, 0f, "TrickOrFreeze/Potion");
                case "Gem_Hex":
                    if (inst.Contains("Green")) return (new Color(0.3f, 0.95f, 0.45f), new Color(0.2f, 1f, 0.4f) * 0.6f, 0f, null);
                    if (inst.Contains("Pink")) return (new Color(1f, 0.35f, 0.65f), new Color(1f, 0.25f, 0.6f) * 0.6f, 0f, null);
                    return (new Color(0.6f, 0.35f, 1f), new Color(0.55f, 0.3f, 1f) * 0.6f, 0f, null);
            }
            return (Color.white, Color.black, 0.0025f, null);
        }

        static Texture2D WhiteTex()
        {
            const string path = "Assets/_Game/Art/Materials/_White4.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(4, 4); var px = new Color[16]; for (int i = 0; i < 16; i++) px[i] = Color.white; t.SetPixels(px); t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path);
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static void ImportAssembly(string dir, Shader toon)
        {
            string name = Path.GetFileName(dir);
            string fbxPath = $"{dir}/{name}.fbx", mapPath = $"{dir}/{name}_map.txt";
            if (!File.Exists(fbxPath) || !File.Exists(mapPath)) return;
            SetupModelImporter(fbxPath, meshLods: true);
            var map = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var line in File.ReadAllLines(mapPath)) { var kv = line.Split('\t'); if (kv.Length == 2) map[kv[0]] = kv[1]; }
            var mats = new System.Collections.Generic.Dictionary<string, Material>();
            Material MatFor(string part, string inst)
            {
                var look = PartLook(part, inst);
                string key = part == "Gem_Hex" ? part + "_" + (inst.Contains("Green") ? "Green" : inst.Contains("Pink") ? "Pink" : "Purple") : part;
                if (mats.TryGetValue(key, out var m)) return m;
                var sh = look.shader != null ? Shader.Find(look.shader) : toon;
                m = GetOrCreateMat($"{MatRoot}/Part_{key}.mat", sh);
                string tex = $"{dir}/{part}_albedo.png";
                if (sh == toon)
                {
                    if (File.Exists(tex)) { SetupTexture(tex); m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tex)); m.SetColor("_BaseColor", look.col); }
                    else { m.SetTexture("_BaseMap", Texture2D.whiteTexture); m.SetColor("_BaseColor", look.col); }
                    m.SetFloat("_OutlineWidth", look.outline);
                    m.SetColor("_EmissionColor", look.emis);
                    // 着色器的自发光遮罩默认是黑色：要发光的部件给一张白图当遮罩（内置 whiteTexture 不能序列化进材质，用工程里的白图资源）
                    if (look.emis.maxColorComponent > 0)
                        m.SetTexture("_EmissionMap", (part == "Gh_Glass" || part == "Ft_Water" || part == "Gh_Door") && File.Exists(tex) ? AssetDatabase.LoadAssetAtPath<Texture2D>(tex) : WhiteTex());   // 玻璃/水面：贴图本身当发光遮罩（植物剪影不发光）
                    m.SetFloat("_RimStrength", 0.35f);
                }
                EditorUtility.SetDirty(m);
                mats[key] = m; return m;
            }
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath));
            go.name = name;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                string inst = r.gameObject.name;
                if (!map.TryGetValue(inst, out var part))
                {
                    // 只有一个物体的 FBX 会被 Unity 折叠成根节点（名字变成文件名）
                    if (map.Count == 1) { foreach (var v in map.Values) part = v; }
                    else { Debug.LogWarning($"[ArtImport] {name}: {inst} 不在对照表里"); continue; }
                }
                var m = MatFor(part, inst);
                var arr = new Material[r.sharedMaterials.Length]; for (int i = 0; i < arr.Length; i++) arr[i] = m;
                r.sharedMaterials = arr;
                bool small = part.StartsWith("P_Candy") || part == "Lollipop" || part == "S10_CandyWrapped" || part == "Gem_Hex" || part == "Candle_Flame";
                r.shadowCastingMode = small ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                if (part == "Candle_Flame" || part == "Fp_Fire") r.gameObject.AddComponent<TrickOrFreeze.Ambient.FlameFlicker>();
            }
            PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        static void ImportSanctum(string dir, Shader shader)
        {
            var colors = new (string file, Color col, float outline)[]
            {
                ("Sanctum_Floor", new Color(0.40f, 0.33f, 0.46f), 0.0015f),
                ("Sanctum_Rim", new Color(0.36f, 0.30f, 0.44f), 0.002f),
                ("Sanctum_Underside", new Color(0.36f, 0.28f, 0.4f), 0.002f),
                ("Bridge_Planks", new Color(0.55f, 0.38f, 0.26f), 0.0015f),
                ("Bridge_Ropes", new Color(0.78f, 0.66f, 0.48f), 0.0f),
                ("Candle_Float", new Color(1f, 0.86f, 0.66f), 0.0f),
                ("Candle_Flame", new Color(1f, 0.82f, 0.45f), 0.0f),
            };
            // 地毯：三个材质槽（毯面深紫、金边、发光星月纹）
            {
                string rugPath = $"{dir}/Sanctum_Rug.fbx";
                if (File.Exists(rugPath))
                {
                    SetupModelImporter(rugPath);
                    var specs = new (Color c, float rim, Color e)[] {
                        (new Color(0.30f, 0.16f, 0.36f), 0.15f, Color.black),
                        (new Color(0.95f, 0.66f, 0.25f), 0.3f, new Color(1f, 0.6f, 0.2f) * 0.25f),
                        (new Color(1f, 0.85f, 0.45f), 0.2f, new Color(1f, 0.72f, 0.3f) * 1.3f) };
                    var mats = new Material[3];
                    for (int i = 0; i < 3; i++)
                    {
                        var m = GetOrCreateMat($"{MatRoot}/Sanctum_Rug_{i}.mat", shader);
                        m.SetColor("_BaseColor", specs[i].c); m.SetTexture("_BaseMap", Texture2D.whiteTexture);
                        m.SetFloat("_OutlineWidth", 0f); m.SetFloat("_RimStrength", specs[i].rim); m.SetColor("_EmissionColor", specs[i].e);
                        EditorUtility.SetDirty(m); mats[i] = m;
                    }
                    var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(rugPath));
                    go.name = "Sanctum_Rug";
                    foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                    {
                        var arr = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < arr.Length; i++) arr[i] = mats[Mathf.Min(i, 2)];
                        r.sharedMaterials = arr; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                    PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/Sanctum_Rug.prefab");
                    Object.DestroyImmediate(go);
                }
            }
            foreach (var (file, col, ow) in colors)
            {
                string fbxPath = $"{dir}/{file}.fbx";
                if (!File.Exists(fbxPath)) continue;
                SetupModelImporter(fbxPath);
                var mat = GetOrCreateMat($"{MatRoot}/{file}.mat", shader);
                mat.SetColor("_BaseColor", col);
                mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                mat.SetFloat("_OutlineWidth", ow);
                if (file.StartsWith("Sanctum")) { mat.SetFloat("_RimStrength", 0.18f); mat.SetColor("_MidTint", new Color(0.86f, 0.78f, 0.84f)); }
                if (file == "Candle_Flame") mat.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.3f) * 3f);
                if (file == "Candle_Float") { mat.SetColor("_EmissionColor", new Color(1f, 0.62f, 0.3f) * 0.35f); mat.SetColor("_ShadowTint", new Color(0.75f, 0.5f, 0.45f)); }
                EditorUtility.SetDirty(mat);
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
                var go = (GameObject)Object.Instantiate(fbx);
                go.name = file;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) r.sharedMaterial = mat;
                PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabRoot}/{file}.prefab");
                Object.DestroyImmediate(go);
            }
        }
    }
}
