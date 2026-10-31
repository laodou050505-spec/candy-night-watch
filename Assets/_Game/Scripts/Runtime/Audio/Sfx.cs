// 音效调度：接收 GameEvent，按事件播放音效。
//   素材音效（banks，来自 ange-embed 素材库，见 ArtSource/Audio/sfx/manifest.json）优先；没有素材的 id 用程序合成的兜底音。
//   每个 id 可以有多个变体：随机挑一个、音高随机 ±pitchJitter，同一个声音连听几遍也不腻。
//   有位置的事件（小鬼被定住、捏起、落桌、进锅……）用 3D 声源在事件发生的位置播放：小鬼在哪儿，声音就从哪儿来
//   （10-28 分析：桌子四周都会来小鬼，光靠眼睛顾不过来，耳朵要能听出方向）。界面、技能、旁白提示是 2D 的。
//   每种小鬼有自己的“叫声”：捏起、溜走、偷到糖、刚出现时都会叫一声，听声音就知道来的是谁、从哪边来。
//   同一个 id 0.06 秒内不重复触发（一帧里多个事件不会叠成爆音）。音量 × 设置里的“音效”。
using System.Collections.Generic;
using TrickOrFreeze.Core;
using UnityEngine;
using static TrickOrFreeze.AudioSys.Synth;

namespace TrickOrFreeze
{
    public class Sfx : MonoBehaviour
    {
        [System.Serializable]
        public class Bank
        {
            public string id;
            public AudioClip[] clips;
            public float volume = 1f;
            public float pitchJitter = 0.06f;
        }

        public static Sfx Instance { get; private set; }
        public List<Bank> banks = new List<Bank>();
        public int voices3D = 14;
        AudioSource src;
        AudioSource[] pool;
        int poolNext;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, Bank> bankMap = new Dictionary<string, Bank>();
        readonly Dictionary<string, float> lastPlay = new Dictionary<string, float>();
        // 10-31 高频事件用更长的节流间隔（Spawn / Wiggle 避免叠音）
        static readonly Dictionary<string, float> _longThrottle = new Dictionary<string, float>
        {
            { GameEventTypes.Spawn, 0.6f },
            { GameEventTypes.Wiggle, 0.25f },
        };
        GameLoop loop;

