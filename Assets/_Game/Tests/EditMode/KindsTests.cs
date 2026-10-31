// 对应原型 test/kinds.test.js（12 项）。
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;
using static TrickOrFreeze.Tests.H;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Tests
{
    public class KindsTests
    {
        [Test(Description = "蝙蝠：空中飞；被盯住缓缓飘落；飘落时可隔空取")]
        public void Kinds01_蝙蝠()
        {
            var g = Night(5);
            var t = g.SpawnToy(new SpawnPoint("s", "", new Vector3(-0.5f, 1.2f, -1.5f), 0), new SpawnOpts { kind = KindIds.Bat, silent = true });
            Run(g, 0.4, Frame(head: AWAY));
            Assert.That(t.pos.y > 0.95f);
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            Assert.AreEqual(ToyStates.Glide, t.state);
            var hp = new Vector3(0.15f, 0.95f, -0.3f);
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hp)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Run(g, 0.7, () => Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hp))));
            Assert.AreEqual(ToyStates.Held, t.state);
        }

        [Test(Description = "糖桶提起来：桌上的小鬼够不着，空中飞的追上去照样能偷")]
        public void Kinds01b_提桶时只有飞的能偷()
        {
            var g = Night(1);
            int start = g.candyJar;
            var w = g.SpawnToy(new SpawnPoint("s", "", JAR.pos + new Vector3(0.2f, 0, 0), 0), new SpawnOpts { kind = KindIds.Skeleton, silent = true });
            w.state = ToyStates.Steal;
            var grip = JAR.pos + Vector3.up * 0.32f;
            g.Update(0.02, Frame(head: AWAY, hands: Hands(Hand(Sides.Right, grip)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(Sides.Right, g.bucketHeld);
            Run(g, 5, () => Frame(head: AWAY, hands: Hands(Hand(Sides.Right, grip))));
            Assert.AreEqual(start, g.candyJar, "桌上的小鬼偷不到提起来的桶");
            var bat = g.SpawnToy(new SpawnPoint("b", "", JAR.pos + new Vector3(0.4f, 0.5f, 0), 0), new SpawnOpts { kind = KindIds.Bat, silent = true });
            Run(g, 12, () => Frame(head: AWAY, hands: Hands(Hand(Sides.Right, grip))));
            Assert.That(g.candyJar < start, "飞的追上去能偷");
        }

        [Test(Description = "蝙蝠在糖桶上空偷糖时被盯住：缓缓飘落，不会定在半空")]
        public void Kinds01c_偷糖蝙蝠被盯住会飘落()
        {
            var g = Night(5);
            var t = g.SpawnToy(new SpawnPoint("b", "", JAR.pos + new Vector3(0.11f, JAR.height * 0.9f, -0.05f), 0), new SpawnOpts { kind = KindIds.Bat, silent = true });
            t.state = ToyStates.Steal;
            float y0 = t.pos.y;
            Run(g, 0.6, () => Frame(gaze: GazeToy(t)));
            Assert.That(t.state == ToyStates.Glide || t.pos.y < y0 - 0.02f, "被盯住后在飘落");
        }

        [Test(Description = "吊丝蜘蛛：从天花板垂到糖桶旁；被盯住就吊在半空不动；捏住扯断丝")]
        public void Kinds02_蜘蛛垂降与定身()
        {
            var g = Night(8);
            var t = g.SpawnToy(Game.SPAWNS[0], new SpawnOpts { kind = KindIds.Spider, silent = true });
            Assert.That(t.pos.y > 2);
            float y0 = t.pos.y;                       // 从藤架椽子底下（约 2.7m）开始垂降
            Run(g, 1.0, Frame(head: AWAY));
            float y1 = t.pos.y;
            Assert.That(y1 < y0 - 0.1f, "没人看时应该往下垂");
            Run(g, 0.6, () => Frame(gaze: GazeToy(t)));
            Assert.That(t.frozen, "被盯住就定住");
            // 10-31 舒适：在视线高度以上被定住时，顺着丝慢慢滑到 SPIDER_REST_Y 再停（不用一直仰头）；到了这个高度就不再下降
            Run(g, 8, () => Frame(gaze: GazeToy(t)));
            Assert.That(Mathf.Abs(t.pos.y - Mathf.Min(y1, Game.SPIDER_REST_Y)) < 0.02f, $"高处定住会滑到视线高度停住（现在 {t.pos.y:F2}）");
            float y2 = t.pos.y;
            Run(g, 0.6, () => Frame(gaze: GazeToy(t)));
            Assert.That(Mathf.Abs(t.pos.y - y2) < 1e-6f, "到了视线高度被盯住时不再下降");
            var hp = new Vector3(0.1f, 0.95f, -0.35f);
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hp)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Run(g, 0.7, () => Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hp))));
            Assert.AreEqual(ToyStates.Held, t.state);
        }

        [Test(Description = "吊丝蜘蛛：没人管就偷到糖、再爬回天花板")]
        public void Kinds03_蜘蛛偷糖回天花板()
        {
            var g = Night(8);
            g.SpawnToy(Game.SPAWNS[0], new SpawnOpts { kind = KindIds.Spider, silent = true });
            bool stole = false, lost = false;
            for (int i = 0; i < 2000 && !lost; i++)
            {
                var evs = g.Update(0.02, Frame(head: AWAY));
                if (Has(evs, GameEventTypes.Steal)) stole = true;
                if (Has(evs, GameEventTypes.CandyLost)) lost = true;
            }
            Assert.That(stole && lost);
            Assert.AreEqual(11, g.candyJar);
        }

        [Test(Description = "南瓜头：被直视反而跑得更快；在余光里才僵住，可以伸手抓")]
        public void Kinds04_南瓜头()
        {
            var g = Night(9);
            var t = Spawn(g, KindIds.Pumpkin);
            Run(g, 0.3, Frame());
            Assert.AreEqual(Zones.Peripheral, t.zone);
            Assert.AreEqual(true, t.frozen);
            var point = new Vector3(t.pos.x, t.pos.y + 0.13f, t.pos.z);
            g.Update(0.02, Frame(hands: Hands(Hand(Sides.Right, point)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(ToyStates.Held, t.state);
            // 被盯超过 0.4 秒才受惊逃跑（比视野外跑得还快）
            var g2 = Night(9); var u = Spawn(g2, KindIds.Pumpkin); var q = u.pos;
            Run(g2, 1.0, () => Frame(gaze: GazeToy(u)));
            Assert.AreEqual(true, u.scared);
            var g3 = Night(9); var w = Spawn(g3, KindIds.Pumpkin); var r = w.pos;
            Run(g3, 1.0, Frame(head: AWAY));
            Assert.That(Vector3.Distance(q, u.pos) > Vector3.Distance(r, w.pos));
            // 扫一眼（0.3 秒）不会受惊，还是僵着的
            var g4 = Night(9); var a = Spawn(g4, KindIds.Pumpkin);
            Run(g4, 0.3, () => Frame(gaze: GazeToy(a)));
            Assert.AreEqual(false, a.scared); Assert.AreEqual(true, a.frozen);
            // 就算一直盯着，手伸到跟前它也吓得不敢动，能抓住（10-29 实机：伸手时眼睛本能会看它）
            var g5 = Night(9); var b = Spawn(g5, KindIds.Pumpkin);
            var bp = new Vector3(b.pos.x, b.pos.y + 0.13f, b.pos.z);
            Run(g5, 1.0, () => Frame(gaze: GazeToy(b), hands: Hands(Hand(Sides.Right, bp))));
            Assert.AreEqual(true, b.frozen);
            g5.Update(0.02, Frame(gaze: GazeToy(b), hands: Hands(Hand(Sides.Right, bp)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(ToyStates.Held, b.state);
        }

        [Test(Description = "黑猫：黑暗里盯不住；在南瓜灯或掌心灯里才能定住")]
        public void Kinds05_黑猫()
        {
            var g = Night(10);
            var t = Spawn(g, KindIds.Cat, 60);
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            Assert.AreEqual(false, t.lit);
            Assert.AreEqual(false, t.frozen);
            g.Update(0.02, Frame(events: Evs(InputEvent.Skill(SkillIds.Lamp, new Vector3(t.pos.x, t.pos.y + 0.1f, t.pos.z)))));
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            Assert.AreEqual(true, t.lit);
            Assert.AreEqual(true, t.frozen);
            // 能召唤南瓜灯的夜晚桌上没有烛台：原来烛台那一圈不再有光
            var u = Spawn(g, KindIds.Cat, pos: new Vector3(CANDLE.x + 0.1f, TABLE.y, CANDLE.z));
            g.Update(0.02, Frame());
            Assert.AreEqual(false, u.lit);
            Assert.AreEqual(false, CANDLE.on);
            Assert.AreEqual(true, Night(5).cfg != null && CANDLE.on, "没有南瓜灯的夜晚烛台还在");
        }

        [Test(Description = "南瓜灯：捏住能挪，松手停在那里；不在本关技能里的手势不起作用")]
        public void Kinds05b_挪南瓜灯()
        {
            var g = Night(10);
            g.Update(0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Lamp, new Vector3(0.2f, 1.0f, -0.5f)))));
            Assert.AreEqual(1, g.lamps.Count);
            var l = g.lamps[0];
            g.Update(0.02, Frame(head: AWAY, hands: Hands(Hand(Sides.Right, l.pos + Vector3.up * 0.03f)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(Sides.Right, l.heldBy);
            var to = new Vector3(-0.3f, 0.95f, -0.9f);
            Run(g, 0.2, () => Frame(head: AWAY, hands: Hands(Hand(Sides.Right, to))));
            g.Update(0.02, Frame(head: AWAY, hands: Hands(Hand(Sides.Right, to)), events: Evs(InputEvent.PinchEnd(Sides.Right))));
            Assert.AreEqual(null, l.heldBy);
            Assert.That(Vector3.Distance(l.pos, to + Vector3.down * 0.05f) < 0.02f, "灯停在松手的地方");
            var early = Night(5);
            early.Update(0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Lamp, new Vector3(0.2f, 1.0f, -0.5f)))));
            Assert.AreEqual(0, early.lamps.Count, "第五夜还没学南瓜灯");
        }

        [Test(Description = "大幽灵：一抓就分裂成小幽灵；占 2 格坩埚")]
        public void Kinds06_大幽灵()
        {
            var g = Night(6);
            var t = Spawn(g, KindIds.Ghost);
            var evs = new List<GameEvent>();
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            evs.AddRange(g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, new Vector3(t.pos.x, t.pos.y + 0.12f, t.pos.z))), events: Evs(InputEvent.PinchStart(Sides.Right)))));
            Assert.That(Has(evs, GameEventTypes.Split));
            Assert.AreEqual(KindIds.Ghostling, t.kind);
            Assert.AreEqual(3, g.toys.Count(x => x.kind == KindIds.Ghostling));
            var g2 = Night(6); var big = Spawn(g2, KindIds.Ghost); g2.PutInBox(big);
            Assert.AreEqual(2, g2.cauldron.used);
        }

        [Test(Description = "缝合熊：盯满 0.8 秒才定住；单手拖不动；双手举起扔两次就离场；坩埚装不下")]
        public void Kinds07_缝合熊()
        {
            var g = Night(12);
            var t = Spawn(g, KindIds.Frank, pos: new Vector3(0f, TABLE.y, -0.6f));
            Run(g, 0.5, () => Frame(gaze: GazeToy(t)));
            Assert.AreEqual(false, t.frozen);
            Run(g, 0.5, () => Frame(gaze: GazeToy(t)));
            Assert.AreEqual(true, t.frozen);
            var L = new Vector3(t.pos.x - 0.1f, t.pos.y + 0.3f, t.pos.z);
            var R = new Vector3(t.pos.x + 0.1f, t.pos.y + 0.3f, t.pos.z);
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Left, L)), events: Evs(InputEvent.PinchStart(Sides.Left))));
            Assert.AreNotEqual(ToyStates.Held, t.state);
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Left, L), Hand(Sides.Right, R)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(ToyStates.Held, t.state);
            Vector3 l = L, r = R;
            var step = new Vector3(0.07f, 0.02f, -0.04f);
            for (int i = 0; i < 5; i++) { l += step; r += step; g.Update(0.02, Frame(hands: Hands(Hand(Sides.Left, l), Hand(Sides.Right, r)))); }
            var evs = g.Update(0.02, Frame(hands: Hands(Hand(Sides.Left, l, false), Hand(Sides.Right, r)), events: Evs(InputEvent.PinchEnd(Sides.Left))));
            // 10-31：两只手抱熊时一只手松开有 0.15 秒宽限（追踪闪断不算松手），之后才按峰值手速判定扔出
            evs.AddRange(Run(g, 0.2, Frame(hands: Hands(Hand(Sides.Left, l, false), Hand(Sides.Right, r)))));
            Assert.That(evs.Exists(e => e.type == GameEventTypes.Throw && e.boss));
            Assert.AreEqual(1, t.hp);
            var g2 = Night(12); var f = Spawn(g2, KindIds.Frank); f.state = ToyStates.Falling; f.pos = new Vector3(BOX.x, BOX.y + 0.6f, BOX.z); f.vel = Vector3.zero;
            g2.PutInBox(f);
            Assert.AreNotEqual(ToyStates.Boxed, f.state);
        }

        [Test(Description = "月光护桶：罩住期间偷不了糖，正在偷的被弹开")]
        public void Kinds08_月光护桶()
        {
            var g = Night(9);
            var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(0.12f, TABLE.y, -0.62f));
            t.path = new List<Vector3> { t.pos, new Vector3(0.12f, TABLE.y, -0.621f) };
            Run(g, 0.2, Frame(head: AWAY));
            Assert.AreEqual(ToyStates.Steal, t.state);
            g.Update(0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Shield))));
            Assert.AreEqual(ToyStates.Falling, t.state);
            Run(g, 5.5, Frame(head: AWAY));
            Assert.AreEqual(12, g.candyJar);
        }

        [Test(Description = "合十：全场冻结后减速；有冷却")]
        public void Kinds09_合十()
        {
            var g = Night(5, residual: false);
            var t = Spawn(g, KindIds.Skeleton);
            g.Update(0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Pray))));
            var p0 = t.pos;
            Run(g, 1.9, Frame(head: AWAY));
            Assert.That(Vector3.Distance(p0, t.pos) < 1e-6f);
            var evs = g.Update(0.02, Frame(events: Evs(InputEvent.Skill(SkillIds.Pray))));
            Assert.That(Has(evs, GameEventTypes.SkillCooling));
        }

        [Test(Description = "外挥：桌上的敌人被吹到远处地上")]
        public void Kinds10_外挥()
        {
            var g = Night(7);
            var a = Spawn(g, KindIds.Skeleton);
            g.Update(0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Sweep))));
            Run(g, 1.5, Frame(head: AWAY));
            // 10-29 用户：被吹出桌面、掉到桌下就消失（不再落地后爬回来）
            Assert.AreEqual(ToyStates.Gone, a.state, Json(a.pos));
            Assert.That(Mathf.Sqrt(a.pos.x * a.pos.x + a.pos.z * a.pos.z) > 1.2f, Json(a.pos));
        }

        [Test(Description = "技能逐夜解锁")]
        public void Kinds11_技能逐夜解锁()
        {
            string Sk(int n) => string.Join(",", Night(n).skills.Keys.OrderBy(x => x, System.StringComparer.Ordinal));
            Assert.AreEqual("", Sk(1));
            Assert.AreEqual("", Sk(4));
            Assert.AreEqual("pray", Sk(5));
            Assert.AreEqual("pray,sweep", Sk(7));
            Assert.AreEqual("pray,shield,sweep", Sk(9));
            Assert.AreEqual("lamp,pray,shield,sweep", Sk(10));
        }

        [Test(Description = "12 夜都能完整跑完（跳过教学、什么都不做）不报错；对应夜出现对应敌人")]
        public void Kinds12_十二夜都能跑完()
        {
            Assert.AreEqual(12, NightDefs.NIGHTS.Length);
            var expect = new Dictionary<int, string> { { 5, KindIds.Bat }, { 6, KindIds.Ghost }, { 8, KindIds.Spider }, { 9, KindIds.Pumpkin }, { 11, KindIds.Cat }, { 12, KindIds.Frank } };   // 10-31：黑猫从第十夜挪到第十一夜
            for (int i = 0; i < 12; i++)
            {
                var g = new Game(new GameOptions { rand = Seeded(i + 2) });
                g.StartNight(i);
                if (g.lesson != null) g.FinishLesson();
                if (g.brief != null) g.FinishBrief();
                var seen = new HashSet<string>();
                for (int k = 0; k < 170 / 0.02 && g.phase == Phases.Playing; k++)
                {
                    if (i == 11 && g.candyJar < 6) g.candyJar = 12;   // 10-31：首领改到 55% 进度出场，挂机玩家撑不到——补糖让这一夜跑完
                    g.Update(0.02, Frame(head: AWAY));
                    foreach (var t in g.toys) seen.Add(t.kind);
                }
                Assert.AreEqual(Phases.Result, g.phase, $"夜{i + 1}");
                if (expect.TryGetValue(i + 1, out var kind)) Assert.That(seen.Contains(kind), $"夜{i + 1} 应出现 {kind}");
            }
        }
    }
}
