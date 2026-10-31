// 《万圣守糖夜》Candy Night Watch（原名《一二三·木头鬼》）核心规则（对应原型 src/logic/game.js）。纯逻辑，不依赖渲染。
// 每帧 Update(dt, input) 推进状态，并通过返回的事件列表把需要表现层处理的事（音效、特效、字幕）送出去。
//
// 眼与手的分工：
//   眼：定身（直视立即定住，移开后残留）、隔空取物时锁定目标；南瓜头反过来要用余光
//   手：捏起拖到任何地方、扔远、晃掉糖、捡糖放回糖桶、放进坩埚、拍锅沿；缝合熊要双手
//   手势技能（有冷却）：合十冻结减速 / 外挥吹远 / 托灯照亮 / 下按护桶
//
// 本类按职责拆成多个 partial 文件：
//   Game.cs           字段、构造、事件、一晚的开始与结束、主循环
//   Game.Paths.cs     出生点与寻路
//   Game.Gaze.cs      光、灯、注视定身
//   Game.Hands.cs     手部历史、捏合/松手、拿着、晃、蓄力、隔空取物
//   Game.Cauldron.cs  坩埚与手势技能
//   Game.Toys.cs      敌人行为状态机与散落的糖
//   Game.Director.cs  导演与边玩边学
using System;
using System.Collections.Generic;
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        // 自己会走动、可被定身的状态
        static readonly HashSet<string> ACTIVE = new HashSet<string> { ToyStates.Walk, ToyStates.Steal, ToyStates.Flee, ToyStates.Stunned };
        // 可被注视选中的状态
        static readonly HashSet<string> GAZEABLE = new HashSet<string> { ToyStates.Walk, ToyStates.Steal, ToyStates.Flee, ToyStates.Stunned, ToyStates.Falling, ToyStates.Pulled, ToyStates.Glide };

        // 教学步骤对应的手势提示动画（表现层负责画）
        static readonly Dictionary<string, string> LESSON_HINT = new Dictionary<string, string>
        {
            { "look", "look" }, { "residual", "look" }, { "grab", "pinch" }, { "cauldron", "box" }, { "shake", "shake" }, { "candy", "jar" },
            { "throw", "throw" }, { "pull", "pull" }, { "launch", "launch" }, { "pray", "pray" }, { "sweep", "sweep" }, { "shield", "shield" }, { "lamp", "lamp" },
            { "pat", "box" }, { "bucket", "none" }, { "pot", "none" },
        };

        public readonly GameConfig cfg;
        public readonly Func<float> rand;
        public readonly bool residualEnabled;
        public readonly bool requireFreeze;

        public string phase = Phases.Title;
        List<GameEvent> events = new List<GameEvent>();
        List<GameEvent> pending = new List<GameEvent>(); // Update 之外（调试键、外部调用）发出的事件，下一帧一起送出
        readonly List<GameEvent>[] evPool = { new List<GameEvent>(64), new List<GameEvent>(64), new List<GameEvent>(64) };
        int evPoolI;
        bool inUpdate;

        public List<Toy> toys = new List<Toy>();
        public List<Candy> candies = new List<Candy>(); // 掉在外面的糖（不在糖桶里、也不在敌人手里）
        public List<Lamp> lamps = new List<Lamp>();     // 托灯技能留下的灯
        public int nightIndex;
        public NightDef night;
        readonly Dictionary<string, List<(double t, Vector3 p)>> handHist = new Dictionary<string, List<(double, Vector3)>>
        {
            { Sides.Left, new List<(double, Vector3)>() }, { Sides.Right, new List<(double, Vector3)>() },
        };
        HeldItem heldLeft, heldRight;
        // 时间轴用 double：JS 全程 double，时间戳的累加误差会改变“宽限期 / 窗口”边界上的帧数判定
        public double time;

        public double clock;          // 已过去的夜晚时间（月亮进度）
        public bool clockRunning;
        public int candyJar;
        public int candyLost;
        public const int MinCandiesToWin = 3;
        public CauldronState cauldron = new CauldronState();
        public Dictionary<string, SkillState> skills = new Dictionary<string, SkillState>();
        public double freezeAllT;      // 合十：全场冻结剩余时间
        public double slowT;          // 合十：冻结后的减速剩余时间
        public double shieldT;        // 护桶：糖桶被月光罩住的剩余时间
        public bool dark;             // 熄灯：只有光里的敌人看得见/定得住
        public GameStats stats = new GameStats();
        public DirectorState director;
        public List<PendingSay> pendingSays = new List<PendingSay>();
        public LessonState lesson;    // null = 本夜没有教学
        public BriefState brief;      // null = 本夜没有开夜介绍
        /// <summary>是否带新手教学（第一夜）。玩家完成或跳过过一次后由表现层设为 false，以后不再出现。</summary>
        public bool tutorial = true;
        public bool practice;          // 正在玩封面的“玩法”练习
        // 10-29 用户：糖桶可以提起来看、坩埚可以拿起来挪位置
        public string bucketHeld;      // 提着糖桶的手（null = 没提）
        public Vector3 bucketPos;      // 提着时糖桶底部的位置（表现层跟着它）；没提时就是 JAR.pos
        public string potHeld;         // 提着坩埚的手（捏住一只锅耳）
        public Vector3 potCarry;       // 提着时坩埚底部中心的位置（表现层跟着它）
        int potEar;                    // 捏的是哪只耳（-1 / +1）
        public int PotEar => potEar;
        bool potDragging;
        Dictionary<string, bool> hints = new Dictionary<string, bool>();
        public Toy gazeTarget;
        public Candy gazeCandy;
        public NightResult result;
        /// <summary>场景占用网格（防穿模，Editor/SolidGridBaker 烘焙，GameLoop 加载）；null = 不检查（测试、桌面调试）</summary>
        public SolidGrid solid;
        double idleT;

        // JS 的 nextId 是模块级全局；这里每局游戏各自计数（id 只用于同一局内识别实体）
        int nextId = 1;

        public Game(GameOptions opts = null)
        {
            opts = opts ?? new GameOptions();
            // 糖桶、坩埚回到默认位置（每次启动游戏一次；之后玩家挪到哪就在哪）
            JAR.Reset(); BOX.Reset();
            cfg = opts.config ?? GameConfig.CreateDefault();
            var sys = new System.Random();
            rand = opts.rand ?? (() => (float)sys.NextDouble());
            residualEnabled = opts.residual;
            requireFreeze = opts.requireFreeze ?? cfg.grab.requireFreeze;
        }

        // ---------- 事件 ----------
        GameEvent Emit(string type)
        {
            var e = new GameEvent(type);
            (inUpdate ? events : pending).Add(e);
            return e;
        }

        // JS: say(text, dur)。这里同时带本地化键与参数；text 是代入参数后的中文默认文本
        void Say(string key, string text, float dur = 3.2f, Dictionary<string, string> args = null)
        {
            var e = Emit(GameEventTypes.Say);
            e.textKey = key;
            e.text = text;
            // 显示时长至少够读完：约每秒 5 个字（头显里读字比平面慢），最长 12 秒（10-29 用户：南瓜头的说明还没看清就没了）
            int n = string.IsNullOrEmpty(text) ? 0 : text.Length;
            e.dur = Mathf.Clamp(Mathf.Max(dur, 2.5f + n * 0.2f), dur, 12f);
            e.args = args;
        }

        // floor(rand() * n)，夹到 [0, n-1]：float 精度下 rand() 可能被舍入成 1.0，JS 的 double 不会
        int RandIndex(int n) => Mathf.Min(n - 1, (int)Mathf.Floor(rand() * n));

        HeldItem GetHeld(string side) => side == Sides.Left ? heldLeft : heldRight;
        void SetHeld(string side, HeldItem h) { if (side == Sides.Left) heldLeft = h; else heldRight = h; }
        public HeldItem HeldLeft => heldLeft;
        public HeldItem HeldRight => heldRight;

        // ---------- 一晚的开始与结束 ----------
        /// <summary>封面“玩法”练习：用第一夜的桌面，跑 PRACTICE 的教学步骤；做完发 PracticeDone，回到封面</summary>
        public void StartPractice(int unlockedNight = 1)
        {
            StartNight(0, NightDefs.BuildPractice(unlockedNight));
        }

        public void StartNight(int index) => StartNight(index, null);

        void StartNight(int index, NightDef overrideDef)
        {
            var nd = overrideDef ?? NightDefs.NIGHTS[index];
            practice = overrideDef != null;
            nightIndex = index;
            night = nd;
            phase = Phases.Playing;
            toys = new List<Toy>();
            candies = new List<Candy>();
            lamps = new List<Lamp>();
            heldLeft = heldRight = null;
            frankLetGo.Clear(); bucketHeld = null; potHeld = null; potDragging = false; bucketPos = JAR.pos; potCarry = BOX.pos;   // 糖桶、坩埚的位置跨夜保留（放在哪就在哪）
            clock = 0;
            bool withLesson = nd.HasLesson && (tutorial || practice);
            bool withBrief = !withLesson && nd.HasBrief;
            clockRunning = !withLesson && !withBrief;
            candyJar = cfg.candy.total;
            candyLost = 0;
            // 坩埚：queue 按放入顺序消化；used = 已占格数；progress = 当前这一只的消化进度（秒）
            cauldron = new CauldronState();
            skills = new Dictionary<string, SkillState>();
            foreach (var id in nd.skills) skills[id] = new SkillState();
            freezeAllT = 0;
            slowT = 0;
            shieldT = 0;
            dark = nd.dark;
            CANDLE.on = System.Array.IndexOf(nd.skills, SkillIds.Lamp) < 0;
            stats = new GameStats();
            director = new DirectorState
            {
                t = withLesson || withBrief ? double.PositiveInfinity : 2.5,
                burstT = nd.burst != null ? nd.burst.every : double.PositiveInfinity,
                bossAt = nd.boss != null ? nd.boss.at : double.PositiveInfinity,
            };
            pendingSays = new List<PendingSay>();
            // 边玩边学：一步一步来，做对了才进下一步；教学期间不计时、敌人不偷糖
            lesson = withLesson ? new LessonState { steps = nd.lesson } : null;
            // 开夜介绍：报完夜名后，按顺序把本夜新本事各说一句（配示范手），说完才开始计时出怪
            brief = withBrief ? new BriefState { steps = nd.brief } : null;
            hints = new Dictionary<string, bool>();
            gazeTarget = null;
            gazeCandy = null;
            var ev = Emit(GameEventTypes.NightStart);
            ev.night = nd.id;
            ev.name = nd.name;
            ev.textKey = nd.nameKey;
            Say(nd.nameKey, nd.name, 3);
            if (!string.IsNullOrEmpty(nd.intro)) Later(3.2f, nd.introKey, nd.intro, 4.5f);
            foreach (var s in nd.tips) Later(s.t, s.textKey, s.text, s.dur > 0 ? s.dur : 4);
        }

        void Later(double t, string key, string text, float dur = 4) => pendingSays.Add(new PendingSay { t = t, key = key, text = text, dur = dur });

        public Toy SpawnToy(SpawnPoint spawn, SpawnOpts opts = null)
        {
            opts = opts ?? new SpawnOpts();
            string kind = opts.kind ?? KindIds.Skeleton;
            var k = EnemyKinds.Get(kind);
            // 垂降类只能从天花板出生；传进来的是地面/空中出生点时换成最近的天花板点
            if (k.mover == Movers.Drop && Array.IndexOf(DROP_SPAWNS, spawn) < 0)
            {
                var best = DROP_SPAWNS[0];
                for (int i = 1; i < DROP_SPAWNS.Length; i++)
                    if (!(Vec.DistXZ(best.pos, spawn.pos) <= Vec.DistXZ(DROP_SPAWNS[i].pos, spawn.pos))) best = DROP_SPAWNS[i];
                spawn = best;
            }
            var toy = new Toy
            {
                id = nextId++,
                kind = kind,
                spawn = spawn,
                state = ToyStates.Walk,
                pos = spawn.pos,
                goal = Goal.ToJar(),
                speedMul = opts.speedMul ?? 1f,
                walkPhase = rand() * 6,
                vel = Vector3.zero,
                lit = true,
                hp = k.hp > 0 ? k.hp : 1,
                splitsLeft = k.splits,
                lesson = opts.lesson,
            };
            if (opts.atRim.HasValue) toy.pos = RimPoint(opts.atRim.Value, 0.08f);
            if (opts.pos.HasValue) toy.pos = opts.pos.Value;
            else if (toy.Kind.mover == Movers.Drop) toy.pos = HangPoint(toy.pos, toy.Kind);
            // 10-31 防穿模：地面出生点贴着摆件（棺材、南瓜堆……），个子大的（缝合熊）一出来身体就嵌在摆件里——挪到最近能容下它的空地
            else if (!opts.atRim.HasValue && solid != null && toy.pos.y < 0.05f && toy.Kind.mover == Movers.Walk && !solid.GroundOk(toy.pos, toy.Kind.BodyR > 0.15f))
                toy.pos = solid.NearestFreeGround(toy.pos, 1.5f, toy.Kind.BodyR > 0.15f);
            toy.path = Safe(k.mover == Movers.Fly ? FlyToJar(toy.pos, k) : k.mover == Movers.Drop ? DropPath(toy.pos) : PlanToJar(toy.pos, k.radius), toy);
            toys.Add(toy);
            var e = Emit(GameEventTypes.Spawn).At(toy.pos);
            e.id = toy.id; e.kind = kind; e.spawn = spawn.id;
            if (director != null && !director.introduced.Contains(kind) && k.HasIntro && !opts.silent)
            {
                director.introduced.Add(kind);
                Say(k.introKey, k.intro, 4.5f);
                var e2 = Emit(GameEventTypes.Introduce);
                e2.id = toy.id; e2.kind = kind;
            }
            return toy;
        }

        public Candy SpawnCandy(Vector3 pos, Vector3 vel = default)
        {
            var c = new Candy { id = nextId++, state = CandyStates.Falling, pos = pos, vel = vel };
            candies.Add(c);
            return c;
        }

        public int ActiveCount()
        {
            int n = 0;
            foreach (var t in toys) if (t.state != ToyStates.Boxed && t.state != ToyStates.Gone) n++;
            return n;
        }

        public int CarriedCandy()
        {
            int s = 0;
            foreach (var t in toys) s += t.state != ToyStates.Gone ? t.candyCount : 0;
            return s;
        }

        public int LooseCandy()
        {
            int n = 0;
            foreach (var c in candies) if (c.state != CandyStates.Gone) n++;
            return n;
        }

        public string HeldBy(Body obj)
        {
            if (heldLeft != null && heldLeft.obj == obj) return Sides.Left;
            if (heldRight != null && heldRight.obj == obj) return Sides.Right;
            return null;
        }

        public bool IsHeld(Body obj) => HeldBy(obj) != null;

        public Vector3 Center(Toy t)
        {
            var k = t.Kind;
            return new Vector3(t.pos.x, t.pos.y + k.height * 0.5f, t.pos.z);
        }

        // ---------- 主循环 ----------
        // dt 用 double：时间轴（time/clock）按 double 累加，与 JS 逐位一致；逐帧状态（残留、速度等）用 float
        public List<GameEvent> Update(double dt, InputFrame input)
        {
            dt = Math.Min(dt, 0.05);
            time += dt;
            events = pending;
            // 10-31 审查：每帧 new 一个列表会产生 GC。三个列表轮换：返回给调用方的列表要到两帧之后才会被清空复用
            pending = evPool[evPoolI]; evPoolI = (evPoolI + 1) % evPool.Length;
            if (pending == events) { pending = evPool[evPoolI]; evPoolI = (evPoolI + 1) % evPool.Length; }
            pending.Clear();
            inUpdate = true;
            try { Step(dt, input ?? new InputFrame()); } finally { inUpdate = false; }
            return events;
        }

        void Step(double dt, InputFrame input)
        {
            double dtD = dt;
            var evs = input.events ?? new List<InputEvent>();

            if (phase == Phases.Title || phase == Phases.Result)
            {
                idleT += dt;
                if (idleT > 1.2f && evs.Exists(e => e.type == InputEventTypes.PinchStart))
                {
                    idleT = 0;
                    if (phase == Phases.Title) StartNight(0);
                    else StartNight(result != null ? result.next : nightIndex);
                }
                UpdateIdleToys(dt);
                return;
            }

            TrackHands(input);
            if (freezeAllT > 0) freezeAllT = MathD.Max(0, freezeAllT - dt);
            else if (slowT > 0) slowT = MathD.Max(0, slowT - dt);
            if (shieldT > 0) { shieldT = MathD.Max(0, shieldT - dt); if (shieldT == 0) { ShieldPush(0.3f, 1.2); Emit(GameEventTypes.ShieldEnd); } }
            foreach (var kv in skills)
            {
                var s = kv.Value; if (s.cd <= 0) continue;
                s.cd = MathD.Max(0, s.cd - dt);
                if (s.cd == 0 && phase == Phases.Playing) { var re = Emit(GameEventTypes.SkillReady); re.strId = kv.Key; }
            }
            UpdateLamps(dt);
            UpdateLight();
            UpdateGaze(input, dt);
            for (int i = 0; i < evs.Count; i++) HandleEvent(evs[i], input);
            UpdateCarried(input, dt);
            UpdateHeld(input, dt);
            UpdatePulled(input, dt);
            UpdateToys(dt);
            UpdateCandies(dt);
            UpdateCauldron(dt);
            UpdateDirector(dtD, input);
            UpdateLesson(dt, input);
            UpdateBrief(dt);

            if (clockRunning) clock += dtD;
            if (clock >= night.duration) EndNight(true);
            else if (candyJar + CarriedCandy() + LooseCandy() <= 0) EndNight(false);
        }

        void UpdateIdleToys(double dt)
        {
            // 天亮后敌人陆续跳回去
            foreach (var t in toys)
            {
                if (t.state == ToyStates.Yawn)
                {
                    t.yawnT -= dt;
                    if (t.yawnT <= 0) { t.state = ToyStates.Gone; var e = Emit(GameEventTypes.HopIn).At(t.pos); e.id = t.id; }
                }
            }
        }

        void EndNight(bool win)
        {
            // 天亮时：敌人手里的糖、散落的糖都归还；敌人打哈欠
            int returned = 0;
            foreach (var t in toys)
            {
                if (t.state != ToyStates.Gone) { returned += t.candyCount; t.candyCount = 0; t.hasCandy = false; }
                if (t.state != ToyStates.Gone && t.state != ToyStates.Boxed)
                {
                    t.state = ToyStates.Yawn;
                    t.yawnT = 0.6f + rand() * 1.6f;
                    if (t.Kind.mover != Movers.Walk && t.pos.y > TABLE.y + 0.05f) t.pos = new Vector3(t.pos.x, OnTable(t.pos) ? TABLE.y : 0f, t.pos.z);
                }
            }
            foreach (var c in candies) if (c.state != CandyStates.Gone) { c.state = CandyStates.Gone; returned++; }
            heldLeft = heldRight = null;
            if (win) candyJar += returned;
            // 10-31 审查：剩 1 颗也算守住太宽松——天亮时少于 3 颗算没守住；星级 11/7/3
            bool dawnFail = win && candyJar < MinCandiesToWin;
            if (dawnFail) win = false;
            int cs = win || dawnFail ? candyJar : 0;
            int starsN = !win ? 0 : cs >= 11 ? 3 : cs >= 7 ? 2 : cs >= MinCandiesToWin ? 1 : 0;
            bool last = nightIndex >= NightDefs.NIGHTS.Length - 1;
            result = new NightResult
            {
                win = win,
                candies = cs,
                stars = starsN,
                stats = stats.Clone(),
                next = win && !last ? nightIndex + 1 : win && last ? 0 : nightIndex,
                allDone = win && last,
                lost = candyLost,
                dawnFail = dawnFail,
            };
            result.stats.lost = candyLost;
            result.challenges = Challenges.Evaluate(night, nightIndex, result);
            if (!win) { var tp = Challenges.Tip(night, result.stats, candyLost); result.tipKey = tp.key; result.tip = tp.text; }
            phase = Phases.Result;
            idleT = 0;
            var e = Emit(win ? GameEventTypes.Dawn : GameEventTypes.Fail);
            e.result = result;
        }

        void HintOnce(string key, string text, float dur = 3.5f, Dictionary<string, string> args = null)
        {
            if (hints.ContainsKey(key)) return;
            hints[key] = true;
            Say("hint." + key, text, dur, args);
        }

        // ---------- 给表现层的只读快照 ----------
        public float MoonProgress => night != null ? Mathf.Min(1, (float)(clock / night.duration)) : 0;
    }
}