        void Awake()
        {
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 Sfx.Awake 开始");
            Instance = this;
            src = gameObject.AddComponent<AudioSource>();
            src.spatialBlend = 0; src.playOnAwake = false;
            foreach (var b in banks) if (b != null && b.clips != null && b.clips.Length > 0) bankMap[b.id] = b;
            // 3D 声源池：对数衰减（0.5 米内满音量、8 米外基本听不见），不做多普勒
            pool = new AudioSource[voices3D];
            for (int i = 0; i < voices3D; i++)
            {
                var go = new GameObject("Voice3D_" + i); go.transform.SetParent(transform, false);
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = 1.6f; a.maxDistance = 16f;   // 10-30 用户：声音不要太小。原来 0.5 米起就衰减，3 米外只剩 17%；现在 3 米外还有 53%
                 a.dopplerLevel = 0; a.spread = 40; a.priority = 64;
                pool[i] = a;
            }
            clips["freeze"] = Make("freeze", 0.4f, d => { Bell(d, 0, 1568, 0.35f, 0.4f); Bell(d, 0.03f, 2349, 0.4f, 0.25f); });
            clips["warn"] = Make("warn", 0.2f, d => { Noise(d, 0, 0.04f, 0.8f, 1400, 8); Noise(d, 0.09f, 0.04f, 0.6f, 1100, 8); });
            clips["slip"] = Make("slip", 0.2f, d => Bloop(d, 0, 700, 1400, 0.18f, 0.5f));
            clips["grab"] = Make("grab", 0.15f, d => { Bloop(d, 0, 260, 180, 0.12f, 0.7f); Noise(d, 0, 0.06f, 0.4f, 600); });
            clips["boxed"] = Make("boxed", 0.6f, d => { Bell(d, 0, 988, 0.5f, 0.5f); Bell(d, 0.06f, 1319, 0.5f, 0.4f); Bell(d, 0.12f, 1976, 0.5f, 0.35f); });
            clips["gulp"] = Make("gulp", 0.4f, d => { Bloop(d, 0, 180, 90, 0.25f, 0.6f); Bloop(d, 0.15f, 260, 120, 0.2f, 0.4f); Noise(d, 0.05f, 0.2f, 0.3f, 400, 1, 0.01f); });
            clips["pull"] = Make("pull", 0.6f, d => Bloop(d, 0, 300, 900, 0.6f, 0.3f));
            clips["whoosh"] = Make("whoosh", 0.3f, d => Noise(d, 0, 0.25f, 0.7f, 1500, 1.2f));
            clips["rattle"] = Make("rattle", 0.1f, d => { Noise(d, 0, 0.04f, 0.5f, 2900, 6); Bell(d, 0, 820, 0.06f, 0.2f); });
            clips["dizzy"] = Make("dizzy", 0.5f, d => { for (int i = 0; i < 3; i++) Bloop(d, i * 0.1f, 1200 - i * 200, 1400 - i * 200, 0.12f, 0.2f); });
            clips["pop"] = Make("pop", 0.15f, d => { Bloop(d, 0, 500, 1500, 0.12f, 0.5f); Noise(d, 0, 0.05f, 0.4f, 3000); });
            clips["tick"] = Make("tick", 0.05f, d => Bell(d, 0, 2600, 0.05f, 0.3f));
            clips["steal"] = Make("steal", 0.5f, d => { for (int i = 0; i < 4; i++) Bell(d, i * 0.07f, new[] { 1047f, 1319, 1568, 2093 }[i], 0.09f, 0.3f); });
            clips["giggle"] = Make("giggle", 0.4f, d => { for (int i = 0; i < 4; i++) Bloop(d, i * 0.09f, 900 + i * 60, 1300, 0.07f, 0.2f); });
            clips["lost"] = Make("lost", 0.8f, d => { Bloop(d, 0, 392, 330, 0.5f, 0.3f); Bloop(d, 0.2f, 311, 280, 0.6f, 0.25f); });
            clips["candy_back"] = Make("candy_back", 0.35f, d => { float[] fs = { 784, 988, 1175, 1568 }; for (int i = 0; i < 4; i++) Bell(d, i * 0.05f, fs[i], 0.25f, 0.4f); });
            // 糖落桌 / 弹跳的兜底（素材库 candy_drop 缺失时也有声）
            clips["candy_drop"] = Make("candy_drop", 0.12f, d => { Bell(d, 0, 2200, 0.08f, 0.3f); Noise(d, 0, 0.03f, 0.25f, 4200); });
            clips["pat"] = Make("pat", 0.18f, d => { Noise(d, 0, 0.1f, 0.8f, 250, 1, 0.01f, 1); Bloop(d, 0, 140, 90, 0.15f, 0.5f); });
            clips["pray"] = Make("pray", 2.6f, d => { Bell(d, 0, 392, 2.4f, 0.5f); Bell(d, 0, 587, 2.4f, 0.35f); Bell(d, 0, 784, 2.4f, 0.25f); for (int i = 0; i < 6; i++) Bell(d, 0.1f + i * 0.07f, 2093 + i * 260, 0.3f, 0.12f); });
            clips["sweep"] = Make("sweep", 0.8f, d => { Noise(d, 0, 0.7f, 0.9f, 900, 0.5f); Noise(d, 0.1f, 0.5f, 0.5f, 2400, 0.8f); });
            clips["lamp"] = Make("lamp", 1.3f, d => { Bell(d, 0, 880, 1.2f, 0.25f); Bell(d, 0.05f, 1320, 1.2f, 0.2f); Bell(d, 0.1f, 1760, 1.2f, 0.15f); });
            clips["shield"] = Make("shield", 1.5f, d => { Bloop(d, 0, 220, 330, 0.8f, 0.4f); Bell(d, 0.1f, 660, 1.4f, 0.2f); Bell(d, 0.16f, 990, 1.4f, 0.15f); });
            clips["charged"] = Make("charged", 0.7f, d => { Bloop(d, 0, 660, 1760, 0.35f, 0.3f); Bell(d, 0.3f, 2640, 0.4f, 0.15f); });
            clips["star"] = Make("star", 0.8f, d => { float[] fs = { 1568, 2093, 2637, 3136 }; for (int i = 0; i < 4; i++) Bell(d, 0.1f + i * 0.08f, fs[i], 0.6f, 0.3f); });
            clips["ready"] = Make("ready", 0.6f, d => { Bell(d, 0, 1319, 0.4f, 0.25f); Bell(d, 0.08f, 1976, 0.5f, 0.2f); });
            clips["thud"] = Make("thud", 0.35f, d => { Bloop(d, 0, 90, 50, 0.3f, 0.7f); Noise(d, 0, 0.15f, 0.6f, 300, 1); });
            clips["boss"] = Make("boss", 1.2f, d => { Bloop(d, 0, 196, 175, 0.7f, 0.5f); Bloop(d, 0.35f, 185, 165, 0.7f, 0.45f); Bloop(d, 0.7f, 175, 155, 0.7f, 0.4f); });
            clips["wave"] = Make("wave", 0.4f, d => { for (int i = 0; i < 6; i++) Noise(d, i * 0.06f, 0.05f, 0.45f, 3000, 4); });
            clips["dawn"] = Make("dawn", 2f, d => { float[] fs = { 523, 659, 784, 988, 1047, 1319 }; for (int i = 0; i < 6; i++) Bell(d, i * 0.18f, fs[i], 1.8f, 0.3f); });
            clips["fail"] = Make("fail", 1.4f, d => { float[] fs = { 392, 370, 349, 330 }; for (int i = 0; i < 4; i++) Bloop(d, i * 0.25f, fs[i], fs[i] - 20, 0.6f, 0.3f); });
            clips["say"] = Make("say", 0.6f, d => Bell(d, 0, 1320, 0.6f, 0.15f));
            clips["step"] = Make("step", 0.06f, d => { Noise(d, 0, 0.05f, 0.4f, 3400, 4); Bell(d, 0, 1800, 0.03f, 0.1f); });
            clips["stepLoud"] = Make("stepLoud", 0.06f, d => { Noise(d, 0, 0.05f, 0.7f, 3400, 4); Bell(d, 0, 1800, 0.03f, 0.15f); });
            clips["empty"] = Make("empty", 0.15f, d => Bloop(d, 0, 200, 180, 0.15f, 0.2f, 1, true));
            // 10-31 新增：SkillCooling（和 BoxFull 的 empty 音色区分开：下行三连音）
            clips["cooling"] = Make("cooling", 0.35f, d => { Bloop(d, 0, 600, 450, 0.12f, 0.25f); Bloop(d, 0.1f, 500, 380, 0.10f, 0.2f); Bloop(d, 0.2f, 400, 300, 0.08f, 0.2f); });
            // 10-31 解冻冰碎：高频噪声 + 短亮音
            clips["thaw"] = Make("thaw", 0.25f, d => { Noise(d, 0, 0.15f, 0.5f, 5000, 5); Bell(d, 0, 2093, 0.15f, 0.2f); });
            // 10-31 HopIn 小跳音（天亮后敌人跳回去）：短促高亮弹音
            clips["hopin"] = Make("hopin", 0.2f, d => { Bloop(d, 0, 800, 1200, 0.1f, 0.4f); Bell(d, 0.05f, 1760, 0.08f, 0.15f); });
            clips["btn"] = Make("btn", 0.1f, d => { Bell(d, 0, 1400, 0.08f, 0.35f); Noise(d, 0, 0.03f, 0.15f, 5000); });
            clips["back"] = Make("back", 0.12f, d => Bloop(d, 0, 880, 440, 0.1f, 0.25f));
            // 手伸进坩埚被烫：滋啦——高频嘶声 + 一点点爆泡（10-29）
            clips["sizzle"] = Make("sizzle", 0.75f, d => { Noise(d, 0, 0.75f, 0.42f, 6200, 1.2f, 0.01f, 1.6f, 11, 3800); Noise(d, 0.02f, 0.3f, 0.25f, 2400, 3f, 0.005f, 3f, 5); });
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 Sfx.Awake 完成");
        }

