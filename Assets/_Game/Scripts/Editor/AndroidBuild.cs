// 打 PICO 安卓包：先“切到 Android”（会重新导入贴图，切一次就行），再“打包 APK”。
// 打包在 delayCall 里跑，结果写到 Logs/android_build.txt（MCP 调用不会被长时间打包卡住）。
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public static class AndroidBuild
    {
        public const string PackageId = "com.toybox.trickorfreeze";
        public const string ProductName = "万圣守糖夜";   // 10-28 改名（原《一二三·木头鬼》）；包名不变，覆盖安装
        public const string ProductNameEn = "Candy Night Watch";   // 11-04 系统语言为英文时的应用名（GradleNoCompress 写进 values-en）
        const string Scene = "Assets/_Game/Scenes/Main.unity";
        const string OutDir = "Builds";
        const string Report = "Logs/android_build.txt";

        static void ApplySettings()
        {
            var t = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(t, PackageId);
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = "toybox";
            // 11-17 启动画面：去掉 Made with Unity，换成排版一样的 MADE WITH + TRIPO 标志（每次打包都重新套一遍）
            ApplySplash();
            // 10-31 命令行打包时自动递增版本号，避免头显拒绝安装同版本包
            if (inClone)
            {
                int vc = PlayerSettings.Android.bundleVersionCode + 1;
                PlayerSettings.Android.bundleVersionCode = vc;
                PlayerSettings.bundleVersion = "1.0." + vc;
            }
            PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)29;                 // PICO SDK 要求最低 API 29
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.buildAppBundle = false;
            QualityTiers.Ensure();
            if (inClone) UsePicoPipelineAsDefault();
            EditorUserBuildSettings.development = debuggable;                            // 可调试包：卡住时能用 lldb 挂上去看主线程停在哪
            CleanPreloadedXR();
            FixSkyBackdropForAndroid();                                                          // 头显必须用 PICO 画质档（MSAA 4）
            ApplyAppIcon();
            // PICO SDK 的打包检查：入口必须是 Activity（不能用 GameActivity）；必须用自己的签名密钥库（Keystore/ 下，密码在本地文件里）
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            string ks = Path.GetFullPath("Keystore/trickorfreeze.keystore"), pw = "Keystore/keystore_pass.txt";
            if (File.Exists(ks) && File.Exists(pw))
            {
                string pass = File.ReadAllText(pw).Trim();
                PlayerSettings.Android.useCustomKeystore = true;
                PlayerSettings.Android.keystoreName = ks;
                PlayerSettings.Android.keystorePass = pass;
                PlayerSettings.Android.keyaliasName = "trickorfreeze";
                PlayerSettings.Android.keyaliasPass = pass;
            }
            else
            {
                // 10-31 找不到密钥库时抛异常，命令行打包会以非零退出，不再悄悄用调试签名
                throw new System.Exception("[AndroidBuild] 找不到 Keystore/trickorfreeze.keystore 或密码文件，打包终止");
            }
        }

        /// <summary>
        /// 清掉残留在“预加载资源”里的 XR 设置（10-29 实机卡在启动画面的原因）：
        /// XR 插件打包前会临时把 XRGeneralSettings / PXR_Settings 塞进预加载列表、打包后再拿掉；
        /// 打包中途失败（比如被 PICO 检查拦下）时没拿掉就留在了工程里，下次打包又塞一份 → 头显上 XR 设置加载两次，启动卡死。
        /// 每次打包前先清一遍，交给 XR 插件自己加。顺带把字体的“构建时清空动态数据”关掉：省得每次启动在主线程上现场生成几百个汉字。
        /// </summary>
        static void CleanPreloadedXR()
        {
            var pre = PlayerSettings.GetPreloadedAssets();
            var keep = System.Array.FindAll(pre, p => p != null && !(p.GetType().Name.Contains("XRGeneralSettings") || p.GetType().Name.Contains("PXR_Settings") || p.GetType().Name.Contains("XRManagerSettings")));
            if (keep.Length != pre.Length) { PlayerSettings.SetPreloadedAssets(keep); Debug.Log($"[AndroidBuild] 清掉残留的 XR 预加载资源 {pre.Length - keep.Length} 个"); }
            foreach (var g in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets/_Game/Art/Fonts" }))
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(g));
                if (!fa) continue;
                var so = new SerializedObject(fa);
                var p = so.FindProperty("m_ClearDynamicDataOnBuild");
                if (p != null && p.boolValue) { p.boolValue = false; so.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(fa); }
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>命令行打包：Unity -batchmode -quit -projectPath &lt;纯英文路径&gt; -executeMethod TrickOrFreeze.EditorTools.AndroidBuild.BatchBuild
        /// 工程真实路径含中文，安卓构建工具不支持，所以要从一个纯英文的软链接路径打开工程来打包。</summary>
        public static void BatchBuild()
        {
            // 命令行打包只在英文路径的工程副本里跑，可以放心改全局设置（不影响原工程）
            inClone = true;
            var args = System.Environment.GetCommandLineArgs();
            debuggable = System.Array.IndexOf(args, "-tofDebuggable") >= 0;
            // 10-31 非发行包：Log/Warning 不带堆栈，减少日志噪音；Error/Exception 保留 ScriptOnly
            if (!debuggable)
            {
                PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
                PlayerSettings.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
                PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            }
            if (!CheckScene()) { EditorApplication.Exit(1); return; }
            DoBuild();
            var rep = File.ReadAllText(Report);
            Debug.Log("[AndroidBuild] " + rep);
            EditorApplication.Exit(rep.Contains("result=Succeeded") ? 0 : 1);
        }

        static bool debuggable, inClone;

        /// <summary>
        /// 10-31 打包前检查场景（1.0.2/1.0.3 打进了“桌子、糖桶、坩埚、花园都关着”的场景）：先让封面放出它藏起来的东西，
        /// 再确认玩法和场景的关键物体都是打开的；有任何一个关着就让打包失败，不出一个坏包
        /// </summary>
        static bool CheckScene()
        {
            var path = EditorBuildSettings.scenes.Length > 0 ? EditorBuildSettings.scenes[0].path : null;
            if (string.IsNullOrEmpty(path)) { Debug.LogError("[AndroidBuild] 没有打包场景"); return false; }
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
            foreach (var cs in Object.FindObjectsByType<TrickOrFreeze.UI.CoverStage>(FindObjectsInactive.Include)) cs.ForceExit();
            var world = GameObject.Find("World");
            if (!world) { Debug.LogError("[AndroidBuild] 场景里没有 World"); return false; }
            var missing = new System.Collections.Generic.List<string>();
            foreach (var n in new[] { "Desk_Table", "Desk_Bucket", "Desk_Cauldron", "Garden", "Halloween", "Moon", "SkyDome", "Game", "Hands", "VineRamps", "XR Origin" })
            {
                var t = world.transform.Find(n);
                if (!t || !t.gameObject.activeSelf) missing.Add(n);
            }
            if (missing.Count > 0) { Debug.LogError("[AndroidBuild] 场景检查失败，这些物体是关着的或不存在：" + string.Join("、", missing)); return false; }
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log("[AndroidBuild] 场景检查通过：桌子、糖桶、坩埚、花园、万圣节摆件、月亮、天空都在");
            return true;
        }

        const string IconPath = "Assets/_Game/Art/UI/AppIcon.png", IconRoundPath = "Assets/_Game/Art/UI/AppIcon_Round.png",
            IconAdaptiveBgPath = "Assets/_Game/Art/UI/AppIcon_AdaptiveBG.png", IconClearPath = "Assets/_Game/Art/UI/AppIcon_Clear.png";

        /// <summary>
        /// 应用图标（10-30 用户：要一个更高级的游戏图标）：AppIcon.png（1024 方图，tripo chat_image_2.5 按游戏内角色生成）设为默认图标和安卓方形图标；
        /// 圆形图标用 AppIcon_Round.png（画面缩到 84%，四周用模糊底延伸，圆形裁切时黑猫、骷髅不被切掉）；
        /// 自适应图标（安卓 8+ 的启动器、PICO“最近安装”一栏用的就是它；10-30 实机：留空时 Unity 塞进自带的默认图标）：
        /// 背景层 = 圆形版图标缩到中间 72/108（系统只显示这一块）、四周同色延伸，前景层 = 全透明。图标不压缩、原尺寸。
        /// </summary>
        [MenuItem("Tools/TrickOrFreeze/Apply App Icon")]
        public static void ApplyAppIcon()
        {
            if (!File.Exists(IconPath)) { Debug.LogWarning("[AndroidBuild] 没有 " + IconPath + "，沿用默认图标"); return; }
            foreach (var p in new[] { IconPath, IconRoundPath, IconAdaptiveBgPath, IconClearPath })
                if (AssetImporter.GetAtPath(p) is TextureImporter ti && (ti.textureCompression != TextureImporterCompression.Uncompressed || ti.maxTextureSize < 1024 || ti.mipmapEnabled || !ti.alphaIsTransparency))
                {
                    ti.textureType = TextureImporterType.Default; ti.textureCompression = TextureImporterCompression.Uncompressed;
                    ti.maxTextureSize = 1024; ti.mipmapEnabled = false; ti.alphaIsTransparency = true; ti.npotScale = TextureImporterNPOTScale.None;
                    ti.SaveAndReimport();
                }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var round = AssetDatabase.LoadAssetAtPath<Texture2D>(IconRoundPath) ?? icon;
            var adaptiveBg = AssetDatabase.LoadAssetAtPath<Texture2D>(IconAdaptiveBgPath);
            var clear = AssetDatabase.LoadAssetAtPath<Texture2D>(IconClearPath);
            if (!adaptiveBg || !clear) Debug.LogError("[AndroidBuild] 缺自适应图标的背景层或透明前景层");
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            var t = NamedBuildTarget.Android;
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(t))
            {
                var icons = PlayerSettings.GetPlatformIcons(t, kind);
                string kn = kind.ToString();
                foreach (var pi in icons)
                {
                    if (kn.Contains("Adaptive")) pi.SetTextures(adaptiveBg, clear);   // 第一层背景、第二层前景
                    else pi.SetTextures(kn.Contains("Round") ? round : icon);
                }
                PlayerSettings.SetPlatformIcons(t, kind, icons);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[AndroidBuild] 应用图标已设置：" + IconPath);
        }

        /// <summary>
        /// 游戏天空的远景剪影 SkyBackdrop.png 是三张遮罩分在 R/G/B 三个通道（6144×896）。安卓上原先被缩到 2048 宽再有损压缩，
        /// 通道互相串色、剪影又糊又带色块重影（10-29 用户：游戏里的天空“很奇怪”）。安卓上改为原尺寸、不压缩。
        /// </summary>
        static void FixSkyBackdropForAndroid()
        {
            const string p = "Assets/_Game/Art/Textures/SkyBackdrop.png";
            if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) return;
            var a = ti.GetPlatformTextureSettings("Android");
            if (a.overridden && a.format == TextureImporterFormat.RGB24 && a.maxTextureSize >= 8192) return;
            a.overridden = true; a.format = TextureImporterFormat.RGB24; a.maxTextureSize = 8192;
            ti.SetPlatformTextureSettings(a); ti.maxTextureSize = 8192; ti.SaveAndReimport();
            Debug.Log("[AndroidBuild] SkyBackdrop 安卓：原尺寸、不压缩");
        }

        /// <summary>
        /// 打包前清掉 Library 里上次导出的 Gradle 工程资源目录（unityLibrary/src/main/assets）。
        /// Unity 复用这个目录、不会删掉多余的旧数据分片，10-29 就是旧的 globalgamemanagers.assets.split9 被打进 APK，
        /// 头显启动时死循环（详见 GradleNoCompress.FixSplits，那里还会再校验一遍）。只删资源目录，不动 IL2CPP 编译缓存。
        /// </summary>
        static void CleanGradleAssets()
        {
            if (!Directory.Exists("Library")) return;
            foreach (var g in Directory.GetDirectories("Library", "Gradle", SearchOption.AllDirectories))
            {
                var a = Path.Combine(g, "unityLibrary", "src", "main", "assets");
                if (Directory.Exists(a)) { Directory.Delete(a, true); Debug.Log("[AndroidBuild] 清掉旧的 Gradle 资源目录 " + a); }
            }
        }

        /// <summary>
        /// 10-29 实机卡在启动画面（游戏代码一行都没跑到）的排查：能正常启动的同版本 Unity 应用，画质档都不单独挂渲染管线，
        /// 只用 GraphicsSettings 的默认管线；我们的 PICO 档单独挂了 Mobile_RPAsset，引擎启动早期加载画质档时要切管线，疑似和加载线程互等。
        /// 打包副本里改成：默认管线 = Mobile_RPAsset，所有画质档都不单独挂管线（画面参数完全一样，只是挂的位置不同）。
        /// </summary>
        static void UsePicoPipelineAsDefault()
        {
            var rp = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (!rp) { Debug.LogError("[AndroidBuild] 找不到 Mobile_RPAsset"); return; }
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = rp;
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            for (int i = 0; i < levels.arraySize; i++) levels.GetArrayElementAtIndex(i).FindPropertyRelative("customRenderPipeline").objectReferenceValue = null;
            qs.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[AndroidBuild] 默认管线改为 Mobile_RPAsset，画质档不再单独挂管线");
        }

        /// <summary>同步打包（脚本 / MCP 调用用）。</summary>
        public static string BuildNow() { DoBuild(); return File.ReadAllText(Report); }

        [MenuItem("Tools/TrickOrFreeze/Android 1 - Switch Platform")]
        public static void SwitchPlatform()
        {
            ApplySettings();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Debug.Log("[AndroidBuild] 平台已是 Android");
        }

        [MenuItem("Tools/TrickOrFreeze/Android 2 - Build APK")]
        public static void BuildApk()
        {
            File.WriteAllText(Report, "building\n");
            EditorApplication.delayCall += DoBuild;
        }

        /// <summary>同步打包（11-05：编辑器在后台不刷新时 delayCall 不触发，菜单打包会一直停在 building；由脚本直接调用这个）</summary>
        public static void BuildApkNow()
        {
            File.WriteAllText(Report, "building\n");
            DoBuild();
        }

        /// <summary>启动画面（11-17 公司署名）：仍用 Unity 自带的启动画面（位置、淡入淡出、深色背景都是同一套），关掉 Unity 标志，换成 Art/Brand/made_with_tripo.png
        /// （透明底：左边两行小字 MADE / WITH，右边官方 TRIPO 标志；成品图来自 MonsterCity，不在这里重做）。
        /// 头显（XR）里 Unity 不画启动画面的标志列表，只显示 Virtual Reality Splash Image 这一张贴图，所以两处都要填：
        /// 头显用 made_with_tripo_vr.png（不透明深色底、内容居中）。图不在时什么都不改。</summary>
        [MenuItem("Tools/TrickOrFreeze/Apply Splash (Made with Tripo)")]
        public static void ApplySplash()
        {
            const string path = "Assets/_Game/Art/Brand/made_with_tripo.png";
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) { Debug.LogWarning("[AndroidBuild] 没找到启动画面图 " + path + "，启动画面保持原样"); return; }
            if (ti.textureType != TextureImporterType.Sprite || ti.mipmapEnabled || ti.textureCompression != TextureImporterCompression.Uncompressed)
            { ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.mipmapEnabled = false; ti.textureCompression = TextureImporterCompression.Uncompressed; ti.alphaIsTransparency = true; ti.SaveAndReimport(); }
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sp) { Debug.LogWarning("[AndroidBuild] 启动画面图导入失败 " + path); return; }
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, sp) };
            const string vrPath = "Assets/_Game/Art/Brand/made_with_tripo_vr.png";
            var vi = AssetImporter.GetAtPath(vrPath) as TextureImporter;
            if (vi == null) { Debug.LogWarning("[AndroidBuild] 没找到头显启动图 " + vrPath); return; }
            if (vi.textureType != TextureImporterType.Default || vi.mipmapEnabled || vi.textureCompression != TextureImporterCompression.Uncompressed || vi.npotScale != TextureImporterNPOTScale.None)
            { vi.textureType = TextureImporterType.Default; vi.mipmapEnabled = false; vi.textureCompression = TextureImporterCompression.Uncompressed; vi.npotScale = TextureImporterNPOTScale.None; vi.wrapMode = TextureWrapMode.Clamp; vi.sRGBTexture = true; vi.SaveAndReimport(); }
            var vt = AssetDatabase.LoadAssetAtPath<Texture2D>(vrPath);
            var all = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (!vt || all == null || all.Length == 0) { Debug.LogWarning("[AndroidBuild] 头显启动图没设上"); return; }
            var so = new SerializedObject(all[0]); var prop = so.FindProperty("m_VirtualRealitySplashScreen");
            if (prop == null) { Debug.LogWarning("[AndroidBuild] 这个版本的 Unity 没有 m_VirtualRealitySplashScreen"); return; }
            if (prop.objectReferenceValue != vt) { prop.objectReferenceValue = vt; so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssets(); }
            Debug.Log("[AndroidBuild] 启动画面：MADE WITH TRIPO（普通标志列表 + 头显专用图都已填）");
        }

        static void DoBuild()
        {
            try
            {
                ApplySettings();
                CleanGradleAssets();
                UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
                Directory.CreateDirectory(OutDir);
                string apk = Path.GetFullPath(Path.Combine(OutDir, "TrickOrFreeze.apk"));
                var opts = new BuildPlayerOptions
                {
                    scenes = new[] { Scene },
                    locationPathName = apk,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = debuggable ? BuildOptions.Development : BuildOptions.None,   // 只改 EditorUserBuildSettings 不够，BuildPlayerOptions 会覆盖
                };
                var r = BuildPipeline.BuildPlayer(opts);
                var s = r.summary;
                var msg = $"result={s.result}\napk={apk}\nsize={s.totalSize}\nerrors={s.totalErrors}\nwarnings={s.totalWarnings}\ntime={s.totalTime}\n";
                foreach (var step in r.steps)
                    foreach (var m in step.messages)
                        if (m.type == LogType.Error || m.type == LogType.Exception) msg += "ERR " + m.content + "\n";
                File.WriteAllText(Report, msg);
            }
            catch (System.Exception e) { File.WriteAllText(Report, "result=Exception\n" + e); }
        }
    }
}
