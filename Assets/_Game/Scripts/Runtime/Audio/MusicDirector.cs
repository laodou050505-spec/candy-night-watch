// 背景音乐总控（取代原来程序合成的八音盒循环 MusicGen）：
//   封面   cover：《Witch's Dancing Doll》八音盒圆舞曲
//   夜里   night：木琴 / 马林巴的俏皮夜行曲（平时）
//          tense：同一路子、更紧张的版本——场上小鬼多、波次、缝合熊来了时平滑换过去，安静下来再换回（有防抖，不会来回跳）
//   短乐句 stinger：开夜（八音盒小调动机）、开夜介绍新本事（两声铃）、天亮（钢片琴琶音）、失败（下行 + 哇哇）
// 每段循环都用两个 AudioSource 首尾交叠（按 dspTime 精确排期），曲子开头结尾自带的淡入淡出正好接上，听不出循环点。
// 暂停时音乐压低一半；短乐句响起时主音乐让一让（ducking）。音量 × 设置里的“音乐”。
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.AudioSys
{
    public class MusicDirector : MonoBehaviour
    {
        public GameLoop loop;
        public AudioClip cover, night, tense;
        public AudioClip stingerNight, stingerSkill, stingerDawn, stingerFail;
        public float coverVol = 0.75f, nightVol = 0.7f, tenseVol = 0.85f, stingerVol = 1f;
        public float overlapCover = 1.2f, overlapNight = 2.0f, overlapTense = 2.0f;

        class Layer
        {
            public AudioClip clip; public AudioSource a, b; public bool useA = true;
            public double next; public float vol, target, overlap; public bool started;
        }
        Layer lCover, lNight, lTense;
        AudioSource stinger;
        float duck = 1, duckT, tenseK, calmT, pauseK = 1;
        bool tenseOn;
        // 10-31 紧张模式至少保持 8 秒，防止小鬼稍减就立刻切回平时音乐
        float tenseEnteredT = -999f;

        void Start()
        {
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 MusicDirector.Start 开始");
            lCover = Make("Cover", cover, overlapCover);
            lNight = Make("Night", night, overlapNight);
            lTense = Make("Tense", tense, overlapTense);
            stinger = gameObject.AddComponent<AudioSource>(); stinger.playOnAwake = false; stinger.spatialBlend = 0; stinger.priority = 16;
            if (loop) loop.OnEvent += OnEvent;
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 MusicDirector.Start 完成");
        }

        void OnDestroy() { if (loop) loop.OnEvent -= OnEvent; }

        Layer Make(string n, AudioClip c, float overlap)
        {
            var L = new Layer { clip = c, overlap = overlap };
            L.a = Src(n + "A"); L.b = Src(n + "B");
            return L;
        }

        AudioSource Src(string n)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.playOnAwake = false; s.loop = false; s.spatialBlend = 0; s.priority = 8; s.volume = 0;
            return s;
        }

        void OnEvent(GameEvent e)
        {
            switch (e.type)
            {
                case GameEventTypes.NightStart: Sting(stingerNight, 0.75f); tenseOn = false; tenseK = 0; break;
                case GameEventTypes.BriefStep: Sting(stingerSkill, 0.55f); break;
                case GameEventTypes.Dawn: Sting(stingerDawn, 1f, 3.5f); break;
                case GameEventTypes.Fail: Sting(stingerFail, 1f, 3.5f); break;
                case GameEventTypes.Wave: case GameEventTypes.BossIn: tenseOn = true; calmT = 0; break;
            }
        }

        void Sting(AudioClip c, float vol, float duckSec = 2.2f)
        {
            if (!c) return;
            stinger.PlayOneShot(c, vol * stingerVol * TrickOrFreeze.UI.GameSettings.Music);
            duckT = Mathf.Max(duckT, duckSec);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var g = loop ? loop.game : null;
            string phase = g != null ? g.phase : Phases.Title;
            // 紧张程度：同时在场（没被定住、没被收走）的小鬼数、有没有抱着糖跑的、缝合熊
            if (g != null && phase == Phases.Playing)
            {
                // 10-31 统计"有效在场敌人"：排除冻结中 / 残留眩晕 / 被抓着 / 坩埚里的敌人
                int act = 0; bool thief = false, boss = false;
                var jar = Layout.JAR.pos;
                foreach (var t in g.toys)
                {
                    if (t.state == ToyStates.Gone || t.state == ToyStates.Boxed || t.state == ToyStates.Launched) continue;
                    if (t.frozen) continue;                                        // 10-31 被定住的不算
                    if (t.state == ToyStates.Stunned && t.residual > 0) continue; // 10-31 残留眩晕中不算
                    if (t.state == ToyStates.Held) continue;                       // 10-31 被抓着的不算
                    act++;
                    // 10-31 拿着糖跑：只有正在逃跑（Flee）且距糖桶超过 0.6m 才算紧张
                    if (t.candyCount > 0 && t.state == ToyStates.Flee && Vector3.Distance(t.pos, jar) > 0.6f)
                        thief = true;
                    if (t.kind == KindIds.Frank) boss = true;
                }
                if (act >= 3 || boss || thief)
                {
                    if (!tenseOn) tenseEnteredT = Time.unscaledTime; // 10-31 记录进入紧张的时刻
                    tenseOn = true; calmT = 0;
                }
                else if (act <= 2)
                {
                    calmT += dt;
                    // 10-31 至少保持 8 秒紧张，超过后才平静
                    if (calmT > 5f && Time.unscaledTime - tenseEnteredT >= 8f) tenseOn = false;
                }
            }
            else tenseOn = false;
            tenseK = Mathf.MoveTowards(tenseK, tenseOn ? 1 : 0, dt / (tenseOn ? 1.5f : 3f));
            float e = Mathf.SmoothStep(0, 1, tenseK);
            bool title = phase == Phases.Title, play = phase == Phases.Playing;
            lCover.target = title ? coverVol : 0;
            lNight.target = play ? nightVol * (1 - e) : 0;
            lTense.target = play ? tenseVol * e : 0;
            // 让一让：短乐句期间主音乐压到 35%；暂停压到 50%
            duckT = Mathf.Max(0, duckT - dt);
            duck = Mathf.MoveTowards(duck, duckT > 0 ? 0.35f : 1f, dt * (duckT > 0 ? 4f : 0.8f));
            pauseK = Mathf.MoveTowards(pauseK, loop && loop.paused ? 0.5f : 1f, dt * 2f);
            float master = TrickOrFreeze.UI.GameSettings.Music * duck * pauseK;
            Tick(lCover, dt, master, 1.2f);
            Tick(lNight, dt, master, 1.5f);
            Tick(lTense, dt, master, 1.5f);
        }

        void Tick(Layer L, float dt, float master, float fadeSec)
        {
            if (!L.clip) return;
            L.vol = Mathf.MoveTowards(L.vol, L.target, dt / fadeSec);
            bool audible = L.vol > 0.001f;
            double now = AudioSettings.dspTime;
            if (audible && !L.started)
            {
                // 从头开始：立刻播第一遍，并排好第二遍
                L.started = true; L.useA = true;
                L.a.clip = L.clip; L.a.PlayScheduled(now + 0.05);
                L.next = now + 0.05 + L.clip.length - L.overlap;
            }
            if (!audible && L.started && L.target <= 0)
            {
                // 10-31 淡出后静音但不 Stop：保持播放位置，恢复时从当前位置继续
                // （换夜 / 回封面时不改，音乐不会因为一时安静就从头来）
                L.a.mute = true; L.b.mute = true;
                // L.started 保持 true，不重置播放位置
            }
            else if (audible && L.started)
            {
                // 10-31 恢复播放：取消静音
                L.a.mute = false; L.b.mute = false;
            }
            if (L.started && now > L.next - 1.0)
            {
                var s = L.useA ? L.b : L.a; L.useA = !L.useA;
                s.clip = L.clip; s.PlayScheduled(L.next);
                L.next += L.clip.length - L.overlap;
            }
            L.a.volume = L.b.volume = L.vol * master;
        }
    }
}
