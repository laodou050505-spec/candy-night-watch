// 搭建主场景：月下浮空南瓜圣坛。编辑器菜单 Tools/TrickOrFreeze/Build Scene。
// 会清空当前场景，然后按设计文档摆放所有物件、灯光、天空盒、后处理和 GameLoop。
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TrickOrFreeze.Ambient;

namespace TrickOrFreeze.EditorTools
{
    public static class SceneBuilder
    {
        const string PrefabRoot = "Assets/_Game/Prefabs/Art";
        const string MatRoot = "Assets/_Game/Art/Materials";
        const string ShaderRoot = "Assets/_Game/Art/Shaders";
        const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        [MenuItem("Tools/TrickOrFreeze/Build Scene")]
        public static void Build()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // ======== 世界根节点 ========
            Columns.Clear();      // 静态列表：上次搭建（可能中途失败）留下的已销毁对象要清掉
            Perches.Clear();
            var world = new GameObject("World");
            // ======== 灯光 ========
            // 月光（主方向光，紫青）
            var moonLightGO = new GameObject("MoonLight");
            moonLightGO.transform.SetParent(world.transform, false);
            var moonLight = moonLightGO.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = HexColor("9DB2FF");
            moonLight.intensity = 0.95f;
            moonLight.shadows = LightShadows.Soft;
            moonLightGO.transform.rotation = Quaternion.LookRotation(-new Vector3(0.22f, 0.75f, -1f).normalized);   // 光从月亮方向（正前偏右上）照来：物体朝玩家一侧逆光，轮廓被月光勾亮
            // 环境光
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = HexColor("6E70C8") * 0.42f;
            RenderSettings.ambientEquatorColor = HexColor("4A3A6A") * 0.3f;
            RenderSettings.ambientGroundColor = HexColor("1E1420") * 0.22f;
            // 天空盒材质
            var skyShader = Shader.Find("TrickOrFreeze/HalloweenSky");
            if (skyShader)
            {
                var skyMat = new Material(skyShader);
                // 远景剪影全景图（Tools/make_sky_backdrop.py 生成）：遮罩数据用线性空间，横向环绕、纵向夹边，保留 mip 防远处闪烁
                const string backPath = "Assets/_Game/Art/Textures/SkyBackdrop.png";
                if (AssetImporter.GetAtPath(backPath) is TextureImporter bti)
                {
                    bti.sRGBTexture = false; bti.mipmapEnabled = true; bti.wrapModeU = TextureWrapMode.Repeat; bti.wrapModeV = TextureWrapMode.Clamp;
                    bti.maxTextureSize = 8192; bti.anisoLevel = 4; bti.textureCompression = TextureImporterCompression.CompressedHQ; bti.alphaSource = TextureImporterAlphaSource.None;
                    bti.SaveAndReimport();
                    skyMat.SetTexture("_Backdrop", AssetDatabase.LoadAssetAtPath<Texture2D>(backPath));
                }
                AssetDatabase.CreateAsset(skyMat, $"{MatRoot}/HalloweenSky.mat");
                RenderSettings.skybox = skyMat;
            }
            // 雾
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = HexColor("1C1A38");
            RenderSettings.fogStartDistance = 9f;
            RenderSettings.fogEndDistance = 72f;
            // ======== 后处理 ========
            var ppGO = new GameObject("PostProcess");
            ppGO.transform.SetParent(world.transform, false);
            ppGO.layer = 0;
            var vol = ppGO.AddComponent<Volume>();
            vol.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/_Game/Art/MainVolumeProfile.asset");
            vol.profile = profile;
            var bloom = profile.Add<Bloom>(); bloom.threshold.Override(0.9f); bloom.intensity.Override(1.2f); bloom.scatter.Override(0.65f);
            var tm = profile.Add<Tonemapping>(); tm.mode.Override(TonemappingMode.Neutral);
            var vignette = profile.Add<Vignette>(); vignette.intensity.Override(0.22f); vignette.color.Override(HexColor("2A1838"));
            var colorAdj = profile.Add<ColorAdjustments>(); colorAdj.postExposure.Override(0.05f); colorAdj.contrast.Override(16f); colorAdj.saturation.Override(12f);
            // 南瓜桌、糖桶、坩埚、桌上烛群（交互尺寸由 Layout 决定，不动）
            // 分部件装配体（Blender 装配，正面朝 +Z；子物体按部件分开：糖果 Candy_i、火苗、汤药液面都可单独驱动）
            PlacePrefab(world.transform, "Desk_Table", new Vector3(0, 0, -1.3f));
            PlacePrefab(world.transform, "Desk_Bucket", new Vector3(0, 0.72f, -0.68f));
            PlacePrefab(world.transform, "Desk_Cauldron", TrickOrFreeze.Core.Layout.BOX.pos, FaceOrigin(TrickOrFreeze.Core.Layout.BOX.pos));             // 玩家右手边
            PlacePrefab(world.transform, "Desk_CandleCluster", TrickOrFreeze.Core.Layout.CANDLE.pos, FaceOrigin(TrickOrFreeze.Core.Layout.CANDLE.pos));
            // ======== 庄园后花园（v5）：藤架下的圆桌、铸铁拱门、树篱、碎石小径通向远处灯火通明的维多利亚大庄园 ========
            BuildGarden(world.transform);
            // ======== 万圣节氛围与动态 ========
            BuildHalloween(world.transform);
            SettlePerches(world.transform);    // 栖息/搁放类摆件贴到下方实际表面上（不靠手填高度）
            PruneShadowCasters(world.transform);   // 性能：远处、细小、发光的东西不投影
            // ======== XR 相机 ========
            var camGO = new GameObject("XR Camera");
            camGO.transform.SetParent(world.transform, false);
            camGO.transform.localPosition = new Vector3(0, 1.15f, 0);
            camGO.transform.localRotation = Quaternion.Euler(6, 180, 0);   // 规则层玩家面朝 -Z
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.fieldOfView = 62;
            cam.nearClipPlane = 0.02f;
            cam.farClipPlane = 200;
            var camData = camGO.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camGO.tag = "MainCamera";
            // ======== 玩法：XR 装置、GameLoop、特效、音效、界面 ========
            GameplayBuilder.Build(world, cam);
            // ======== 保存 ========
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[SceneBuilder] 主场景搭建完成 → " + ScenePath);
        }

