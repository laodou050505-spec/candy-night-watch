// 玩法接线（SceneBuilder 最后调用）：XR 头显装置 + GameLoop 全套（输入、敌人/糖/灯预制体、特效、音效、音乐、界面、桌面表现）。
// 也负责生成 TMP 字体资产（中文按用字预填）、粒子贴图与材质、技能灯笼预制体、把语言表同步进 Resources。
using System.Collections.Generic;
using System.IO;
using ByteDance.PICO.XR;
using TMPro;
using TrickOrFreeze.AudioSys;
using TrickOrFreeze.Core;
using TrickOrFreeze.Input;
using TrickOrFreeze.UI;
using TrickOrFreeze.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TextCore.LowLevel;

namespace TrickOrFreeze.EditorTools
{
    public static class GameplayBuilder
    {
        const string PrefabRoot = "Assets/_Game/Prefabs/Art";
        const string GenRoot = "Assets/_Game/Art/Generated";
        const string FontRoot = "Assets/_Game/Art/Fonts";

        public static void Build(GameObject world, Camera cam)
        {
            Directory.CreateDirectory(GenRoot);
            SyncLocalization();
            // ======== XR 装置：原点在地面，相机由头显驱动（桌面调试时由 DesktopInputProvider 驱动）========
            var origin = new GameObject("XR Origin").transform;
            origin.SetParent(world.transform, false);
            cam.transform.SetParent(origin, false);
            cam.transform.localPosition = new Vector3(0, Layout.EYE_HEIGHT, Layout.SEAT_Z);
            var tpd = cam.gameObject.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("HeadPos", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("HeadRot", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            // 游戏天空：画在 180 米的天空球上，相机不再画引擎天空盒（10-29 实机：天空盒在头显上满天三角形）
            {
                var oldDome = world.transform.Find("SkyDome"); if (oldDome) Object.DestroyImmediate(oldDome.gameObject);
                var dome = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(dome.GetComponent<Collider>());
                dome.name = "SkyDome"; dome.transform.SetParent(world.transform, false); dome.transform.localScale = Vector3.one * 360f;
                var dr = dome.GetComponent<Renderer>(); dr.sharedMaterial = RenderSettings.skybox;
                dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; dr.receiveShadows = false;
                dr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; dr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
            }
            // 音频监听器：10-29 实机整个游戏没有声音——场景里一个 AudioListener 都没有（相机是代码新建的，不会自带）
            if (!cam.GetComponent<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            origin.gameObject.AddComponent<XRFloorOrigin>();
            var pxr = origin.gameObject.AddComponent<PXR_Manager>();
            pxr.eyeTracking = true;
            var pc = PXR_ProjectSetting.GetProjectConfig();
            if (pc != null) { pc.handTracking = true; pc.eyeTracking = true; pc.eyetrackingCalibration = true; EditorUtility.SetDirty(pc); }

            // ======== GameLoop ========
            var gameGO = new GameObject("Game");
            gameGO.transform.SetParent(world.transform, false);
            var loop = gameGO.AddComponent<GameLoop>();
            loop.worldRoot = world.transform;
            var desk = gameGO.AddComponent<DesktopInputProvider>(); desk.worldRoot = world.transform; desk.cam = cam;
            var pico = gameGO.AddComponent<PicoInputProvider>(); pico.worldRoot = world.transform; pico.xrOrigin = origin; pico.xrCamera = cam;
            loop.desktopInput = desk; loop.picoInput = pico;
            var map = new (string kind, string prefab)[]
            {
                (KindIds.Skeleton, "E1_Skelly"), (KindIds.Bat, "E2_Batsy"), (KindIds.Spider, "E3_Dangle"), (KindIds.Pumpkin, "E4_Jack"),
                (KindIds.Cat, "E5_Mitten"), (KindIds.Ghost, "E6_Boo"), (KindIds.Frank, "E7_Frankenbear"),
            };
            loop.enemyPrefabs = new List<GameLoop.KindPrefab>();
            foreach (var (k, p) in map)
                loop.enemyPrefabs.Add(new GameLoop.KindPrefab { kind = k, prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{p}.prefab") });
            loop.candyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/S10_CandyWrapped.prefab");
            // 10-31 抱糖位置：按游戏里同样的摆法生成一次每种小怪，量出肚子那一圈最靠前的位置存起来（包里网格不可读，运行时量不了）
            foreach (var kp in loop.enemyPrefabs)
            {
                if (!kp.prefab) continue;
                var tmp = new GameObject("__BellyMeasure");
                EnemyView v = null;
                try
                {
                    v = tmp.AddComponent<EnemyView>();
                    v.Init(new Toy { kind = kp.kind, state = ToyStates.Walk }, kp.prefab, null, null, loop.candyPrefab, null);
                    kp.bellyFront = v.measuredBelly;
                }
                finally { if (v) v.EditorDispose(); Object.DestroyImmediate(tmp); }
            }
            Debug.Log("[GameplayBuilder] 抱糖肚子前缘：" + string.Join("，", loop.enemyPrefabs.ConvertAll(k => $"{k.kind} {k.bellyFront:F3}")));
            loop.candyIcon = UISprite("Icon_Candy");                                        // 抱糖小鬼头顶的图标
            loop.lampPrefab = LampPrefab();
            var toon = Shader.Find("TrickOrFreeze/ToonLit");
            loop.dizzyMat = SaveMat("Dizzy", toon, m => { m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.3f)); m.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.2f) * 1.5f); m.SetTexture("_EmissionMap", Tex("White", 4, (u, v) => Color.white)); });
            loop.trailMat = ParticleMat("Trail", Tex("SoftDot", 64, SoftDot), true);

            // ======== 特效 ========
            var fx = new GameObject("Fx").AddComponent<SceneFx>();
            fx.transform.SetParent(gameGO.transform, false);
            fx.sparks = MakePs(fx.transform, "Sparks", ParticleMat("Sparks", Tex("SoftDot", 64, SoftDot), true), 0.25f, shrink: true);
            fx.puffs = MakePs(fx.transform, "Puffs", ParticleMat("Puffs", Tex("SoftDot", 64, SoftDot), false), -0.05f, grow: true);
            fx.ring = MakePs(fx.transform, "Ring", ParticleMat("Ring", Tex("Ring", 128, RingTex), true), 0, ring: true);
            fx.skyStars = gameGO.AddComponent<SkyStars>();
            // 10-31 夜空星星：星身/描边用读顶点色的透明材质（双面），背后叠一团柔光
            {
                var starMat = ParticleMat("SkyStar", null, false);
                starMat.SetFloat("_Cull", 0); EditorUtility.SetDirty(starMat);
                fx.skyStars.starMat = starMat;
                fx.skyStars.haloMat = ParticleMat("SkyStarHalo", Tex("SoftDot", 64, SoftDot), true);
                fx.skyStars.worldRoot = world.transform;
                fx.skyStars.cam = cam;
            }
            // 只找 World 下直接挂着的桌面物件：Halloween 布景里也有一口放大 3.5 倍的装饰大坩埚，叫同一个名字。
            // 10-30 实机：GameObject.Find 先找到了那口大锅，SanctumView 每帧把它挪到桌上，大锅贴在玩家脸边
            var bucketT = world.transform.Find("Desk_Bucket");
            var cauldronT = world.transform.Find("Desk_Cauldron");
            var bucket = bucketT ? bucketT.gameObject : null;
            var cauldron = cauldronT ? cauldronT.gameObject : null;
            fx.candyBucket = bucket ? bucket.transform : null;
            fx.cauldron = cauldron ? cauldron.transform : null;
            loop.fx = fx;
            // ======== 音效 + 音乐 ========
            BuildAudio(gameGO, loop);
            // ======== 桌面表现 ========
            var sanctum = gameGO.AddComponent<SanctumView>();
            sanctum.loop = loop;
            sanctum.bucket = bucket ? bucket.transform : null;
            {
                var gm = ParticleMat("BucketGlow", Tex("SoftDot", 64, SoftDot), true);
                gm.SetFloat("_Cull", 0); EditorUtility.SetDirty(gm);
                sanctum.glowMat = gm;
            }
            if (cauldron)
                foreach (var r in cauldron.GetComponentsInChildren<Renderer>())
                    if (r.name.StartsWith("Potion")) sanctum.potion = r;
            var bl = new GameObject("BucketLight").AddComponent<Light>();
            bl.transform.SetParent(world.transform, false);
            bl.transform.localPosition = Layout.JAR.pos + Vector3.up * 0.35f;
            bl.type = LightType.Point; bl.range = 0.9f; bl.intensity = 0.9f; bl.color = new Color(1f, 0.72f, 0.4f); bl.shadows = LightShadows.None;
            sanctum.bucketLight = bl;
            sanctum.cauldron = cauldron ? cauldron.transform : null;
            sanctum.font = FontAsset("Baloo2-SemiBold", false);
            loop.sanctum = sanctum;
            // ======== 界面 ========
            var ui = new GameObject("UI");
            ui.transform.SetParent(world.transform, false);
            var hud = ui.AddComponent<HudPresenter>();
            hud.loop = loop;
            hud.fontZh = FontAsset("ZCOOLKuaiLe-Subset", true);
            hud.fontTitleEn = FontAsset("Creepster-Regular", false);
            hud.fontEn = FontAsset("Baloo2-SemiBold", false);
            // 英文字体没有中文字形：把中文字体设为回退，任何地方误用英文字体写中文也不会出方块
            foreach (var f in new[] { hud.fontEn, hud.fontTitleEn, sanctum.font })
            {
                if (!f) continue;
                f.fallbackFontAssetTable = new List<TMP_FontAsset> { hud.fontZh };
                EditorUtility.SetDirty(f);
            }
            var ptr = ui.AddComponent<UIPointer>();
            ptr.loop = loop; ptr.cam = cam;
            hud.pointer = ptr;
            loop.hud = hud;
            // ======== 封面：漆黑里的漂浮南瓜桌布景 + 标题字 + 按钮（在南瓜藤之后搭，隐藏列表才找得到藤）========
            BuildLadders(world.transform);
            var cover = BuildCover(world.transform, cam);
            hud.cover = cover;
            hud.brandLogo = BrandSprite("tripo_logo_h_800_white");
            hud.logo = UISprite("Logo_zh"); hud.logoEn = UISprite("Logo_en"); hud.btnPrimary = UISprite("Btn_Primary"); hud.btnSecondary = UISprite("Btn_Secondary");
            hud.panel = UISprite("Panel"); hud.card = UISprite("Card"); hud.shopCard = UISprite("ShopCard");
            var iconNames = new[] { "Moon", "Candy", "Cauldron", "Star", "Lock", "Gear", "Globe", "Back", "Pause", "Play", "Retry", "Hand" };
            hud.icons = System.Array.ConvertAll(iconNames, n => UISprite("Icon_" + n));
            var hints = new GameObject("OffscreenHints").AddComponent<OffscreenHints>();
            hints.transform.SetParent(world.transform, false);
            hints.loop = loop; hints.cam = cam; hints.arrow = UISprite("Hint_Arrow");
            hud.gestures = System.Array.ConvertAll(new[] { "Pinch", "Shake", "Throw", "Pull", "Launch", "Pray", "Sweep", "Shield", "Lamp" }, n => UISprite("Gesture_" + n));
            ScreenFade.Create(cam);
            // ======== 双手：卡通手套 ========
            BuildHands(world.transform, loop);
            // （桌腿旁的南瓜藤已在封面之前搭好）
        }

        // ---------- 只重建玩法部分：保留场景里手工调整过的摆件 ----------
        [MenuItem("Tools/TrickOrFreeze/Rebuild Gameplay (keep scene)")]
        public static void RebuildGameplayOnly()
        {
            var world = GameObject.Find("World");
            if (!world) { Debug.LogError("[GameplayBuilder] 场景里没有 World"); return; }
            var cam = world.GetComponentInChildren<Camera>(true);
            if (!cam) { Debug.LogError("[GameplayBuilder] 场景里没有 XR 相机"); return; }
            cam.transform.SetParent(world.transform, true);
            foreach (var c in cam.GetComponents<UnityEngine.InputSystem.XR.TrackedPoseDriver>()) Object.DestroyImmediate(c);
            for (int i = cam.transform.childCount - 1; i >= 0; i--) if (cam.transform.GetChild(i).name == "ScreenFade") Object.DestroyImmediate(cam.transform.GetChild(i).gameObject);
            // 10-31：重建前先让封面把它藏起来的东西（桌子、花园……）全部放出来，否则 CoverStage 被删后它们永远是关着的
            foreach (var oldCover in Object.FindObjectsByType<CoverStage>(FindObjectsInactive.Include)) oldCover.ForceExit();
            foreach (var n in new[] { "XR Origin", "Game", "UI", "BucketLight", "Hands", "WebLadders", "VineRamps", "Actors", "CoverStage", "OffscreenHints" })
            {
                var t = world.transform.Find(n);
                if (t) Object.DestroyImmediate(t.gameObject);
            }
            // 桌上的坩埚和南瓜灯烛台是规则物件，位置跟规则层布局走
            foreach (var (n, p, sc) in new[] { ("Desk_Cauldron", Layout.BOX.pos, Layout.BOX.scale), ("Desk_CandleCluster", Layout.CANDLE.pos, 1f), ("Desk_Bucket", Layout.JAR.pos, Layout.JAR.scale) })
            {
                var t = world.transform.Find(n);
                if (t) { t.localPosition = p; t.localRotation = Quaternion.Euler(0, Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg, 0); t.localScale = Vector3.one * sc; }
            }
            SettleBucketCandies(world.transform.Find("Desk_Bucket"));
            BuildCauldronSlots(world.transform.Find("Desk_Cauldron"));
            SeatCauldronEars(world.transform.Find("Desk_Cauldron"));
            Build(world, cam);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            SolidGridBaker.Bake();   // 10-31 防穿模：用户挪过摆件也能同步
            Debug.Log("[GameplayBuilder] 玩法部分已重建（场景摆件未动）");
        }

        /// <summary>
        /// 糖桶里的糖贴到糖堆表面（10-29 用户：糖不要悬在桶里）：糖堆是中间高、四周低的拱形，原来的糖按中间高度摆，
        /// 靠边的悬空 1~2 厘米，从南瓜脸的镂空看得一清二楚。用临时碰撞体从上往下打射线，把每颗糖的底落到糖堆上（略陷进去一点）；
        /// 棒棒糖是插着的，杆子多插进去一些。
        /// </summary>
        static void SettleBucketCandies(Transform bucket)
        {
            if (!bucket) return;
            var bed = bucket.Find("Bed"); if (!bed) return;
            var mc = bed.gameObject.AddComponent<MeshCollider>();
            try
            {
                int n = 0;
                foreach (Transform c in bucket)
                {
                    if (!c.name.StartsWith("Candy_")) continue;
                    var r = c.GetComponent<Renderer>(); if (!r) continue;
                    var top = new Vector3(c.position.x, bed.GetComponent<Renderer>().bounds.max.y + 0.2f, c.position.z);
                    if (!mc.Raycast(new Ray(top, Vector3.down), out var hit, 1f)) continue;
                    bool stick = r.bounds.size.y > r.bounds.size.x * 1.2f;            // 棒棒糖
                    float sink = stick ? 0.03f : 0.004f;
                    c.position = new Vector3(c.position.x, hit.point.y - sink, c.position.z);
                    n++;
                }
                Debug.Log($"[GameplayBuilder] 糖桶里 {n} 颗糖贴到糖堆上");
            }
            finally { Object.DestroyImmediate(mc); }
        }

        /// <summary>
        /// 坩埚容量灯（10-29 第二版，用户：要像宝石、贴紧锅壁；原先两侧的灯像要掉下来）：
        /// 锅身正对玩家、肚子最鼓的那一圈，嵌一排切面宝石——六边形切面（台面 + 冠部斜面，平直着色显出棱面），
        /// 宝石大半埋进锅壁、只露出 3 毫米冠部，外面一圈铁质包边也埋进去只露出一道细边；每颗都沿锅壁法线朝外。
        /// 最多 7 颗（格数随章节、升级变化），Slot_0 在正中，往后左右交替排开。旧的大宝石隐藏。
        /// </summary>
        static void BuildCauldronSlots(Transform pot, float potScale = -1f)
        {
            if (!pot) return;
            foreach (Transform c in pot) if (c.name.StartsWith("Gem_")) c.gameObject.SetActive(false);
            for (int i = pot.childCount - 1; i >= 0; i--) if (pot.GetChild(i).name.StartsWith("Slot_") || pot.GetChild(i).name.StartsWith("Socket_")) Object.DestroyImmediate(pot.GetChild(i).gameObject);
            var body = pot.Find("Body"); if (!body) return;
            var br = body.GetComponent<Renderer>();
            var gemMesh = GemMesh(); var bezelMesh = BezelMesh();
            var gemMat = SaveMat("CauldronGem", Shader.Find("TrickOrFreeze/ToonLit"), m =>
            {
                m.SetColor("_BaseColor", new Color(0.35f, 0.95f, 0.75f)); m.SetColor("_EmissionColor", new Color(0.2f, 0.6f, 0.45f));
                if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 1.2f);
                if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", new Color(1f, 1f, 1f));
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0f);
            });
            Material ringMat = null; var ringT = pot.Find("Ring_0"); if (ringT) ringMat = ringT.GetComponent<Renderer>().sharedMaterial;
            var mc = body.gameObject.AddComponent<MeshCollider>();
            try
            {
                float beltY = br.bounds.center.y + br.bounds.extents.y * 0.05f;
                var axis = new Vector3(br.bounds.center.x, beltY, br.bounds.center.z);
                int[] order = { 0, 1, -1, 2, -2, 3, -3 };
                float ls = pot.lossyScale.x;
                for (int i = 0; i < order.Length; i++)
                {
                    float a = order[i] * 11.5f * Mathf.Deg2Rad;   // 7 颗排在 ±35° 内，避开两侧的骨头装饰
                    var dir = pot.TransformDirection(new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a))); dir.y = 0; dir.Normalize();
                    if (!mc.Raycast(new Ray(axis + dir * 0.6f, -dir), out var hit, 1f)) continue;
                    var n = hit.normal;
                    var rot = Quaternion.LookRotation(n, Vector3.up);
                    GameObject Piece(string name, Mesh mesh, Material m, float size, float sink)
                    {
                        var g = new GameObject(name); g.transform.SetParent(pot, true);
                        g.transform.position = hit.point - n * sink; g.transform.rotation = rot;
                        g.transform.localScale = Vector3.one * (size / ls);
                        g.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        return g;
                    }
                    // 网格单位：宝石半径 1、冠高 0.55、亭部深 0.8；尺寸 = 半径（米）
                    float gk = (potScale > 0 ? potScale : Layout.BOX.scale) / 1.05f;          // 宝石跟着锅一起缩放（封面小锅传自己的世界缩放）
                    if (ringMat) Piece("Socket_" + i, bezelMesh, ringMat, 0.0135f * gk, 0.0045f * gk);
                    Piece("Slot_" + i, gemMesh, gemMat, 0.0105f * gk, 0.0028f * gk);
                }
            }
            finally { Object.DestroyImmediate(mc); }
        }

        /// <summary>
        /// 锅耳（10-30 用户：骨头要中段贴在锅上）：骨头沿锅身切线方向横放，中段一半嵌进锅肩，两端自然离开锅壁；
        /// 铁环竖直挂在骨头中段（环面过锅轴，正好套住骨杆），向外下方垂着。环的摆角从小到大试，取第一个不碰锅肚的角度（留 3 毫米）。
        /// 量出的环心写进日志，Layout.BOX.EAR_X / EAR_Y 要与之一致（捏锅耳的判定点）。
        /// </summary>
        static void SeatCauldronEars(Transform pot)
        {
            if (!pot) return;
            var body = pot.Find("Body"); if (!body) return;
            var mc = body.gameObject.AddComponent<MeshCollider>();
            try
            {
                float Surf(Vector3 dirL, float y)
                {
                    var o = pot.TransformPoint(new Vector3(0, y, 0) + dirL * 0.5f);
                    if (!mc.Raycast(new Ray(o, -pot.TransformDirection(dirL).normalized), out var h, 2f)) return 0;
                    var lp = pot.InverseTransformPoint(h.point); return new Vector2(lp.x, lp.z).magnitude;
                }
                const float lugY = 0.112f, boneLen = 0.078f;
                foreach (Transform c in pot)
                {
                    bool isRing = c.name.StartsWith("Ring_"), isBone = c.name.StartsWith("Bone_");
                    if (!isRing && !isBone) continue;
                    int si = c.name.EndsWith("_1") ? 1 : 0;
                    var dirL = new Vector3(si == 1 ? 1 : -1, 0, 0);                         // 两侧各一组：Ring_1 / Bone_1 在 +X，Ring_0 / Bone_0 在 -X
                    float rs = Surf(dirL, lugY);
                    var ringT = pot.Find("Ring_" + si);
                    var rb = ringT ? ringT.GetComponent<MeshFilter>().sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(0.053f, 0.053f, 0.009f));
                    float tube = Mathf.Min(rb.size.x, rb.size.y, rb.size.z), ringR = rb.size.x * 0.5f - tube * 0.5f;
                    var shaft = dirL * rs + Vector3.up * lugY;                              // 骨头中心线在锅肩表面上（一半嵌进去）
                    if (isRing)
                    {
                        // 环的最高点内侧套着骨杆：环心 = 骨杆 + 半径 × (朝下偏外的方向)
                        Vector3 center = shaft; float best = 80f;
                        for (float deg = 15f; deg <= 80f; deg += 2.5f)
                        {
                            float a = deg * Mathf.Deg2Rad;
                            var test = shaft + (Vector3.down * Mathf.Cos(a) + dirL * Mathf.Sin(a)) * ringR;
                            bool clear = true;
                            for (int k = 0; k < 32 && clear; k++)
                            {
                                float t = k / 32f * Mathf.PI * 2;
                                var p = test + (dirL * Mathf.Cos(t) + Vector3.up * Mathf.Sin(t)) * ringR;
                                // 骨杆附近（环套住骨头的地方）允许贴着，其余都要离开锅壁
                                if ((p - shaft).magnitude > ringR * 0.45f && Vector3.Dot(p, dirL) - tube * 0.5f < Surf(dirL, p.y) + 0.003f) clear = false;
                            }
                            if (clear) { center = test; best = deg; break; }
                        }
                        if (center == shaft) center = shaft + (Vector3.down * Mathf.Cos(best * Mathf.Deg2Rad) + dirL * Mathf.Sin(best * Mathf.Deg2Rad)) * ringR;
                        c.localRotation = Quaternion.identity; c.localScale = Vector3.one;
                        c.localPosition = center - rb.center;
                        Debug.Log($"[GameplayBuilder] 锅耳环心（锅本地）x={center.x:F3} y={center.y:F3}，摆角 {best:F0}°，环半径 {ringR:F3}，锅肩半径 {rs:F3}");
                    }
                    else
                    {
                        // 骨头模型是斜着建的：用最远的两个顶点求出真正的长轴，再把长轴摆到切线方向（局部 Z）
                        var mf = c.GetComponent<MeshFilter>(); var vs = mf.sharedMesh.vertices; var cen = Vector3.zero; foreach (var v in vs) cen += v; cen /= Mathf.Max(1, vs.Length);
                        Vector3 A = cen, B = cen; float far = -1;
                        foreach (var v in vs) { float d = (v - cen).sqrMagnitude; if (d > far) { far = d; A = v; } }
                        far = -1; foreach (var v in vs) { float d = (v - A).sqrMagnitude; if (d > far) { far = d; B = v; } }
                        var axisM = (B - A).normalized; float lenM = Vector3.Distance(A, B); var midM = (A + B) * 0.5f;
                        float k = boneLen / Mathf.Max(lenM, 1e-4f);
                        var rot = Quaternion.FromToRotation(axisM, Vector3.forward);
                        c.localRotation = rot; c.localScale = Vector3.one * k;
                        c.localPosition = shaft - rot * (midM * k);
                    }
                }
            }
            finally { Object.DestroyImmediate(mc); }
        }

        /// <summary>六边形切面宝石：台面（小六边形）+ 冠部 12 个斜面 + 腰 + 亭部尖底；每个面单独法线（平直着色出棱面）。+Z 朝外</summary>
        static Mesh GemMesh()
        {
            string path = $"{GenRoot}/CauldronGem.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (old) AssetDatabase.DeleteAsset(path);
            var v = new List<Vector3>(); var t = new List<int>();
            void Face(params Vector3[] p) { int b = v.Count; v.AddRange(p); for (int k = 1; k < p.Length - 1; k++) { t.Add(b); t.Add(b + k); t.Add(b + k + 1); } }
            Vector3 P(float r, float ang, float z) => new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, z);
            const int N = 6; float step = Mathf.PI * 2 / N;
            for (int i = 0; i < N; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step, am = (i + 0.5f) * step;
                // 冠部：腰（r=1, z=0）→ 冠中（r=0.8, z=0.38，转半格）→ 台面（r=0.52, z=0.55）
                Face(P(1f, a0, 0), P(0.8f, am, 0.38f), P(1f, a1, 0));
                Face(P(1f, a0, 0), P(0.52f, a0, 0.55f), P(0.8f, am, 0.38f));
                Face(P(1f, a1, 0), P(0.8f, am, 0.38f), P(0.52f, a1, 0.55f));
                Face(P(0.8f, am, 0.38f), P(0.52f, a0, 0.55f), P(0.52f, a1, 0.55f));
                // 亭部（埋在锅壁里，封住即可）
                Face(P(1f, a1, 0), P(0, 0, -0.8f), P(1f, a0, 0));
            }
            var top = new Vector3[N]; for (int i = 0; i < N; i++) top[i] = P(0.52f, -i * step, 0.55f);
            Face(top);
            // 绕序：Unity 正面是顺时针，从 +Z 往回看；上面按逆时针写的，整体翻一下
            for (int i = 0; i < t.Count; i += 3) { int x = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = x; }
            var m = new Mesh { name = "CauldronGem" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        /// <summary>宝石包边：一圈扁平的六边形铁框（外半径 1、内半径 0.78、高 0.35），+Z 朝外</summary>
        static Mesh BezelMesh()
        {
            string path = $"{GenRoot}/CauldronBezel.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (old) AssetDatabase.DeleteAsset(path);
            var v = new List<Vector3>(); var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { int k = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); t.Add(k); t.Add(k + 2); t.Add(k + 1); t.Add(k); t.Add(k + 3); t.Add(k + 2); }
            Vector3 P(float r, float ang, float z) => new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, z);
            const int N = 6; float step = Mathf.PI * 2 / N; const float ro = 1f, ri = 0.78f, h = 0.35f;
            for (int i = 0; i < N; i++)
            {
                float a0 = i * step, a1 = (i + 1) * step;
                Quad(P(ri, a0, h), P(ro * 0.94f, a0, h), P(ro * 0.94f, a1, h), P(ri, a1, h));      // 顶面
                Quad(P(ro * 0.94f, a0, h), P(ro, a0, 0), P(ro, a1, 0), P(ro * 0.94f, a1, h));     // 外斜面
                Quad(P(ri, a1, h), P(ri, a1, 0), P(ri, a0, 0), P(ri, a0, h));                     // 内壁
            }
            var m = new Mesh { name = "CauldronBezel" }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        // ---------- 音频：背景音乐（MusicDirector）、音效素材库（Sfx.banks）、环境声（AmbienceBed） ----------
        const string AudioRoot = "Assets/_Game/Audio";
        static AudioClip Clip(string path, bool music)
        {
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is AudioImporter ai)
            {
                var st = ai.defaultSampleSettings;
                // 长曲子：压缩存在内存里（Vorbis，三首加起来不到 10MB）；短音效：载入时解压（零延迟），3D 音效强制单声道。
                // 不用流式播放 / 后台加载；一定要预加载：10-29 实机没有音乐——音乐没预加载，PlayScheduled 排期时播不出声
                var old = ai.defaultSampleSettings;
                st.loadType = music ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
                st.compressionFormat = AudioCompressionFormat.Vorbis;
                st.quality = music ? 0.6f : 0.75f;
                st.preloadAudioData = true;
                bool dirty = !old.preloadAudioData || old.loadType != st.loadType || old.compressionFormat != st.compressionFormat || !Mathf.Approximately(old.quality, st.quality)
                             || ai.forceToMono != !music || ai.loadInBackground;
                ai.defaultSampleSettings = st; ai.forceToMono = !music; ai.loadInBackground = false;
                if (dirty) ai.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        static void BuildAudio(GameObject gameGO, GameLoop loop)
        {
            // 背景音乐
            var mgo = new GameObject("Music"); mgo.transform.SetParent(gameGO.transform, false);
            var md = mgo.AddComponent<TrickOrFreeze.AudioSys.MusicDirector>();
            md.loop = loop;
            md.cover = Clip($"{AudioRoot}/Music/music_cover.mp3", true);
            md.night = Clip($"{AudioRoot}/Music/music_night.mp3", true);
            md.tense = Clip($"{AudioRoot}/Music/music_tense_ambush.wav", true);   // 10-30：素材库 Night Ambush 的激烈后段（原 music_tense.mp3 保留）
            md.stingerNight = Clip($"{AudioRoot}/Music/stinger_night.wav", false);
            md.stingerSkill = Clip($"{AudioRoot}/Music/stinger_skill.wav", false);
            md.stingerDawn = Clip($"{AudioRoot}/Music/stinger_dawn.wav", false);
            md.stingerFail = Clip($"{AudioRoot}/Music/stinger_fail.wav", false);
            // 音效：Sfx 目录下按文件名 <id>_<序号> 分组成 bank（amb_* 与 bubble_loop 给环境声用）
            var sfx = gameGO.AddComponent<Sfx>();
            loop.sfx = sfx;
            var groups = new System.Collections.Generic.SortedDictionary<string, System.Collections.Generic.List<AudioClip>>();
            string dir = $"{AudioRoot}/Sfx";
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir))
                {
                    var ext = Path.GetExtension(f).ToLower();
                    if (ext != ".wav" && ext != ".ogg" && ext != ".mp3") continue;
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (n == "freeze_3") continue;   // 10-31：freeze_3 和 star_1 是同一个源文件，定身和变星星会听起来一样
                    int us = n.LastIndexOf('_');
                    string id = us > 0 && int.TryParse(n.Substring(us + 1), out _) ? n.Substring(0, us) : n;
                    var c = Clip(f.Replace('\\', '/'), false);
                    if (!c) continue;
                    if (!groups.TryGetValue(id, out var l)) groups[id] = l = new System.Collections.Generic.List<AudioClip>();
                    l.Add(c);
                }
            // 各音效的音量（素材响度不一，按耳朵听着平衡；没列的按 1）
            var vol = new System.Collections.Generic.Dictionary<string, float> {
                { "step", 0.7f }, { "bone", 0.9f }, { "tick", 0.8f }, { "ui_hover", 0.6f }, { "ui_click", 0.9f }, { "ui_back", 0.9f } };   // 10-30 用户：声音不要太小，只留脚步、悬停这类会连响的压一点
            sfx.banks = new System.Collections.Generic.List<Sfx.Bank>();
            foreach (var kv in groups)
            {
                if (kv.Key.StartsWith("amb_") || kv.Key == "bubble_loop") continue;
                sfx.banks.Add(new Sfx.Bank { id = kv.Key, clips = kv.Value.ToArray(), volume = vol.TryGetValue(kv.Key, out var v) ? v : 1f, pitchJitter = kv.Key.StartsWith("ui_") ? 0.02f : 0.07f });
            }
            // 环境声
            var amb = new GameObject("Ambience").AddComponent<TrickOrFreeze.AudioSys.AmbienceBed>();
            amb.transform.SetParent(gameGO.transform, false);
            amb.loop = loop;
            AudioClip First(string id) => groups.TryGetValue(id, out var l) && l.Count > 0 ? l[0] : null;
            AudioClip[] All(string id) => groups.TryGetValue(id, out var l) ? l.ToArray() : new AudioClip[0];
            amb.nightLoop = First("amb_night_loop"); amb.bubbleLoop = First("bubble_loop");
            amb.owl = All("amb_owl"); amb.wolf = All("amb_wolf"); amb.gate = All("amb_gate");
            Debug.Log($"[GameplayBuilder] 音效 bank {sfx.banks.Count} 组：{string.Join(",", sfx.banks.ConvertAll(b => b.id + "x" + b.clips.Length))}");
        }

        // 界面贴图：导入成 Sprite（保留透明、带 mip，VR 里远看不闪）
        // 公司标志：官方原图，不压缩（标志小、压缩会糊边变色），带 mipmap + 三线性过滤（头显里显示得小，不闪）
        static Sprite BrandSprite(string name)
        {
            string path = $"Assets/_Game/Art/Brand/{name}.png";
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool dirty = ti.textureType != TextureImporterType.Sprite || !ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed || ti.filterMode != FilterMode.Trilinear;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = true; ti.alphaIsTransparency = true;
                ti.sRGBTexture = true; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.filterMode = FilterMode.Trilinear; ti.wrapMode = TextureWrapMode.Clamp;
                if (dirty) ti.SaveAndReimport();
            }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sp) Debug.LogWarning("[GameplayBuilder] 缺少公司标志 " + path);
            return sp;
        }

        static Sprite UISprite(string name)
        {
            string path = $"Assets/_Game/Art/UI/{name}.png";
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                bool dirty = ti.textureType != TextureImporterType.Sprite || !ti.mipmapEnabled || !ti.alphaIsTransparency;
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = true; ti.alphaIsTransparency = true;
                ti.sRGBTexture = true; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.maxTextureSize = 2048; ti.wrapMode = TextureWrapMode.Clamp;
                if (dirty) ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        // 摆一个预制体并按目标高度等比缩放（底部贴在 localPos 上）
        static GameObject PlaceH(Transform parent, string prefab, Vector3 localPos, float yaw, float height)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/{prefab}.prefab");
            if (!p) { Debug.LogWarning($"[封面] 缺少 {prefab}"); return new GameObject(prefab + "_MISSING"); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float s = height / Mathf.Max(0.001f, b.size.y / parent.lossyScale.y);
            float bottom = (b.min.y - go.transform.position.y) / parent.lossyScale.y;
            go.transform.localScale = Vector3.one * s;
            go.transform.localPosition = localPos - Vector3.up * bottom * s;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            foreach (var r in rs) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        // 封面布景（本地坐标：玩家在原点、眼高 1.15、面朝 -Z）。左边漂浮的南瓜桌小舞台，右边标题字与按钮（锚点由 HudPresenter 挂界面）
        static CoverStage BuildCover(Transform world, Camera cam)
        {
            var host = new GameObject("CoverStage");
            host.transform.SetParent(world, false);
            var cs = host.AddComponent<CoverStage>();
            cs.cam = cam;
            var stage = new GameObject("Stage").transform; stage.SetParent(host.transform, false);
            cs.stage = stage;
            // —— 左：漂浮小舞台 ——
            var dio = new GameObject("Diorama").transform; dio.SetParent(stage, false);
            dio.localPosition = new Vector3(0.62f, 0.2f, -2.25f); dio.localRotation = Quaternion.Euler(0, -14f, 0);   // 玩家左手边（+X）
            var bob = dio.gameObject.AddComponent<TrickOrFreeze.Ambient.Bobber>(); bob.amplitude = 0.03f; bob.period = 6f; bob.tilt = 0f;   // 11-05：不再左右摇摆——桌沿那条细描边随摇摆一直爬动出锯齿
            const float Y = 0.42f;                                                       // 缩小后的桌面高度（相对 Diorama）
            var table = PlaceH(dio, "Desk_Table", Vector3.zero, 0, 0.42f);               // 游戏里那张南瓜桌当舞台
            // 11-05 用户：封面桌面边缘一直有横截面锯齿、晃起来不舒服。原因：桌面（Top）和桌沿（Rim）是两块几乎贴合的网格，
            //   Top 的描边壳在桌沿上方跟 Rim 的表面挤在同一深度，缩小到 0.56 倍后只剩一两个像素宽，随漂浮一直闪成锯齿。
            //   封面桌用自己的材质：桌面不画描边，桌沿描边往后推一点只在外轮廓露出来。
            foreach (var r in table.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name != "Top" && r.name != "Rim") continue;
                var src = r.sharedMaterial; bool top = r.name == "Top";
                r.sharedMaterial = SaveMat("Cover_Table" + r.name, src.shader, m =>
                {
                    m.CopyPropertiesFromMaterial(src);
                    m.SetFloat("_OutlineWidth", top ? 0f : src.GetFloat("_OutlineWidth"));
                    m.SetFloat("_OutlinePush", top ? 0f : 0.008f);
                });
            }
            PlaceH(dio, "Desk_Bucket", new Vector3(0, Y, 0), 0, 0.46f);
            var coverPot = PlaceH(dio, "Desk_Cauldron", new Vector3(0.43f, Y, 0.1f), -30f, 0.24f);
            // 11-04 用户：封面上的锅还是旧样子——和游戏里的锅走同一套处理（嵌入锅壁的宝石灯 + 骨头锅耳），封面上点亮 4 颗做装饰
            BuildCauldronSlots(coverPot.transform, coverPot.transform.lossyScale.x);
            SeatCauldronEars(coverPot.transform);
            for (int i = 0; i < 7; i++) { var sl = coverPot.transform.Find("Slot_" + i); if (sl) sl.gameObject.SetActive(i < 4); }
            PlaceH(dio, "Desk_CandleCluster", new Vector3(-0.44f, Y, -0.08f), 20f, 0.2f);
            // 小鬼（模型正面朝 -X：yaw +90 让正面朝玩家 +Z）
            PlaceH(dio, "E1_Skelly", new Vector3(-0.2f, Y, 0.2f), 90f + 25f, 0.27f).AddComponent<TrickOrFreeze.Ambient.Swayer>().angle = 5f;
            PlaceH(dio, "E4_Jack", new Vector3(0.22f, Y, 0.3f), 90f - 40f, 0.26f).AddComponent<TrickOrFreeze.Ambient.Swayer>().angle = 7f;
            PlaceH(dio, "E5_Mitten", new Vector3(0.0f, Y, 0.42f), 90f, 0.16f);
            PlaceH(dio, "E7_Frankenbear", new Vector3(0.36f, Y, -0.32f), 90f - 20f, 0.42f);
            var ghost = PlaceH(dio, "E6_Boo", new Vector3(-0.22f, Y + 0.48f, -0.06f), 90f + 15f, 0.24f);
            var gb = ghost.AddComponent<TrickOrFreeze.Ambient.Bobber>(); gb.amplitude = 0.04f; gb.period = 3.2f; gb.tilt = 4f;
            var spider = PlaceH(dio, "E3_Dangle", new Vector3(0.07f, Y + 0.36f, 0.06f), 90f, 0.1f);
            var sb = spider.AddComponent<TrickOrFreeze.Ambient.Bobber>(); sb.amplitude = 0.03f; sb.period = 2.6f; sb.tilt = 6f;
            var silk = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(silk.GetComponent<Collider>());
            silk.name = "Silk"; silk.transform.SetParent(dio, false); silk.transform.localPosition = new Vector3(0.07f, Y + 0.55f, 0.06f); silk.transform.localScale = new Vector3(0.002f, 0.1f, 0.002f);
            PlaceH(dio, "H2_SpookyTree", new Vector3(-0.5f, Y, -0.38f), 160f, 0.75f);
            PlaceH(dio, "H1_JackLanternBig", new Vector3(0.52f, Y, 0.3f), 90f - 30f, 0.13f);
            var flock = new GameObject("CoverBats").AddComponent<TrickOrFreeze.Ambient.FlockOrbit>();
            flock.transform.SetParent(dio, false); flock.transform.localPosition = new Vector3(0, Y, 0);
            flock.prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/E2_Batsy.prefab");
            flock.count = 4; flock.radius = 0.7f; flock.radiusZScale = 0.55f; flock.height = 0.62f; flock.heightWave = 0.12f; flock.scale = 0.9f; flock.speed = 0.06f; flock.spread = 0.12f;
            flock.wingFlap = true; flock.flap = 5f;                                       // 离得近：扇慢一点，看得清是在扇翅膀
            // —— 更多万圣节点缀（都在黑暗里漂着，像一个悬浮的万圣节小剧场）——
            void Float(GameObject go, float amp, float period, float tilt) { var bb = go.AddComponent<TrickOrFreeze.Ambient.Bobber>(); bb.amplitude = amp; bb.period = period; bb.tilt = tilt; }
            // 桌上撒点糖：糖果玉米一小堆、几颗包装糖、一支南瓜棒棒糖（都放在桌沿空处，不挡小鬼）
            PlaceH(dio, "H7_CandyCornPile", new Vector3(0.27f, Y, -0.14f), 30f, 0.085f);
            PlaceH(dio, "S10_CandyWrapped", new Vector3(0.1f, Y, 0.46f), 40f, 0.022f);
            PlaceH(dio, "S10_CandyWrapped", new Vector3(-0.36f, Y, 0.34f), -70f, 0.022f);
            PlaceH(dio, "S10_CandyWrapped", new Vector3(0.48f, Y, -0.12f), 120f, 0.022f);
            PlaceH(dio, "S11_LollipopPumpkin", new Vector3(-0.4f, Y, 0.16f), 15f, 0.07f).transform.localRotation *= Quaternion.Euler(0, 0, 22f);
            // 树上挂一张蛛网
            var web = PlaceH(dio, "Deco_Web", new Vector3(-0.52f, Y + 0.38f, -0.33f), 165f, 0.26f);
            // 漂浮蜡烛：一圈绕着小舞台上下浮动（正前方低处留空，不挡桌上的小鬼）
            var candles = new GameObject("CoverCandles").transform; candles.SetParent(dio, false);
            var flamePf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/Candle_Flame.prefab");
            var rng = new System.Random(7);
            var haloMat = ParticleMat("CandleHalo", Tex("SoftDot", 64, SoftDot), true);
            haloMat.SetColor("_BaseColor", new Color(1f, 0.58f, 0.22f, 0.6f)); EditorUtility.SetDirty(haloMat);
            (float ang, float r, float h)[] spots = { (-150, 0.95f, 0.85f), (-110, 1.0f, 0.55f), (-70, 0.85f, 0.95f), (-35, 1.05f, 0.75f), (40, 1.05f, 0.7f), (75, 0.95f, 1.0f), (115, 0.8f, 0.6f), (150, 1.0f, 0.95f), (180, 0.9f, 0.7f), (-180, 0.75f, 1.15f) };
            Material coverCandleMat = null;
            GameObject MakeCandle(Transform parent, Vector3 pos, float height)
            {
                var c = PlaceH(parent, "Candle_Float", pos, (float)rng.NextDouble() * 360f, height);
                c.transform.localRotation *= Quaternion.Euler((float)rng.NextDouble() * 8 - 4, 0, (float)rng.NextDouble() * 8 - 4);
                // 封面蜡烛更“自发光”一点：黑暗里背光那一面也不会发黑
                var cr = c.GetComponentsInChildren<Renderer>();
                if (coverCandleMat == null && cr.Length > 0)
                {
                    coverCandleMat = SaveMat("CoverCandle", cr[0].sharedMaterial.shader, m =>
                    {
                        m.CopyPropertiesFromMaterial(cr[0].sharedMaterial);
                        m.SetColor("_EmissionColor", new Color(1f, 0.6f, 0.3f) * 0.45f);
                        m.SetColor("_ShadowTint", new Color(0.95f, 0.72f, 0.6f));             // 背光面也是暖的蜡色，不发黑
                    });
                }
                foreach (var x in cr) x.sharedMaterial = coverCandleMat;
                if (flamePf)
                {
                    var f = (GameObject)PrefabUtility.InstantiatePrefab(flamePf, c.transform);
                    var frs = f.GetComponentsInChildren<Renderer>();
                    foreach (var fr in frs) fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    // 火苗外一圈暖色光晕（面片朝向镜头，由 CandleSea 驱动），黑暗里一眼能看出是点着的蜡烛
                    var halo = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(halo.GetComponent<Collider>());
                    halo.name = "Halo"; halo.transform.SetParent(f.transform, false);
                    if (frs.Length > 0) halo.transform.position = frs[0].bounds.center;
                    halo.transform.localScale = Vector3.one * (0.07f / Mathf.Max(1e-4f, f.transform.lossyScale.x));
                    var hr = halo.GetComponent<Renderer>(); hr.sharedMaterial = haloMat; hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                return c;
            }
            foreach (var (ang, r, h) in spots)
            {
                float a = ang * Mathf.Deg2Rad;                                              // 0° = 朝玩家（+Z）
                MakeCandle(candles, new Vector3(Mathf.Sin(a) * r, Y + h - 0.08f, Mathf.Cos(a) * r), 0.12f + (float)rng.NextDouble() * 0.06f);
            }
            var sea = candles.gameObject.AddComponent<TrickOrFreeze.Ambient.CandleSea>(); sea.amplitude = 0.035f; sea.period = 5.5f;
            // 两盏漂着的小南瓜灯
            Float(PlaceH(dio, "H1_JackLanternBig", new Vector3(-0.98f, Y + 0.5f, -0.12f), 90f + 35f, 0.11f), 0.03f, 4.4f, 1.5f);
            Float(PlaceH(dio, "H1_JackLanternBig", new Vector3(1.12f, Y + 0.62f, 0.12f), 90f - 50f, 0.09f), 0.03f, 5.1f, 1.5f);
            // 左后方一座漂浮的小墓园（墓碑、铁栅栏）＋一摞南瓜，配一盏淡紫光
            var isle = new GameObject("CoverGraveIsle").transform; isle.SetParent(dio, false);
            isle.localPosition = new Vector3(1.3f, 0.12f, -1.05f); isle.localRotation = Quaternion.Euler(0, -35f, 0);   // 左后方、比桌面略低，和桌子分开
            Float(isle.gameObject, 0.015f, 8f, 0f);   // 11-05 用户：桌子左侧右眼锯齿——不再倾斜摇摆，栅栏尖、草叶这些细线一晃就闪
            PlaceH(isle, "H3_GraveyardSet", Vector3.zero, 0, 0.42f);
            // 10-30 用户：小墓园要正对玩家。模型正面（RIP 墓碑、南瓜、破土的手、栅栏开口）朝它自己的 -X，
            // 按玩家（舞台原点）方向转：-X 指向玩家
            {
                var toPlayer = stage.position - isle.position; toPlayer.y = 0;
                if (toPlayer.sqrMagnitude > 1e-4f) isle.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up) * Quaternion.Euler(0, 90f, 0);
            }
            PlaceH(isle, "Deco_PumpkinStack", new Vector3(0.22f, 0.06f, 0.36f), -60f, 0.2f);       // 左后侧，不挡墓碑
            // 11-05：小墓园离得远、细节又碎（栅栏、草叶、破土的手），描边在头显里只剩一两个像素、会一直闪——封面这座浮岛不画描边
            foreach (var r in isle.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var src = mats[mi]; if (!src || !src.HasProperty("_OutlineWidth")) continue;
                    mats[mi] = SaveMat("Cover_Isle_" + src.name, src.shader, m => { m.CopyPropertiesFromMaterial(src); m.SetFloat("_OutlineWidth", 0f); });
                }
                r.sharedMaterials = mats;
            }
            var isleLight = new GameObject("IsleGlow").AddComponent<Light>(); isleLight.transform.SetParent(isle, false); isleLight.transform.localPosition = new Vector3(-0.3f, 0.45f, 0f);
            isleLight.type = LightType.Point; isleLight.color = new Color(0.62f, 0.5f, 1f); isleLight.intensity = 0.7f; isleLight.range = 1.0f; isleLight.shadows = LightShadows.None;
            // 黑暗里飘着的暖橙 / 淡紫光点
            // 11-05 用户：封面桌子左侧右眼锯齿。桌沿那颗南瓜灯用的是“彩色勾线”（描边按本色提亮），离眼睛近、
            //   在头显里是一圈闪动的红色细线；小鬼们也一样。封面布景统一：墨色描边、宽度减到 7 成（桌子本身前面已单独处理）。
            foreach (var r in dio.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(table.transform) || r.transform.IsChildOf(isle)) continue;
                var mats = r.sharedMaterials; bool changed = false;
                for (int mi = 0; mi < mats.Length; mi++)
                {
                    var src = mats[mi];
                    if (!src || !src.HasProperty("_OutlineWidth") || src.name.StartsWith("Cover_")) continue;
                    if (src.GetFloat("_OutlineWidth") <= 0f) continue;
                    mats[mi] = SaveMat("Cover_" + src.name, src.shader, m =>
                    {
                        m.CopyPropertiesFromMaterial(src);
                        m.SetFloat("_OutlineWidth", src.GetFloat("_OutlineWidth") * 0.7f);
                        if (m.HasProperty("_ColorLine")) m.SetFloat("_ColorLine", 0f);
                    });
                    changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            var motes = new GameObject("CoverMotes").AddComponent<TrickOrFreeze.Ambient.AmbientParticles>();
            motes.transform.SetParent(dio, false); motes.transform.localPosition = new Vector3(0.1f, Y + 0.45f, -0.1f);
            motes.coverMode = true; motes.coverBox = new Vector3(2.6f, 1.3f, 1.6f);
            // 背景：封面专用的“月下纸剧场”（10-29 用户：不要和游戏场景一样），见 CoverBackdrop
            // 右边（标题与按钮外侧）也漂几根蜡烛和一盏小南瓜灯，左右不至于一边满一边空；都在按钮外圈，不挡字
            var rightCandles = new GameObject("CoverCandlesRight").transform; rightCandles.SetParent(stage, false);
            foreach (var p in new[] { new Vector3(-1.55f, 1.55f, -2.7f), new Vector3(-1.75f, 0.85f, -3.0f), new Vector3(-1.25f, 2.35f, -3.1f), new Vector3(-0.15f, 2.55f, -3.2f), new Vector3(-1.5f, 0.45f, -2.45f) })
                MakeCandle(rightCandles, p, 0.14f + (float)rng.NextDouble() * 0.05f);
            rightCandles.gameObject.AddComponent<TrickOrFreeze.Ambient.CandleSea>().amplitude = 0.04f;
            // 骑扫帚的女巫：每隔一阵从月亮前掠过（经典剪影）
            var witchPf = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/H4_WitchOnBroom.prefab");
            if (witchPf)
            {
                var fly = new GameObject("CoverWitch").AddComponent<TrickOrFreeze.Ambient.SkyFlyer>();
                fly.transform.SetParent(stage, false);
                var w = (GameObject)PrefabUtility.InstantiatePrefab(witchPf, fly.transform);
                w.transform.localScale = Vector3.one * 0.32f;
                foreach (var wr in w.GetComponentsInChildren<Renderer>()) wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                fly.rider = w.transform;
                fly.from = new Vector3(2.7f, 0.85f, -4.05f); fly.via = new Vector3(0.9f, 2.55f, -4.1f); fly.to = new Vector3(-0.55f, 2.05f, -4.05f);
                fly.duration = 8f; fly.interval = 12f; fly.firstDelay = 2.5f; fly.bobAmp = 0.04f;
                w.transform.localPosition = fly.from;
                w.SetActive(false);
            }
            // 灯光：糖桶暖光、背后紫色轮廓光、正面柔和主光
            Light L(string n, LightType t, Vector3 p, Color c, float i, float range)
            {
                var l = new GameObject(n).AddComponent<Light>(); l.transform.SetParent(dio, false); l.transform.localPosition = p;
                l.type = t; l.color = c; l.intensity = i; l.range = range; l.shadows = LightShadows.None; return l;
            }
            L("BucketGlow", LightType.Point, new Vector3(0, Y + 0.32f, 0.05f), new Color(1f, 0.62f, 0.25f), 1.6f, 1.4f);
            L("RimViolet", LightType.Point, new Vector3(0.1f, Y + 0.6f, -0.75f), new Color(0.6f, 0.45f, 1f), 2.2f, 2.2f);
            var key = L("Key", LightType.Spot, new Vector3(0.4f, Y + 1.4f, 1.3f), new Color(0.95f, 0.9f, 1f), 3.5f, 4f);
            key.spotAngle = 60; key.transform.LookAt(dio.TransformPoint(new Vector3(0, Y + 0.2f, 0)));
            // —— 右：标题字与按钮锚点 ——
            cs.logoAnchor = new GameObject("LogoAnchor").transform; cs.logoAnchor.SetParent(stage, false);
            cs.logoAnchor.localPosition = new Vector3(-0.7f, 1.8f, -2.05f);
            cs.buttonAnchor = new GameObject("ButtonAnchor").transform; cs.buttonAnchor.SetParent(stage, false);
            cs.buttonAnchor.localPosition = new Vector3(-0.66f, 0.98f, -1.8f);
            cs.panelAnchor = new GameObject("PanelAnchor").transform; cs.panelAnchor.SetParent(stage, false);
            cs.panelAnchor.localPosition = new Vector3(-0.2f, 1.12f, -1.45f);
            foreach (var a in new[] { cs.logoAnchor, cs.buttonAnchor, cs.panelAnchor })
            {
                var d = a.localPosition - new Vector3(0, 1.15f, 0); d.y = 0;
                a.localRotation = Quaternion.LookRotation(d.normalized, Vector3.up);       // 画布正面（-forward）朝玩家
            }
            // 身后渐黑罩子：罩住头部的球，正前方透明、往身后平滑变黑（CoverVignette）
            AssetDatabase.ImportAsset("Assets/_Game/Art/Shaders/CoverVignette.shader");
            var vmat = SaveMat("CoverVignette", Shader.Find("TrickOrFreeze/CoverVignette"), m => { m.SetFloat("_Clear", 72f); m.SetFloat("_Black", 150f); m.SetFloat("_Floor", 0.3f); });
            var vg = GameObject.CreatePrimitive(PrimitiveType.Sphere); Object.DestroyImmediate(vg.GetComponent<Collider>());
            vg.name = "CoverVignette"; vg.transform.SetParent(host.transform, false);
            vg.transform.localPosition = new Vector3(0, 1.15f, 0); vg.transform.localScale = Vector3.one * 1.4f;   // 半径 0.7 米：比手和封面都近的东西不多
            var vr = vg.GetComponent<Renderer>(); vr.sharedMaterial = vmat; vr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; vr.receiveShadows = false;
            cs.vignette = vr; vg.SetActive(false);
            cs.hideInCover = new GameObject[0];
            CoverBackdrop.Build(stage);
            stage.gameObject.SetActive(false);
            return cs;
        }

        static Mesh LoadMesh(string fbx)
        {
            AssetDatabase.ImportAsset(fbx);
            if (AssetImporter.GetAtPath(fbx) is ModelImporter mi && mi.materialImportMode != ModelImporterMaterialImportMode.None)
            { mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.SaveAndReimport(); }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is Mesh m) return m;
            return null;
        }

        static Material ToonMat(string name, string albedo, Color tint, float outline)
        {
            var toon = Shader.Find("TrickOrFreeze/ToonLit");
            return SaveMat(name, toon, m =>
            {
                AssetDatabase.ImportAsset(albedo);
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
                m.SetColor("_BaseColor", tint);
                m.SetFloat("_OutlineWidth", outline);
                m.SetTexture("_EmissionMap", Tex("White", 4, (u, v) => Color.white));
                m.SetColor("_EmissionColor", Color.black);
                if (m.HasProperty("_ShadowTint")) { m.SetColor("_ShadowTint", new Color(0.42f, 0.38f, 0.58f)); m.SetColor("_MidTint", new Color(0.86f, 0.84f, 0.94f)); }
                if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.45f);
            });
        }

        // 卡通手套：方案 A 奶油白手套 + 南瓜橙袖口（部件见 Tools/blender_glove.py）
        static void BuildHands(Transform world, GameLoop loop)
        {
            const string dir = "Assets/_Game/Art/Models/Glove";
            var palm = LoadMesh($"{dir}/Gl_Palm.fbx"); var cuff = LoadMesh($"{dir}/Gl_Cuff.fbx");
            var seg = LoadMesh($"{dir}/Gl_Seg.fbx"); var ball = LoadMesh($"{dir}/Gl_Ball.fbx");
            // 10-30（用户：手套有点粗糙）：贴细针织布纹（GloveFabric / CuffRib，tofdbg fabric.py 生成，可平铺），
            // 底色从纯白改成暖奶油色（纯白在头显里发糊、没有明暗）；描边细一半，手指相交处不再出现内描线
            var glove = ToonMat("Glove", $"{dir}/GloveFabric.png", new Color(1f, 0.985f, 0.95f), 0.0022f);
            var palmM = ToonMat("GlovePalm", $"{dir}/GloveFabric.png", new Color(1f, 0.985f, 0.95f), 0.0022f);
            var cuffM = ToonMat("GloveCuff", $"{dir}/CuffRib.png", Color.white, 0.0022f);
            foreach (var m in new[] { glove, palmM, cuffM })
            {
                m.SetFloat("_Soft", 0.32f);                                                   // 明暗过渡柔一些，布料感
                if (m.HasProperty("_ShadowTint")) { m.SetColor("_ShadowTint", new Color(0.62f, 0.56f, 0.72f)); m.SetColor("_MidTint", new Color(0.93f, 0.9f, 0.95f)); }
                if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.3f);
                if (m.HasProperty("_ColorLine")) m.SetFloat("_ColorLine", 0f);
                // 10-30 第五版手套是一整张封闭曲面，不再需要双面（省一半片元）；袖口有内壁、口沿，仍双面
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", m == cuffM ? 0f : 2f);
                if (m.HasProperty("_OutlinePush")) m.SetFloat("_OutlinePush", 0.004f);       // 指缝里不出断续黑线
                if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", 0.0034f);    // 着色器整体减细后，手套仍保持清楚的轮廓
                EditorUtility.SetDirty(m);
            }
            var hands = new GameObject("Hands").transform;
            hands.SetParent(world, false);
            foreach (bool right in new[] { false, true })
            {
                var hv = new GameObject(right ? "RightHand" : "LeftHand").AddComponent<HandView>();
                hv.transform.SetParent(hands, false);
                hv.loop = loop; hv.right = right;
                hv.palmMesh = palm; hv.cuffMesh = cuff; hv.segMesh = seg; hv.ballMesh = ball;
                hv.gloveMat = glove; hv.palmMat = palmM; hv.cuffMat = cuffM;
                hv.bakedSkin = SaveGloveMesh(hv, right ? "GloveSkin_R" : "GloveSkin_L");
                hv.lampFaceMat = LampFaceMat();
            }
            // 教学示范手：同一套手套部件，换成半透明发青光的幽灵材质，由 DemoHands 按教学步骤摆动作
            AssetDatabase.ImportAsset("Assets/_Game/Art/Shaders/GhostGlove.shader");
            var ghost = SaveMat("GhostGlove", Shader.Find("TrickOrFreeze/GhostGlove"), m => { m.SetFloat("_Alpha", 0.85f); m.SetColor("_Color", new Color(0.22f, 0.78f, 0.95f)); m.SetColor("_Rim", new Color(0.85f, 1.35f, 1.45f)); });
            var demo = new GameObject("DemoHands").AddComponent<DemoHands>();
            demo.transform.SetParent(hands, false);
            demo.loop = loop; demo.cam = world.GetComponentInChildren<Camera>(true); demo.ghostMat = ghost;
            foreach (bool right in new[] { false, true })
            {
                var hv = new GameObject(right ? "DemoRight" : "DemoLeft").AddComponent<HandView>();
                hv.transform.SetParent(demo.transform, false);
                hv.loop = null; hv.right = right;
                // 示范手不要袖口：半透明的喇叭袖口从后上方看像只玻璃杯，反而把手形盖住了
                hv.palmMesh = palm; hv.cuffMesh = null; hv.segMesh = seg; hv.ballMesh = ball;
                hv.gloveMat = hv.palmMat = hv.cuffMat = ghost;
                hv.bakedSkin = SaveGloveMesh(hv, right ? "GloveSkinDemo_R" : "GloveSkinDemo_L");
                hv.lampFaceMat = LampFaceMat();
                if (right) demo.right = hv; else demo.left = hv;
            }
        }

        /// <summary>南瓜鬼脸贴花（三角眼、三角鼻、带两颗牙的大笑嘴），加色发光；手背上用</summary>
        static Material LampFaceMat()
        {
            bool Tri(float x, float y, Vector2 a, Vector2 b, Vector2 c)
            {
                float d1 = (x - b.x) * (a.y - b.y) - (a.x - b.x) * (y - b.y), d2 = (x - c.x) * (b.y - c.y) - (b.x - c.x) * (y - c.y), d3 = (x - a.x) * (c.y - a.y) - (c.x - a.x) * (y - a.y);
                bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
                return !(neg && pos);
            }
            var tex = Tex("LampFace2", 192, (u, v) =>
            {
                // u、v ∈ [-1,1]，v 向上
                bool eye = Tri(u, v, new Vector2(-0.62f, 0.12f), new Vector2(-0.18f, 0.12f), new Vector2(-0.38f, 0.55f)) ||
                           Tri(u, v, new Vector2(0.18f, 0.12f), new Vector2(0.62f, 0.12f), new Vector2(0.38f, 0.55f));
                bool nose = Tri(u, v, new Vector2(-0.1f, -0.08f), new Vector2(0.1f, -0.08f), new Vector2(0f, 0.08f));
                float my = -0.42f + 0.28f * u * u;                                  // 嘴：上沿是往上翘的弧
                bool mouth = Mathf.Abs(u) < 0.7f && v < my + 0.14f - 0.1f * u * u && v > my - 0.2f + 0.25f * u * u;
                bool tooth = (Mathf.Abs(u - 0.2f) < 0.07f || Mathf.Abs(u + 0.24f) < 0.07f) && v > my + 0.0f;
                float a = (eye || nose || (mouth && !tooth)) ? 1f : 0f;
                return new Color(1, 1, 1, a);
            });
            var m = ParticleMat("LampFace", tex, true);
            m.SetColor("_BaseColor", new Color(1f, 0.85f, 0.35f, 1f)); EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>手套网格（距离场提取，约 1 秒/只）在编辑器里生成好存成资源，头显启动时直接读</summary>
        static Mesh SaveGloveMesh(HandView hv, string name)
        {
            string path = $"{GenRoot}/{name}.asset";
            var mesh = hv.BakeSkinMesh(); mesh.name = name;
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // 四条桌腿旁各一根南瓜藤（小鬼爬上桌的路）：中心线 = Game.VINE 剖面（藤顶面）往下一个藤半径，渐细；
        // 藤根扎进土里并打个卷，藤梢平贴桌面；藤身上几片叶子、两三根卷须。网格存成资源（场景引用）
        static void BuildLadders(Transform world)
        {
            var toon = Shader.Find("TrickOrFreeze/ToonLit");
            var vineTex = Tex("VineStripe2", 64, (u, v) =>
            {
                float stripe = 0.5f + 0.5f * Mathf.Cos(u * Mathf.PI * 2 * 6);          // 绕藤一圈 6 道浅色棱（南瓜藤那种棱纹）
                return Color.Lerp(new Color(0.26f, 0.5f, 0.18f), new Color(0.5f, 0.78f, 0.28f), stripe * stripe);
            });
            var vineMat = SaveMat("PumpkinVine", toon, m =>
            {
                m.SetTexture("_BaseMap", vineTex); m.SetColor("_BaseColor", Color.white); m.SetFloat("_OutlineWidth", 0.003f); m.SetFloat("_Soft", 0.15f);
                m.SetColor("_ShadowTint", new Color(0.36f, 0.38f, 0.56f)); m.SetColor("_MidTint", new Color(0.86f, 0.9f, 0.92f));
            });
            var leafMat = SaveMat("PumpkinVineLeaf", toon, m =>
            {
                m.SetTexture("_BaseMap", Tex("White", 4, (u, v) => Color.white)); m.SetColor("_BaseColor", new Color(0.36f, 0.68f, 0.24f)); m.SetFloat("_OutlineWidth", 0.003f); m.SetFloat("_Soft", 0.15f);
            });
            var wrapMat = SaveMat("PumpkinVineWrap", toon, m =>
            {
                m.SetTexture("_BaseMap", Tex("White", 4, (u, v) => Color.white)); m.SetColor("_BaseColor", new Color(0.2f, 0.42f, 0.16f)); m.SetFloat("_OutlineWidth", 0.002f); m.SetFloat("_Soft", 0.15f);
            });
            var pumpkinMat = SaveMat("PumpkinVineFruit", toon, m =>
            {
                m.SetTexture("_BaseMap", Tex("PumpkinRib", 64, (u, v) => Color.Lerp(new Color(0.95f, 0.42f, 0.08f), new Color(1f, 0.62f, 0.18f), Mathf.Pow(Mathf.Abs(Mathf.Cos(u * Mathf.PI * 4)), 0.6f))));
                m.SetColor("_BaseColor", Color.white); m.SetFloat("_OutlineWidth", 0.003f); m.SetFloat("_Soft", 0.15f);
            });
            const string dir = "Assets/_Game/Art/Models/Glove";
            var ball = LoadMesh($"{dir}/Gl_Ball.fbx");
            var root = new GameObject("VineRamps").transform;
            root.SetParent(world, false);
            foreach (var a in Game.LEGS)
            {
                // 中心线：藤根先从地下 4cm 冒出来，然后沿剖面走；越往梢越细
                int n = Game.VINE.Length;
                var ctrl = new System.Collections.Generic.List<Vector3>();
                var rootPt = Game.LegFloor(a) + (Game.LegFloor(a) - Game.VinePoint(a, 1)).normalized * 0.06f; rootPt.y = -0.04f;
                ctrl.Add(rootPt);
                for (int i = 0; i < n; i++) ctrl.Add(Game.VinePoint(a, i));
                float Rad(float t) => Game.VINE_R * (1.15f - 0.85f * t * t);
                var pts = CatmullRom(ctrl, 10);
                for (int i = 0; i < pts.Count; i++) { float t = i / (float)(pts.Count - 1); var p = pts[i]; if (i > 0) p.y -= Rad(t); pts[i] = p; }
                var mesh = TubeMesh(pts, Rad, 12);
                mesh.name = $"Vine_{a:0}";
                string mp = $"{GenRoot}/Vine_{a:0}.asset";
                AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
                var vine = new GameObject($"Vine_{a:0}");
                vine.transform.SetParent(root, false);
                vine.AddComponent<MeshFilter>().sharedMesh = mesh;
                var vr = vine.AddComponent<MeshRenderer>(); vr.sharedMaterial = vineMat;
                // 叶子：贴在藤身两侧，叶面微微翘起
                var rnd = new System.Random((int)a + 500);
                foreach (float t in new[] { 0.14f, 0.27f, 0.4f, 0.52f, 0.63f, 0.76f })
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(t * (pts.Count - 1)), 1, pts.Count - 2);
                    var tan = (pts[i + 1] - pts[i - 1]).normalized;
                    var side = Vector3.Cross(Vector3.up, tan).normalized * (i % 2 == 0 ? 1 : -1);
                    var leaf = new GameObject("Leaf");
                    leaf.transform.SetParent(vine.transform, false);
                    leaf.transform.localPosition = pts[i] + side * (Rad(t) + 0.045f) + Vector3.up * 0.004f;
                    leaf.transform.localRotation = Quaternion.LookRotation(side, Vector3.up) * Quaternion.Euler(-20 - (float)rnd.NextDouble() * 25, 0, (float)rnd.NextDouble() * 30 - 15);
                    leaf.transform.localScale = new Vector3(0.045f, 0.006f, 0.058f);
                    leaf.AddComponent<MeshFilter>().sharedMesh = ball;
                    leaf.AddComponent<MeshRenderer>().sharedMaterial = leafMat;
                }
                // 卷须：从藤身伸出的细螺旋
                foreach (float t in new[] { 0.28f, 0.66f })
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(t * (pts.Count - 1)), 1, pts.Count - 2);
                    var tan = (pts[i + 1] - pts[i - 1]).normalized;
                    var side = Vector3.Cross(tan, Vector3.up).normalized * (i % 2 == 0 ? 1 : -1);
                    var up2 = Vector3.Cross(side, tan).normalized;
                    var curl = new System.Collections.Generic.List<Vector3>();
                    for (int k = 0; k <= 30; k++)
                    {
                        float u = k / 30f, ang = u * Mathf.PI * 4.2f, rr = 0.03f * (1 - u * 0.75f);
                        curl.Add(pts[i] + side * (Rad(t) * 0.8f + 0.05f * u) + (side * Mathf.Cos(ang) + up2 * Mathf.Sin(ang)) * rr - side * 0.03f);
                    }
                    var cm = TubeMesh(curl, u => 0.0035f * (1 - 0.6f * u), 6);
                    string cp = $"{GenRoot}/VineCurl_{a:0}_{t:0.00}.asset";
                    AssetDatabase.DeleteAsset(cp); AssetDatabase.CreateAsset(cm, cp);
                    var c = new GameObject("Curl");
                    c.transform.SetParent(vine.transform, false);
                    c.AddComponent<MeshFilter>().sharedMesh = cm;
                    c.AddComponent<MeshRenderer>().sharedMaterial = leafMat;
                }
                // 缠在主藤外面的一根细藤：螺旋 4 圈，从藤根绕到桌沿
                var wrap = new System.Collections.Generic.List<Vector3>();
                var nrm0 = Vector3.Cross((pts[1] - pts[0]).normalized, Vector3.up).normalized;
                int wn = Mathf.RoundToInt(pts.Count * 0.82f);
                for (int i = 0; i < wn; i++)
                {
                    var tan = (pts[Mathf.Min(pts.Count - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                    nrm0 = (nrm0 - tan * Vector3.Dot(nrm0, tan)).normalized;
                    var bin = Vector3.Cross(tan, nrm0);
                    float t = i / (float)(pts.Count - 1), ang = t * Mathf.PI * 2 * 4f;
                    wrap.Add(pts[i] + (nrm0 * Mathf.Cos(ang) + bin * Mathf.Sin(ang)) * (Rad(t) + 0.004f));
                }
                var wm = TubeMesh(wrap, u => 0.0075f * (1 - 0.5f * u), 6);
                string wp = $"{GenRoot}/VineWrap_{a:0}.asset";
                AssetDatabase.DeleteAsset(wp); AssetDatabase.CreateAsset(wm, wp);
                var w = new GameObject("Wrap"); w.transform.SetParent(vine.transform, false);
                w.AddComponent<MeshFilter>().sharedMesh = wm; w.AddComponent<MeshRenderer>().sharedMaterial = wrapMat;
                // 半腰挂一个小南瓜（藤外侧，垂在藤下）
                {
                    int i = Mathf.RoundToInt(0.34f * (pts.Count - 1));
                    var tan = (pts[i + 1] - pts[i - 1]).normalized;
                    var side = Vector3.Cross(Vector3.up, tan).normalized;
                    var pk = new GameObject("MiniPumpkin"); pk.transform.SetParent(vine.transform, false);
                    pk.transform.localPosition = pts[i] + side * (Rad(0.34f) + 0.032f) - Vector3.up * 0.02f;
                    pk.transform.localScale = new Vector3(0.042f, 0.034f, 0.042f);
                    pk.AddComponent<MeshFilter>().sharedMesh = ball; pk.AddComponent<MeshRenderer>().sharedMaterial = pumpkinMat;
                    var st = new GameObject("Stem"); st.transform.SetParent(vine.transform, false);
                    st.transform.localPosition = pk.transform.localPosition + Vector3.up * 0.036f - side * 0.012f;
                    st.transform.localRotation = Quaternion.LookRotation(-side, Vector3.up) * Quaternion.Euler(-40, 0, 0);
                    st.transform.localScale = new Vector3(0.007f, 0.007f, 0.02f);
                    st.AddComponent<MeshFilter>().sharedMesh = ball; st.AddComponent<MeshRenderer>().sharedMaterial = wrapMat;
                }
            }
        }

        static System.Collections.Generic.List<Vector3> CatmullRom(System.Collections.Generic.List<Vector3> P, int seg)
        {
            var o = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < P.Count - 1; i++)
            {
                var p0 = P[Mathf.Max(0, i - 1)]; var p1 = P[i]; var p2 = P[i + 1]; var p3 = P[Mathf.Min(P.Count - 1, i + 2)];
                for (int k = 0; k < seg; k++)
                {
                    float t = k / (float)seg;
                    o.Add(0.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t));
                }
            }
            o.Add(P[P.Count - 1]);
            return o;
        }

        // 沿折线扫圆截面的封闭管（两端各收一个顶点封口），平行移动标架避免扭曲；UV：u 绕一圈，v 沿长度
        static Mesh TubeMesh(System.Collections.Generic.List<Vector3> pts, System.Func<float, float> radius, int sides)
        {
            int n = pts.Count;
            var verts = new System.Collections.Generic.List<Vector3>(); var uvs = new System.Collections.Generic.List<Vector2>(); var tris = new System.Collections.Generic.List<int>();
            var tan0 = (pts[1] - pts[0]).normalized;
            var nrm = Vector3.Cross(tan0, Mathf.Abs(tan0.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            float len = 0;
            for (int i = 0; i < n; i++)
            {
                var tan = (pts[Mathf.Min(n - 1, i + 1)] - pts[Mathf.Max(0, i - 1)]).normalized;
                nrm = (nrm - tan * Vector3.Dot(nrm, tan)).normalized;
                var bin = Vector3.Cross(tan, nrm);
                if (i > 0) len += Vector3.Distance(pts[i], pts[i - 1]);
                float r = radius(i / (float)(n - 1));
                for (int k = 0; k <= sides; k++)
                {
                    float ang = k / (float)sides * Mathf.PI * 2;
                    verts.Add(pts[i] + (nrm * Mathf.Cos(ang) + bin * Mathf.Sin(ang)) * r);
                    uvs.Add(new Vector2(k / (float)sides, len * 4f));
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = i * (sides + 1) + k, b = a + sides + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });   // Unity 正面为顺时针：法线朝外
                }
            // 两端封口
            int c0 = verts.Count; verts.Add(pts[0]); uvs.Add(new Vector2(0.5f, 0));
            int c1 = verts.Count; verts.Add(pts[n - 1]); uvs.Add(new Vector2(0.5f, len * 4f));
            for (int k = 0; k < sides; k++)
            {
                tris.AddRange(new[] { c0, k, k + 1 });
                int e = (n - 1) * (sides + 1);
                tris.AddRange(new[] { c1, e + k + 1, e + k });
            }
            var m = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // ---------- 语言表同步到 Resources ----------
        static void SyncLocalization()
        {
            const string dst = "Assets/Resources/Localization";
            Directory.CreateDirectory(dst);
            foreach (var l in new[] { "zh", "en" })
            {
                File.Copy($"Assets/_Game/Localization/{l}.json", $"{dst}/{l}.json", true);
                AssetDatabase.ImportAsset($"{dst}/{l}.json");
            }
        }

        // ---------- 字体：TMP SDF，预填本地化里用到的全部字符 ----------
        static TMP_FontAsset FontAsset(string file, bool cjk)
        {
            string ttf = $"{FontRoot}/{file}.ttf", path = $"{FontRoot}/{file} SDF.asset";
            AssetDatabase.ImportAsset(ttf);
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (fa == null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(ttf);
                fa = TMP_FontAsset.CreateFontAsset(font, cjk ? 72 : 90, 9, GlyphRenderMode.SDFAA, cjk ? 2048 : 1024, cjk ? 2048 : 1024, AtlasPopulationMode.Dynamic, true);
                fa.name = file + " SDF";
                AssetDatabase.CreateAsset(fa, path);
                fa.atlasTextures[0].name = file + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
                fa.material.name = file + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            // 中文用的是子集字体：每次改文案都会重新生成子集，旧资产里缓存的字形编号就对不上了（会把“设置”画成别的字）。
            // 所以每次搭建都清空重灌，字形一定来自当前这份子集字体。
            if (cjk) fa.ClearFontAssetData(false);
            var chars = new HashSet<char>("0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz .,:;!?'\"()-+×★·…，。！？：、（）“”%/");
            foreach (var l in new[] { "zh", "en" })
                foreach (var ch in File.ReadAllText($"Assets/_Game/Localization/{l}.json")) if (ch >= 32) chars.Add(ch);
            fa.TryAddCharacters(new string(new List<char>(chars).ToArray()), out _);
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        // ---------- 技能灯笼：南瓜灯 + 暖光 + 光晕 + 桌面光圈（表现由 LampView 驱动） ----------
        static GameObject LampPrefab()
        {
            string path = $"{PrefabRoot}/Lamp_Skill.prefab";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabRoot}/H1_JackLanternBig.prefab");
            var root = new GameObject("Lamp_Skill");
            var view = root.AddComponent<TrickOrFreeze.View.LampView>();
            if (src)
            {
                var holder = new GameObject("Lantern").transform; holder.SetParent(root.transform, false);
                var j = (GameObject)PrefabUtility.InstantiatePrefab(src, holder);
                j.transform.localScale = Vector3.one * 0.2f;                                   // 10-30：0.16 → 0.2，看得清是盏南瓜灯
                j.transform.localPosition = new Vector3(0, -0.07f, 0);
                j.transform.localRotation = Quaternion.Euler(0, 90f, 0);                       // 南瓜脸朝玩家
                foreach (var r in j.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                view.lantern = holder;
            }
            var li = new GameObject("Light").AddComponent<Light>();
            li.transform.SetParent(root.transform, false);
            li.transform.localPosition = new Vector3(0, 0.02f, 0);
            li.type = LightType.Point; li.range = 0.65f; li.intensity = 0.7f; li.color = new Color(1f, 0.66f, 0.28f); li.shadows = LightShadows.None;
            view.glowLight = li;
            // 光晕：一团朝向镜头的暖色柔光
            var haloMat = ParticleMat("LampHalo", Tex("SoftDot", 64, SoftDot), true);
            haloMat.SetColor("_BaseColor", new Color(1f, 0.6f, 0.22f, 0.55f)); EditorUtility.SetDirty(haloMat);
            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.name = "Halo"; halo.transform.SetParent(root.transform, false);
            var hr = halo.GetComponent<Renderer>(); hr.sharedMaterial = haloMat; hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.halo = halo.transform;
            // 桌面光圈：中间淡淡一层暖光，边缘一道亮一点的圈，正好标出照亮范围
            var poolTex = Tex("LampPool", 128, (u, v) =>
            {
                float r = Mathf.Sqrt(u * u + v * v);
                float fill = Mathf.Clamp01(1 - r) * 0.55f;
                float rim = Mathf.Exp(-Mathf.Pow((r - 0.9f) / 0.05f, 2)) * 0.9f;
                return new Color(1, 1, 1, r > 1 ? 0 : Mathf.Clamp01(fill + rim));
            });
            var poolMat = ParticleMat("LampPool", poolTex, true);
            poolMat.SetColor("_BaseColor", new Color(1f, 0.62f, 0.25f, 0.5f)); EditorUtility.SetDirty(poolMat);
            var pool = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(pool.GetComponent<Collider>());
            pool.name = "Pool"; pool.transform.SetParent(root.transform, false);
            var pr = pool.GetComponent<Renderer>(); pr.sharedMaterial = poolMat; pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            view.pool = pool.transform;
            var p = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return p;
        }

        // ---------- 粒子 ----------
        static ParticleSystem MakePs(Transform parent, string name, Material mat, float gravity, bool shrink = false, bool grow = false, bool ring = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000; main.startSpeed = 0; main.startLifetime = 1; main.startSize = 0.02f;
            main.gravityModifier = gravity;
            var em = ps.emission; em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(ring ? 0.9f : 1, 0), new GradientAlphaKey(ring ? 0.6f : 0.9f, 0.4f), new GradientAlphaKey(0, 1) });
            col.color = g;
            if (shrink || grow || ring)
            {
                var sz = ps.sizeOverLifetime; sz.enabled = true;
                sz.size = new ParticleSystem.MinMaxCurve(1, ring ? AnimationCurve.EaseInOut(0, 0.05f, 1, 1) : shrink ? AnimationCurve.Linear(0, 1, 1, 0.1f) : AnimationCurve.EaseInOut(0, 0.5f, 1, 1.6f));
            }
            if (!ring)
            {
                var noise = ps.noise; noise.enabled = true; noise.strength = shrink ? 0.15f : 0.05f; noise.frequency = 1.5f;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ring ? ParticleSystemRenderMode.HorizontalBillboard : ParticleSystemRenderMode.Billboard;
            r.sortingFudge = ring ? 0 : -1;
            return ps;
        }

        static Material ParticleMat(string name, Texture2D tex, bool additive)
        {
            string path = $"{GenRoot}/FX_{name}.mat";
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh;
            m.SetTexture("_BaseMap", tex);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0);
            m.SetOverrideTag("RenderType", "Transparent");
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material SaveMat(string name, Shader sh, System.Action<Material> setup)
        {
            string path = $"{GenRoot}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; setup(m); EditorUtility.SetDirty(m);
            return m;
        }

        static Color SoftDot(float u, float v) { float a = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v)); return new Color(1, 1, 1, a * a); }
        static Color RingTex(float u, float v) { float r = Mathf.Sqrt(u * u + v * v); float a = Mathf.Clamp01(1 - Mathf.Abs(r - 0.82f) / 0.12f); return new Color(1, 1, 1, a * a); }

        static Texture2D Tex(string name, int n, System.Func<float, float, Color> f)
        {
            string path = $"{GenRoot}/T_{name}.png";
            if (!File.Exists(path))
            {
                var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) t.SetPixel(x, y, f((x + 0.5f) / n * 2 - 1, (y + 0.5f) / n * 2 - 1));
                t.Apply();
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);
                var ti = (TextureImporter)AssetImporter.GetAtPath(path);
                ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true; ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
