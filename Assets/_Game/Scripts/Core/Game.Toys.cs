// 敌人行为状态机（走、偷、逃、飘落、晕、下落、飞向窗外）与散落的糖。
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        // 速度用 double 计算（倍率连乘与 JS 一致），最后由 StepPath 转成 float 位移
        double MoveSpeed(Toy t)
        {
            var S = cfg.speed;
            var k = t.Kind;
            double zoneMul = t.zone == Zones.Outside ? S.outside : S.peripheral;
            if (k.freeze == FreezeRules.Inverse) zoneMul = t.scared ? k.gazedSpeed : S.outside * 0.8;
            double nightMul = night.speedMul != 0 ? night.speedMul : 1;
            double speed = (double)S.@base * k.speed * zoneMul * t.speedMul * nightMul * DirectorSpeedMul() * SlowMul;
            if (InLamp(Center(t))) speed *= cfg.skills.lamp.slowMul;
            if (t.dragged) speed *= 0.25;
            return speed;
        }

        void UpdateToys(double dt)
        {
            // JS 用 for...of 遍历数组；循环中 spawnToy 不会被调用（分裂发生在 updateHeld），这里用下标遍历避免枚举器失效
            for (int i = 0; i < toys.Count; i++)
            {
                var t = toys[i];
                var k = t.Kind;
                if (t.slipCd > 0) t.slipCd -= dt;
                if (t.dizzy > 0 && t.state != ToyStates.Held && t.state != ToyStates.Falling) t.dizzy = MathD.Max(0, t.dizzy - dt);
                switch (t.state)
                {
                    case ToyStates.Walk:
                    case ToyStates.Flee:
                    {
                        if (t.frozen)
                        {
                            // 飞行敌人被定住：缓缓飘落（可以被接住或隔空取）
                            if (k.mover == Movers.Fly && t.pos.y > TABLE.y + 0.02f) { t.state = ToyStates.Glide; t.vel = Vector3.zero; }
                            // 10-31 舒适：蜘蛛在高处被定住时顺着丝慢慢滑到大约视线高度再停（不用一直仰着头去盯、去拉）
                            if (k.mover == Movers.Drop && t.pos.y > SPIDER_REST_Y) t.pos.y = Mathf.Max(SPIDER_REST_Y, (float)(t.pos.y - 0.2 * dt));
                            break;
                        }
                        if (t.state == ToyStates.Walk) MaybeRetarget(t, dt);
                        double speed = MoveSpeed(t);
                        if (t.state == ToyStates.Flee) speed *= 1.1;
                        if (k.mover == Movers.Walk)
                        {
                            // 地面上小跑（大多被桌子挡住），爬藤略放慢让玩家有反应时间
                            if (t.pos.y < 0.02f) speed *= 2.2;
                            else if (OnClimbSegment(t)) speed *= 0.8;
                        }
                        else speed *= 1.3;
                        // 糖桶被提在手里：飞的敌人改追着桶飞（航线最后两点跟着桶口走）；桌上走的够不着，见 Steal
                        if (k.mover == Movers.Fly && t.state == ToyStates.Walk && t.goal != null && t.goal.type == Goal.Jar && t.path != null && t.path.Count >= 2)
                        {
                            var mouth = FlyMouth(t);
                            t.path[t.path.Count - 1] = mouth;
                            if (t.path.Count >= 3 && t.seg < t.path.Count - 2) t.path[t.path.Count - 2] = new Vector3(mouth.x, Mathf.Max(mouth.y, k.flyY), mouth.z);
                        }
                        t.walkPhase += (float)(dt * (8 + speed * 20));
                        if (StepPath(t, (float)(speed * dt))) OnArrive(t);
                        break;
                    }
                    case ToyStates.Launched:
                    {
                        var L = t.launch;
                        L.t += dt / (L.blow ? cfg.launch.blowSec : cfg.launch.flySec);
                        float k2 = (float)MathD.Min(L.endK, L.t);
                        t.pos = LaunchPos(L, k2);
                        t.walkPhase += (float)(dt * 30);
                        if (k2 < L.endK) break;
                        t.state = ToyStates.Gone;
                        if (L.blow)
                        {
                            stats.blownAway++;
                            if (night != null && night.duration - clock > 15) director.returns.Add((time + 10, t.kind));
                            var be = Emit(GameEventTypes.BlownAway).At(t.pos); be.id = t.id; be.kind = t.kind;
                        }
                        else
                        {
                            stats.stars++;
                            if (k.twoHand) { stats.bossDown++; var bg = Emit(GameEventTypes.BossGone).At(t.pos); bg.id = t.id; bg.star = true; }
                            var se = Emit(GameEventTypes.Star).At(t.pos); se.id = t.id; se.kind = t.kind;
                        }
                        break;
                    }
                    case ToyStates.Glide:
                    {
                        // 被定住时下落（定身解除就重新飞起来）
                        if (!t.frozen)
                        {
                            t.state = t.hasCandy ? ToyStates.Flee : ToyStates.Walk;
                            t.seg = 0;
                            t.path = Safe(t.hasCandy ? FlyAway(t.pos, t.spawn) : FlyToJar(t.pos, k), t);
                            break;
                        }
                        float floor = OnTable(t.pos) ? TABLE.y : 0;
                        float ny = Mathf.Max(floor, (float)(t.pos.y - k.glide * dt));
                        // 10-31 防穿模：正下方有场景摆件（矮墙、石瓮、土坡……）就落在它顶上，不往里沉
                        if (solid != null && ny > floor && solid.SolidSphere(new Vector3(t.pos.x, ny + k.height * 0.5f, t.pos.z), k.BodyR)) { Land(t, t.pos.y); break; }
                        t.pos.y = ny;
                        t.walkPhase += (float)(dt * 3);
                        if (t.pos.y <= floor + 1e-4f)
                        {
                            if (floor == 0 && solid != null && !solid.GroundOk(t.pos, k.BodyR > 0.15f)) t.pos = solid.NearestFreeGround(t.pos, 2.5f, k.BodyR > 0.15f);
                            Land(t, floor);
                        }
                        break;
                    }
                    case ToyStates.Steal:
                    {
                        var jarC = bucketHeld != null ? bucketPos : JAR.pos;
                        t.yaw = Mathf.Atan2(jarC.x - t.pos.x, jarC.z - t.pos.z);
                        // 飞的在糖桶上空悬停偷糖时被盯住：和飞行途中一样缓缓飘落（10-30 实机：蝙蝠被看住就定在半空不下来）
                        if (t.frozen && k.mover == Movers.Fly && t.pos.y > TABLE.y + 0.02f) { t.state = ToyStates.Glide; t.vel = Vector3.zero; t.stealT = 0; break; }
                        if (t.frozen || t.noSteal) break;
                        if (bucketHeld != null)
                        {
                            // 糖桶提起来了：桌上的敌人够不着（10-30 用户），空中飞的跟着桶口追、追上了照样能偷
                            if (k.mover != Movers.Fly) break;
                            var m = FlyMouth(t);
                            t.pos = Vector3.MoveTowards(t.pos, m, (float)(dt * 1.2));
                            if (Vector3.Distance(t.pos, m) > 0.06f) { t.stealT = 0; break; }
                        }
                        if (shieldT > 0) { t.stealT = 0; break; }
                        if (candyJar <= 0)
                        {
                            // 糖桶空了：手里有糖就带走，没有就去找地上的糖
                            if (t.hasCandy) StartFlee(t);
                            else Retarget(t);
                            break;
                        }
                        t.stealT += dt * SlowMul;
                        t.walkPhase += (float)(dt * 10);
                        double stealSec = k.stealSec > 0 ? k.stealSec : cfg.candy.stealSec;
                        if (t.stealT >= stealSec)
                        {
                            t.stealT = 0;
                            candyJar--;
                            t.candyCount++;
                            t.hasCandy = true;
                            var se = Emit(GameEventTypes.Steal).At(t.pos); se.id = t.id; se.left = candyJar; se.kind = t.kind;
                            HintOnce("stealSeen", "它偷到糖了！捏住它，晃一晃。", 3.5f);
                            // 缝合熊一次能抱好几颗，抱满才走
                            if (t.candyCount >= (k.steal > 0 ? k.steal : 1) || candyJar <= 0) StartFlee(t);
                        }
                        break;
                    }
                    case ToyStates.Stunned:
                    {
                        t.stun -= dt;
                        if (t.stun <= 0 && t.dizzy <= 0 && !t.frozen)
                        {
                            if (t.knockout)
                            {
                                t.state = ToyStates.Gone;
                                var bg = Emit(GameEventTypes.BossGone).At(t.pos); bg.id = t.id;
                                LoseCandyFrom(t, "ko");
                                break;
                            }
                            Retarget(t);
                        }
                        break;
                    }
                    case ToyStates.Falling:
                    {
                        var prev = t.pos;
                        t.vel.y -= (float)(9.8 * dt);
                        t.pos = t.pos + MathD.Mul(t.vel, dt);
                        var mid = new Vector3(t.pos.x, t.pos.y + k.height * 0.5f, t.pos.z);
                        if (t.vel.y <= 0 && !k.twoHand && CrossedBoxTop(new Vector3(prev.x, prev.y + k.height * 0.5f, prev.z), mid)) { PutInBox(t); break; }
                        if (!k.twoHand && !t.potAim) PushOutOfPot(t);
                        // 10-31 防穿模：被扔飞、掉落时撞上场景摆件——水平方向弹回；落在摆件顶上就往最近的空地滑下去。
                        // 检查这一帧走过的整段（快速飞行时不会一帧穿过薄的墓碑）
                        if (solid != null)
                        {
                            float r = k.BodyR;
                            var up = Vector3.up * (k.height * 0.5f);
                            if (solid.FirstHit(prev + up, t.pos + up, r) >= 0)
                            {
                                var hp = new Vector3(prev.x, t.pos.y, prev.z);
                                if (!solid.SolidSphere(hp + up, r) && solid.FirstHit(prev + up, hp + up, r) < 0)
                                {
                                    t.pos = hp;
                                    if (Mathf.Abs(t.vel.x) + Mathf.Abs(t.vel.z) > 0.8f) { var we = Emit(GameEventTypes.WallHit).At(t.pos); we.id = t.id; }
                                    t.vel.x *= -0.3f; t.vel.z *= -0.3f;
                                }
                                else
                                {
                                    // 落在摆件顶上：停在上一帧（那里是空的），一步一步往最近的空地挪，每一步都要是空的
                                    t.pos = prev; t.vel = new Vector3(0, Mathf.Max(0, t.vel.y), 0);
                                    bool big = k.BodyR > 0.15f;
                                    var free = solid.NearestFreeGround(t.pos, 2.5f, big); var dv = free - t.pos; dv.y = 0;
                                    if (dv.sqrMagnitude > 1e-6f)
                                    {
                                        bool moved = false;
                                        foreach (float rot in new[] { 0f, 35f, -35f, 70f, -70f, 110f, -110f })
                                        {
                                            var step = Quaternion.Euler(0, rot, 0) * dv.normalized * Mathf.Min(dv.magnitude, 0.04f);
                                            var cand = t.pos + step;
                                            if (!solid.SolidSphere(cand + up, r)) { t.pos = cand; moved = true; break; }
                                        }
                                        if (!moved)
                                        {
                                            // 四面都被挡：直接挪到那片空地的正上方（同一高度空着就保持高度，否则落到地上）
                                            var top = new Vector3(free.x, t.pos.y, free.z);
                                            t.pos = !solid.SolidSphere(top + up, r) ? top : new Vector3(free.x, 0f, free.z);
                                        }
                                    }
                                }
                                mid = new Vector3(t.pos.x, t.pos.y + k.height * 0.5f, t.pos.z);
                            }
                        }
                        // 落桌：只要上一帧身体中心还在桌面以上就算落在桌上——拎着放到桌上时它吊在手下方，脚常常已经低于桌面，
                        // 以前要求“脚在桌面以上”才落桌，结果直接穿过桌板掉到桌下
                        if (!t.blown && prev.y + k.height * 0.5f >= TABLE.y - 0.001f && t.pos.y <= TABLE.y && OnTable(t.pos, 0.02f)) Land(t, TABLE.y);
                        else if (t.blown && t.pos.y < TABLE.y - 0.12f && !OnTable(t.pos, 0.05f))
                        {
                            // 被狂风吹出桌沿、掉下去了：“噗”地消失（10-29 用户：吹走的敌人掉下桌才消失，不再爬回来）
                            t.state = ToyStates.Gone;
                            stats.blownAway++;
                            var be = Emit(GameEventTypes.BlownAway).At(t.pos); be.id = t.id; be.kind = t.kind;
                            break;
                        }
                        else if (t.pos.y <= 0)
                        {
                            if (solid != null && !solid.GroundOk(t.pos, k.BodyR > 0.15f)) t.pos = solid.NearestFreeGround(t.pos, 2.5f, k.BodyR > 0.15f);   // 落点贴着摆件 / 掉进摆件之间封死的小坑：挪到最近的能站的地
                            Land(t, 0);
                        }
                        if (ClampRoom(t) > 0.5f) { var we = Emit(GameEventTypes.WallHit).At(t.pos); we.id = t.id; }
                        break;
                    }
                }
            }
            // 消失的敌人保留 3 秒（给表现层播离场动画）再移除
            toys.RemoveAll(t =>
            {
                if (t.state != ToyStates.Gone) return false;
                if (!t.goneAt.HasValue) t.goneAt = time;
                return !(time - t.goneAt.Value < 3);
            });
        }

        // 花园边界就是看得见的矮墙和栅栏：撞上去反弹，返回是否撞到（撞击速度）
        static float ClampRoom(Body o)
        {
            float hit = 0;
            if (o.pos.x < ROOM.minX || o.pos.x > ROOM.maxX) { hit = Mathf.Max(hit, Mathf.Abs(o.vel.x)); o.pos.x = Vec.Clamp(o.pos.x, ROOM.minX, ROOM.maxX); o.vel.x *= -0.3f; }
            if (o.pos.z < ROOM.minZ || o.pos.z > ROOM.maxZ) { hit = Mathf.Max(hit, Mathf.Abs(o.vel.z)); o.pos.z = Vec.Clamp(o.pos.z, ROOM.minZ, ROOM.maxZ); o.vel.z *= -0.3f; }
            return hit;
        }

        public void StartFlee(Toy t)
        {
            t.state = ToyStates.Flee;
            t.seg = 0;
            string m = t.Kind.mover;
            // 蜘蛛：沿丝爬回天花板；其他：飞回或走回出生点
            t.path = Safe(m == Movers.Fly ? FlyAway(t.pos, t.spawn) : m == Movers.Drop ? ClimbBack(t.pos, t.spawn) : PlanToSpawn(t.pos, t.spawn), t);
        }

        void OnArrive(Toy t)
        {
            var k = t.Kind;
            if (k.mover == Movers.Drop && t.state == ToyStates.Walk)
            {
                if (t.respawnDrop)
                {
                    // 爬回天花板：换一个垂降点重新下来
                    t.respawnDrop = false;
                    var sp = DROP_SPAWNS[RandIndex(DROP_SPAWNS.Length)];
                    t.spawn = sp;
                    t.pos = HangPoint(sp.pos, k);
                    t.path = DropPath(t.pos);
                    t.seg = 0;
                    return;
                }
                if (t.pos.y > TABLE.y + 0.01f) return;
                // 刚垂降到桌面：走最后一小段到糖桶
                if (t.path.Count == 2 && t.path[0].y > TABLE.y + 0.5f) { t.path = new System.Collections.Generic.List<Vector3> { t.pos, JarApproach(t.pos, k.radius) }; t.seg = 0; return; }
            }
            if (t.state == ToyStates.Flee)
            {
                t.state = ToyStates.Gone;
                candyLost += t.candyCount;
                stats.lost += t.candyCount;
                t.candyCount = 0;
                t.hasCandy = false;
                director.recentLoss.Add(time);
                var e = Emit(GameEventTypes.CandyLost).At(t.pos); e.id = t.id;
                return;
            }
            if (t.goal.type == Goal.CandyGoal)
            {
                var c = t.goal.candy;
                if (c.state == CandyStates.Rest && Vector3.Distance(c.pos, t.pos) < 0.12f)
                {
                    c.state = CandyStates.Gone;
                    t.candyCount++;
                    t.hasCandy = true;
                    StartFlee(t);
                    var e = Emit(GameEventTypes.PickCandy).At(t.pos); e.id = t.id; e.candy = c.id;
                }
                else Retarget(t);
                return;
            }
            t.state = ToyStates.Steal;
            t.stealT = 0;
            var re = Emit(GameEventTypes.ReachJar); re.id = t.id;
        }

        // 选目标：散落的糖（如果比糖桶近）或糖桶
        void Retarget(Toy t)
        {
            var k = t.Kind;
            t.seg = 0;
            t.replanT = 0.6f;
            if (t.hasCandy && (t.candyCount >= (k.steal > 0 ? k.steal : 1) || candyJar <= 0)) { StartFlee(t); return; }
            t.state = ToyStates.Walk;
            if (k.mover == Movers.Fly)
            {
                // 飞行敌人：落地后先飞起来
                t.goal = Goal.ToJar();
                t.path = Safe(FlyToJar(t.pos, k), t);
                return;
            }
            if (k.mover == Movers.Drop)
            {
                // 蜘蛛：在桌上就直接走向糖桶；在地上就爬回天花板重新垂降
                t.goal = Goal.ToJar();
                bool on = IsOnTable(t.pos);
                t.path = Safe(on ? new System.Collections.Generic.List<Vector3> { t.pos, JarApproach(t.pos, k.radius) } : ClimbBack(t.pos, t.spawn), t);
                if (!on) t.respawnDrop = true;
                return;
            }
            var jarPath = PlanToJar(t.pos, k.radius);
            var bestPath = jarPath;
            float bestLen = PathLength(jarPath) + (candyJar > 0 ? 0 : 99);
            var bestGoal = Goal.ToJar();
            foreach (var c in candies)
            {
                if (c.state != CandyStates.Rest) continue;
                if (c.claimedBy != 0 && c.claimedBy != t.id && toys.Exists(o => o.id == c.claimedBy && o.state == ToyStates.Walk)) continue;
                var p = PlanTo(t.pos, c.pos);
                float l = PathLength(p);
                if (l < bestLen) { bestPath = p; bestLen = l; bestGoal = new Goal { type = Goal.CandyGoal, candy = c }; }
            }
            if (t.goal.type == Goal.CandyGoal && t.goal.candy.claimedBy == t.id) t.goal.candy.claimedBy = 0;
            t.goal = bestGoal;
            if (bestGoal.type == Goal.CandyGoal) bestGoal.candy.claimedBy = t.id;
            t.path = Safe(bestPath, t);
        }

        void MaybeRetarget(Toy t, double dt)
        {
            if (t.Kind.mover == Movers.Fly) return;
            t.replanT -= dt;
            if (t.replanT > 0) return;
            // 爬藤途中（离开地面、还没到桌面）不改主意
            if (OnClimbSegment(t) || (t.pos.y > 0.02f && !IsOnTable(t.pos))) { t.replanT = 0.3f; return; }
            var g = t.goal;
            if (g.type == Goal.CandyGoal && g.candy.state != CandyStates.Rest) { Retarget(t); return; }
            if (candies.Exists(c => c.state == CandyStates.Rest && c.claimedBy == 0)) { Retarget(t); return; }
            t.replanT = 0.6f;
        }

        // 穿过锅口（圆形）：从锅沿高度上方落进锅口内径
        static bool CrossedBoxTop(Vector3 a, Vector3 b)
        {
            float top = BOX.y + BOX.height;
            if (!(a.y >= top && b.y < top)) return false;
            float den = a.y - b.y;
            float k = (a.y - top) / (den != 0 ? den : 1);
            var p = Vec.Lerp(a, b, k);
            return Vec.DistXZ(p, BOX.pos) <= BOX.mouth;
        }

        // 锅身是实心的：落在锅外侧的小鬼被锅壁挡住，沿锅壁往外弹开滑落（不会穿过锅身）
        static void PushOutOfPot(Toy t)
        {
            float r = BOX.half + t.Kind.radius * 0.6f;
            if (t.pos.y > BOX.y + BOX.height || t.pos.y + t.Kind.height < BOX.y) return;
            var d = new Vector3(t.pos.x - BOX.x, 0, t.pos.z - BOX.z);
            float m = d.magnitude;
            if (m >= r) return;
            if (m < BOX.mouth && t.pos.y > BOX.y + BOX.height * 0.5f) return;   // 正对锅口往下落：让它落进锅口，不推
            var n = m > 1e-4f ? d / m : new Vector3(0, 0, 1);
            t.pos.x = BOX.x + n.x * r; t.pos.z = BOX.z + n.z * r;
            float vn = t.vel.x * n.x + t.vel.z * n.z;
            if (vn < 0) { t.vel.x -= n.x * vn * 1.4f; t.vel.z -= n.z * vn * 1.4f; }      // 往外反弹，速度减半左右
        }

        /// <summary>
        /// 飞行曲线（甩成星星 / 被风吹走共用；更新和防穿模预检都用它，保证预检的就是实际飞的）。
        /// 吹走：前 30% 走折线（钻出桌沿 → 竖直升过桌面），之后加速飞向远处、越来越高；
        /// 星星：前 30% 沿手甩出的方向冲出去，之后弧线拐向铁拱门外
        /// </summary>
        public static Vector3 LaunchPos(LaunchState L, float k2)
        {
            if (L.blow)
            {
                if (k2 < 0.3f)
                {
                    float q = k2 / 0.3f;
                    bool under = (L.w1 - L.from).sqrMagnitude > 1e-4f;
                    return under ? (q < 0.5f ? Vec.Lerp(L.from, L.w1, q * 2f) : Vec.Lerp(L.w1, L.w2, q * 2f - 1f)) : Vec.Lerp(L.from, L.w2, q);
                }
                float q2 = (k2 - 0.3f) / 0.7f;
                var p = Vec.Lerp(L.w2, L.to, q2 * q2);
                p.y += Mathf.Sin(Mathf.PI * q2) * 0.25f;
                return p;
            }
            var kick = L.from + L.dir * 0.9f;
            kick.y = Mathf.Max(kick.y, L.from.y + 0.4f);
            var a = Vec.Lerp(L.from, kick, Mathf.Min(1, k2 / 0.3f));
            var pos = Vec.Lerp(a, L.to, k2 * k2);
            pos.y += Mathf.Sin(Mathf.PI * k2) * 0.5f;
            return pos;
        }

        /// <summary>防穿模：沿飞行曲线逐点查场景占用网格，返回撞上摆件之前的进度（没有网格或一路畅通返回 1）</summary>
        float FlightEnd(LaunchState L, Toy t)
        {
            if (solid == null) return 1f;
            var K = t.Kind;
            float r = K.BodyR;
            var up = Vector3.up * (K.height * 0.5f);
            // 按曲线长度加密采样（每 1.5 厘米一个点），薄的椽子也漏不掉
            float len = 0; var prev = LaunchPos(L, 0);
            for (int i = 1; i <= 40; i++) { var p = LaunchPos(L, i / 40f); len += Vector3.Distance(prev, p); prev = p; }
            int N = Mathf.Clamp(Mathf.CeilToInt(len / 0.015f), 120, 1200);
            for (int i = 2; i <= N; i++)
            {
                float k = (float)i / N;
                if (solid.SolidSphere(LaunchPos(L, k) + up, r)) return Mathf.Max(0.05f, k - Mathf.Max(3f / N, 0.04f));
            }
            return 1f;
        }

        /// <summary>飞行曲线在 endK 之前走过的长度（挑方向时比较用）</summary>
        static float FlightLength(LaunchState L)
        {
            float len = 0; var prev = LaunchPos(L, 0);
            for (int i = 1; i <= 40; i++) { var p = LaunchPos(L, L.endK * i / 40f); len += Vector3.Distance(prev, p); prev = p; }
            return len;
        }

        void Land(Toy t, float y)
        {
            t.blown = false;
            t.potAim = false;
            t.pos.y = y;
            t.vel = Vector3.zero;
            t.state = ToyStates.Stunned;
            t.stun = 0.6f;
            var e = Emit(GameEventTypes.Land).At(t.pos); e.id = t.id; e.hard = t.dizzy > 0; e.heavy = t.Kind.twoHand;
            // 10-31 审查：第四夜以后普通扔几乎没用——被扔出去重重落地时，把落点 15 厘米内的小鬼也砸晕（晕了可以随便抓）
            if (t.dizzy > 0)
                foreach (var o in toys)
                {
                    if (o == t || !ACTIVE.Contains(o.state) || o.Kind.twoHand) continue;
                    if (Vec.DistXZ(o.pos, t.pos) > 0.15f || Mathf.Abs(o.pos.y - t.pos.y) > 0.1f) continue;
                    o.dizzy = MathD.Max(o.dizzy, 1.5);
                    var he = Emit(GameEventTypes.Shaken).At(o.pos); he.id = o.id;
                }
        }

        // 爬藤：这一段坡度陡（落差大于水平距离的一半）
        public static bool IsClimb(Vector3 a, Vector3 b)
        {
            float dy = Mathf.Abs(a.y - b.y), h = Vec.DistXZ(a, b);
            return dy > 0.03f && dy > h * 0.5f;
        }

        bool OnClimbSegment(Toy t)
        {
            if (t.Kind.mover == Movers.Fly) return false;
            if (t.path == null || t.seg + 1 >= t.path.Count) return false;
            var a = t.path[t.seg];
            var b = t.path[t.seg + 1];
            return IsClimb(a, b);
        }

        // 沿折线路径前进 d 米，返回是否到达终点
        /// <summary>飞行敌人偷糖时悬停的位置：糖桶口侧上方（桶被提着时跟着桶走）</summary>
        Vector3 FlyMouth(Toy t)
        {
            var b = bucketHeld != null ? bucketPos : JAR.pos;
            float side = (t.id & 1) == 0 ? 0.11f : -0.11f;
            return new Vector3(b.x + side, b.y + JAR.height * 0.9f, b.z - 0.05f);
        }

        static bool StepPath(Toy t, float d)
        {
            while (d > 0 && t.seg < t.path.Count - 1)
            {
                var target = t.path[t.seg + 1];
                var to = target - t.pos;
                float l = to.magnitude;
                if (Mathf.Abs(to.x) + Mathf.Abs(to.z) > 1e-4f) t.yaw = Mathf.Atan2(to.x, to.z);
                if (l <= d) { t.pos = target; t.seg++; d -= l; }
                else { t.pos = t.pos + to * (d / l); d = 0; }
            }
            return t.seg >= t.path.Count - 1;
        }

        // ---------- 散落的糖 ----------
        void UpdateCandies(double dt)
        {
            foreach (var c in candies)
            {
                if (c.state != CandyStates.Falling) continue;
                var prev = c.pos;
                c.vel.y -= (float)(9.8 * dt);
                c.pos = c.pos + MathD.Mul(c.vel, dt);
                // 糖桶吸力（10-29 用户：收糖要有吸附感）：桶口附近、还在空中的糖被吸向桶口中心，靠得够近就收进去
                float top = JAR.y + JAR.height;
                {
                    var mouth = new Vector3(JAR.pos.x, top + 0.02f, JAR.pos.z);
                    var toM = mouth - c.pos; float dm = toM.magnitude;
                    float R = 0.22f + magnetBonus;
                    if (dm < R && c.pos.y > TABLE.y + 0.03f)
                    {
                        float pull = 6f * (1 - dm / R) + 2f;
                        c.vel = Vector3.Lerp(c.vel, toM.normalized * Mathf.Max(0.6f, c.vel.magnitude), Mathf.Clamp01(pull * (float)dt));
                        if (dm < 0.06f) { CandyToJar(c); continue; }
                    }
                }
                if (prev.y >= top && c.pos.y < top && Vec.DistXZ(c.pos, JAR.pos) <= JAR.radius) { CandyToJar(c); continue; }
                // 桌板有厚度：糖在桌板里（贴着桌面放手时常见）也算落在桌上，不会穿下去
                if (prev.y >= TABLE.y - TABLE_SLAB && c.pos.y <= TABLE.y + CANDY_R && OnTable(c.pos, 0.01f)) SettleCandy(c, TABLE.y + CANDY_R);
                else if (c.pos.y <= CANDY_R) SettleCandy(c, CANDY_R);
                ClampRoom(c);
            }
            candies.RemoveAll(c => c.state == CandyStates.Gone);
        }

        void SettleCandy(Candy c, float y)
        {
            // 小弹一下再停
            if (Mathf.Abs(c.vel.y) > 1.2f)
            {
                c.pos.y = y;
                c.vel = new Vector3(c.vel.x * 0.4f, -c.vel.y * 0.3f, c.vel.z * 0.4f);
                var be = Emit(GameEventTypes.CandyBounce).At(c.pos); be.id = c.id;
                return;
            }
            c.pos.y = y;
            c.vel = Vector3.zero;
            c.state = CandyStates.Rest;
            var e = Emit(GameEventTypes.CandyLand).At(c.pos); e.id = c.id;
        }
    }
}
