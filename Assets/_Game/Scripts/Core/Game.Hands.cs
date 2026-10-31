// 手：手部历史（估计抛出速度）、捏合/松手、拿着、晃、蓄力、隔空取物、缝合熊双手。
using System.Collections.Generic;
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        static readonly string[] BOTH_SIDES = { Sides.Left, Sides.Right };

        // ---------- 手部历史：估计抛出速度 ----------
        readonly Dictionary<string, double> frankLetGo = new Dictionary<string, double>();
        public const double BUCKET_HOLD_MAX = 8;
        double bucketHeldT;

        /// <summary>放下糖桶：放在哪就在哪，落到手下方的桌面上（离开桌面、压着坩埚或南瓜灯时挪到最近的空位）</summary>
        void PutBucket(string side)
        {
            bucketHeld = null;
            bucketHeldT = 0;
            var spot = BucketSpot(bucketPos);
            JAR.Set(spot.x, spot.z);
            bucketPos = JAR.pos;
            var be = Emit(GameEventTypes.BucketPut).At(bucketPos); be.side = side;
            // 正在往糖桶去的小鬼改道去新位置
            foreach (var t in toys) if ((t.state == ToyStates.Walk && t.goal != null && t.goal.type == Goal.Jar) || t.state == ToyStates.Steal) Retarget(t);
        }

        void TrackHands(InputFrame input)
        {
            if (input.hands == null) return;
            foreach (var h in input.hands)
            {
                if (!h.tracked || !h.hasPoint || h.stale) continue;   // 沿用的旧位置不算速度（10-31：手重新追踪到时位置一跳，会被误判成甩出/晃动）
                var hist = handHist[h.side];
                // 单帧跳变超过 15 厘米（追踪重新接上、左右手纠正）：之前的历史作废，免得算出一个假的高速
                if (hist.Count > 0 && Vector3.Distance(hist[hist.Count - 1].p, h.point) > 0.15f) hist.Clear();
                hist.Add((time, h.point));
                while (hist.Count > 2 && time - hist[0].t > 0.4) hist.RemoveAt(0);
            }
        }

        /// <summary>当前手速：最近约 0.1 秒的平均速度</summary>
        public Vector3 HandVelocity(string side) => WindowVelocity(handHist[side], handHist[side].Count - 1, 0.1);

        /// <summary>
        /// 最近 0.3 秒里最快的那一下（10-30 实机：发金光后甩出去很难变星星——人甩东西时手指往往等手已经减速了才张开，
        /// 再加上松手判定的一点延迟，用“松手那一刻”的手速常常不够）
        /// </summary>
        public Vector3 PeakHandVelocity(string side, double window = 0.3)
        {
            var hist = handHist[side];
            Vector3 best = Vector3.zero;
            for (int i = hist.Count - 1; i >= 1; i--)
            {
                if (time - hist[i].t > window) break;
                var v = WindowVelocity(hist, i, 0.08);
                if (v.sqrMagnitude > best.sqrMagnitude) best = v;
            }
            return best;
        }

        static Vector3 WindowVelocity(List<(double t, Vector3 p)> hist, int end, double win)
        {
            if (end < 1) return Vector3.zero;
            int start = end;
            while (start > 0 && hist[end].t - hist[start - 1].t <= win) start--;
            if (start == end) start = end - 1;
            double dt = hist[end].t - hist[start].t;
            if (dt <= 0) return Vector3.zero;
            return (hist[end].p - hist[start].p) * (float)(1.0 / dt);
        }

        /// <summary>手是否伸在身前：沿头部水平朝向，比头往前 25 厘米以上（没有头部数据时退回固定座位判断）</summary>
        static bool ReachedOut(Vector3 p, InputFrame input)
        {
            if (input.head == null) return p.z < CHEST.z - 0.1f;
            var f = input.head.fwd; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) return p.z < CHEST.z - 0.1f;
            f.Normalize();
            var o = p - input.head.pos; o.y = 0;
            return Vector3.Dot(o, f) > 0.25f;
        }

        static HandInput FindHand(InputFrame input, string side)
        {
            if (input.hands == null) return null;
            foreach (var h in input.hands) if (h.side == side && h.tracked) return h;
            return null;
        }

        // ---------- 输入事件 ----------
        void HandleEvent(InputEvent e, InputFrame input)
        {
            switch (e.type)
            {
                case InputEventTypes.PinchStart: OnPinchStart(e.side, input); break;
                case InputEventTypes.PinchEnd: OnPinchEnd(e.side, input); break;
                case InputEventTypes.Press: if (e.pos.HasValue) OnPress(e.pos.Value); break;
                case InputEventTypes.Skill: UseSkill(e.id, e, input); break;
            }
        }

        bool CanGrab(Toy t)
        {
            if (!requireFreeze) return true;
            return t.frozen || t.state == ToyStates.Falling || t.state == ToyStates.Stunned || t.state == ToyStates.Glide;
        }

        void OnPinchStart(string side, InputFrame input)
        {
            // 抱熊的手在松手宽限内又捏上了：当作没松过
            if (frankLetGo.Remove(side)) return;
            var h = FindHand(input, side);
            if (h == null || GetHeld(side) != null) return;
            var H = cfg.hand;
            // 1) 手边的敌人
            Toy near = null;
            float nd = float.PositiveInfinity;
            foreach (var t in toys)
            {
                if (!GAZEABLE.Contains(t.state) || t.state == ToyStates.Pulled) continue;
                var k = t.Kind;
                // 缝合熊可以被第二只手加握；其他敌人被拿着时不能再抓
                if (IsHeld(t) && !(k.twoHand && t.grips != null && !t.grips.Get(side).HasValue)) continue;
                var c = new Vector3(t.pos.x, t.pos.y + k.height * 0.6f, t.pos.z);
                float d = Vector3.Distance(c, h.point);
                if (d <= H.grabRadius + Mathf.Min(0.15f, Mathf.Max(k.height, k.radius * 2) * 0.5f) && d < nd) { nd = d; near = t; }   // 10-31：体型加成封顶 15 厘米（熊原来 30 厘米内都算抓熊，抢走了旁边的锅耳、提手）
            }
            // 2) 手边的糖（比敌人更近时优先捡糖）
            Candy nearC = null;
            float cd = float.PositiveInfinity;
            foreach (var c in candies)
            {
                if ((c.state != CandyStates.Rest && c.state != CandyStates.Falling) || IsHeld(c)) continue;
                float d = Vector3.Distance(c.pos, h.point);
                if (d <= H.candyGrabRadius && d < cd) { cd = d; nearC = c; }
            }
            // 3) 手边的南瓜灯（10-30 用户：召唤出来的灯要能挪位置）：比敌人、糖都近时捏住它
            Lamp nearL = null; float ld = 0.08f;
            foreach (var l in lamps) { if (l.heldBy != null) continue; float d = Vector3.Distance(l.pos, h.point); if (d < ld) { ld = d; nearL = l; } }
            if (nearL != null && ld < nd && ld < cd)
            {
                nearL.heldBy = side;
                var lg = Emit(GameEventTypes.LampGrab).At(nearL.pos); lg.id = nearL.id; lg.side = side;
                return;
            }
            if (nearC != null && (near == null || cd < nd)) { GrabCandy(nearC, side); return; }
            if (near != null && near.lit && (CanGrab(near) || (near.Kind.twoHand && near.grips != null))) { GrabToy(near, side, h); return; }
            // 10-31 审查：糖桶提手、锅耳排在隔空取物前面——手伸向糖桶时正好对着桶边偷糖的小鬼，以前会把它拉过来，桶却提不起来
            // 4) 空手捏住糖桶提手（提手弧线附近 6 厘米内）：提起来看（10-29 用户：要捏提手才能提）
            if (bucketHeld == null && NearBucketHandle(h.point, 0.06f))
            {
                bucketHeld = side;
                stats.bucketLifts++;
                var be = Emit(GameEventTypes.BucketLift).At(JAR.pos); be.side = side;
                return;
            }
            // 5) 空手捏住一只锅耳：把坩埚提起来挪位置（10-29 用户：只有捏锅耳才能提）
            // 只在锅身外侧才算捏锅耳：手在锅口上方（锅身半径以内）是拍锅沿，不能被锅耳抢走（10-30 审查）
            if (potHeld == null && Vec.DistXZ(h.point, BOX.pos) > BOX.half - 0.004f)
                for (int s = -1; s <= 1; s += 2)
                    if (Vector3.Distance(h.point, BOX.Ear(s)) < 0.065f)
                    {
                        potHeld = side; potEar = s; potDragging = true; potCarry = BOX.pos;
                        stats.potMoves++;
                        var pe = Emit(GameEventTypes.PotLift).At(BOX.Ear(s)); pe.side = side;
                        return;
                    }
            if (near != null)
            {
                // 手边的敌人没照亮 / 在动：抓不住。只提示，不吞掉这次捏合——后面的拍锅沿照常判断（10-31 审查）
                if (near.lit && near.slipCd <= 0)
                {
                    // 动着的敌人抓不住：从指缝里溜走
                    near.slipCd = 0.5f;
                    var away = Vec.Norm(new Vector3(near.pos.x - h.point.x, 0f, near.pos.z - h.point.z));
                    near.pos.x += away.x * 0.05f;
                    near.pos.z += away.z * 0.05f;
                    if (near.path != null && near.seg < near.path.Count) near.path[near.seg] = near.pos;
                    var e = Emit(GameEventTypes.Slip).At(near.pos); e.id = near.id;
                    if (near.kind == KindIds.Pumpkin) HintOnce("slipPumpkin", "别盯着南瓜头！看旁边，用余光照着它。");
                    else HintOnce("slip", "它在动，抓不住。先用眼睛定住它。");
                }
            }
            else
            {
                // 3) 隔空取物：注视 + 捏合（缝合熊太重，拉不动）。手边有敌人时不隔空取（玩家想抓的是手边这只）
                // 10-30 实机：“没捏它也自己飞过来”——手放松时拇指食指常靠得很近被当成捏合，又正好看着某只小鬼。
                // 隔空取物要求手是抬起来、伸在身前的（高于桌面，沿头部朝向比头往前 25 厘米以上；10-31：原来按固定 z 判断，侧身拿侧面的小鬼时失效）
                bool reachOut = h.point.y > TABLE.y + 0.05f && ReachedOut(h.point, input);
                var gt = reachOut ? PullTarget(h, input) : null;
                if (gt != null)
                {
                    gt.pull = new PullState { from = gt.pos, ctrl = PullCtrl(gt.pos, h.point), t = 0, side = side, lost = 0 };
                    gt.state = ToyStates.Pulled;
                    stats.pulls++;
                    var e = Emit(GameEventTypes.PullStart); e.id = gt.id; e.side = side;
                    return;
                }
                if (gazeTarget != null && gazeTarget.Kind.twoHand && gazeTarget.gazed) HintOnce("frankHeavy", "缝合熊太重了，隔空拉不动。等它走近，用两只手捏住。");
                var gc = gazeCandy;
                if (reachOut && gc != null && !IsHeld(gc) && gc.pull == null)
                {
                    gc.pull = new PullState { from = gc.pos, ctrl = PullCtrl(gc.pos, h.point), t = 0, side = side, lost = 0 };
                    gc.state = CandyStates.Pulled;
                    var e = Emit(GameEventTypes.PullStart); e.id = gc.id; e.side = side; e.isCandy = true;
                    return;
                }
            }
            // 6) 空手捏在坩埚上方 = 拍锅沿（桌面调试也走这里）
            if (Vec.DistXZ(h.point, BOX.pos) <= BOX.half + 0.08f && h.point.y < BOX.y + BOX.height + 0.25f) OnPress(h.point);
        }

        /// <summary>手是否在糖桶提手附近：提手是糖桶正上方、沿左右方向的一道拱（两端接在桶沿，顶高约 32 厘米）</summary>
        static bool NearBucketHandle(Vector3 p, float r)
        {
            const float rx = 0.125f, cy = 0.17f, ry = 0.15f;
            float yaw = Mathf.Atan2(-JAR.x, -JAR.z);
            var ax = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));          // 提手所在平面的横向
            var rel = p - JAR.pos;
            float lx = Vector3.Dot(rel, ax), lz = Vector3.Dot(rel, new Vector3(Mathf.Sin(yaw), 0, Mathf.Cos(yaw)));
            float best = 1e9f;
            for (int i = 0; i <= 24; i++)
            {
                float th = i / 24f * Mathf.PI;
                float dx = lx - rx * Mathf.Cos(th), dy = rel.y - (cy + ry * Mathf.Sin(th));
                best = Mathf.Min(best, dx * dx + dy * dy + lz * lz);
            }
            return best < r * r;
        }

        // 提着的糖桶、坩埚、南瓜灯跟着手走
        void UpdateCarried(InputFrame input, double dt)
        {
            foreach (var l in lamps)
            {
                if (l.heldBy == null) continue;
                var lh = FindHand(input, l.heldBy);
                if (lh != null && lh.hasPoint) { l.pos = lh.point + Vector3.down * 0.05f; l.from = l.pos; }   // 灯吊在捏住的指尖下面
                else l.heldBy = null;                                                                       // 手丢了：灯就停在那儿
            }
            if (bucketHeld != null)
            {
                var h = FindHand(input, bucketHeld);
                if (h != null && h.hasPoint)
                {
                    var spot = BucketSpot(h.point);
                    bucketPos = new Vector3(spot.x, Mathf.Clamp(h.point.y - 0.32f, TABLE.y, TABLE.y + 0.35f), spot.z);
                }
                // 10-31 审查：提着糖桶时地上的小鬼偷不到，一直单手举着就能“逃课”——糖桶太沉，最多提 BUCKET_HOLD_MAX 秒
                bucketHeldT += dt;
                if (bucketHeldT > BUCKET_HOLD_MAX && phase == Phases.Playing)
                {
                    HintOnce("bucketHeavy", "糖桶太沉了，只能提一会儿。", 3f);
                    PutBucket(bucketHeld);
                }
            }
            else { bucketPos = JAR.pos; bucketHeldT = 0; }
            if (potHeld != null)
            {
                var h = FindHand(input, potHeld);
                if (h == null || !h.hasPoint) return;
                // 坩埚吊在捏住的那只耳朵下面；规则层的锅位置取它在桌面上的投影（放下时就落在那里）
                float a = BOX.Yaw * Mathf.Deg2Rad, lx = potEar * BOX.EAR_X;
                var earOff = new Vector3(Mathf.Cos(a) * lx, BOX.EAR_Y, -Mathf.Sin(a) * lx);
                potCarry = h.point - earOff;
                potCarry.y = Mathf.Clamp(potCarry.y, TABLE.y, TABLE.y + BOX.LIFT_MAX);
                // 水平位置也限制在桌面上能放锅的范围里（不会被拎到玩家脸前）
                var spot = PotSpot(potCarry);
                potCarry.x = spot.x; potCarry.z = spot.z;
                var p = PotSpot(potCarry);
                BOX.Set(p.x, p.z);
            }
            else potCarry = BOX.pos;
        }

        /// <summary>糖桶能放的位置：整个桶在桌面里，不压着坩埚、南瓜灯，离玩家至少 32 厘米</summary>
        public static Vector3 BucketSpot(Vector3 want)
        {
            var c = new Vector3(TABLE.center.x, 0, TABLE.center.z);
            var p = new Vector3(want.x, 0, want.z);
            float maxR = TABLE.radius - JAR.radius - 0.05f;
            var off = p - c; if (off.magnitude > maxR) p = c + off.normalized * maxR;
            p = PushOut(p, BOX.pos, JAR.radius + BOX.half + 0.04f);
            if (CANDLE.on) p = PushOut(p, CANDLE.pos, JAR.radius + 0.12f);
            if (p.z > -0.32f) p.z = -0.32f;
            off = p - c; if (off.magnitude > maxR) p = c + off.normalized * maxR;
            return new Vector3(p.x, TABLE.y, p.z);
        }

        /// <summary>坩埚能放的位置：整个锅身在桌面里，不压着糖桶、南瓜灯，也不贴到玩家胸口</summary>
        public static Vector3 PotSpot(Vector3 want)
        {
            var c = new Vector3(TABLE.center.x, 0, TABLE.center.z);
            var p = new Vector3(want.x, 0, want.z);
            float maxR = TABLE.radius - BOX.half - 0.05f;
            var off = p - c; if (off.magnitude > maxR) p = c + off.normalized * maxR;
            p = PushOut(p, JAR.pos, JAR.radius + BOX.half + 0.04f);
            if (CANDLE.on) p = PushOut(p, CANDLE.pos, BOX.half + 0.12f);
            if (p.z > -0.32f) p.z = -0.32f;                                   // 离玩家至少 32 厘米，不顶着胸口
            off = p - c; if (off.magnitude > maxR) p = c + off.normalized * maxR;
            return new Vector3(p.x, TABLE.y, p.z);
        }

        void GrabToy(Toy t, string side, HandInput h)
        {
            var k = t.Kind;
            if (k.twoHand) { GripFrank(t, side, h); return; }
            // 大幽灵：一抓就裂开，手里留下一只小幽灵，其余的蹦出去
            if (t.splitsLeft > 0) { SplitGhost(t, side); return; }
            string by = t.gazed ? "gaze" : t.residual > 0 ? "residual" : "other";
            if (!t.gazed && t.residual > 0 && t.residual <= 0.5f)
            {
                stats.close++;
                var ce = Emit(GameEventTypes.CloseCall).At(t.pos); ce.id = t.id;
            }
            t.state = ToyStates.Held;
            t.pull = null;
            t.shake = new ShakeState();
            t.chargeT = 0;
            t.charged = false;
            SetHeld(side, HeldItem.OfToy(t));
            var e = Emit(GameEventTypes.Grab).At(t.pos); e.id = t.id; e.side = side; e.by = by; e.hasCandy = t.hasCandy;
            if (t.hasCandy) HintOnce("shake", "它抱着糖！捏着它用力晃一晃。");
        }

        void SplitGhost(Toy t, string side)
        {
            var b = t.pos;
            int n = t.splitsLeft;
            // 外壳本身变成手里的小幽灵（保留糖）
            t.kind = KindIds.Ghostling;
            t.splitsLeft = 0;
            t.state = ToyStates.Held;
            t.pull = null;
            t.shake = new ShakeState();
            SetHeld(side, HeldItem.OfToy(t));
            var kids = new List<int>();
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n * Mathf.PI * 2 + rand();
                var kid = SpawnToy(t.spawn, new SpawnOpts { kind = KindIds.Ghostling, pos = new Vector3(b.x, b.y + 0.05f, b.z), silent = true });
                kid.state = ToyStates.Falling;
                kid.vel = new Vector3(Mathf.Cos(a) * 0.7f, 1.3f, Mathf.Sin(a) * 0.7f);
                kid.dizzy = 0.3f;
                kids.Add(kid.id);
            }
            stats.split++;
            var e = Emit(GameEventTypes.Split).At(b); e.id = t.id; e.kids = kids.ToArray();
            HintOnce("split", "分裂了！小幽灵跑得快，赶紧定住它们。");
        }

        // 缝合熊：一只手捏住只能拖慢它；两只手都捏住才能举起来
        void GripFrank(Toy t, string side, HandInput h)
        {
            if (t.grips == null) t.grips = new Grips();
            t.grips.Set(side, h.point - t.pos);
            SetHeld(side, HeldItem.OfToy(t));
            bool both = t.grips.Both;
            var fe = Emit(GameEventTypes.FrankGrip); fe.id = t.id; fe.side = side; fe.both = both;
            if (both)
            {
                t.state = ToyStates.Held;
                t.pull = null;
                t.shake = new ShakeState();
                t.chargeT = 0;
                t.charged = false;
                var e = Emit(GameEventTypes.Grab).At(t.pos); e.id = t.id; e.side = side; e.by = "twoHand"; e.hasCandy = t.hasCandy;
                HintOnce("frankLift", "举起来了！用力扔出去，或者晃一晃。");
            }
            else HintOnce("frankOne", "一只手抓不动它！另一只手也捏住。");
        }

        bool ReleaseFrankGrip(Toy t, string side, InputFrame input)
        {
            if (t.grips == null) return false;
            bool wasBoth = t.grips.Both;
            t.grips.Set(side, null);
            string other = Sides.Other(side);
            if (wasBoth)
            {
                // 两只手里先松开一只：看松手时的手速决定是扔还是放
                // 用最近 0.45 秒的峰值手速（松手有 0.15 秒宽限，再加上人甩完才张手，用当前手速常常不够）
                var vel = (PeakHandVelocity(Sides.Left, 0.45) + PeakHandVelocity(Sides.Right, 0.45)) * (0.5f * cfg.hand.throwMul);
                float sp = vel.magnitude;
                var H = cfg.hand;
                if (sp > H.throwMax) vel = vel * (H.throwMax / sp);
                var ho = GetHeld(other);
                if (ho != null && ho.obj == t) SetHeld(other, null);
                frankLetGo.Clear();
                t.grips = null;
                t.shake = null;
                if (t.charged && sp >= cfg.launch.minSpeed) { LaunchToStar(t, vel); return true; }   // 10-29 用户：发金光后轻轻一甩就飞（阈值降到 0.6m/s）；轻轻放下仍是放下
                if (sp >= H.throwSpeed * 0.8f)
                {
                    stats.thrown++;
                    t.hp = Mathf.Max(0, t.hp - 1);
                    var e = Emit(GameEventTypes.Throw).At(t.pos); e.id = t.id; e.speed = sp; e.boss = true; e.hp = t.hp;
                    DropToy(t, vel, "throw");
                    t.dizzy = MathD.Max(t.dizzy, 2.5);
                    if (t.hp <= 0) { t.knockout = true; stats.bossDown++; }
                }
                else DropToy(t, new Vector3(0, Mathf.Min(0, vel.y), 0), "release");
                return true;
            }
            if (t.grips.None) t.grips = null;
            return true;
        }

        void GrabCandy(Candy c, string side)
        {
            c.state = CandyStates.Held;
            c.pull = null;
            c.claimedBy = 0;
            SetHeld(side, HeldItem.OfCandy(c));
            var e = Emit(GameEventTypes.GrabCandy).At(c.pos); e.id = c.id; e.side = side;
            HintOnce("candyBack", "把糖放回糖罐里。");
        }

        void OnPinchEnd(string side, InputFrame input)
        {
            foreach (var l in lamps)
                if (l.heldBy == side)
                {
                    // 放哪儿就停在哪儿；在桌面上空时不低于桌面 8 厘米（灯不陷进桌子）
                    l.heldBy = null;
                    if (OnTable(l.pos)) l.pos.y = Mathf.Max(l.pos.y, TABLE.y + 0.08f);
                    l.pos.y = Mathf.Max(l.pos.y, 0.15f);
                    var lp = Emit(GameEventTypes.LampPut).At(l.pos); lp.id = l.id; lp.side = side;
                    return;
                }
            if (bucketHeld == side) { PutBucket(side); return; }
            if (potHeld == side)
            {
                potHeld = null; potDragging = false; potCarry = BOX.pos;
                var pe = Emit(GameEventTypes.PotPut).At(BOX.pos); pe.side = side; return;
            }
            // 隔空取物途中松手：东西掉下
            foreach (var t in toys) if (t.state == ToyStates.Pulled && t.pull.side == side) DropToy(t, Vector3.zero, "pullBreak");
            foreach (var c in candies) if (c.state == CandyStates.Pulled && c.pull.side == side) DropCandy(c, Vector3.zero);
            var held = GetHeld(side);
            if (held == null) return;
            // 两只手抱着熊时，一只手的捏合闪断一下（追踪抖动）不算松手：0.15 秒内又捏上就当没松（10-31 审查：熊动不动整只掉下）
            if (held.isToy && held.Toy.Kind.twoHand && held.Toy.grips != null && held.Toy.grips.Both) { frankLetGo[side] = time; return; }
            SetHeld(side, null);
            if (held.isToy && held.Toy.Kind.twoHand) { ReleaseFrankGrip(held.Toy, side, input); return; }
            var hh = FindHand(input, side);
            var hp = hh != null && hh.hasPoint ? hh.point : held.obj.pos;
            var H = cfg.hand;
            var vel = HandVelocity(side) * H.throwMul;
            float sp = vel.magnitude;
            if (sp > H.throwMax) vel = vel * (H.throwMax / sp);

            if (!held.isToy)
            {
                var c = held.Candy;
                if (Vec.DistXZ(hp, JAR.pos) <= JAR.radius + 0.15f + magnetBonus && hp.y >= JAR.y && hp.y < JAR.y + JAR.height + 0.45f) { CandyToJar(c); return; }   // 10-29：桶口附近松手就吸进去
                DropCandy(c, vel);
                return;
            }
            var toy = held.Toy;
            toy.shake = null;
            // 用捏合点判定（敌人吊在手下方）：手在坩埚口上方、XZ 稍放宽，就算放进去了
            // 手在锅口上方附近松手 = 放进锅（范围放宽：锅外缘再往外 9 厘米、锅口以上半米内都算）
            if (Vec.DistXZ(hp, BOX.pos) <= BOX.half + 0.09f && hp.y >= BOX.y && hp.y < BOX.y + BOX.height + 0.5f) { PutInBox(toy); return; }
            if (toy.charged)
            {
                var pv = PeakHandVelocity(side);
                if (pv.magnitude >= cfg.launch.minSpeed) { LaunchToStar(toy, pv); return; }
            }   // 10-29 用户：发金光后轻轻一甩就飞（阈值降到 0.6m/s）
            toy.chargeT = 0; toy.charged = false;
            bool thrown = vel.magnitude >= H.throwSpeed;
            // 慢速松手 = 轻轻放下：去掉水平速度，落在手正下方
            if (!thrown) vel = new Vector3(0, Mathf.Min(0, vel.y), 0);
            // 扔向坩埚：大致扔对方向就修正到锅口正中（吸附），扔歪太多不管
            toy.potAim = false;
            if (thrown && !toy.Kind.twoHand && AimAtPot(toy.pos + Vector3.up * toy.Kind.height * 0.5f, ref vel)) { toy.potAim = true; var ae = Emit(GameEventTypes.PotAssist).At(toy.pos); ae.id = toy.id; }
            if (thrown) { stats.thrown++; var e = Emit(GameEventTypes.Throw).At(toy.pos); e.id = toy.id; e.speed = vel.magnitude; }
            DropToy(toy, vel, thrown ? "throw" : "release");
            if (thrown) toy.dizzy = MathD.Max(toy.dizzy, 1.2);
        }

        // 蓄力扔出：沿抛出方向飞起，再弧线飞向窗外，飞出去变成夜空里的一颗星（永久消灭）
        void LaunchToStar(Toy t, Vector3 vel)
        {
            t.state = ToyStates.Launched;
            t.pull = null;
            t.shake = null;
            t.grips = null;
            t.launch = new LaunchState { t = 0, from = t.pos, dir = Vec.Norm(vel) };
            // 窗口上随机一点（两次 rand 的顺序与 JS 一致：先 x 后 y）
            float rx = rand();
            float ry = rand();
            t.launch.to = new Vector3(WINDOW.x + (rx - 0.5f) * WINDOW.w * 0.6f, WINDOW.y + (ry - 0.3f) * WINDOW.h * 0.5f, WINDOW.z - 0.5f);
            // 10-31 防穿模：弧线会撞上摆件（藤架立柱、椽子……）时，换铁拱门上的其他目标点再试，挑飞得最完整的；都不行就在撞上前变成星星
            t.launch.endK = FlightEnd(t.launch, t);
            if (solid != null && t.launch.endK < 1f)
            {
                var keep = t.launch.to; float bestK = t.launch.endK;
                for (int i = 0; i < 12 && bestK < 1f; i++)
                {
                    float u = (i % 4) / 3f, v = (i / 4) / 2f;
                    t.launch.to = new Vector3(WINDOW.x + (u - 0.5f) * WINDOW.w * 0.8f, WINDOW.y + (v - 0.2f) * WINDOW.h * 0.6f, WINDOW.z - 0.5f);
                    float k = FlightEnd(t.launch, t);
                    if (k > bestK) { bestK = k; keep = t.launch.to; }
                }
                t.launch.to = keep; t.launch.endK = bestK;
            }
            stats.thrown++;
            var e = Emit(GameEventTypes.Launch).At(t.pos); e.id = t.id;
            // 抱着的糖在飞出去时撒下来
            if (t.candyCount > 0) LoseCandyFrom(t, "launch");
        }

        void DropToy(Toy t, Vector3 vel, string reason)
        {
            t.state = ToyStates.Falling;
            t.pull = null;
            t.vel = vel;
            if (reason == "pullBreak") { var e = Emit(GameEventTypes.PullBreak).At(t.pos); e.id = t.id; }
        }

        void DropCandy(Candy c, Vector3 vel)
        {
            c.state = CandyStates.Falling;
            c.pull = null;
            c.vel = vel;
        }

        void CandyToJar(Candy c)
        {
            c.state = CandyStates.Gone;
            candyJar++;
            stats.recovered++;
            var e = Emit(GameEventTypes.CandyBack); e.id = c.id;
        }

        // 敌人手里的糖掉出来
        /// <summary>测试用：让小鬼把怀里的糖全部掉出来。</summary>
        public void LoseCandyFromForTest(Toy t) => LoseCandyFrom(t, "test");
        public void LaunchForTest(Toy t, Vector3 v) => LaunchToStar(t, v);
        public void BlowForTest(Toy t, Vector3 dir) => BlowAway(t, dir);

        void LoseCandyFrom(Toy t, string reason, int n = int.MaxValue)
        {
            int drop = Mathf.Min(n, t.candyCount);
            if (drop <= 0) return;
            t.candyCount -= drop;
            t.hasCandy = t.candyCount > 0;
            var k = t.Kind;
            var p = new Vector3(t.pos.x, t.pos.y + k.height * 0.9f, t.pos.z);
            for (int i = 0; i < drop; i++)
            {
                // 掉出来的糖一定落在桌面上（10-28 实机反馈：掉到地上就捡不回来了）：
                // 落点 = 离小鬼最近的桌面点（桌沿往里 12 厘米）再随机偏一点，按抛物线算出初速度飞过去
                float a = rand() * Mathf.PI * 2;
                var land = CandyLandingOnTable(t.pos) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (0.05f + 0.07f * rand());
                land = AvoidProps(ClampIntoTable(land, 0.1f));
                var c = SpawnCandy(p, BallisticTo(p, land));
                var e = Emit(GameEventTypes.CandyDrop).At(p); e.id = t.id; e.candy = c.id; e.reason = reason;
            }
            // 偷到糖往回跑的敌人丢了糖，就重新去找糖
            if (!t.hasCandy && t.state == ToyStates.Flee) Retarget(t);
        }

        // ---------- 被拿着 ----------
        void UpdateHeld(InputFrame input, double dt)
        {
            var SH = cfg.shake;
            var done = new HashSet<Toy>();
            foreach (var side in BOTH_SIDES)
            {
                var held = GetHeld(side);
                if (held == null) continue;
                var h = FindHand(input, side);
                if (h == null)
                {
                    SetHeld(side, null);
                    if (held.isToy && held.Toy.Kind.twoHand) { ReleaseFrankGrip(held.Toy, side, input); continue; }
                    if (held.isToy) { held.Toy.lostAt = time; DropToy(held.Toy, Vector3.zero, "lost"); }
                    else DropCandy(held.Candy, Vector3.zero);
                    continue;
                }
                if (!held.isToy)
                {
                    held.obj.pos = new Vector3(h.point.x, h.point.y - 0.02f, h.point.z);
                    RestOnTableTop(held.obj, CANDY_R);
                    continue;
                }
                var t = held.Toy;
                var k = t.Kind;
                if (done.Contains(t)) continue;
                done.Add(t);
                if (k.twoHand)
                {
                    // 松手宽限：超过 0.15 秒还没再捏上，才真的松开
                    bool released = false;
                    foreach (var s2 in BOTH_SIDES)
                    {
                        if (!frankLetGo.TryGetValue(s2, out var lt)) continue;   // 又捏上了会在 OnPinchStart 里取消
                        if (time - lt < 0.15) continue;
                        frankLetGo.Remove(s2);
                        if (GetHeld(s2) != null && GetHeld(s2).obj == t) SetHeld(s2, null);
                        ReleaseFrankGrip(t, s2, input);
                        released = true;
                        break;
                    }
                    if (released) continue;
                    var L = FindHand(input, Sides.Left);
                    var R = FindHand(input, Sides.Right);
                    if (t.state == ToyStates.Held && L != null && R != null)
                    {
                        // 两只手中点决定位置
                        var mid = (L.point + R.point) * 0.5f;
                        t.pos = new Vector3(mid.x, mid.y - k.height * 0.5f, mid.z);   // 10-31：吊在手下 0.5 个身高（原 0.75，要举到胸口才离桌）
                        RestOnTableTop(t, 0);
                    }
                    else
                    {
                        // 只抓住一只手：拖慢它，不能举起；手离太远就脱手
                        var grip = t.grips?.Get(side);
                        if (grip.HasValue && Vector3.Distance(h.point, t.pos + grip.Value) > 0.25f) { SetHeld(side, null); ReleaseFrankGrip(t, side, input); continue; }
                        t.dragged = true;
                        continue;
                    }
                }
                else
                {
                    t.pos = new Vector3(h.point.x, h.point.y - k.height * 0.95f, h.point.z);
                    RestOnTableTop(t, 0);
                }
                t.walkPhase += (float)(dt * 14); // 被拎着时蹬腿
                // 晃动检测：手速超过阈值时，方向反转计一次
                var v = k.twoHand ? (HandVelocity(Sides.Left) + HandVelocity(Sides.Right)) * 0.5f : HandVelocity(side);
                float sp = v.magnitude;
                var sh = t.shake;
                sh.wiggle = MathD.Max(0, sh.wiggle - dt * 2);
                // 蓄力：捏住、手基本不动（< 0.25m/s），攒满后发光；一晃就清零（晃和蓄力是两种用法）。
                // 拎着走（搬去锅里、桌上）时不蓄力：10-29 甩出阈值降到 0.6m/s 后，搬着走一会儿再放下会被误当成甩出去
                // 10-31 审查：发金光之后不再被“晃动”清掉——人甩东西前会先往回带一下，这一下反向会被算成晃动，金光一灭就甩不成星星了
                if (t.charged || (sh.wiggle < 0.15f && sp < 0.25f))
                {
                    t.chargeT += dt;
                    if (!t.charged && t.chargeT >= cfg.launch.chargeSec)
                    {
                        t.charged = true;
                        var e = Emit(GameEventTypes.Charged).At(t.pos); e.id = t.id;
                        HintOnce("charged", "它在发光！用力甩出去，送它去天上当星星。", 3.5f);
                    }
                }
                else { t.chargeT = 0; t.charged = false; }
                // 发金光后捏着用力一甩：不等松手，直接飞出去变星星（往下甩不算，免得搬去坩埚时误发）
                if (t.charged && !k.twoHand && sp >= cfg.launch.flingSpeed && v.y > -0.2f * sp)
                {
                    SetHeld(side, null);
                    LaunchToStar(t, v);
                    continue;
                }
                if (sp > SH.minSpeed && !t.charged)
                {
                    var dir = v * (1f / sp);
                    if (sh.lastDir.HasValue && Vector3.Dot(dir, sh.lastDir.Value) < -0.3f)
                    {
                        double now = time;
                        sh.flips.RemoveAll(x => !(now - x < SH.windowSec * SH.reversals));
                        sh.flips.Add(time);
                        sh.wiggle = MathD.Min(1, sh.wiggle + 0.4);
                        var e = Emit(GameEventTypes.Wiggle); e.id = t.id; e.n = sh.flips.Count;
                    }
                    sh.lastDir = dir;
                }
                if (sh.flips.Count >= SH.reversals)
                {
                    sh.flips.Clear();
                    stats.shaken++;
                    t.dizzy = MathD.Max(t.dizzy, SH.dizzySec);
                    // 缝合熊一次只晃掉一颗
                    if (t.candyCount > 0) LoseCandyFrom(t, "shake", k.twoHand ? 1 : int.MaxValue);
                    var e = Emit(GameEventTypes.Shaken).At(t.pos); e.id = t.id;
                }
            }
            foreach (var t in toys) if (t.grips == null || t.grips.Both) t.dragged = false;
        }

        // 离 p 最近的桌面点（桌沿往里 inset）；p 本来就在桌面范围内则就地
        static Vector3 CandyLandingOnTable(Vector3 p) => ClampIntoTable(new Vector3(p.x, TABLE.y + CANDY_R, p.z), 0.12f);

        static Vector3 ClampIntoTable(Vector3 p, float inset)
        {
            var d = new Vector3(p.x - TABLE.center.x, 0, p.z - TABLE.center.z);
            float r = TABLE.radius - inset;
            if (d.magnitude > r) d = d.normalized * r;
            return new Vector3(TABLE.center.x + d.x, TABLE.y + CANDY_R, TABLE.center.z + d.z);
        }

        // 扔坩埚的吸附：按当前速度预测抛物线，在下落穿过锅口高度那一刻离锅口中心不超过 POT_ASSIST，
        // 而且水平方向大体朝着锅（夹角 < 50°），就改成正好落进锅口中心的速度（飞行时间不变，手感不变）。
        public const float POT_ASSIST = 0.42f;   // 扔远扔近 42 厘米内都吸进去（坐着扔，力道很难控制；10-29 锅缩小后锅口更低，同样力道落得更远）
        static bool AimAtPot(Vector3 from, ref Vector3 vel)
        {
            // from = 小鬼身体中心；入锅判定也是用身体中心穿过锅口高度（CrossedBoxTop），所以瞄锅口下方 2 厘米
            var mouth = new Vector3(BOX.x, BOX.y + BOX.height - 0.02f, BOX.z);
            var toPot = new Vector3(mouth.x - from.x, 0, mouth.z - from.z);
            var flatV = new Vector3(vel.x, 0, vel.z);
            if (toPot.magnitude > 1.2f || flatV.magnitude < 0.2f) return false;
            if (Vector3.Angle(toPot, flatV) > 50f) return false;
            // 解 y(t) = mouth.y 的较大根（下落那一次）
            float a = -4.9f, b = vel.y, c = from.y - mouth.y;
            float disc = b * b - 4 * a * c;
            if (disc < 0) return false;
            float T = (-b - Mathf.Sqrt(disc)) / (2 * a);
            if (T <= 0.05f || T > 2f) return false;
            var hit = new Vector3(from.x + vel.x * T, mouth.y, from.z + vel.z * T);
            if (Vec.DistXZ(hit, mouth) > POT_ASSIST) return false;
            var d = mouth - from;
            vel = new Vector3(d.x / T, (d.y + 4.9f * T * T) / T, d.z / T);
            return true;
        }

        // 落点别落进糖桶、坩埚、烛台（糖桶口会把它直接收走，坩埚里捡不出来）：落在它们圈内就推到圈外
        static Vector3 AvoidProps(Vector3 p)
        {
            p = PushOut(p, JAR.pos, JAR.radius + 0.06f);
            p = PushOut(p, BOX.pos, BOX.half + 0.06f);
            if (CANDLE.on) p = PushOut(p, CANDLE.pos, 0.12f);
            return ClampIntoTable(p, 0.1f);
        }

        static Vector3 PushOut(Vector3 p, Vector3 c, float r)
        {
            var d = new Vector3(p.x - c.x, 0, p.z - c.z);
            if (d.magnitude >= r) return p;
            if (d.sqrMagnitude < 1e-6f) d = new Vector3(p.x - TABLE.center.x, 0, p.z - TABLE.center.z);
            d = (d.sqrMagnitude < 1e-6f ? Vector3.right : d.normalized) * r;
            return new Vector3(c.x + d.x, p.y, c.z + d.z);
        }

        // 从 from 抛到 to 的初速度：飞行时间随距离 0.45~1.0 秒，先往上弹一下再落
        static Vector3 BallisticTo(Vector3 from, Vector3 to)
        {
            var d = to - from; var flat = new Vector3(d.x, 0, d.z);
            float T = Mathf.Clamp(0.45f + flat.magnitude * 0.35f, 0.45f, 1.0f);
            return new Vector3(flat.x / T, (d.y + 0.5f * 9.8f * T * T) / T, flat.z / T);
        }

        // 拎着的东西在桌子上方时，脚（糖的底）不会低于桌面：手压得再低，它也只是踩在桌上，不会陷进桌板
        static void RestOnTableTop(Body b, float lift)
        {
            float top = TABLE.y + lift;
            // 身子还有一部分在桌面上方（中心伸出桌沿 8 厘米以内）也算在桌上：拎着小鬼贴着桌沿走时不会从桌板侧面穿进去（10-30 实机）
            if (OnTable(b.pos, -0.08f) && b.pos.y < top && b.pos.y > TABLE.y - TABLE_SLAB - 0.25f) b.pos.y = top;
        }

        // ---------- 隔空取物 ----------
        void UpdatePulled(InputFrame input, double dt)
        {
            var H = cfg.hand;
            var items = new List<Body>();
            foreach (var t in toys) if (t.state == ToyStates.Pulled) items.Add(t);
            foreach (var c in candies) if (c.state == CandyStates.Pulled) items.Add(c);
            foreach (var obj in items)
            {
                var toy = obj as Toy;
                bool isToy = toy != null;
                var h = FindHand(input, obj.pull.side);
                if (h == null || GetHeld(obj.pull.side) != null) { FailPull(obj); continue; }
                // 飞来途中视线很难精确跟住：放宽到 pullConeDeg，且允许短暂脱离
                var c = isToy ? Center(toy) : obj.pos;
                bool on = input.gaze != null && input.gaze.valid && Vec.AngleDeg(input.gaze.dir, c - input.gaze.origin) <= H.pullConeDeg;
                obj.pull.lost = on ? 0 : obj.pull.lost + dt;
                // 飞过半程以后不再因为视线离开而掉（10-30 实机：拉到半路小鬼掉下去看不见了，像是“消失了”）
                if (obj.pull.lost > H.pullGraceSec && obj.pull.t < 0.5) { FailPull(obj); continue; }
                obj.pull.t += dt / H.pullSec;
                float k = (float)MathD.Min(1, obj.pull.t);
                float e = k * k * (3 - 2 * k);
                var target = isToy
                    ? new Vector3(h.point.x, h.point.y - toy.Kind.height * 0.95f, h.point.z)
                    : new Vector3(h.point.x, h.point.y - 0.02f, h.point.z);
                // 弧线飞来：二次贝塞尔（起点 → 控制点 → 手），控制点在起点上方、桌面以下出发时在桌沿外，整条路线不穿桌板
                float u1 = 1 - e;
                var mid = obj.pull.from * (u1 * u1) + obj.pull.ctrl * (2 * u1 * e) + target * (e * e);
                obj.pos = mid;
                if (k >= 1)
                {
                    if (isToy) GrabToy(toy, obj.pull.side, h);
                    else GrabCandy((Candy)obj, obj.pull.side);
                }
            }
        }

        /// <summary>
        /// 隔空取物的目标（10-30 实机：“捏了却拉不过来”——人捏的那一下眼睛常会瞟向自己的手，捏的瞬间已经不在看小鬼了）：
        /// ① 最近 0.5 秒内看过的、能抓的小鬼（取最后看的那只）；② 没有的话用手瞄：从头经过捏合点的方向 12° 内最近的能抓的小鬼
        /// </summary>
        Toy PullTarget(HandInput h, InputFrame input)
        {
            bool Ok(Toy t) => GAZEABLE.Contains(t.state) && t.state != ToyStates.Pulled && t.lit && !IsHeld(t) && CanGrab(t) && !t.Kind.twoHand && time - t.lostAt > 1.0;
            Toy best = null; double last = -1;
            foreach (var t in toys) if (Ok(t) && time - t.lastDirect <= 0.5 && t.lastDirect > last) { last = t.lastDirect; best = t; }
            if (best != null || input?.head == null) return best;
            var dir = h.point - input.head.pos; if (dir.sqrMagnitude < 1e-6f) return null;
            float bestA = 12f;
            foreach (var t in toys)
            {
                if (!Ok(t)) continue;
                var to = Center(t) - input.head.pos;
                float angR = Mathf.Atan2(Mathf.Max(t.Kind.height, t.Kind.radius * 2) * 0.6f, to.magnitude) * Mathf.Rad2Deg;
                float a = Vec.AngleDeg(dir, to) - angR;
                if (a < bestA) { bestA = a; best = t; }
            }
            return best;
        }

        /// <summary>隔空取物的路线控制点：起点上方 25 厘米；起点在桌面以下（地上、爬藤上）时先挪到桌沿外再升到桌面以上</summary>
        static Vector3 PullCtrl(Vector3 from, Vector3 hand)
        {
            var c = new Vector3(from.x, Mathf.Max(from.y, hand.y) + 0.25f, from.z);
            if (from.y < TABLE.y)
            {
                var off = new Vector3(from.x - TABLE.center.x, 0, from.z - TABLE.center.z);
                if (off.sqrMagnitude < 1e-6f) off = Vector3.forward;
                var rim = TABLE.center + off.normalized * (TABLE.radius + 0.15f);
                c = new Vector3(rim.x, Mathf.Max(TABLE.y + 0.35f, hand.y + 0.15f), rim.z);
            }
            return c;
        }

        void FailPull(Body obj)
        {
            if (obj is Toy t) DropToy(t, Vector3.zero, "pullBreak");
            else DropCandy((Candy)obj, Vector3.zero);
        }
    }
}
