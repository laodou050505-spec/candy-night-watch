// 真机诊断（只在安卓真机上跑）：每 5 秒把帧率、画质、手部追踪、手套显示状态写进
//   /sdcard/Android/data/<包名>/files/diag.log  （adb pull 拉回来看；不需要接着 logcat）
// 同时打一行 [TOF-Diag] 到 logcat。开销：每 5 秒一次字符串拼接，可以忽略。
using System.IO;
using System.Text;
using TrickOrFreeze.View;
using UnityEngine;
using UnityEngine.XR;

namespace TrickOrFreeze.Diag
{
    public class DeviceDiag : MonoBehaviour
    {
        const float Period = 5f;
        float t, worst, sum; int frames, slow, gazeEye, gazeHead;
        string path;
        GameLoop loop;
        HandView[] hands;

        // 启动分阶段记录（10-29 实机卡在启动画面）：logcat 里搜 [TOF-Boot]，最后一条就是卡住之前走到的地方
        // 10-31 只在 Debug Build 里输出，发行包不打启动日志
        public static void Stage(string s) { if (Debug.isDebugBuild) Debug.Log($"[TOF-Boot] {Time.realtimeSinceStartup:F2}s {s}"); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void S0() => Stage("1 子系统注册");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)] static void S1() => Stage("2 程序集已加载");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)] static void S2() => Stage("3 启动画面前");
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] static void S3() => Stage("4 场景加载前");
        bool firstFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Stage("5 场景已加载");
            if (Application.isEditor || Application.platform != RuntimePlatform.Android) return;
            var go = new GameObject("__DeviceDiag");
            DontDestroyOnLoad(go);
            go.AddComponent<DeviceDiag>();
        }

        void Start()
        {
            path = Path.Combine(Application.persistentDataPath, "diag.log");
            File.WriteAllText(path, $"=== start {System.DateTime.Now:MM-dd HH:mm:ss} quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} " +
                $"rp={(QualitySettings.renderPipeline ? QualitySettings.renderPipeline.name : "default")} msaa={QualitySettings.antiAliasing} " +
                $"eyeTex={XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} renderScale={XRSettings.eyeTextureResolutionScale} refresh={XRDevice.refreshRate}\n");
        }

        void Update()
        {
            if (!firstFrame) { firstFrame = true; Stage("7 第一帧 Update"); }
            float dt = Time.unscaledDeltaTime;
            t += dt; sum += dt; frames++;
            if (dt > worst) worst = dt;
            float target = XRDevice.refreshRate > 1 ? 1f / XRDevice.refreshRate : 1f / 72f;
            if (dt > target * 1.5f) slow++;
            if (!loop) loop = FindAnyObjectByType<GameLoop>();
            var gz = loop != null && loop.CurrentInput != null ? loop.CurrentInput.gaze : null;
            if (gz != null && gz.valid) { if (gz.source == TrickOrFreeze.Core.GazeSources.Eye) gazeEye++; else gazeHead++; }
            if (t < Period) return;
            if (!loop) loop = FindAnyObjectByType<GameLoop>();
            if (hands == null || hands.Length == 0) hands = FindObjectsByType<HandView>(FindObjectsInactive.Include);
            var sb = new StringBuilder();
            sb.Append($"{Time.realtimeSinceStartup:F0}s fps={frames / sum:F1} worst={worst * 1000:F0}ms slow={slow}/{frames} refresh={XRDevice.refreshRate:F0}");
            sb.Append($" gazeEye={(gazeEye + gazeHead > 0 ? 100f * gazeEye / (gazeEye + gazeHead) : 0):F0}%");
            gazeEye = gazeHead = 0;
            if (loop != null && loop.game != null) sb.Append($" phase={loop.game.phase} night={loop.game.nightIndex + 1} toys={loop.game.toys.Count}");
            var input = loop != null ? loop.CurrentInput : null;
            if (input != null && input.hands != null)
                foreach (var h in input.hands)
                    sb.Append($" | in.{h.side}: tracked={h.tracked} pinch={h.pinching} joints={(h.joints != null ? h.joints.Length : 0)} pt={(h.hasPoint ? h.point.ToString("F2") : "-")}");
            if (input != null && input.head != null) sb.Append($" | head={input.head.pos:F2}");
            if (hands != null)
                foreach (var hv in hands)
                    if (hv && hv.loop) sb.Append($" | glove.{(hv.right ? "R" : "L")}: tracked={hv.DiagTracked} visible={hv.DiagVisible} wrist={hv.DiagWrist:F2}");
            var line = sb.ToString();
            Debug.Log("[TOF-Diag] " + line);
            try { File.AppendAllText(path, line + "\n"); } catch { }
            t = sum = worst = 0; frames = slow = 0;
        }
    }
}