        /// <summary>手被坩埚烫到（HandView 调用，规则层坐标）</summary>
        public void Burn(Vector3 local) => PlayAt("sizzle", local, 0.9f);
        void OnEnable() { TrickOrFreeze.View.HandView.SceneFxBurn += Burn; }
        void OnDisable() { TrickOrFreeze.View.HandView.SceneFxBurn -= Burn; }

        /// <summary>界面音效（按钮悬停/点击/返回），由 UI 直接调用</summary>
        public void Ui(string id, float vol = 1f)
        {
            string bid = id == "btn" ? "ui_click" : id == "back" ? "ui_back" : id == "hover" ? "ui_hover" : id;
            Play(bankMap.ContainsKey(bid) ? bid : id, vol);
        }

        bool Throttle(string id)
        {
            float now = Time.unscaledTime;
            // 10-31 高频事件用各自配置的节流间隔，其余用默认 0.06s
            float interval = _longThrottle.TryGetValue(id, out var ov) ? ov : 0.06f;
            if (lastPlay.TryGetValue(id, out var t) && now - t < interval) return true;
            lastPlay[id] = now; return false;
        }

        AudioClip Pick(string id, out float vol, out float pitch)
        {
            vol = 1; pitch = 1;
            if (bankMap.TryGetValue(id, out var b))
            {
                vol = b.volume;
                pitch = 1 + Random.Range(-b.pitchJitter, b.pitchJitter);
                return b.clips[Random.Range(0, b.clips.Length)];
            }
            return clips.TryGetValue(id, out var c) ? c : null;
        }