        // 万圣节符号要在玩家视野里一眼认出：巨型南瓜灯、鬼脸树、墓园、蝙蝠群、骑扫帚的女巫、蛛网、旗串……
        // 并且全部是“活”的：灯火闪烁、旗串/灯笼摆动、蜘蛛上下爬、蝙蝠盘旋、女巫掠过月亮、落叶萤火鬼火。
        // Tripo 模型（参考图三分之四正面）导出后正面朝模型 -X；让正面朝向玩家（原点）所需的 yaw
        static float FacePlayer(Vector3 pos) => Mathf.Atan2(-pos.x, -pos.z) * Mathf.Rad2Deg + 90f;
        const float JackFaceYawOffset = 90f;    // 实测雕脸朝模型 -X：Euler(0,θ)·(-X) 指向玩家 ⇔ θ = 朝向角 + 90
        // ======== v5 庄园后花园 ========
        // 规则层坐标不变：玩家坐在原点面朝 -Z，桌心 (0,0.72,-1.3)；地面 y=0。
        // 藤架椽子（2.75m）= 蜘蛛垂降的“天花板”；藤架两侧梁头 = 蝙蝠出生点；铸铁拱门（z=-4.8，门洞 1.85m）= 甩飞敌人的出口。
        // 4 个地面出生点各放一个“敌人钻出来的地方”：棺材、獠牙衣柜、裂开的墓碑、南瓜堆。
        const float MansionZ = -40f;
        static void BuildGarden(Transform world)
        {
            var g = new GameObject("Garden").transform;
            g.SetParent(world, false);
            PlacePrefab(g, "Deco_Ground", Vector3.zero);
            PlacePrefab(g, "Deco_Pergola", Vector3.zero);
            PlacePrefab(g, "Deco_GardenGate", Vector3.zero);
            PlacePrefab(g, "Deco_Wall", Vector3.zero);          // 石栏杆矮墙（墩柱顶南瓜灯/石瓮），视线可越过看到墙外
            // 墩柱顶：隔一个放一盏南瓜灯（雕脸朝花园中心），另一半是石瓮枯枝（在矮墙装配里）
            var pierTxt = File.ReadAllText("ArtSource/Procedural/garden2_piers.json");
            var nums = System.Text.RegularExpressions.Regex.Matches(pierTxt, @"-?\d+(\.\d+)?");
            for (int i = 0; i + 1 < nums.Count; i += 2)
            {
                var pp = new Vector3(float.Parse(nums[i].Value), 1.3f, float.Parse(nums[i + 1].Value));
                if (Mathf.Abs(Mathf.Abs(pp.x) - 6.2f) < 0.3f && Mathf.Abs(pp.z + 4.8f) < 0.45f) continue;   // 紧挨着大门石柱的墙端墩：石柱更高、顶上已有尖饰，不再叠摆件
                if ((i / 2) % 2 != 0)
                {
                    var urn = PlacePrefab(g, "Deco_StoneUrn", pp, Random.Range(0, 360f));   // 装配原点就在方座底面，直接落在墩柱顶
                    urn.transform.localScale = Vector3.one * 0.62f;
                    continue;
                }
                float yaw = Mathf.Atan2(-pp.x, -pp.z) * Mathf.Rad2Deg + JackFaceYawOffset;
                var jl = PlacePrefab(g, "H1_JackLanternBig", pp, yaw);
                jl.transform.localScale = Vector3.one * 0.5f;
                AddGlowCore(jl.transform, new Vector3(0, 0.33f, 0), 0.42f);
            }
            PlacePrefab(g, "Deco_Cemetery", Vector3.zero);      // 左：山坡墓园 + 陵墓
            PlacePrefab(g, "Deco_PumpkinPatch", Vector3.zero);  // 右：南瓜田、草垛、玉米秆、推车
            PlacePrefab(g, "Deco_Greenhouse", Vector3.zero);    // 后：维多利亚玻璃温室
            PlacePrefab(g, "Deco_Fountain", Vector3.zero);      // 前：小径上的喷泉广场
            PlacePrefab(g, "Deco_GroundDetail", Vector3.zero);  // 满地秋叶 + 墙根路边的枯草丛
            // 角落蛛网：藤架前后四角（梁柱夹角）、拱门两根门柱内侧、矮墙几处墩柱与栏杆的夹角
            foreach (var (p, yaw, sc) in new[] {
                (new Vector3(2.98f, 2.49f, -4.04f), 0f, 1.0f), (new Vector3(-2.98f, 2.49f, -4.04f), 180f, 0.9f),
                (new Vector3(2.98f, 2.49f, 1.44f), 0f, 0.8f), (new Vector3(-2.98f, 2.49f, 1.44f), 180f, 1.0f),
                (new Vector3(0.9f, 2.18f, -4.75f), 0f, 0.6f), (new Vector3(-0.9f, 2.18f, -4.75f), 180f, 0.55f),
                (new Vector3(-6.0f, 1.16f, -2.0f), 90f, 0.45f), (new Vector3(6.0f, 1.16f, 2.3f), -90f, 0.45f), (new Vector3(-3.38f, 1.16f, 4.6f), 0f, 0.45f) })
            {
                var w = PlacePrefab(g, "Deco_CornerWeb", p, yaw);
                w.transform.localScale = Vector3.one * sc;
            }
            // 墙外近景枯树（围着墓园、温室两侧、小径两旁）
            foreach (var (n, p, sc, yaw) in new[] {
                ("Deco_TreeB", new Vector3(-22.5f, 0, 2f), 1.2f, 30f), ("Deco_TreeA", new Vector3(-20.5f, 0, -11f), 1.0f, 140f), ("Deco_TreeC", new Vector3(-8.2f, 0, 7.5f), 1.1f, 200f),
                ("Deco_TreeA", new Vector3(-7.5f, 0, 15.5f), 1.25f, 80f), ("Deco_TreeC", new Vector3(7.8f, 0, 16f), 1.2f, 300f), ("Deco_TreeB", new Vector3(10.5f, 0, 8.5f), 1.0f, 10f),
                ("Deco_TreeA", new Vector3(-6.5f, 0, -21f), 1.3f, 250f), ("Deco_TreeC", new Vector3(6.8f, 0, -23f), 1.15f, 120f), ("Deco_TreeB", new Vector3(21f, 0, -6f), 1.1f, 60f),
                ("Deco_TreeA", new Vector3(16f, 0, -15.5f), 0.95f, 170f) })
            {
                var t = PlacePrefab(g, n, p, yaw);
                t.transform.localScale = Vector3.one * sc;
                t.AddComponent<Swayer>().angle = 0.6f;
            }
            // 地平线一圈树林剪影（减面版），正前方留出庄园；配合雾成为层层叠叠的黑树林
            Random.InitState(77);
            for (int i = 0; i < 46; i++)
            {
                float b = i * (360f / 46) + Random.Range(-3f, 3f);
                if (Mathf.Abs(Mathf.DeltaAngle(b, 0)) < 14f) continue;
                float d = Random.Range(30f, 56f);
                var tp = Bearing(b, d);
                // 远景树避开建筑：庄园、小屋、钟楼、温室周围留空
                bool blocked = false;
                foreach (var (c, r) in new[] { (new Vector2(0.5f, MansionZ - 4f), 13f), (new Vector2(-17f, -32f), 7f), (new Vector2(18f, -34f), 6f), (new Vector2(0f, 13f), 8f) })
                    if (Vector2.Distance(new Vector2(tp.x, tp.z), c) < r) blocked = true;
                if (blocked) continue;
                var t = PlacePrefab(g, "Deco_TreeFar" + "ABC"[i % 3], tp, Random.Range(0, 360f));
                t.transform.localScale = Vector3.one * Random.Range(1.3f, 2.4f);
            }
            // 局部光：温室透出的青绿光、陵墓门缝的绿光、墓园烛光、南瓜田暖光
            AddFlickerLight(g, new Vector3(0, 2.2f, 10.2f), new Color(0.35f, 1f, 0.8f), 1.6f, 9f);
            AddFlickerLight(g, new Vector3(-17.2f, 1.6f, -0.8f), new Color(0.3f, 1f, 0.5f), 1.4f, 6f);
            AddFlickerLight(g, new Vector3(-12.5f, 1.4f, -1f), new Color(1f, 0.7f, 0.35f), 1.0f, 6f);
            AddFlickerLight(g, new Vector3(11f, 1.5f, -4f), new Color(1f, 0.6f, 0.25f), 1.0f, 7f);
            // 远处的大庄园（正面朝玩家），门廊台阶正对碎石小径
            PlacePrefab(g, "Deco_Mansion", new Vector3(0.5f, 0, MansionZ));
            // 路灯：拱门外两侧 + 沿小径
            int lampN = 0;
            foreach (var z in new[] { -5.6f, -12f, -19f, -26f, -33f })
                foreach (var sx in new[] { -1f, 1f })
                {
                    var lp = PlacePrefab(g, "Deco_LampPost", new Vector3(sx * 1.45f, 0, z));
                    if (lampN++ < 4) AddFlickerLight(lp.transform, new Vector3(0, 2.65f, 0), new Color(1f, 0.78f, 0.45f), 1.4f, 6f);
                }
            // 敌人钻出来的地方（放在出生点外侧 0.35 米，不挡路）
            foreach (var (name, spawn, scale) in new[] {
                ("S7_Coffin", new Vector3(-2.3f, 0, -1.1f), 0.75f), ("S6_FangWardrobe", new Vector3(2.3f, 0, -1.05f), 0.55f),
                ("S12_Gravestone", new Vector3(-1.3f, 0, -3.4f), 1.0f), ("S8_PumpkinPile", new Vector3(1.3f, 0, -3.4f), 1.1f) })
            {
                var outward = new Vector3(spawn.x, 0, spawn.z - TrickOrFreeze.Core.Layout.TABLE.center.z).normalized;
                Decor(g, name, spawn + outward * 0.35f, FacePlayer(spawn), scale, checkPath: false);
            }
            // 月亮：低悬在地平线上（仰角约 12°，正前偏右 32°：玩家面朝 -Z，-X 在右手边；夹在鬼脸树、蝙蝠灯与藤架右前柱之间），坐着平视就能从藤架前梁下看到；远处枯树剪影挡在月亮前面
            var moonShader = Shader.Find("TrickOrFreeze/MoonDisc");
            var moonGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            moonGO.name = "Moon";
            Object.DestroyImmediate(moonGO.GetComponent<Collider>());
            moonGO.transform.SetParent(world, false);
            var eye = new Vector3(0, 1.15f, 0);
            var moonDir = new Vector3(-0.53f, 0.2126f, -0.848f).normalized;
            moonGO.transform.localPosition = eye + moonDir * 120f;
            moonGO.transform.localScale = Vector3.one * 26f;
            moonGO.transform.LookAt(world.TransformPoint(eye)); moonGO.transform.Rotate(0, 180, 0);
            var moonMat = new Material(moonShader ? moonShader : Shader.Find("Universal Render Pipeline/Unlit"));
            if (moonShader) { moonMat.SetFloat("_HaloSize", 0.25f); moonMat.SetColor("_Color", new Color(0.96f, 0.92f, 0.82f)); moonMat.SetColor("_Halo", new Color(0.6f, 0.62f, 0.9f, 0.7f)); }
            AssetDatabase.CreateAsset(moonMat, $"{MatRoot}/Moon.mat");
            moonGO.GetComponent<Renderer>().sharedMaterial = moonMat;
            moonGO.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (RenderSettings.skybox)
            {
                if (RenderSettings.skybox.HasProperty("_MoonDir")) RenderSettings.skybox.SetVector("_MoonDir", moonDir);
                if (RenderSettings.skybox.HasProperty("_AuroraStrength")) RenderSettings.skybox.SetFloat("_AuroraStrength", 0f);   // 不要极光
            }
        }

