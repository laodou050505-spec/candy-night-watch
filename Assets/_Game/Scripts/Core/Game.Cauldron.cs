// 女巫坩埚（收纳 + 慢慢消化）与手势技能。
using System.Collections.Generic;
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        // ---------- 女巫坩埚 ----------
        /// <summary>坩埚当前格数：基础 + 章节加成（第 5 夜起 +1、第 9 夜起再 +1）+ 糖果铺升级（10-29 用户：后期太忙，锅要能装更多）</summary>
        public int Capacity => cfg.cauldron.capacity + (nightIndex >= 3 ? 1 : 0) + (nightIndex >= 8 ? 1 : 0) + capUpgrade;   // 10-31：第一次 +1 从第五夜提前到第四夜
        public int capUpgrade;          // 糖果铺“坩埚扩容”等级（GameLoop 开夜前设置）
        public float magnetBonus;       // 糖果铺“糖桶吸力”加成（米）
        public float digestMul = 1f;    // 糖果铺“消化加快”（<1 更快）
        public int CauldronFree() => Capacity - cauldron.used;

        static int SizeOf(EnemyKind k) => k.size > 0 ? k.size : 1;

        public const int RIM_MAX = 2;

        public void PutInBox(Toy t)
        {
            var k = t.Kind;
            int size = SizeOf(k);
            bool tooBig = size > Capacity || k.twoHand;   // 缝合熊永远装不下（只能扔走），扩容后也一样
            if (size > CauldronFree() || tooBig)
            {
                stats.boxFull++;
                var args = new Dictionary<string, string> { { "kind", t.kind }, { "free", CauldronFree().ToString() }, { "size", size.ToString() } };
                // 10-31 审查：装不下就弹回来要重抓，玩家只能捏着干等。改成坐到锅沿上排队，腾出空格自己掉进去
                if (!tooBig && cauldron.rim.Count < RIM_MAX)
                {
                    int slot = cauldron.rim.Exists(o => o.rimSlot == 0) ? 1 : 0;
                    t.state = ToyStates.Boxed;
                    t.rimWait = true; t.rimSlot = slot;
                    t.residual = 0; t.pull = null; t.charged = false; t.chargeT = 0;
                    t.pos = RimPos(slot);
                    cauldron.rim.Add(t);
                    ReturnCarried(t);
                    var we = Emit(GameEventTypes.BoxFull).At(t.pos); we.id = t.id; we.need = size; we.free = CauldronFree(); we.wait = true;
                    if (time - cauldron.fullSayT > 15)
                    {
                        cauldron.fullSayT = time;
                        Say("say.cauldronRim", $"坩埚满了，{k.label}先在锅沿上排队，空出一格就自己掉进去。", 3.5f, args);
                    }
                    return;
                }
                // 装不下（太大，或锅沿也坐满了）：弹出来，并说明原因
                t.state = ToyStates.Falling;
                t.vel = new Vector3(-0.6f, 1.4f, -0.4f);
                var e = Emit(GameEventTypes.BoxFull);
                e.id = t.id; e.need = size; e.free = CauldronFree(); e.tooBig = tooBig;
                // 参数 kind 是敌人 id，表现层用 kind.<id>.name 查本地化名字后再代入
                if (tooBig) HintOnce("cauldronTooBig", $"{k.label}太大了，坩埚装不下！把它扔出去。", 4, args);
                else if (time - cauldron.fullSayT > 15)
                {
                    cauldron.fullSayT = time;
                    Say("say.cauldronNoRoom", $"坩埚满了（还剩 {CauldronFree()} 格，{k.label}要 {size} 格）。等它消化，或拍拍锅沿加快。", 4, args);
                }
                return;
            }
            BoxIn(t, size);
        }

        /// <summary>锅沿上排队的位置：朝玩家一侧的锅沿左右各一个（锅可以被挪动，每帧重算）</summary>
        static Vector3 RimPos(int slot)
        {
            float a = (slot == 0 ? -40f : 40f) * Mathf.Deg2Rad;
            float r = BOX.mouth + 0.012f;
            return new Vector3(BOX.x + Mathf.Sin(a) * r, BOX.y + BOX.height - 0.01f, BOX.z + Mathf.Cos(a) * r);
        }

        void ReturnCarried(Toy t)
        {
            if (t.candyCount <= 0) return;
            candyJar += t.candyCount;
            stats.recovered += t.candyCount;
            t.candyCount = 0;
            t.hasCandy = false;
            var ce = Emit(GameEventTypes.CandyBack); ce.id = t.id;
        }

        void BoxIn(Toy t, int size)
        {
            t.state = ToyStates.Boxed;
            t.rimWait = false;
            t.residual = 0;
            t.pull = null;
            float rx = rand();
            float rz = rand();
            t.pos = new Vector3(BOX.x + (rx - 0.5f) * 0.08f, BOX.y + 0.03f, BOX.z + (rz - 0.5f) * 0.08f);
            cauldron.queue.Add(t);
            cauldron.used += size;
            stats.boxed++;
            var be = Emit(GameEventTypes.Boxed).At(t.pos); be.id = t.id; be.used = cauldron.used; be.size = size;
            ReturnCarried(t);
            if (cauldron.used >= Capacity) HintOnce("cauldronFull", "坩埚满了。拍拍锅沿能让它消化得快一点。", 4);
        }

        // 拍锅沿：加速消化
        void OnPress(Vector3 pos)
        {
            var C = cfg.cauldron;
            if (Vec.DistXZ(pos, BOX.pos) > BOX.half + 0.1f) return;
            if (cauldron.stirCd > 0) return;
            cauldron.stirCd = C.stirCooldown;
            if (cauldron.queue.Count > 0) cauldron.stirT = C.stirSec;
            stats.pats++;
            var e = Emit(GameEventTypes.Pat); e.count = cauldron.queue.Count;
        }

        void UpdateCauldron(double dt)
        {
            var c = cauldron;
            var C = cfg.cauldron;
            if (c.stirCd > 0) c.stirCd -= dt;
            if (c.stirT > 0) c.stirT = MathD.Max(0, c.stirT - dt);
            // 锅沿排队的：跟着锅走；有空格了按先来后到掉进去
            for (int i = 0; i < c.rim.Count; i++) c.rim[i].pos = RimPos(c.rim[i].rimSlot);
            if (c.rim.Count > 0 && SizeOf(c.rim[0].Kind) <= CauldronFree())
            {
                var w = c.rim[0]; c.rim.RemoveAt(0);
                BoxIn(w, SizeOf(w.Kind));
            }
            if (c.queue.Count == 0) { c.progress = 0; return; }
            var head = c.queue[0];
            double need = C.digestSec * digestMul * SizeOf(head.Kind);
            c.progress += dt * (c.stirT > 0 ? C.stirMul : 1);
            if (c.progress >= need)
            {
                c.queue.RemoveAt(0);
                c.used -= SizeOf(head.Kind);
                c.progress = 0;
                head.state = ToyStates.Gone;
                stats.digested++;
                var e = Emit(GameEventTypes.Digested); e.id = head.id; e.kind = head.kind; e.used = c.used;
            }
        }

        // 当前这一只的消化进度 0~1（给表现层画锅沿灯）
        public float DigestK
        {
            get
            {
                if (cauldron == null || cauldron.queue.Count == 0) return 0;
                var head = cauldron.queue[0];
                return (float)MathD.Min(1, cauldron.progress / (cfg.cauldron.digestSec * digestMul * SizeOf(head.Kind)));
            }
        }

        // ---------- 手势技能 ----------
        public void UseSkill(string id, InputEvent e = null, InputFrame input = null)
        {
            input = input ?? new InputFrame();
            if (id == null || !skills.TryGetValue(id, out var s)) return;
            if (s.cd > 0) { var ce = Emit(GameEventTypes.SkillCooling); ce.strId = id; ce.left = (float)s.cd; return; }
            var SK = cfg.skills;
            if (id == SkillIds.Pray)
            {
                freezeAllT = SK.pray.freezeSec;
                slowT = SK.pray.slowSec;
                foreach (var t in toys) if (ACTIVE.Contains(t.state)) t.frozen = true;
                var ev = Emit(GameEventTypes.Skill); ev.strId = id;
            }
            else if (id == SkillIds.Sweep)
            {
                var S = SK.sweep;
                var hit = new List<int>();
                foreach (var t in toys)
                {
                    if (!ACTIVE.Contains(t.state) && t.state != ToyStates.Glide) continue;
                    var k = t.Kind;
                    float d = Vec.DistXZ(t.pos, Vector3.zero);
                    if (d > S.radius) continue;
                    // 从玩家向外吹：越近吹得越远；缝合熊太重，只推开一点
                    var dir = Vec.Norm(new Vector3(t.pos.x, 0, t.pos.z - 0.15f));
                    float m = (1 - d / S.radius * 0.5f) * k.sweepMul;
                    if (t.hasCandy) LoseCandyFrom(t, "sweep");
                    if (k.sweepMul >= 1)
                    {
                        // 10-30 用户：吹走的小鬼要飞远，不能穿过桌面、模型。沿弧线先升到桌面以上，再朝远离玩家的方向飞，
                        // 越飞越小，在碰到围墙之前消失在远处（表现层缩小 + 旋转），规则层到点就移除
                        BlowAway(t, dir);
                    }
                    else
                    {
                        t.state = ToyStates.Falling;                    // 缝合熊太重：只被推开一点，正常落回桌面
                        t.vel = new Vector3(dir.x * S.push * m, S.lift * m, dir.z * S.push * m);
                        t.dizzy = MathD.Max(t.dizzy, S.dizzySec);
                    }
                    hit.Add(t.id);
                }
                var ev = Emit(GameEventTypes.Skill); ev.strId = id; ev.hit = hit.ToArray();
            }
            else if (id == SkillIds.Lamp)
            {
                var S = SK.lamp;
                // 南瓜灯：从掌心前飞出去，停在前方（手势给出位置）
                var pos = e != null && e.pos.HasValue ? e.pos.Value : DefaultLampPos(input);
                if (lamps.Count >= S.max) lamps.RemoveAt(0);
                var lamp = new Lamp { id = nextId++, pos = pos, from = e != null && e.from.HasValue ? e.from.Value : pos, radius = S.radius, t = S.duration, dur = (float)S.duration };
                lamps.Add(lamp);
                var ev = Emit(GameEventTypes.Skill).At(pos); ev.strId = id; ev.lamp = lamp.id;
                HintOnce("lampMove", "捏住南瓜灯，可以把它挪到想照的地方。", 3.5f);
            }
            else if (id == SkillIds.Shield)
            {
                shieldT = SK.shield.duration;
                // 正在偷的、桶边 35 厘米内走着的敌人都被弹开并晕一下（10-31 审查：只弹正在偷的，护罩一结束桶边排队的会一拥而上）
                ShieldPush(0.35f, 1.5);
                var ev = Emit(GameEventTypes.Skill); ev.strId = id;
            }
            s.cd = SK.Cooldown(id);
            stats.skills++;
            stats.skillUse[id] = stats.SkillUse(id) + 1;
            if (id == SkillIds.Lamp) stats.lamps++;
        }

        /// <summary>护桶：把糖桶附近（地面/桌面上走着、正在偷的）敌人弹开并晕一下。开启和结束时各一次</summary>
        void ShieldPush(float radius, double dizzy)
        {
            foreach (var t in toys)
            {
                if (t.state != ToyStates.Steal && !(ACTIVE.Contains(t.state) && t.Kind.mover != Movers.Fly)) continue;
                if (t.Kind.twoHand) continue;
                if (Vec.DistXZ(t.pos, JAR.pos) > radius || Mathf.Abs(t.pos.y - JAR.y) > 0.25f) continue;
                if (t.hasCandy) LoseCandyFrom(t, "shield");
                var dir = Vec.Norm(new Vector3(t.pos.x - JAR.x, 0, t.pos.z - JAR.z));
                t.state = ToyStates.Falling;
                t.vel = new Vector3(dir.x * 1.1f, 1.0f, dir.z * 1.1f);
                t.dizzy = MathD.Max(t.dizzy, dizzy);
            }
        }

        void BlowAway(Toy t, Vector3 dir)
        {
            dir.y = 0; if (dir.sqrMagnitude < 1e-6f) dir = Vector3.back; dir.Normalize();
            // 10-31 防穿模：沿原方向和左右偏 20°/40°/60° 各规划一条，按场景占用网格算出撞上摆件前能飞多远，挑飞得最远的
            //（原方向能飞 1.6 米以上就用原方向）。飞到 endK 就结束，表现层在那之前缩小消失
            LaunchState best = null; float bestLen = -1;
            foreach (float off in new[] { 0f, -20f, 20f, -40f, 40f, -60f, 60f })
            {
                var d = Quaternion.Euler(0, off, 0) * dir;
                var L = PlanBlow(t, d);
                L.endK = FlightEnd(L, t);
                float len = FlightLength(L);
                if (len > bestLen + 0.05f) { bestLen = len; best = L; }
                if (off == 0f && (solid == null || len > 1.6f)) break;
            }
            t.state = ToyStates.Launched;
            t.pull = null; t.shake = null; t.grips = null; t.frozen = false;
            t.launch = best;
        }

        LaunchState PlanBlow(Toy t, Vector3 dir)
        {
            // 沿这个方向走到围墙前 0.6 米为止（不出花园、不撞墙），高度 2.1 米（低于藤架椽子）
            float s = 50f;
            if (dir.x > 1e-4f) s = Mathf.Min(s, (ROOM.maxX - 0.6f - t.pos.x) / dir.x);
            if (dir.x < -1e-4f) s = Mathf.Min(s, (ROOM.minX + 0.6f - t.pos.x) / dir.x);
            if (dir.z > 1e-4f) s = Mathf.Min(s, (ROOM.maxZ - 0.6f - t.pos.z) / dir.z);
            if (dir.z < -1e-4f) s = Mathf.Min(s, (ROOM.minZ + 0.6f - t.pos.z) / dir.z);
            s = Mathf.Max(1.0f, s);
            var to = new Vector3(t.pos.x + dir.x * s, 2.1f, t.pos.z + dir.z * s);
            // 斜着直接飞会穿过桌板——先钻出桌沿（在桌下时），再竖直升到桌面以上 0.4 米，最后才往外飞
            var w1 = t.pos;
            if (OnTable(t.pos, -0.05f) && t.pos.y < TABLE.y - 0.02f)
            {
                var o = new Vector3(t.pos.x - TABLE.center.x, 0, t.pos.z - TABLE.center.z);
                if (o.sqrMagnitude < 1e-4f) o = dir;
                o.Normalize();
                w1 = new Vector3(TABLE.center.x + o.x * (TABLE.radius + 0.18f), t.pos.y, TABLE.center.z + o.z * (TABLE.radius + 0.18f));
            }
            var w2 = new Vector3(w1.x, Mathf.Max(w1.y, TABLE.y) + 0.4f, w1.z);
            return new LaunchState { t = 0, from = t.pos, dir = (dir + Vector3.up * 0.9f).normalized, to = to, blow = true, w1 = w1, w2 = w2 };
        }

        Vector3 DefaultLampPos(InputFrame input)
        {
            if (input.hands != null)
                foreach (var h in input.hands)
                    if (h.tracked && h.hasPoint) return new Vector3(h.point.x, h.point.y + 0.08f, h.point.z);
            return new Vector3(0, TABLE.y + 0.3f, -0.8f);
        }

        public double SlowMul => slowT > 0 && freezeAllT <= 0 ? cfg.skills.pray.slowMul : 1;
    }
}
