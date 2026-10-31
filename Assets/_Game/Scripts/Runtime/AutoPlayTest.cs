// 编辑器自动试玩（只在编辑器里、且存在 /tmp/tof_autoplay.txt 时启用）：
// 进入 Play 后自动开指定的夜晚，按设定时间截图到 /tmp/auto_<秒>.png，并把规则层状态写进 /tmp/auto_log.txt。
// 配置文件每行 key=value：night=1、shots=2,6,12、gaze=1（自动看向最近的敌人，测试定身）、grab=1（自动捏住定住的敌人丢进坩埚）。
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze
{
    public class AutoPlayTest : MonoBehaviour
    {
        const string Cfg = "/tmp/tof_autoplay.txt";
        GameLoop loop;
        float t, started = -1;
        readonly List<float> shots = new List<float>();
        int night;
        readonly StringBuilder log = new StringBuilder();
        readonly Dictionary<string, int> evCount = new Dictionary<string, int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!File.Exists(Cfg)) return;
            new GameObject("__AutoPlayTest").AddComponent<AutoPlayTest>();
        }

        void Start()
        {
            foreach (var line in File.ReadAllLines(Cfg))
            {
                var kv = line.Split('='); if (kv.Length != 2) continue;
                if (kv[0] == "night") night = int.Parse(kv[1]);
                if (kv[0] == "shots") foreach (var s in kv[1].Split(',')) shots.Add(float.Parse(s));
            }
            loop = FindAnyObjectByType<GameLoop>();
            loop.OnEvent += e => { evCount.TryGetValue(e.type, out var n); evCount[e.type] = n + 1; };
            Shot("title");
        }

        void Update()
        {
            t += Time.deltaTime;
            if ((int)t != (int)(t - Time.deltaTime)) Write($"tick t={t:F1} frame={Time.frameCount}");
            if (started < 0 && t > 1.5f) { loop.StartNight(night); started = t; Write($"start night {night}"); }
            if (started < 0) return;
            float since = t - started;
            for (int i = shots.Count - 1; i >= 0; i--)
                if (since >= shots[i]) { Shot(((int)shots[i]).ToString()); shots.RemoveAt(i); }
        }

        void Shot(string tag)
        {
            var cam = Camera.main;
            var rt = new RenderTexture(1280, 960, 24) { antiAliasing = 4 };
            var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 960, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 960), 0, 0); tex.Apply();
            RenderTexture.active = null;
            File.WriteAllBytes($"/tmp/auto_{tag}.png", tex.EncodeToPNG());
            Destroy(tex); rt.Release();
            var g = loop.game;
            var sb = new StringBuilder($"[{tag}] t={t:F1} phase={g.phase} toys={g.toys.Count} candyJar={g.candyJar} clock={g.clock:F1}/{(g.night != null ? g.night.duration : 0)}");
            if (g.lesson != null) sb.Append($" lesson={g.lesson.step}/{g.lesson.steps.Count} done={g.lesson.done}");
            foreach (var toy in g.toys) sb.Append($"\n   toy {toy.kind} {toy.state} pos={toy.pos} res={toy.residual:F2}");
            sb.Append("\n   events: "); foreach (var kv in evCount) sb.Append($"{kv.Key}={kv.Value} ");
            Write(sb.ToString());
        }

        void Write(string s) { log.AppendLine(s); File.WriteAllText("/tmp/auto_log.txt", log.ToString()); }
    }
}
#endif
