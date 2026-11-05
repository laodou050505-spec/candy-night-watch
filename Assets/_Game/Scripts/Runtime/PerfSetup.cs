// 头显性能设置（只在安卓真机生效，编辑器里什么都不做）。
// 10-29 实机 PICO 性能统计：游戏中 GPU 每帧约 25ms（90Hz 需要 ≤11ms），只能跑 30 帧，系统插帧补齐——
// 离眼睛最近的糖桶、坩埚转头时重影、拖影最明显，让人头晕；CPU 只用 3~7ms，瓶颈完全在 GPU。
//   1) 刷新率固定 72Hz：稳定 72 帧比在 90/30 之间跳舒服得多（每帧预算 13.9ms）
//   2) 眼动注视点渲染（高）：看着的地方全分辨率，余光区降分辨率，省得最多又最难察觉；不支持时退回固定注视点
//   3) 关掉相机后期处理：场景里的后期体积本来就没有任何效果，开着只是多一次全屏解析和拷贝
//   4) 眼睛缓冲渲染比例 0.9（像素少约两成，有 4 倍 MSAA 兜底，清晰度损失很小）
// 11-05 实机复测（用户：右眼看着全是锯齿、很不舒服）：后来做过减负，GPU 每帧中位只有 4.3ms（72Hz 预算 13.9ms），余量很大。
//   锯齿主要来自“注视点渲染（高）”——视线中心以外降分辨率，物体边缘一圈台阶；右眼的注视中心偏得更多，所以右眼更明显。
//   改为：注视点渲染降到最低档（Low），眼睛缓冲比例提到 1.2（超采样，边缘更细）。
// 11-05 再次实机（用户：桌子左侧、锅那里像有一道明显的截面，眼睛一动它也跟着动，看着晕）：
//   这正是眼动注视点渲染高清区和降分辨率区的交界，最低档也还在。注视点渲染彻底关掉（眼动和固定两种都关）；
//   GPU 余量足够（关掉前中位 4.3ms / 预算 13.9ms），比例保持 1.2。
using UnityEngine;
using UnityEngine.Rendering.Universal;
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_PICO_XR_SDK
using ByteDance.PICO.XR;
#endif

namespace TrickOrFreeze
{
    public static class PerfSetup
    {
        public const float RefreshRate = 72f, EyeScale = 1.2f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            // 保险：场景里没有音频监听器就给主相机补一个（没有它整个游戏静音，10-29 实机就是这样）
            if (Object.FindAnyObjectByType<AudioListener>() == null && Camera.main) { Camera.main.gameObject.AddComponent<AudioListener>(); Debug.LogWarning("[TOF-Perf] 补了 AudioListener"); }
#if UNITY_ANDROID && !UNITY_EDITOR && ENABLE_PICO_XR_SDK
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                var cd = cam.GetComponent<UniversalAdditionalCameraData>();
                if (cd) cd.renderPostProcessing = false;
            }
            UnityEngine.XR.XRSettings.eyeTextureResolutionScale = EyeScale;
            int rr = PXR_Plugin.System.UPxr_SetSystemDisplayFrequency(RefreshRate);
            bool etfr = PXR_FoveationRendering.SetFoveationLevel(FoveationLevel.None, true);
            bool ffr = PXR_FoveationRendering.SetFoveationLevel(FoveationLevel.None, false);
            Debug.Log($"[TOF-Perf] refresh={RefreshRate}(r={rr}) eyeScale={EyeScale} foveation=关(etfr={etfr},ffr={ffr},now={PXR_FoveationRendering.GetFoveationLevel()}) post=off");
#endif
        }
    }
}