        /// <summary>2D 播放（界面、技能、提示）</summary>
        void Play(string id, float vol = 1f)
        {
            if (Throttle(id)) return;
            var c = Pick(id, out var bv, out var _);
            if (c) src.PlayOneShot(c, vol * bv * TrickOrFreeze.UI.GameSettings.Sfx);
        }

        /// <summary>3D 播放：在规则层坐标 local 的位置发声</summary>
        void PlayAt(string id, Vector3 local, float vol = 1f)
        {
            if (Throttle(id)) return;
            var c = Pick(id, out var bv, out var pitch);
            if (!c) return;
            if (!loop) { src.PlayOneShot(c, vol * bv * TrickOrFreeze.UI.GameSettings.Sfx); return; }
            var a = pool[poolNext]; poolNext = (poolNext + 1) % pool.Length;
            a.transform.position = loop.W(local);
            a.pitch = pitch;
            a.clip = c; a.volume = vol * bv * TrickOrFreeze.UI.GameSettings.Sfx;
            a.Play();
        }

        // 每种小鬼的叫声
        static string VoiceOf(string kind)
        {
            switch (kind)
            {
                case KindIds.Skeleton: return "bone";
                case KindIds.Bat: return "voice_bat";
                case KindIds.Ghost: case KindIds.Ghostling: return "voice_ghost";
                case KindIds.Cat: return "voice_cat";
                case KindIds.Spider: return "voice_spider";
                case KindIds.Pumpkin: return "voice_pumpkin";
                case KindIds.Frank: return "voice_frank";
                default: return "giggle";
            }
        }

        void Voice(GameEvent e, Game game, float vol = 0.8f)
        {
            var t = game.toys.Find(x => x.id == e.id);
            if (t != null) PlayAt(VoiceOf(t.kind), t.pos + Vector3.up * t.Kind.height * 0.6f, vol);
        }