        static void BuildHalloween(Transform world)
        {
            Random.InitState(1031);
            var root = new GameObject("Halloween").transform;
            root.SetParent(world, false);
            // —— 巨型南瓜灯：花园四角（拱门两侧、身后两侧），雕脸朝玩家
            foreach (var (pos, sc) in new[] { (new Vector3(-4.6f, 0, -4.0f), 1.25f), (new Vector3(4.7f, 0, -3.9f), 1.15f) })
            {
                float yaw = Mathf.Atan2(-pos.x, -pos.z) * Mathf.Rad2Deg + JackFaceYawOffset;
                var jl = Decor(root, "H1_JackLanternBig", pos, yaw, sc, checkPath: false);
                AddFlickerLight(jl.transform, new Vector3(0, 0.25f, 0), new Color(1f, 0.6f, 0.2f), 1.4f, 3.2f);
                AddGlowCore(jl.transform, new Vector3(0, 0.33f, 0), 0.42f);
            }
            // —— 墙外：稻草人站在南瓜田垄间（面朝花园），鬼脸树守在小径两侧
            Decor(root, "Deco_Scarecrow", new Vector3(12.7f, 0.12f, -4.2f), FaceOrigin(new Vector3(12.7f, 0, -4.2f)), 1.25f, checkPath: false);
            foreach (var p in new[] { new Vector3(7.4f, 0, -7.2f), new Vector3(7.5f, 0, -3.0f) })
            {
                var jl = Decor(root, "H1_JackLanternBig", p, Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg + JackFaceYawOffset, 0.9f, checkPath: false);
                AddGlowCore(jl.transform, new Vector3(0, 0.33f, 0), 0.42f);
            }
            foreach (var (p, sc) in new[] { (new Vector3(-3.6f, 0, -10.5f), 1.4f), (new Vector3(4.2f, 0, -12.5f), 1.3f) })
            {
                var tr = Decor(root, "H2_SpookyTree", p, FacePlayer(p), sc, checkPath: false);
                AddFlickerLight(tr.transform, new Vector3(0, 2.2f, 0), new Color(1f, 0.55f, 0.2f), 0.9f, 4f);
                tr.AddComponent<Swayer>().angle = 0.8f;
            }
            // 远处：花园小屋（左后）与家族小礼拜堂（右后）
            Decor(root, "Deco_Cottage", new Vector3(-17f, -0.42f * 0.75f, -32f), FaceOrigin(new Vector3(-17f, 0, -32f)), 0.75f, checkPath: false);   // 浮岛时期的草地圆台压进地面
            Decor(root, "Deco_BellTower", new Vector3(18f, -0.37f * 0.95f, -34f), FaceOrigin(new Vector3(18f, 0, -34f)), 0.95f, checkPath: false);
            // ======== 成组的小场景（每组围绕一个主角件，配角件互相呼应，不再单件散放）========
            // —— 身后左角·女巫篝火：石圈篝火上吊着冒泡的大坩埚，背靠矮墙的药水架、斜靠墙的扫帚、书堆、骷髅烛台、小南瓜
            var pit = new Vector3(-3.9f, 0, 3.0f);
            PlacePrefab(root, "Deco_FirePit", pit, 25f);
            var bigPot = PlacePrefab(root, "Desk_Cauldron", pit + new Vector3(0, 0.49f, 0), 25f);
            bigPot.transform.localScale = Vector3.one * 3.5f;
            AddFlickerLight(root, pit + new Vector3(0, 0.45f, 0), new Color(1f, 0.55f, 0.18f), 1.5f, 5f);
            var shelf = Decor(root, "H8_PotionShelf", new Vector3(-2.36f, 0, 4.1f), -90f, 1.1f, checkPath: false);   // 卡在后墙两墩柱之间、与墙平行，背面离墙 2cm，正面朝花园（模型正面 -X → -Z）
            var broom = Decor(root, "S9_Broom", new Vector3(-5.6f, 0.02f, 3.9f), 0f, 1.0f, checkPath: false);
            broom.transform.localRotation = Quaternion.Euler(0, 0, 16f);                                         // 顶端往 -X 倒，斜靠在左侧矮墙上
            Decor(root, "S4_BookTower", new Vector3(-5.3f, 0, 2.0f), FacePlayer(new Vector3(-5.3f, 0, 2.0f)), 0.42f, checkPath: false);
            var c1 = Decor(root, "Deco_SkullCandelabra", new Vector3(-2.75f, 0, 2.35f), FaceOrigin(new Vector3(-2.75f, 0, 2.35f)), 2.4f, checkPath: false);
            AddFlickerLight(c1.transform, new Vector3(0, 0.3f, 0), new Color(1f, 0.78f, 0.45f), 0.5f, 2f);
            Decor(root, "H1_JackLanternBig", new Vector3(-4.95f, 0, 1.75f), Mathf.Atan2(4.95f, -1.75f) * Mathf.Rad2Deg + JackFaceYawOffset, 0.38f, checkPath: false);
            // —— 身后右角·丰收堆：草垛叠成小山，顶上两盏南瓜灯，玉米秆束站两边，脚下南瓜堆与糖果玉米
            var hayPos = new Vector3(3.05f, 0, 3.6f);
            PlacePrefab(root, "Deco_HayStack", hayPos, 180f);
            foreach (var (dx, y, sc) in new[] { (-0.35f, 1.72f, 0.42f), (0.75f, 0.86f, 0.36f) })
            {
                var hp = hayPos + new Vector3(dx, y, 0.1f);
                var jl = Perch(Decor(root, "H1_JackLanternBig", hp, Mathf.Atan2(-hp.x, -hp.z) * Mathf.Rad2Deg + JackFaceYawOffset, sc, checkPath: false), 0.02f);
                AddGlowCore(jl.transform, new Vector3(0, 0.33f, 0), 0.42f);
            }
            AddFlickerLight(root, hayPos + new Vector3(0, 1.4f, -0.6f), new Color(1f, 0.6f, 0.22f), 1.2f, 4f);
            Decor(root, "S8_PumpkinPile", new Vector3(5.1f, 0, 2.2f), FacePlayer(new Vector3(5.1f, 0, 2.2f)), 1.2f, checkPath: false);
            Decor(root, "H7_CandyCornPile", new Vector3(2.55f, 0, 2.7f), FacePlayer(new Vector3(2.55f, 0, 2.7f)), 1.2f, checkPath: false);
            // —— 前左·墓碑出生点：墓碑脚下冒出发光毒蘑菇，棺材旁立骷髅烛台
            foreach (var (p, sc) in new[] { (new Vector3(-2.2f, 0, -4.05f), 0.24f), (new Vector3(-0.85f, 0, -4.3f), 0.17f), (new Vector3(-3.4f, 0, -2.85f), 0.3f) })
                Decor(root, "L9_MushroomLamp", p, FacePlayer(p), sc, checkPath: false);
            var c2 = Decor(root, "Deco_SkullCandelabra", new Vector3(-3.4f, 0, -1.75f), FaceOrigin(new Vector3(-3.4f, 0, -1.75f)), 2.4f, checkPath: false);
            AddFlickerLight(c2.transform, new Vector3(0, 0.3f, 0), new Color(1f, 0.78f, 0.45f), 0.5f, 2f);
            // —— 前右·衣柜与南瓜出生点：糖果玉米撒了一地，骷髅烛台照着獠牙衣柜
            Decor(root, "H7_CandyCornPile", new Vector3(3.5f, 0, -2.6f), FacePlayer(new Vector3(3.5f, 0, -2.6f)), 1.2f, checkPath: false);
            var c3 = Decor(root, "Deco_SkullCandelabra", new Vector3(3.45f, 0, -0.25f), FaceOrigin(new Vector3(3.45f, 0, -0.25f)), 2.4f, checkPath: false);
            AddFlickerLight(c3.transform, new Vector3(0, 0.3f, 0), new Color(1f, 0.78f, 0.45f), 0.5f, 2f);
            // —— 左侧·蘑菇仙女环：一圈大小不一的发光蘑菇
            for (int k = 0; k < 5; k++)
            {
                float a = k / 5f * Mathf.PI * 2 + 0.3f;
                var p = new Vector3(-4.7f + Mathf.Cos(a) * 0.75f, 0, 0.1f + Mathf.Sin(a) * 0.75f);
                Decor(root, "L9_MushroomLamp", p, Random.Range(0, 360f), new[] { 0.36f, 0.2f, 0.27f, 0.16f, 0.3f }[k], checkPath: false);
            }
            AddFlickerLight(root, new Vector3(-4.7f, 0.5f, 0.1f), new Color(0.45f, 1f, 0.9f), 0.4f, 2.5f);
            // ======== 布景小动物与挂饰（Tripo 精细件；尚未生成完的会自动跳过）========
            GameObject Prop(string n, Vector3 p, float yaw, float sc = 1f)
            {
                if (!AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{n}.prefab")) return null;
                var go = PlacePrefab(root, n, p, yaw); go.transform.localScale = Vector3.one * sc; return go;
            }
            // 乌鸦：矮墙墩柱、拱门拱顶、稻草人横杆、陵墓屋檐上，三三两两
            // 乌鸦：矮墙栏杆顶（墩柱顶已有南瓜灯/石瓮）、拱门拱顶、稻草人两只袖口、陵墓屋檐、药水架顶；高度由 SettlePerches 贴到实际表面
            foreach (var (p, yaw) in new[] { (new Vector3(-6.2f, 0.9f, -0.6f), 95f), (new Vector3(6.2f, 0.9f, -1.05f), -85f), (new Vector3(-0.15f, 2.72f, -4.8f), 15f),
                                             (new Vector3(12.43f, 1.9f, -5.1f), FaceOrigin(new Vector3(12.43f, 0, -5.1f))), (new Vector3(13.0f, 1.9f, -3.32f), FaceOrigin(new Vector3(13f, 0, -3.32f)) + 20f),
                                             (new Vector3(-16.5f, 4.9f, -2.3f), 95f), (new Vector3(-2.0f, 1.66f, 4.1f), 165f) })
            {
                var c = Perch(Prop("Deco_Crow", p, yaw));
                if (c) { var pi = c.AddComponent<PerchIdle>(); pi.hop = 0.05f; }
            }
            // 黑猫：坐在前右墩柱上盯着桌子；另一只坐在草垛顶
            foreach (var (p, yaw) in new[] { (new Vector3(6.2f, 1.3f, -4.5f), FaceOrigin(new Vector3(6.2f, 0, -4.5f))), (new Vector3(3.9f + 0.6f, 1.04f, 3.62f), 180f) })
            {
                var c = Prop("Deco_BlackCat", p, yaw);
                if (c) { var pi = c.AddComponent<PerchIdle>(); pi.hop = 0; pi.turnMax = 30f; }
            }
            // 猫头鹰：藤架后梁上、陵墓山花顶上
            foreach (var (p, yaw) in new[] { (new Vector3(-2.3f, 2.9f, 1.4f), 180f), (new Vector3(-16.75f, 5.55f, -0.8f), 90f) })
            {
                var c = Perch(Prop("Deco_Owl", p, yaw));
                if (c) { var pi = c.AddComponent<PerchIdle>(); pi.hop = 0; pi.turnMax = 70f; }
            }
            // 白布幽灵：吊在小径两侧鬼脸树的枝下、藤架前梁左侧，随风轻摆上下浮动（吊绳一起摆）
            foreach (var (p, len) in new[] { (new Vector3(-2.7f, 3.2f, -10.4f), 1.1f), (new Vector3(3.2f, 3.4f, -12.6f), 1.3f), (new Vector3(-2.55f, 2.5f, -4.1f), 0.75f) })
            {
                var piv = new GameObject("GhostPivot").transform; piv.SetParent(root, false); piv.localPosition = p;
                var gh = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Deco_SheetGhost.prefab");
                if (!gh) continue;
                var g2 = (GameObject)PrefabUtility.InstantiatePrefab(gh, piv);
                g2.transform.localPosition = new Vector3(0, -len - 0.95f * 0.72f, 0); g2.transform.localScale = Vector3.one * 0.72f;
                g2.transform.localRotation = Quaternion.Euler(0, FaceOrigin(p), 0);
                var str = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(str.GetComponent<Collider>());
                str.name = "String"; str.transform.SetParent(piv, false); str.transform.localPosition = new Vector3(0, -len / 2, 0); str.transform.localScale = new Vector3(0.008f, len / 2, 0.008f);
                str.GetComponent<Renderer>().sharedMaterial = IronMat();
                var sw = piv.gameObject.AddComponent<Swayer>(); sw.angle = 7f; sw.period = 3.1f + p.x * 0.1f; sw.axis = Vector3.right;
                var bo = g2.AddComponent<Bobber>(); bo.amplitude = 0.04f; bo.period = 2.6f;
            }
            // 大蜘蛛：蹲在墓园铁门拱顶上；南瓜摞：守在拱门内侧两边、温室门两边
            {   // 大蜘蛛：从墓园铁门拱顶垂丝吊下来，慢慢上下荡（丝随摆动）
                var piv = new GameObject("SpiderPivot").transform; piv.SetParent(root, false); piv.localPosition = new Vector3(-9.5f, 2.44f, -1.0f);
                var sp = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Deco_GiantSpider.prefab");
                if (sp)
                {
                    var g3 = (GameObject)PrefabUtility.InstantiatePrefab(sp, piv);
                    g3.transform.localPosition = new Vector3(0, -0.95f, 0); g3.transform.localScale = Vector3.one * 0.8f; g3.transform.localRotation = Quaternion.Euler(0, 90f, 0);
                    var str = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(str.GetComponent<Collider>());
                    str.name = "Silk"; str.transform.SetParent(piv, false); str.transform.localPosition = new Vector3(0, -0.42f, 0); str.transform.localScale = new Vector3(0.006f, 0.42f, 0.006f);   // 丝一直插进蜘蛛身体里，上下晃也不断
                    str.GetComponent<Renderer>().sharedMaterial = IronMat();
                    var sw = piv.gameObject.AddComponent<Swayer>(); sw.angle = 5f; sw.period = 4.3f; sw.axis = Vector3.forward;
                    var bo = g3.AddComponent<Bobber>(); bo.amplitude = 0.06f; bo.period = 3.7f;
                }
            }
            foreach (var p in new[] { new Vector3(-1.7f, 0, -4.3f), new Vector3(1.75f, 0, -4.3f), new Vector3(-1.6f, 0, 8.25f), new Vector3(1.6f, 0, 8.25f) })
                Prop("Deco_PumpkinStack", p, FaceOrigin(p) + Random.Range(-15f, 15f), Random.Range(0.85f, 1.05f));
            // 提灯：用短铁链吊在藤架前梁上，左右框住拱门
            foreach (var x in new[] { -0.8f, 0.8f })
            {
                var lpv = new GameObject("LanternPivot").transform; lpv.SetParent(root, false); lpv.localPosition = new Vector3(x, 2.5f, -4.1f);
                var ln = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Deco_Lantern.prefab");
                if (!ln) continue;
                var lg = (GameObject)PrefabUtility.InstantiatePrefab(ln, lpv); lg.transform.localPosition = new Vector3(0, -0.72f, 0);
                var ch = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(ch.GetComponent<Collider>());
                ch.name = "Chain"; ch.transform.SetParent(lpv, false); ch.transform.localPosition = new Vector3(0, -0.13f, 0); ch.transform.localScale = new Vector3(0.012f, 0.13f, 0.012f);
                ch.GetComponent<Renderer>().sharedMaterial = IronMat();
                var sw = lpv.gameObject.AddComponent<Swayer>(); sw.angle = 4f; sw.period = 2.3f + x; sw.axis = Vector3.right;
                AddFlickerLight(lg.transform, new Vector3(0, 0.2f, 0), new Color(1f, 0.7f, 0.35f), 0.6f, 2.5f);
            }
            // 篝火角：木桶 + 桶上的女巫帽、骷髅堆；丰收角：苹果箱；喷泉边：两张花园长椅相对
            Prop("Deco_Barrel", new Vector3(-5.55f, 0, 3.2f), 30f, 1f);
            Perch(Prop("Deco_WitchHat", new Vector3(-5.55f, 0.9f, 3.2f), 120f, 1f), 0.015f);
            Prop("Deco_SkullPile", new Vector3(-3.5f, 0, 3.98f), FaceOrigin(new Vector3(-3.5f, 0, 3.98f)), 1f);
            Prop("Deco_AppleCrate", new Vector3(4.0f, 0, 2.6f), FaceOrigin(new Vector3(4.0f, 0, 2.6f)), 1f);
            Prop("Deco_GardenBench", new Vector3(-3.6f, 0, -16f), 90f, 1f);
            Prop("Deco_GardenBench", new Vector3(3.6f, 0, -16f), -90f, 1f);
            Perch(Prop("Deco_SkullPile", new Vector3(-12.6f, 1.05f, 0.9f), 90f, 1.1f), 0.04f);
            // —— 藤架上的装饰：前后横梁挂蝙蝠灯笼、旗串，前排立柱上角挂蛛网
            int li = 0;
            foreach (var z in new[] { -4.1f, 1.5f })
                foreach (var sx in new[] { -1f, 1f })
                {
                    var hang = new GameObject("BatLanternPivot").transform;
                    hang.SetParent(root, false);
                    hang.localPosition = new Vector3(sx * 1.9f, 2.5f, z);
                    var lan = PlacePrefab(hang, "H6_BatLantern", new Vector3(0, -0.62f, 0), FacePlayer(hang.localPosition));
                    lan.transform.localScale = Vector3.one * 1.0f;
                    var sw = hang.gameObject.AddComponent<Swayer>(); sw.angle = 6f; sw.period = 2.6f + li * 0.2f; sw.axis = Vector3.right;
                    AddFlickerLight(lan.transform, new Vector3(0, -0.15f, 0), new Color(1f, 0.82f, 0.4f), 0.8f, 3f);
                    var chain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.DestroyImmediate(chain.GetComponent<Collider>());
                    chain.name = "Chain"; chain.transform.SetParent(hang, false);
                    chain.transform.localPosition = new Vector3(0, -0.15f, 0); chain.transform.localScale = new Vector3(0.022f, 0.15f, 0.022f);
                    chain.GetComponent<Renderer>().sharedMaterial = IronMat();
                    li++;
                }
            foreach (var (z, x0, x1) in new[] { (-4.17f, -3.0f, -1.1f), (-4.17f, 1.1f, 3.0f), (1.57f, -3.0f, 3.0f) })   // 前梁中段留给两盏吊提灯
            {
                var piv = new GameObject("BuntingPivot").transform;
                piv.SetParent(root, false);
                piv.localPosition = new Vector3((x0 + x1) / 2, 2.47f, z);
                var bt = PlacePrefab(piv, "Deco_Bunting", Vector3.zero);
                bt.transform.localScale = new Vector3((x1 - x0) / 4f, 0.8f, 1);
                var sw = piv.gameObject.AddComponent<Swayer>(); sw.axis = Vector3.right; sw.angle = 4f; sw.period = 3.4f + z * 0.1f;
            }
            // 小蜘蛛：挂在藤架前排两角的角落蛛网里（蛛网在 BuildGarden 里，梁柱夹角处），时不时顺着丝垂下来
            foreach (var sx in new[] { -1f, 1f })
            {
                var anchor = new GameObject("SpiderAnchor").transform; anchor.SetParent(root, false);
                anchor.localPosition = new Vector3(sx * 2.6f, 2.3f, -4.04f);
                PlacePrefab(anchor, "Deco_Spider", Vector3.zero, sx > 0 ? 0f : 180f).AddComponent<SpiderClimb>();
            }
            // —— 蝙蝠群：一群绕藤架上空，一群绕远处庄园塔楼
            var batPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/E2_Batsy.prefab");
            var f1 = new GameObject("BatFlockNear").AddComponent<FlockOrbit>();
            f1.transform.SetParent(root, false); f1.prefab = batPrefab; f1.count = 6; f1.radius = 9; f1.height = 6.5f; f1.scale = 3.5f; f1.speed = 0.035f; f1.wingFlap = true;
            var f2 = new GameObject("BatFlockFar").AddComponent<FlockOrbit>();
            f2.transform.SetParent(root, false); f2.transform.localPosition = new Vector3(-6f, 0, MansionZ);
            f2.prefab = batPrefab; f2.count = 14; f2.radius = 9; f2.height = 19; f2.scale = 6f; f2.speed = -0.03f; f2.spread = 2.5f; f2.wingFlap = true;
            // —— 小幽灵在墓园上空飘
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/E6_Boo.prefab");
            var g1 = new GameObject("GhostDrift").AddComponent<FlockOrbit>();
            g1.transform.SetParent(root, false); g1.transform.localPosition = new Vector3(-14f, 1.2f, -1f);
            g1.prefab = ghost; g1.count = 3; g1.radius = 2.2f; g1.height = 2.0f; g1.heightWave = 0.5f; g1.scale = 2.2f; g1.speed = 0.05f; g1.flap = 2.5f; g1.spread = 0.4f;
            // —— 骑扫帚的女巫：定期从月亮前掠过
            var witchPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/H4_WitchOnBroom.prefab");
            if (witchPrefab)
            {
                var fly = new GameObject("WitchFlyby").AddComponent<SkyFlyer>();
                fly.transform.SetParent(root, false);
                var w = (GameObject)PrefabUtility.InstantiatePrefab(witchPrefab, fly.transform);
                w.transform.localScale = Vector3.one * 5f;
                fly.rider = w.transform;
                fly.from = new Vector3(-70, 30, -95); fly.via = new Vector3(20, 46, -100); fly.to = new Vector3(80, 34, -80);
                w.transform.localPosition = fly.from;
                w.SetActive(false);
            }
            // —— 氛围粒子（萤火、落叶、鬼火）
            root.gameObject.AddComponent<AmbientParticles>();
            // —— 贴地雾带：墓园山坡、南瓜田、拱门外小径、温室前、庄园前；花园内不放（不挡桌面玩法）
            var mist = root.gameObject.AddComponent<GroundMist>();
            var mc = new Color(0.62f, 0.62f, 0.85f, 0.16f);
            mist.zones = new[] {
                new GroundMist.Zone { center = new Vector3(-14f, 1.0f, -1f), size = new Vector3(12f, 0.6f, 15f), count = 26, color = new Color(0.55f, 0.75f, 0.75f, 0.2f) },
                new GroundMist.Zone { center = new Vector3(13.5f, 0.4f, -4f), size = new Vector3(11f, 0.4f, 14f), count = 18, color = mc },
                new GroundMist.Zone { center = new Vector3(0f, 0.35f, -14f), size = new Vector3(12f, 0.4f, 16f), count = 22, color = mc },
                new GroundMist.Zone { center = new Vector3(0f, 0.35f, 10f), size = new Vector3(16f, 0.4f, 6f), count = 12, color = mc },
                new GroundMist.Zone { center = new Vector3(0f, 0.5f, -32f), size = new Vector3(26f, 0.6f, 10f), count = 20, color = new Color(0.55f, 0.55f, 0.8f, 0.14f) },
            };
            // —— 桌上烛群、蘑菇灯也闪烁
            foreach (var t in world.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Desk_CandleCluster") || t.name.StartsWith("L9_MushroomLamp"))
                    if (!t.GetComponentInChildren<Light>())
                        AddFlickerLight(t, new Vector3(0, 0.25f, 0), t.name.StartsWith("L9") ? new Color(0.45f, 1f, 0.9f) : new Color(1f, 0.75f, 0.4f), t.name.StartsWith("L9") ? 0.35f : 0.28f, t.name.StartsWith("L9") ? 2f : 1.6f);
        }

        // 南瓜灯发光芯：南瓜内部一个发光球（不出壳），从雕刻的眼睛嘴巴透出暖光，配合 Bloom 成为“点亮的南瓜灯”
        static Material glowMat;
        static void AddGlowCore(Transform parent, Vector3 local, float diameter)
        {
            if (!glowMat)
            {
                glowMat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                glowMat.color = new Color(1f, 0.55f, 0.12f) * 1.6f;   // 过亮会被色调映射成白色
                AssetDatabase.CreateAsset(glowMat, $"{MatRoot}/JackGlowCore.mat");
            }
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            g.name = "GlowCore";
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = local; g.transform.localScale = Vector3.one * diameter;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = glowMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.AddComponent<GlowPulse>();
        }

        static void AddFlickerLight(Transform parent, Vector3 local, Color c, float intensity, float range)
        {
            var go = new GameObject("FlickerLight");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            var f = go.AddComponent<Flicker>(); f.target = l; f.baseIntensity = intensity;
        }

        // ======== 落位：鸟、灯、帽子这类“搁在别的东西上”的摆件，按实际几何贴到表面 ========
        static readonly System.Collections.Generic.List<(GameObject go, float sink)> Perches = new System.Collections.Generic.List<(GameObject, float)>();
        static GameObject Perch(GameObject go, float sink = 0.01f) { if (go) Perches.Add((go, sink)); return go; }

        // 临时给场景里所有网格加 MeshCollider，从每个摆件上方 0.6m 往下打射线，让摆件包围盒底面落在命中点（再压进 sink），然后撤掉碰撞体
        static void SettlePerches(Transform world)
        {
            var own = new System.Collections.Generic.HashSet<Transform>();
            foreach (var (go, _) in Perches) if (go) foreach (var t in go.GetComponentsInChildren<Transform>(true)) own.Add(t);
            var added = new System.Collections.Generic.List<MeshCollider>();
            foreach (var mf in world.GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh || own.Contains(mf.transform) || mf.GetComponent<Collider>()) continue;
                var r = mf.GetComponent<Renderer>();
                if (!r || !r.enabled) continue;
                var mc = mf.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added.Add(mc);
            }
            Physics.SyncTransforms();
            foreach (var (go, sink) in Perches)
            {
                if (!go) continue;
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var p = go.transform.position;
                // 脚印中心 + 四个方向（包围盒半宽的 55%）各打一条，取最高的命中：身体任何一处都不会陷进旁边更高的表面
                float best = float.NegativeInfinity;
                foreach (var (dx, dz) in new[] { (0f, 0f), (0.55f, 0f), (-0.55f, 0f), (0f, 0.55f), (0f, -0.55f) })
                {
                    var o = new Vector3(b.center.x + dx * b.extents.x, b.min.y + 0.6f, b.center.z + dz * b.extents.z);
                    if (Physics.Raycast(o, Vector3.down, out var hit, 1.3f, ~0, QueryTriggerInteraction.Ignore)) best = Mathf.Max(best, hit.point.y);
                }
                if (best > float.NegativeInfinity) go.transform.position = p + Vector3.up * (best - sink - b.min.y);
                else Debug.LogWarning($"[SceneBuilder] 落位失败（下方 0.7m 内没有表面）：{go.name} @ {p}");
            }
            foreach (var mc in added) Object.DestroyImmediate(mc);
            Perches.Clear();
        }

        // 投影只留给玩家身边（离眼睛 9m 内）、有一定体量、不发光的物体；远景、落叶草丛、灯泡火焰蛛网之类一律不投影
        static void PruneShadowCasters(Transform world)
        {
            var eye = new Vector3(0, 1.15f, 0);
            string[] never = { "Bulb", "Wire", "Flame", "Fire", "Ember", "Glow", "Web", "Leaves", "Tuft", "Leaf", "Mist", "Window", "Glass", "Water", "Silk", "String", "Chain", "Candle", "Moon" };
            int off = 0, on = 0;
            foreach (var r in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off) continue;
                var b = r.bounds;
                float d = Vector3.Distance(eye, b.ClosestPoint(eye));
                bool kill = d > 9f || b.size.magnitude < 0.3f;
                if (!kill) foreach (var n in never) if (r.name.Contains(n) || r.transform.parent && r.transform.parent.name.Contains(n)) { kill = true; break; }
                if (kill) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; off++; } else on++;
            }
            Debug.Log($"[SceneBuilder] 投影：保留 {on} 个，关闭 {off} 个");
        }

        // 装配体（正面 +Z）朝向原点处玩家的 yaw
        static float FaceOrigin(Vector3 p) => Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg;

        static GameObject PlacePrefab(Transform parent, string name, Vector3 pos, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{name}.prefab");
            if (!prefab) { Debug.LogWarning($"[SceneBuilder] 缺少预制体 {name}"); return new GameObject(name + "_MISSING"); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.localPosition = pos;
            if (yaw != 0) go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            return go;
        }

        const float R_COL = 6.6f;
        const float RIM_IN = 7.42f;
        const float RimTop = 0.36f;           // 护边顶面（相对规则层地面）

        // 摆一个装饰；checkPath 时检查与敌人行进路线（桥头 → 桌腿）的净空，太近就报警告
        static GameObject Decor(Transform parent, string name, Vector3 pos, float yaw, float scale, bool checkPath = true)
        {
            var go = PlacePrefab(parent, name, pos, yaw);
            go.transform.localScale = Vector3.one * scale;
            if (checkPath)
            {
                float rad = GroundFootprint(go);
                foreach (var bb in BridgeBearings)
                    foreach (float leg in new[] { -100f, -40f, 40f, 100f })
                    {
                        var head = Bearing(bb, BridgeInner); var lg = TrickOrFreeze.Core.Layout.RimPoint(leg, -0.06f); lg.y = 0;
                        float d = DistPointSeg(new Vector2(pos.x, pos.z), new Vector2(head.x, head.z), new Vector2(lg.x, lg.z));
                        // 只检查这座桥最近的两条桌腿（敌人会走最近的）
                        if (Vector3.Distance(head, lg) > Vector3.Distance(head, TrickOrFreeze.Core.Layout.TABLE.center) + 0.6f) continue;
                        if (d < rad + 0.35f) Debug.LogWarning($"[SceneBuilder] {name} 离敌人路线（桥 {bb}° → 桌腿 {leg}°）只有 {d - rad:F2} 米");
                    }
            }
            return go;
        }

        // 地面投影半径：只统计 0.5 米以下的顶点（树冠、灯笼之类的高处部分不挡路）
        static float GroundFootprint(GameObject go)
        {
            var lod0 = go.transform.Find("LOD0");
            float rad = 0.05f; var c = go.transform.position;
            foreach (var mf in (lod0 ? lod0.gameObject : go).GetComponentsInChildren<MeshFilter>())
            {
                if (!mf.sharedMesh) continue;
                var vs = mf.sharedMesh.vertices;
                for (int i = 0; i < vs.Length; i += 3)
                {
                    var w = mf.transform.TransformPoint(vs[i]);
                    if (w.y - c.y > 0.5f) continue;
                    rad = Mathf.Max(rad, new Vector2(w.x - c.x, w.z - c.z).magnitude);
                }
            }
            return rad;
        }

        static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static float DistPointSeg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        static Material ironMat;
        static Material IronMat()
        {
            if (ironMat) return ironMat;
            ironMat = new Material(Shader.Find("TrickOrFreeze/ToonLit"));
            ironMat.SetColor("_BaseColor", new Color(0.16f, 0.12f, 0.2f));
            ironMat.SetFloat("_OutlineWidth", 0f);
            AssetDatabase.CreateAsset(ironMat, $"{MatRoot}/Iron.mat");
            return ironMat;
        }

        static void BuildCandleSea(Transform root)
        {
            var candle = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Candle_Float.prefab");
            var flame = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Candle_Flame.prefab");
            if (!candle) { Debug.LogWarning("[SceneBuilder] 缺少 Candle_Float"); return; }
            var sea = new GameObject("CandleSea").transform;
            sea.SetParent(root, false);
            int placed = 0;
            for (int i = 0; i < 500 && placed < 120; i++)
            {
                float bb = Random.Range(-180f, 180f), d = Mathf.Sqrt(Random.Range(2.0f * 2.0f, 6.0f * 6.0f)), y = Random.Range(3.6f, 7.4f);
                if (Mathf.Abs(bb) < 30f && Random.value < 0.8f) continue;       // 正前方稀疏
                var pos = Bearing(bb, d) + Vector3.up * y;
                var c = (GameObject)PrefabUtility.InstantiatePrefab(candle, sea);
                c.transform.localPosition = pos;
                c.transform.localRotation = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0, 360f), Random.Range(-6f, 6f));
                c.transform.localScale = Vector3.one * Random.Range(0.9f, 1.5f);
                foreach (var r in c.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (flame)
                {
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(flame, c.transform);
                    foreach (var r in f.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                placed++;
            }
            sea.gameObject.AddComponent<TrickOrFreeze.Ambient.CandleSea>();
        }

        // ---- 布局常量与辅助 ----
        const float FloorTop = 0.095f;        // 石板顶面高出模型原点的量（整岛下移这么多，规则层地面 y=0 = 石板面）
        const float RimOuter = 7.82f;
        const float BridgeLen = 5.6f;
        const float BridgeInner = 7.1f;       // 桥内端到圆心的距离
        static readonly float[] BridgeBearings = { -62f, 62f, -128f, 128f };
        static readonly System.Collections.Generic.List<Transform> Columns = new System.Collections.Generic.List<Transform>();
        // 方位角（0 = 正前 -Z，正值 = 向 +X）+ 距离 → 位置
        static Vector3 Bearing(float deg, float dist)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * dist, 0, -Mathf.Cos(a) * dist);
        }

        // 卫星岛：浮岛底座 + 建筑。建筑底部落在岛顶面（按包围盒实测），建筑正面朝玩家。
        static void PlaceIsland(Transform parent, string building, Vector3 pos, float islandScale, float islandXZ, float bldgScale, bool bob = false, string extra = null)
        {
            var root = new GameObject("Isle_" + building);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = pos;
            var island = PlacePrefab(root.transform, "L7_FloatIsland", Vector3.zero);
            island.transform.localScale = new Vector3(islandScale * islandXZ, islandScale, islandScale * islandXZ);
            // 浮岛 Y 轴随机旋转会让倾斜的顶面朝向乱变：固定朝向，让顶面最平
            island.transform.localRotation = Quaternion.identity;
            // 用中心附近（半径 0.9 米内）顶点的最高 y 作为站立面，把它放到 root 原点
            float top = SurfaceY(island, root.transform.position, 0.9f * islandScale * islandXZ) - root.transform.position.y;
            island.transform.localPosition = new Vector3(0, -top, 0);
            // 分部件装配体（Deco_*）正面朝 +Z，Tripo 整体模型正面朝 -X
            var bldg = PlacePrefab(root.transform, building, Vector3.zero, building.StartsWith("Deco_") ? FaceOrigin(pos) : FacePlayer(pos));
            bldg.transform.localScale = Vector3.one * bldgScale;
            if (extra != null)
            {
                var ex = PlacePrefab(root.transform, extra, new Vector3(1.1f, 0, 0.6f), FacePlayer(pos) + 30f);
                ex.transform.localScale = Vector3.one * 1.8f;
            }
            if (bob)
            {
                var bo = root.AddComponent<Bobber>();
                bo.amplitude = Random.Range(0.35f, 0.6f); bo.period = Random.Range(8f, 12f); bo.tilt = 1.2f;
            }
        }

        // 物体在某点附近的表面高度（世界 y）：取 LOD0 在水平半径 r 内顶点的最高值
        static float SurfaceY(GameObject go, Vector3 at, float r)
        {
            var lod0 = go.transform.Find("LOD0");
            float y = float.MinValue;
            foreach (var mf in (lod0 ? lod0.gameObject : go).GetComponentsInChildren<MeshFilter>())
            {
                var vs = mf.sharedMesh.vertices;
                foreach (var v in vs)
                {
                    var w = mf.transform.TransformPoint(v);
                    if (new Vector2(w.x - at.x, w.z - at.z).sqrMagnitude < r * r) y = Mathf.Max(y, w.y);
                }
            }
            return y == float.MinValue ? TopOf(go) : y;
        }

        // 物体渲染包围盒顶部（世界 y）：只取 LOD0
        static float TopOf(GameObject go)
        {
            var lod0 = go.transform.Find("LOD0");
            float top = float.MinValue;
            foreach (var r in (lod0 ? lod0.gameObject : go).GetComponentsInChildren<Renderer>()) top = Mathf.Max(top, r.bounds.max.y);
            return top;
        }

        static Color HexColor(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }
    }
}
