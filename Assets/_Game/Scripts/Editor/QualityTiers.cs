// 画质档：只有两档，并且保证安卓（PICO）一定用 PICO 档。
//   PC   ：编辑器 / 桌面预览用（PC_RPAsset），排除 Android、iPhone
//   PICO ：头显用（Mobile_RPAsset）：4 倍 MSAA、关 HDR、单级软阴影只覆盖桌边 7 米、每物体最多 4 盏附加灯
// 10-28 切安卓平台时 PICO 档丢了，头显上跑的是 PC 档（MSAA 关、4 级阴影、HDR）→ 满屏锯齿闪烁、掉帧抖动。
// 所以打包前（AndroidBuild.ApplySettings）一定先调 Ensure()。
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace TrickOrFreeze.EditorTools
{
    public static class QualityTiers
    {
        const string PcRp = "Assets/Settings/PC_RPAsset.asset";
        const string PicoRp = "Assets/Settings/Mobile_RPAsset.asset";

        [MenuItem("Tools/TrickOrFreeze/Setup Quality Tiers")]
        public static void Ensure()
        {
            var qs = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
            var levels = qs.FindProperty("m_QualitySettings");
            int pc = Find(levels, "PC"), pico = Find(levels, "PICO");
            if (pc < 0 && pico < 0) { Debug.LogError("[QualityTiers] PC、PICO 两档都没有"); return; }
            if (pc < 0)
            {
                // 10-29：工程里只剩 PICO 一档（PC 档丢了），之前直接 return，PICO 档的调整整个被跳过。按 PICO 档复制出 PC 档放在最前面
                levels.InsertArrayElementAtIndex(pico);
                levels.MoveArrayElement(pico, 0);
                pc = 0; pico = Find(levels, "PICO");
                if (pico == pc) pico = 1;
                levels.GetArrayElementAtIndex(pc).FindPropertyRelative("name").stringValue = "PC";
                Debug.LogWarning("[QualityTiers] 补回 PC 档");
            }
            if (pico < 0)
            {
                levels.InsertArrayElementAtIndex(pc);       // 复制一份 PC 档，挪到最后改成 PICO
                levels.MoveArrayElement(pc + 1, levels.arraySize - 1);
                pico = levels.arraySize - 1;
            }
            var P = levels.GetArrayElementAtIndex(pico);
            P.FindPropertyRelative("name").stringValue = "PICO";
            P.FindPropertyRelative("customRenderPipeline").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PicoRp);
            P.FindPropertyRelative("antiAliasing").intValue = 4;
            P.FindPropertyRelative("anisotropicTextures").intValue = 2;          // 斜看桌面、地砖不糊、不闪
            P.FindPropertyRelative("lodBias").floatValue = 0.5f;              // 10-29：更早换低模——远处小物件的三角形比像素还细，是锯齿闪烁的来源，也省 GPU
            P.FindPropertyRelative("meshLodThreshold").floatValue = 5f;
            P.FindPropertyRelative("enableLODCrossFade").boolValue = false;      // 交叉淡化是抖动镂空，头显里会闪
            P.FindPropertyRelative("vSyncCount").intValue = 0;
            P.FindPropertyRelative("softParticles").boolValue = false;
            P.FindPropertyRelative("realtimeReflectionProbes").boolValue = false;
            SetExcluded(P, "Standalone");
            var C = levels.GetArrayElementAtIndex(pc);
            C.FindPropertyRelative("customRenderPipeline").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PcRp);
            SetExcluded(C, "Android", "iPhone");
            // 每个平台的默认档
            var per = qs.FindProperty("m_PerPlatformDefaultQuality");
            per.arraySize = 2;
            per.GetArrayElementAtIndex(0).FindPropertyRelative("first").stringValue = "Android";
            per.GetArrayElementAtIndex(0).FindPropertyRelative("second").intValue = pico;
            per.GetArrayElementAtIndex(1).FindPropertyRelative("first").stringValue = "Standalone";
            per.GetArrayElementAtIndex(1).FindPropertyRelative("second").intValue = pc;
            qs.ApplyModifiedPropertiesWithoutUndo();
            TunePicoPipeline();
            AssetDatabase.SaveAssets();
            Debug.Log($"[QualityTiers] PC={pc} PICO={pico}，安卓默认 PICO");
        }

        static int Find(SerializedProperty levels, string name)
        {
            for (int i = 0; i < levels.arraySize; i++)
                if (levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue == name) return i;
            return -1;
        }

        static void SetExcluded(SerializedProperty level, params string[] platforms)
        {
            var ex = level.FindPropertyRelative("excludedTargetPlatforms");
            ex.arraySize = platforms.Length;
            for (int i = 0; i < platforms.Length; i++) ex.GetArrayElementAtIndex(i).stringValue = platforms[i];
        }

        // PICO 渲染管线：清晰优先（MSAA 4），省掉移动端用不起的东西
        static void TunePicoPipeline()
        {
            var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PicoRp);
            if (!a) return;
            a.msaaSampleCount = 4;
            a.renderScale = 1f;
            a.supportsHDR = false;                    // 主相机不做后处理，HDR 只是白白多一倍带宽
            a.shadowDistance = 7f;                    // 阴影只覆盖桌边（远处的阴影像素粗、会闪，也不需要）
            a.shadowCascadeCount = 1;
            a.mainLightShadowmapResolution = 2048;    // 7 米 / 2048 ≈ 3.4 毫米一个阴影像素，边缘不出锯齿
            a.maxAdditionalLightsCount = 2;   // 10-29 实机 GPU 超预算一倍多：每个物体最多受 2 盏点光（桌上蜡烛 + 最近的一盏）
            a.supportsCameraDepthTexture = false;
            a.supportsCameraOpaqueTexture = false;
            // 只读属性走序列化字段：软阴影开（边缘柔和不锯齿），附加灯不投影
            var so = new SerializedObject(a);
            var soft = so.FindProperty("m_SoftShadowsSupported"); if (soft != null) soft.boolValue = true;
            var addSh = so.FindProperty("m_AdditionalLightShadowsSupported"); if (addSh != null) addSh.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(a);
            // 11-05 用户：封面桌子左侧、锅那里有一道竖直的“截面”，眼睛一动它也跟着动。
            //   原因是 Forward+：屏幕切成格子分灯，蜡烛点光一多，有的格子漏一盏灯，格子边缘就是一道硬分界，跟着视角走。
            //   PICO 渲染器固定用普通 Forward（每个物体按距离挑灯，没有屏幕格子分界）。
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/Mobile_Renderer.asset");
            if (rd && rd.renderingMode != RenderingMode.Forward) { rd.renderingMode = RenderingMode.Forward; EditorUtility.SetDirty(rd); }
        }
    }
}