        public void Handle(GameEvent e, GameLoop g)
        {
            loop = g;
            var game = g.game;
            var P = e.pos;
            var pot = Layout.BOX.pos + Vector3.up * Layout.BOX.height;
            var jar = Layout.JAR.pos + Vector3.up * Layout.JAR.height;
            switch (e.type)
            {
                case GameEventTypes.GazeOn: { var t = game.toys.Find(x => x.id == e.id); if (t != null && t.residual <= 0) PlayAt("freeze", t.pos + Vector3.up * t.Kind.height * 0.5f, 0.8f); break; }
                case GameEventTypes.ThawWarn: PlayAt("warn", P, 0.7f); break;
                case GameEventTypes.Slip: PlayAt("slip", P); Voice(e, game, 0.6f); break;
                case GameEventTypes.BucketLift: case GameEventTypes.PotLift: PlayAt("grab", P, 0.7f); break;
                case GameEventTypes.BucketPut: PlayAt("candy_back", jar, 0.6f); break;
                case GameEventTypes.PotPut: PlayAt("thud", pot, 0.7f); break;
                case GameEventTypes.BlownAway: PlayAt("whoosh", P, 0.6f); break;
                case GameEventTypes.Grab: PlayAt("grab", P); Voice(e, game, 0.75f); break;
                case GameEventTypes.FrankGrip: PlayAt("grab", P); PlayAt("voice_frank", P, 0.8f); break;
                case GameEventTypes.CloseCall: Play("ready", 0.7f); break;
                case GameEventTypes.Boxed: PlayAt("boxed", pot); break;
                case GameEventTypes.Digested: PlayAt("gulp", pot); break;
                case GameEventTypes.BoxFull: PlayAt("empty", pot); break;
                case GameEventTypes.Pat: PlayAt("pat", pot); break;
                case GameEventTypes.PullStart: PlayAt("pull", P); break;
                case GameEventTypes.PullBreak: PlayAt("whoosh", P, 0.6f); break;
                case GameEventTypes.Land: PlayAt("thud", P, e.heavy ? 1f : 0.55f); break;
                case GameEventTypes.WallHit: PlayAt("thud", P, 0.8f); break;
                case GameEventTypes.Throw: PlayAt("whoosh", P); break;
                case GameEventTypes.Launch: PlayAt("whoosh", P, 1.1f); break;
                case GameEventTypes.Star: Play("star"); break;
                case GameEventTypes.Charged: PlayAt("charged", P); break;
                case GameEventTypes.ReachJar: PlayAt("giggle", jar, 0.8f); break;
                case GameEventTypes.Steal: PlayAt("steal", jar); Voice(e, game, 0.8f); break;
                case GameEventTypes.CandyDrop: PlayAt("candy_drop", P); break;
                case GameEventTypes.CandyBounce: case GameEventTypes.CandyLand: PlayAt("candy_drop", P, 0.4f); break;
                case GameEventTypes.GrabCandy: PlayAt("tick", P, 0.6f); break;
                case GameEventTypes.CandyBack: PlayAt("candy_back", jar); break;
                case GameEventTypes.CandyLost: Play("lost"); break;
                case GameEventTypes.PickCandy: PlayAt("giggle", P, 0.7f); break;
                case GameEventTypes.Shaken: PlayAt("dizzy", P); break;
                case GameEventTypes.Wiggle: { var t = game.toys.Find(x => x.id == e.id); if (t != null) PlayAt(t.kind == KindIds.Skeleton ? "bone" : VoiceOf(t.kind), t.pos, 0.45f); break; }
                case GameEventTypes.Spawn: { var t = game.toys.Find(x => x.id == e.id); if (t != null) PlayAt(VoiceOf(t.kind), t.pos, 0.9f); break; }   // 一出现就叫一声：听得出从哪边来
                case GameEventTypes.BossIn: Play("boss"); break;
                case GameEventTypes.BossGone: Play("star"); break;
                // 10-31 删除重复的 Say 音效（HudPresenter 里已经播放过了）
                case GameEventTypes.LessonOk: Play("ready"); break;
                case GameEventTypes.Wave: Play("wave"); break;
                case GameEventTypes.Split: PlayAt("pop", P); PlayAt("voice_ghost", P, 0.7f); break;
                case GameEventTypes.PumpkinRun: PlayAt("voice_pumpkin", P, 0.8f); break;
                case GameEventTypes.Revealed: PlayAt("voice_cat", P, 0.7f); break;
                case GameEventTypes.LampEnd: case GameEventTypes.ShieldEnd: Play("tick", 0.6f); break;
                case GameEventTypes.LampGrab: PlayAt("grab", P, 0.6f); break;
                case GameEventTypes.LampPut: PlayAt("lamp", P, 0.5f); break;
                case GameEventTypes.Skill:
                    switch (e.strId)
                    {
                        case "pray": Play("pray"); break;
                        case "sweep": Play("sweep"); break;
                        case "lamp": Play("lamp"); break;
                        case "shield": Play("shield"); break;
                    }
                    break;
                case GameEventTypes.SkillCooling: Play("cooling"); break;  // 10-31 换成下行三连音，区别于 BoxFull 的 empty
                case GameEventTypes.SkillReady: Play("ready"); break;       // 10-31 技能冷却结束：播放 ready 提示音
                case GameEventTypes.Thaw: if (e.hasPos) PlayAt("thaw", P, 0.8f); else Play("thaw", 0.6f); break;   // 10-31 解冻冰碎音
                case GameEventTypes.HopIn: if (e.hasPos) PlayAt("hopin", P, 0.5f); else Play("hopin", 0.5f); break; // 10-31 天亮后敌人跳回去
                // 天亮 / 失败的音乐由 MusicDirector 的短乐句负责，这里不再叠音效
            }
        }
    }
}
