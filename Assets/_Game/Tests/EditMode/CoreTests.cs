// 对应原型 test/core.test.js（15 项）。
using System.Linq;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;
using static TrickOrFreeze.Tests.H;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Tests
{
    public class CoreTests
    {
        static readonly GameConfig CONFIG = GameConfig.CreateDefault();

        [Test(Description = "直视立即定身；余光里慢挪；视野外更快")]
        public void Core01_直视定身_余光慢挪_视野外更快()
        {
            var g = Night(3);
            var t = Spawn(g, KindIds.Skeleton);
            var p0 = t.pos;
            Run(g, 1, () => Frame(gaze: GazeToy(t)));
            Assert.That(Vector3.Distance(p0, t.pos) < 1e-6f);
            var a = Spawn(Night(3), KindIds.Skeleton);
            var g2 = Night(3); var b = Spawn(g2, KindIds.Skeleton); var q = b.pos; Run(g2, 0.5, Frame());
            var g3 = Night(3); var c = Spawn(g3, KindIds.Skeleton); var r = c.pos; Run(g3, 0.5, Frame(head: AWAY));
            Assert.AreEqual(Zones.Peripheral, b.zone);
            Assert.That(Mathf.Abs(Vector3.Distance(q, b.pos) / Vector3.Distance(r, c.pos) - CONFIG.speed.peripheral) < 0.05f);
            Assert.IsNotNull(a);
        }

        [Test(Description = "残留定身：盯 1 秒，移开后仍僵约 1 秒，有咔哒预警，然后解冻")]
        public void Core02_残留定身()
        {
            var g = Night(3);
            var t = Spawn(g, KindIds.Skeleton);
            Run(g, 1.0, () => Frame(gaze: GazeToy(t)));
            var p0 = t.pos;
            // 10-31：注视宽限 0.22 秒（眨眼不算移开），移开后先多算一小会儿注视，残留相应长一点
            var evs = Run(g, 1.1, Frame());
            Assert.That(Vector3.Distance(p0, t.pos) < 1e-6f);
            Assert.That(Has(evs, GameEventTypes.ThawWarn));
            Run(g, 0.6, Frame());
            Assert.That(Vector3.Distance(p0, t.pos) > 0.005f);
        }

        [Test(Description = "动着的敌人抓不住，会从指缝溜走")]
        public void Core03_动着的抓不住()
        {
            var g = Night(3, residual: false);
            var t = Spawn(g, KindIds.Skeleton);
            Run(g, 0.1, Frame());
            var evs = g.Update(0.02, Frame(hands: Hands(Hand(Sides.Right, new Vector3(t.pos.x, t.pos.y + 0.12f, t.pos.z))), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.That(Has(evs, GameEventTypes.Slip));
            Assert.AreNotEqual(ToyStates.Held, t.state);
        }

        [Test(Description = "拖到任何位置：慢慢放下落在手正下方")]
        public void Core04_拖动慢放()
        {
            var g = Night(3);
            var t = Spawn(g, KindIds.Skeleton);
            var p = PickUp(g, t);
            Assert.AreEqual(ToyStates.Held, t.state);
            var goal = new Vector3(-0.6f, 0.95f, -1.4f);
            var q = MoveHand(g, Sides.Right, p, goal, 60);
            Release(g, Sides.Right, q);
            Run(g, 0.5, Frame(head: AWAY));
            Assert.That(Mathf.Abs(t.pos.x + 0.6f) < 0.05f && Mathf.Abs(t.pos.z + 1.4f) < 0.05f, Json(t.pos));
            Assert.That(Mathf.Abs(t.pos.y - TABLE.y) < 1e-6f);
        }

        [Test(Description = "拎着放到桌上时手压得很低（小鬼吊在手下、脚已低于桌面）：松手落在桌上，不会穿到桌下")]
        public void Core04b_压低放下不穿桌()
        {
            foreach (var goal in new[] { new Vector3(-0.3f, TABLE.y + 0.12f, -1.0f), new Vector3(0.4f, TABLE.y + 0.03f, -1.2f) })
            {
                var g = Night(3);
                var t = Spawn(g, KindIds.Skeleton);
                var p = PickUp(g, t);
                var q = MoveHand(g, Sides.Right, p, goal, 60);
                Assert.That(t.pos.y >= TABLE.y - 1e-5f, "拎着时脚不陷进桌板 " + Json(t.pos));
                Release(g, Sides.Right, q);
                Run(g, 0.6, Frame(head: AWAY));
                Assert.That(Mathf.Abs(t.pos.y - TABLE.y) < 1e-4f, Json(t.pos));
            }
            // 下落时脚已在桌板里、身体中心还在桌面以上（比如手贴着桌面往下一送）：也落在桌上
            {
                var g = Night(3);
                var t = Spawn(g, KindIds.Skeleton);
                t.state = ToyStates.Falling;
                t.pos = new Vector3(0.3f, TABLE.y - 0.04f, -1.1f);
                t.vel = new Vector3(0, -1.5f, 0);
                Run(g, 0.5, Frame(head: AWAY));
                Assert.That(Mathf.Abs(t.pos.y - TABLE.y) < 1e-4f, Json(t.pos));
            }
        }

        [Test(Description = "扔坩埚有吸附：大致朝锅口扔就能进锅；朝反方向扔不受影响")]
        public void Core05b_扔锅吸附()
        {
            // 从桌子正前方，朝锅的方向（稍偏）往上抛
            foreach (var off in new[] { 0f, 12f, -15f })
            {
                var g = Night(3);
                var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(0.05f, TABLE.y, -0.85f));
                var p = PickUp(g, t);
                var to = new Vector3(BOX.x - p.x, 0, BOX.z - p.z).normalized;
                var dir = (Quaternion.Euler(0, off, 0) * to + Vector3.up * 0.9f).normalized;
                Fling(g, Sides.Right, p, dir, 2.2f);
                Run(g, 1.6, Frame(head: AWAY));
                Assert.AreEqual(ToyStates.Boxed, t.state, $"偏 {off}° 没进锅 " + Json(t.pos));
            }
            {
                var g = Night(3);
                var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(0.05f, TABLE.y, -0.85f));
                var p = PickUp(g, t);
                var away = new Vector3(p.x - BOX.x, 0, p.z - BOX.z).normalized;
                Fling(g, Sides.Right, p, (away + Vector3.up * 0.6f).normalized, 3f);
                Run(g, 1.6, Frame(head: AWAY));
                Assert.AreNotEqual(ToyStates.Boxed, t.state, "往反方向扔不该进锅");
            }
        }

        [Test(Description = "扔远：落地会晕，之后重新爬回桌上")]
        public void Core05_扔远()
        {
            var g = Night(3);
            var t = Spawn(g, KindIds.Skeleton);
            var p = PickUp(g, t);
            var evs = Fling(g, Sides.Right, p, Vec.Norm(new Vector3(-1f, 0.2f, -0.3f)));
            Assert.That(Has(evs, GameEventTypes.Throw));
            Run(g, 1.5, Frame(head: AWAY));
            Assert.That(t.pos.y < 0.01f && t.pos.x < -1.1f, Json(t.pos));
            Run(g, 30, Frame(head: AWAY));
            Assert.That(t.pos.y > 0.7f || new[] { ToyStates.Flee, ToyStates.Gone, ToyStates.Steal }.Contains(t.state), t.state);
        }

        [Test(Description = "晃掉的糖一定落在桌面上（在桌边、在桌外的地上、被举高时都一样），能捡起来放回糖桶")]
        public void Core06b_掉糖只落桌面()
        {
            var spots = new[] { new Vector3(0.0f, TABLE.y, -1.0f), RimPoint(60, 0.02f), RimPoint(-80, 0.02f), new Vector3(1.6f, 0f, -1.5f), new Vector3(-0.3f, 1.3f, -0.9f) };
            foreach (var at in spots)
            {
                var g = Night(3);
                var t = Spawn(g, KindIds.Skeleton, pos: at);
                t.pos = at; t.candyCount = 3; t.hasCandy = true;
                g.LoseCandyFromForTest(t);
                t.state = ToyStates.Gone;                 // 只看糖落在哪：小鬼走开，免得它把糖又捡回去
                Run(g, 2.5, Frame(head: AWAY));
                Assert.AreEqual(3, g.candies.Count, Json(at));
                foreach (var c in g.candies)
                {
                    Assert.AreEqual(CandyStates.Rest, c.state, Json(c.pos));
                    Assert.That(Mathf.Abs(c.pos.y - (TABLE.y + 0.018f)) < 0.002f && OnTable(c.pos), "糖落在桌外 " + Json(c.pos) + " 来自 " + Json(at));
                }
                // 捡起来放回糖桶
                var c0 = g.candies[0];
                g.Update(0.02, Frame(gaze: GazeAt(c0.pos), hands: Hands(Hand(Sides.Right, c0.pos)), events: Evs(InputEvent.PinchStart(Sides.Right))));
                Assert.AreEqual(CandyStates.Held, c0.state);
                int jar = g.candyJar;
                var q = MoveHand(g, Sides.Right, c0.pos, new Vector3(JAR.x, JAR.y + JAR.height + 0.08f, JAR.z), 30);
                Release(g, Sides.Right, q);
                Run(g, 0.3, Frame(head: AWAY));
                Assert.AreEqual(jar + 1, g.candyJar);
            }
        }

        [Test(Description = "晃动：偷糖的敌人被晃晕、糖掉出来；晕着的不用定身也能直接抓")]
        public void Core06_晃动()
        {
            var g = Night(3, residual: false);
            var t = Spawn(g, KindIds.Skeleton);
            GiveCandy(g, t);
            var p = PickUp(g, t);
            var (q, evs) = Shake(g, Sides.Right, p);
            Assert.That(Has(evs, GameEventTypes.Shaken));
            Assert.That(Has(evs, GameEventTypes.CandyDrop));
            Assert.AreEqual(false, t.hasCandy);
            Assert.That(t.dizzy >= CONFIG.shake.dizzySec - 0.5f);
            Release(g, Sides.Right, q);
            Run(g, 0.6, Frame(head: AWAY));
            Assert.AreEqual(ToyStates.Stunned, t.state);
            // 没有盯着它，也能抓
            var evs2 = g.Update(0.02, Frame(head: AWAY, hands: Hands(Hand(Sides.Left, new Vector3(t.pos.x, t.pos.y + 0.12f, t.pos.z))), events: Evs(InputEvent.PinchStart(Sides.Left))));
            Assert.AreEqual(ToyStates.Held, t.state, string.Join(",", evs2.Select(e => e.type)));
        }

        [Test(Description = "捡糖放回南瓜糖桶")]
        public void Core07_捡糖放回()
        {
            var g = Night(3);
            var c = g.SpawnCandy(new Vector3(0.15f, 0.8f, -0.5f));
            Run(g, 0.6, Frame(head: AWAY));
            Assert.AreEqual(CandyStates.Rest, c.state);
            g.Update(0.02, Frame(hands: Hands(Hand(Sides.Right, c.pos)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(CandyStates.Held, c.state);
            var above = new Vector3(JAR.x, JAR.y + JAR.height + 0.1f, JAR.z);
            var q = MoveHand(g, Sides.Right, c.pos, above, 10);
            int before = g.candyJar;
            Release(g, Sides.Right, q);
            Assert.AreEqual(before + 1, g.candyJar);
        }

        [Test(Description = "长按蓄力 1.2 秒后甩出 → 飞出窗外变成星星，永不回来；带的糖掉下")]
        public void Core08_蓄力变星星()
        {
            var g = Night(4);
            var t = Spawn(g, KindIds.Skeleton);
            GiveCandy(g, t);
            var p = PickUp(g, t);
            var evs = Run(g, CONFIG.launch.chargeSec + 0.1, Frame(hands: Hands(Hand(Sides.Right, p))));
            Assert.That(Has(evs, GameEventTypes.Charged));
            var evs2 = Fling(g, Sides.Right, p, Vec.Norm(new Vector3(0f, 0.5f, -1f)), 3);
            Assert.That(Has(evs2, GameEventTypes.Launch));
            var evs3 = Run(g, 2, Frame(head: AWAY));
            Assert.That(Has(evs3, GameEventTypes.Star));
            Assert.AreEqual(ToyStates.Gone, t.state);
            Assert.AreEqual(1, g.stats.stars);
            Assert.AreEqual(0, g.CarriedCandy());
        }

        [Test(Description = "发金光后：甩完手已经慢下来才松手也能变星星；捏着用力一甩不等松手就飞出去")]
        public void Core08b_甩星星更容易()
        {
            var g = Night(4);
            var t = Spawn(g, KindIds.Skeleton);
            var p = PickUp(g, t);
            Run(g, CONFIG.launch.chargeSec + 0.1, Frame(hands: Hands(Hand(Sides.Right, p))));
            Assert.That(t.charged);
            var q = MoveHand(g, Sides.Right, p, p + new Vector3(0f, 0.04f, -0.12f), 6);   // 约 1 米/秒，没到“直接飞”的门槛
            Run(g, 0.12, Frame(hands: Hands(Hand(Sides.Right, q))));                    // 手已经停下来了才松手
            var evs = Release(g, Sides.Right, q);
            Assert.That(Has(evs, GameEventTypes.Launch), "松手时手已停，按最近 0.3 秒的最快手速判断");

            var g2 = Night(4);
            var t2 = Spawn(g2, KindIds.Skeleton);
            var p2 = PickUp(g2, t2);
            Run(g2, CONFIG.launch.chargeSec + 0.1, Frame(hands: Hands(Hand(Sides.Right, p2))));
            MoveHand(g2, Sides.Right, p2, p2 + new Vector3(0f, 0.1f, -0.3f), 6);       // 约 2.6 米/秒，不松手
            Assert.AreEqual(ToyStates.Launched, t2.state, "捏着用力一甩直接飞出去");
        }

        [Test(Description = "没蓄满就甩：只是普通扔远")]
        public void Core09_没蓄满就甩()
        {
            var g = Night(4);
            var t = Spawn(g, KindIds.Skeleton);
            var p = PickUp(g, t);
            var evs = Fling(g, Sides.Right, p, Vec.Norm(new Vector3(0f, 0.5f, -1f)), 3);
            Assert.That(Has(evs, GameEventTypes.Throw));
            Assert.That(!Has(evs, GameEventTypes.Launch));
        }

        [Test(Description = "蓄力中晃动会打断蓄力（晃和蓄是两种操作）")]
        public void Core10_晃动打断蓄力()
        {
            var g = Night(4);
            var t = Spawn(g, KindIds.Skeleton);
            var p = PickUp(g, t);
            Run(g, 0.8, Frame(hands: Hands(Hand(Sides.Right, p))));
            Shake(g, Sides.Right, p, 4);
            Assert.That(t.chargeT < 0.5f, t.chargeT.ToString());
        }

        [Test(Description = "坩埚：3 格；装满后再放会被弹出并说明原因")]
        public void Core11_坩埚满了()
        {
            var g = Night(3);
            var ts = new[] { 0, 1, 2, 3 }.Select(i => Spawn(g, KindIds.Skeleton, -30 + i * 20)).ToArray();
            void Into(Toy t) { t.state = ToyStates.Falling; t.pos = new Vector3(BOX.x, BOX.y + 0.5f, BOX.z); t.vel = Vector3.zero; Run(g, 0.5, Frame(head: AWAY)); }
            Into(ts[0]); Into(ts[1]); Into(ts[2]);
            Assert.AreEqual(3, g.cauldron.used);
            var evs = new System.Collections.Generic.List<GameEvent>();
            ts[3].state = ToyStates.Falling; ts[3].pos = new Vector3(BOX.x, BOX.y + 0.5f, BOX.z); ts[3].vel = Vector3.zero;
            evs.AddRange(Run(g, 0.4, Frame(head: AWAY)));
            var full = evs.Find(e => e.type == GameEventTypes.BoxFull);
            Assert.IsNotNull(full);
            Assert.AreEqual(0, full.free);
            // 10-31：装不下不再弹回来，坐到锅沿上排队（不占格），空出一格自己掉进去
            Assert.That(full.wait, "坐到锅沿上排队");
            Assert.That(ts[3].rimWait);
            Assert.AreEqual(3, g.cauldron.used);
            Assert.That(evs.Exists(e => e.type == GameEventTypes.Say && e.text.Contains("满")));
            var later = Run(g, CONFIG.cauldron.digestSec + 0.3, Frame(head: AWAY));
            Assert.That(!ts[3].rimWait, "消化掉一只后，锅沿上的掉进锅里");
            Assert.That(later.Exists(e => e.type == GameEventTypes.Boxed && e.id == ts[3].id));
            Assert.AreEqual(3, g.cauldron.used);
        }

        [Test(Description = "锅沿最多坐 2 只；再多的弹出来；“坩埚满了”的字幕 15 秒内不重复")]
        public void Core11b_锅沿排队上限()
        {
            var g = Night(3);
            var ts = new[] { 0, 1, 2, 3, 4, 5 }.Select(i => Spawn(g, KindIds.Skeleton, -40 + i * 15)).ToArray();
            for (int i = 0; i < 3; i++) g.PutInBox(ts[i]);
            var evs = new System.Collections.Generic.List<GameEvent>();
            g.PutInBox(ts[3]); g.PutInBox(ts[4]);
            evs.AddRange(Run(g, 0.1, Frame(head: AWAY)));
            Assert.That(ts[3].rimWait && ts[4].rimWait);
            Assert.AreNotEqual(ts[3].pos, ts[4].pos, "锅沿两个位置不重叠");
            g.PutInBox(ts[5]);
            Assert.AreEqual(ToyStates.Falling, ts[5].state, "锅沿也坐满了：弹出来");
            evs.AddRange(Run(g, 0.1, Frame(head: AWAY)));
            Assert.AreEqual(1, evs.FindAll(e => e.type == GameEventTypes.Say && (e.textKey == "say.cauldronRim" || e.textKey == "say.cauldronNoRoom")).Count, "锅沿排队/装不下的字幕 15 秒内只说一次");
        }

        [Test(Description = "发金光之后先往回带一下再甩（换向），金光不会被当成晃动清掉")]
        public void Core08c_回拉再甩不丢金光()
        {
            var g = Night(4);
            var t = Spawn(g, KindIds.Skeleton);
            var p = PickUp(g, t);
            Run(g, CONFIG.launch.chargeSec + 0.1, Frame(hands: Hands(Hand(Sides.Right, p))));
            Assert.That(t.charged);
            var q = p;
            for (int i = 0; i < 6; i++) { q += new Vector3(0f, 0f, 0.025f); g.Update(0.02, Frame(hands: Hands(Hand(Sides.Right, q)))); }   // 往回带（约 1.25 米/秒）
            Assert.That(t.charged, "回拉不清金光");
            var evs = Fling(g, Sides.Right, q, Vec.Norm(new Vector3(0f, 0.4f, -1f)), 3);
            Assert.That(Has(evs, GameEventTypes.Launch));
        }

        [Test(Description = "坩埚：按顺序慢慢消化，空出格子；拍锅沿加速")]
        public void Core12_坩埚消化与拍锅沿()
        {
            var g = Night(3);
            Toy a = Spawn(g, KindIds.Skeleton), b = Spawn(g, KindIds.Skeleton, 30);
            g.PutInBox(a); g.PutInBox(b);
            Assert.AreEqual(2, g.cauldron.used);
            var evs = Run(g, CONFIG.cauldron.digestSec + 0.2, Frame(head: AWAY));
            Assert.That(evs.Exists(e => e.type == GameEventTypes.Digested && e.id == a.id));
            Assert.AreEqual(1, g.cauldron.used);
            Assert.AreEqual(ToyStates.Gone, a.state);
            // 拍锅沿：第二只更快
            var g2 = Night(3); var c = Spawn(g2, KindIds.Skeleton); g2.PutInBox(c);
            // JS 用 double 累加 t；这里同样用 double，保证“每 50 帧拍一次”的帧号判定一致
            double t = 0;
            for (; t < 20 && c.state != ToyStates.Gone; t += 0.02)
            {
                var ev = System.Math.Round(t * 50, System.MidpointRounding.AwayFromZero) % 50 == 0
                    ? Evs(InputEvent.Press(new Vector3(BOX.x, BOX.y + 0.15f, BOX.z))) : Evs();
                g2.Update(0.02, Frame(head: AWAY, events: ev));
            }
            Assert.That(t < CONFIG.cauldron.digestSec * 0.7, $"拍锅沿后用时 {t:F1}s");
        }

        [Test(Description = "坩埚：偷糖的敌人放进去，糖回到糖桶")]
        public void Core13_放进坩埚糖回桶()
        {
            var g = Night(3);
            var t = Spawn(g, KindIds.Skeleton);
            GiveCandy(g, t);
            Assert.AreEqual(11, g.candyJar);
            g.PutInBox(t);
            Assert.AreEqual(12, g.candyJar);
        }

        [Test(Description = "隔空取物：盯着 + 捏合，0.6 秒到手；途中视线离开太久就掉")]
        public void Core14_隔空取物()
        {
            var g = Night(3, residual: false);
            var t = Spawn(g, KindIds.Skeleton);
            var hp = new Vector3(0.15f, 0.95f, -0.3f);
            Run(g, 0.2, () => Frame(gaze: GazeToy(t)));
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hp)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(ToyStates.Pulled, t.state);
            Run(g, 0.7, () => Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hp))));
            Assert.AreEqual(ToyStates.Held, t.state);
        }

        [Test(Description = "隔空取物：捏的那一下眼睛瞟向了手（已经不在看它），刚看过的小鬼照样拉得过来")]
        public void Core14c_刚看过就能拉()
        {
            var g = Night(3, residual: true);
            var t = Spawn(g, KindIds.Skeleton);
            var hp = new Vector3(0.15f, 0.95f, -0.3f);
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            Run(g, 0.2, () => Frame());
            g.Update(0.02, Frame(hands: Hands(Hand(Sides.Right, hp)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(ToyStates.Pulled, t.state);
        }

        [Test(Description = "隔空取物要伸手：手搁在桌面上（放松时拇指食指靠得近被当成捏合）不会把看着的小鬼拉过来")]
        public void Core14b_手搁着不隔空取物()
        {
            var g = Night(3, residual: false);
            var t = Spawn(g, KindIds.Skeleton);
            var rest = new Vector3(0.15f, 0.74f, -0.25f);
            Run(g, 0.2, () => Frame(gaze: GazeToy(t)));
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, rest)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreNotEqual(ToyStates.Pulled, t.state);
        }

        [Test(Description = "什么都不做必然失败；只用眼睛很难守住后期")]
        public void Core15_什么都不做必然失败()
        {
            var g = Night(7);
            g.director.t = 2; g.director.burstT = 30;
            Run(g, 140, Frame(head: AWAY));
            Assert.AreEqual(Phases.Result, g.phase);
            Assert.AreEqual(false, g.result.win);
        }
    }
}
