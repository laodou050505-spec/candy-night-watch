// 导演（控制压力、出怪、首领、波次）与边玩边学的分步教学。
using System.Collections.Generic;
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        // ---------- 导演：控制压力 ----------
        double DirectorSpeedMul()
        {
            int recent = 0;
            foreach (var t in director.recentLoss) if (time - t < 20) recent++;
            return recent >= 2 ? 0.85 : 1.0;
        }

        string PickKind()
        {
            var mix = night.mix != null && night.mix.Count > 0 ? night.mix : new List<MixEntry> { new MixEntry(KindIds.Skeleton, 1) };
            float sum = 0;
            foreach (var m in mix) sum += m.weight;
            float r = rand() * sum;
            foreach (var m in mix) { r -= m.weight; if (r <= 0) return m.kind; }
            return KindIds.Skeleton;
        }

        static SpawnPoint[] SpawnPool(string kind)
        {
            string m = EnemyKinds.Get(kind).mover;
            return m == Movers.Fly ? SKY_SPAWNS : m == Movers.Drop ? DROP_SPAWNS : SPAWNS;
        }

        SpawnPoint SpawnPick(InputFrame input, string kind)
        {
            // 蜘蛛从头顶下来，本来就在视野上方，不需要藏起来
            if (EnemyKinds.Get(kind).mover == Movers.Drop) return DROP_SPAWNS[RandIndex(DROP_SPAWNS.Length)];
            return PickSpawn(input, SpawnPool(kind));
        }

        void UpdateDirector(double dt, InputFrame input)
        {
            double dtD = dt;
            var d = director;
            foreach (var s in pendingSays) s.t -= dt;
            var due = pendingSays.Find(s => s.t <= 0);
            if (due != null) { Say(due.key, due.text, due.dur); pendingSays.Remove(due); }
            if (!clockRunning) return;
            d.recentLoss.RemoveAll(t => !(time - t < 30));
            d.t -= dtD;
            // 首领登场
            if (clock >= d.bossAt)
            {
                d.bossAt = double.PositiveInfinity;
                var sp = SPAWNS[2 + RandIndex(2)];
                SpawnToy(sp, new SpawnOpts { kind = KindIds.Frank });
                Emit(GameEventTypes.BossIn);
            }
            // 被风吹走的：约 10 秒后从视野外的出生点回来（10-31 审查：吹走即消灭让外挥一招清场）
            for (int i = d.returns.Count - 1; i >= 0; i--)
            {
                if (d.returns[i].t > time) continue;
                string rk = d.returns[i].kind; d.returns.RemoveAt(i);
                if (night.duration - clock < 8 || ActiveCount() >= night.maxActive + 2) continue;
                SpawnToy(SpawnPick(input, rk), new SpawnOpts { kind = rk });
            }
            // 一波：同时从多个出生点冒出来（不受 maxActive 限制太多，留出用外挥的机会）
            d.burstT -= dtD;
            if (d.burstT <= 0 && night.duration - clock > 12 && night.burst != null)
            {
                d.burstT = night.burst.every;
                int n = Mathf.Min(night.burst.count, night.maxActive + 2 - ActiveCount());
                for (int i = 0; i < n; i++)
                {
                    string kind = PickKind();
                    var pool = SpawnPool(kind);
                    SpawnToy(pool[i % pool.Length], new SpawnOpts { kind = kind });
                }
                if (n > 0) { var we = Emit(GameEventTypes.Wave); we.count = n; }
            }
            int max = Mathf.Min(night.maxActive, cfg.director.maxActive);
            // 最后 8 秒不再生成，留出收尾
            if (d.t > 0 || night.duration - clock < 8) return;
            if (ActiveCount() >= max) { d.t = 1; return; }
            // 新敌人：本夜的 feature 种类在开场后第一只先单独出场
            string k = PickKind();
            if (!string.IsNullOrEmpty(night.feature) && !d.featured) { k = night.feature; d.featured = true; }
            SpawnToy(SpawnPick(input, k), new SpawnOpts { kind = k });
            double lo = night.spawnEvery[0], hi = night.spawnEvery[1];
            double next = lo + (hi - lo) * rand();
            int recent = d.recentLoss.Count;
            if (recent >= 2) next *= 1.6;                                                          // 连续丢糖：放缓
            else if (clock > 20 && recent == 0 && candyJar >= cfg.candy.total - 1) next *= 0.8;     // 太顺：加压
            if (k != KindIds.Skeleton && d.featured && toys.FindAll(t => t.kind == k).Count == 1) next = MathD.Max(next, 5);
            d.t = next;

            int act = ActiveCount();
            if (act >= 4 && skills.ContainsKey(SkillIds.Pray) && stats.skills == 0) HintOnce("pray", "照看不过来了？双手合十，全场都会僵住再变慢。", 4.5f);
        }

        SpawnPoint PickSpawn(InputFrame input, SpawnPoint[] pool)
        {
            var head = input.head;
            var hidden = new List<SpawnPoint>();
            foreach (var s in pool)
            {
                if (head == null) { hidden.Add(s); continue; }
                var c = new Vector3(s.pos.x, s.pos.y + 0.1f, s.pos.z);
                if (Vec.AngleDeg(head.fwd, c - head.pos) > cfg.gaze.peripheralHalfDeg) hidden.Add(s);
            }
            // 10-31 审查（舒适）：总挑视野外的出生点，敌人就总从两侧 70° 外冒出来，玩家得不停左右转头——四成概率不挑，正前方也会来
            var p = hidden.Count > 0 && rand() < 0.6f ? hidden.ToArray() : pool;
            return p[RandIndex(p.Length) % p.Length];
        }

        // ---------- 边玩边学 ----------
        // 每一步：先说一句话（必要时放出教学用敌人），玩家做对了才进下一步。全部完成后才开始计时。
        void UpdateLesson(double dt, InputFrame input)
        {
            var L = lesson;
            if (L == null || L.done) return;
            L.t += dt;
            if (L.step < 0)
            {
                if (L.t > 2.2f) LessonStepTo(0);
                return;
            }
            var st = L.steps[L.step];
            // 教学敌人不偷糖：走到糖桶就停住等
            foreach (var t in L.toys) t.noSteal = true;
            if (LessonDone(st, input))
            {
                var ok = Emit(GameEventTypes.LessonOk); ok.strId = st.id;
                if (L.step + 1 < L.steps.Count) LessonStepTo(L.step + 1);
                else FinishLesson();
                return;
            }
            // 卡住太久：重复提示
            if (L.t - L.sayAt > 14)
            {
                L.sayAt = L.t;
                Say(st.sayKey, st.say, 5);
                var he = Emit(GameEventTypes.LessonHint); he.strId = st.id;
            }
            // 教学敌人逃掉了 / 被消化了：补一只，不卡关
            if (st.NeedToy && !L.toys.Exists(t => t.state != ToyStates.Gone)) LessonSpawnToys(new LessonSpawn { kind = KindIds.Skeleton, rim = 0 });
        }

        void LessonStepTo(int i)
        {
            var L = lesson;
            L.step = i;
            L.sayAt = L.t;
            var st = L.steps[i];
            // 记下这一步开始时的统计，用来判断“做到了没有”
            L.@base = stats.Clone();
            L.flag = new LessonFlag();
            Say(st.sayKey, st.say, 5.5f);
            if (st.spawn != null) LessonSpawnToys(st.spawn);
            var se = Emit(GameEventTypes.LessonStep); se.strId = st.id; se.index = i; se.total = L.steps.Count;
            var hh = Emit(GameEventTypes.HandHint);
            hh.kind = LESSON_HINT.TryGetValue(st.id, out var hk) ? hk : "none";
            hh.id = L.toys.Count > 0 ? L.toys[L.toys.Count - 1].id : 0;
        }

        void LessonSpawnToys(LessonSpawn sp)
        {
            var L = lesson;
            int n = sp.count > 0 ? sp.count : 1;
            for (int i = 0; i < n; i++)
            {
                float rim = sp.rim ?? (-40 + i * (80f / Mathf.Max(1, n - 1)));
                var opts = new SpawnOpts { kind = sp.kind, atRim = rim, speedMul = sp.slow ?? 0.6f, lesson = true, silent = true };
                if (sp.nearJar) opts.pos = new Vector3(JAR.x + (i != 0 ? 0.16f : -0.16f), TABLE.y, JAR.z - 0.12f);
                if (sp.far) opts.pos = RimPoint(rim, 0.06f);
                var t = SpawnToy(SPAWNS[i % SPAWNS.Length], opts);
                if (sp.withCandy) { t.candyCount = 1; t.hasCandy = true; candyJar--; StartFlee(t); t.speedMul = 0.35f; }
                L.toys.Add(t);
            }
        }

        bool LessonDone(LessonStep st, InputFrame input)
        {
            var L = lesson;
            var b = L.@base;
            var live = L.toys.FindAll(t => t.state != ToyStates.Gone);
            switch (st.id)
            {
                // 盯满 1 秒：攒够残留，下一步“移开视线”时才看得到它继续僵着
                case "look": return live.Exists(t => t.gazed && t.gazeAccum > 1.0f);
                case "residual":
                {
                    // 先盯住，再移开，看着它靠残留继续僵住 0.5 秒
                    float dt = (float)(time - (L.flag.last ?? time));
                    L.flag.last = time;
                    if (live.Exists(t => !t.gazed && t.residual > 0 && t.frozen)) L.flag.seen += dt;
                    // 残留快结束也算（玩家已经看到“移开还僵着”）
                    return L.flag.seen >= 0.5f || (L.flag.seen > 0 && live.TrueForAll(t => t.residual <= 0));
                }
                case "grab": return live.Exists(t => t.state == ToyStates.Held);
                case "cauldron": return stats.boxed > b.boxed;
                case "shake": return stats.shaken > b.shaken;
                case "candy": return stats.recovered > b.recovered;
                case "throw": return stats.thrown > b.thrown;
                case "pull": return (stats.pulls > b.pulls && live.Exists(t => t.state == ToyStates.Held)) || stats.pulls > b.pulls + 1;
                case "launch": return stats.stars > b.stars;
                case "pat": return stats.pats > b.pats;
                case "bucket": return stats.bucketLifts > b.bucketLifts;
                case "pot": return stats.potMoves > b.potMoves;
                case "lamp": return stats.lamps > b.lamps;
                default: return stats.SkillUse(st.id) > b.SkillUse(st.id); // pray / sweep / shield
            }
        }

        // ---------- 开夜介绍 ----------
        public const double BRIEF_START = 3.2, BRIEF_LINE = 6.5;

        void UpdateBrief(double dt)
        {
            var B = brief;
            if (B == null || B.done) return;
            B.t += dt;
            int i = B.t < BRIEF_START ? -1 : (int)((B.t - BRIEF_START) / BRIEF_LINE);
            if (i == B.step) return;
            if (i >= B.steps.Count) { FinishBrief(); return; }
            B.step = i;
            var st = B.steps[i];
            Say(st.sayKey, st.say, (float)BRIEF_LINE - 0.6f);
            var se = Emit(GameEventTypes.BriefStep); se.strId = st.id; se.index = i; se.total = B.steps.Count;
        }

        public void FinishBrief()
        {
            var B = brief;
            if (B == null || B.done) return;
            B.done = true;
            Emit(GameEventTypes.BriefDone);
            clockRunning = true;
            director.t = 1.5;
        }

        public void FinishLesson()
        {
            var L = lesson;
            if (L == null || L.done) return;
            L.done = true;
            foreach (var t in L.toys) t.noSteal = false;
            if (practice)
            {
                // 练习做完：夸一句，回封面（不计时、不出怪、不记成绩）
                Say("practice.done", "学会啦！去守夜吧。", 3f);
                var hp = Emit(GameEventTypes.HandHint); hp.kind = "none";
                Emit(GameEventTypes.PracticeDone);
                return;
            }
            Say("say.lessonDone", "很好。守住糖桶，一直到天亮！", 3.5f);
            var hh = Emit(GameEventTypes.HandHint); hh.kind = "none";
            Emit(GameEventTypes.LessonDone);
            clockRunning = true;
            director.t = 2.5;
        }
    }
}
