// 10-31 全面审查后的整改：抓取优先级、注视、手势防误触、吹飞返回、胜负评星、挑战、练习、糖桶时长
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;
using static TrickOrFreeze.Tests.H;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Tests
{
    public class ReviewTests
    {
        static readonly GameConfig CONFIG = GameConfig.CreateDefault();
        static GestureHand GH(float x, float nx, float y = 0.95f) => new GestureHand(new Vector3(x, y, -0.3f), new Vector3(nx, 0f, 0f));

        [Test(Description = "外挥：单手往侧面伸（另一只手不动）不算外挥")]
        public void Review01_单手侧伸不算外挥()
        {
            var g = new GestureDetector();
            var evs = new List<InputEvent>();
            for (int i = 0; i < 5; i++) evs.AddRange(g.Update(0.02f, GH(0.1f, -1f), GH(-0.1f, 1f)));
            for (int i = 1; i <= 15; i++) evs.AddRange(g.Update(0.02f, GH(0.1f, -1f), GH(-0.1f - i * 0.04f, 1f)));
            Assert.AreEqual(0, evs.Count(e => e.id == SkillIds.Sweep));
        }

        [Test(Description = "合十之后两手自然分开，不会再触发外挥")]
        public void Review02_合十后分开不外挥()
        {
            var g = new GestureDetector();
            var evs = new List<InputEvent>();
            for (int i = 0; i < 40; i++) evs.AddRange(g.Update(0.02f, GH(0.03f, -1f), GH(-0.03f, 1f)));
            for (int i = 1; i <= 15; i++) { float x = 0.03f + i * 0.03f; evs.AddRange(g.Update(0.02f, GH(x, -1f), GH(-x, 1f))); }
            Assert.AreEqual(1, evs.Count(e => e.id == SkillIds.Pray));
            Assert.AreEqual(0, evs.Count(e => e.id == SkillIds.Sweep));
            // 完全分开过一次之后，再正常外挥可以触发
            for (int i = 0; i < 40; i++) g.Update(0.02f, GH(0.5f, -1f), GH(-0.5f, 1f));
            var evs2 = new List<InputEvent>();
            for (int i = 0; i < 5; i++) evs2.AddRange(g.Update(0.02f, GH(0.1f, -1f), GH(-0.1f, 1f)));
            for (int i = 1; i <= 15; i++) { float x = 0.1f + i * 0.024f; evs2.AddRange(g.Update(0.02f, GH(x, -1f), GH(-x, 1f))); }
            Assert.AreEqual(1, evs2.Count(e => e.id == SkillIds.Sweep));
        }

        [Test(Description = "没学会的技能手势不识别，也不占用识别锁")]
        public void Review03_未学会的技能不识别()
        {
            var g = new GestureDetector { allowed = id => id == SkillIds.Pray };
            var evs = new List<InputEvent>();
            for (int i = 0; i < 5; i++) evs.AddRange(g.Update(0.02f, GH(0.1f, -1f), GH(-0.1f, 1f)));
            for (int i = 1; i <= 15; i++) { float x = 0.1f + i * 0.024f; evs.AddRange(g.Update(0.02f, GH(x, -1f), GH(-x, 1f))); }
            Assert.AreEqual(0, evs.Count, "外挥没学会：不识别");
            for (int i = 0; i < 20; i++) evs.AddRange(g.Update(0.02f, GH(0.03f, -1f), GH(-0.03f, 1f)));
            Assert.AreEqual(1, evs.Count(e => e.id == SkillIds.Pray), "紧接着合十照常识别");
        }

        [Test(Description = "注视：直视缝合熊旁边的小鬼，选中的是小鬼（大目标的角半径加成封顶）")]
        public void Review04_熊旁边的小鬼看得定()
        {
            var g = Night(12);
            var f = Spawn(g, KindIds.Frank, pos: new Vector3(0f, TABLE.y, -0.6f));
            var s = Spawn(g, KindIds.Skeleton, pos: new Vector3(0.15f, TABLE.y, -0.6f));
            Run(g, 0.2, () => Frame(gaze: GazeToy(s)));
            Assert.AreEqual(s, g.gazeTarget);
        }

        [Test(Description = "盯熊时眨一下眼（移开 0.3 秒）不用从头再盯 0.8 秒")]
        public void Review05_眨眼不清零()
        {
            var g = Night(12);
            var f = Spawn(g, KindIds.Frank, pos: new Vector3(0f, TABLE.y, -0.6f));
            Run(g, 0.5, () => Frame(gaze: GazeToy(f)));
            Assert.IsFalse(f.frozen);
            Run(g, 0.3, Frame(gaze: NO_GAZE));
            Run(g, 0.3, () => Frame(gaze: GazeToy(f)));
            Assert.IsTrue(f.frozen);
        }

        [Test(Description = "伸手去捏糖桶提手：桶边刚看过的小鬼不会被隔空拉过来，糖桶被提起")]
        public void Review06_提手优先于隔空取物()
        {
            var g = Night(3);
            var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(JAR.x + 0.25f, TABLE.y, JAR.z - 0.05f));
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            var handle = new Vector3(JAR.x, JAR.y + 0.32f, JAR.z);
            var evs = g.Update(0.02, Frame(head: HEAD, gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, handle)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.That(Has(evs, GameEventTypes.BucketLift));
            Assert.IsFalse(Has(evs, GameEventTypes.PullStart));
        }

        [Test(Description = "被风吹走的小鬼约 10 秒后从远处回来（不是永久消灭）")]
        public void Review07_吹走的会回来()
        {
            var g = Night(7);
            var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(0.2f, TABLE.y, -0.7f));
            var evs = Run(g, 0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Sweep))));
            Assert.AreEqual(ToyStates.Launched, t.state);
            evs = Run(g, CONFIG.launch.blowSec + 0.1, Frame(head: AWAY));
            Assert.That(Has(evs, GameEventTypes.BlownAway));
            Assert.AreEqual(ToyStates.Gone, t.state);
            int before = g.toys.Count(x => x.state != ToyStates.Gone);
            evs = Run(g, 10.5, Frame(head: AWAY));
            Assert.That(evs.Exists(e => e.type == GameEventTypes.Spawn), "回来了");
            Assert.Greater(g.toys.Count(x => x.state != ToyStates.Gone), before);
        }

        [Test(Description = "吹走的路线：在桌下的先钻出桌沿，再升到桌面以上，全程不穿桌板")]
        public void Review08_吹走不穿桌()
        {
            var g = Night(7);
            var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(0.3f, 0.3f, -1.0f));   // 桌子下面（藤蔓上）
            t.state = ToyStates.Walk;
            Run(g, 0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Sweep))));
            if (t.state != ToyStates.Launched) Assert.Ignore("桌下的不在外挥范围内");
            for (int i = 0; i < 60 && t.state == ToyStates.Launched; i++)
            {
                g.Update(0.02, Frame(head: AWAY));
                bool insideSlab = OnTable(t.pos, 0.02f) && t.pos.y > TABLE.y - 0.06f && t.pos.y < TABLE.y + 0.01f;
                Assert.IsFalse(insideSlab, $"第 {i} 帧穿进桌板 {t.pos}");
            }
        }

        [Test(Description = "天亮时少于 3 颗糖算没守住；星级 11/7/3")]
        public void Review09_胜负与星级()
        {
            var g = Night(2);
            g.candyJar = 2;
            g.clock = g.night.duration - 0.01;
            var evs = Run(g, 0.1, Frame(head: AWAY));
            Assert.That(Has(evs, GameEventTypes.Fail));
            Assert.IsNotNull(g.result.tip, "没守住给一条小技巧");
            var g2 = Night(2);
            g2.candyJar = 7;
            g2.clock = g2.night.duration - 0.01;
            Run(g2, 0.1, Frame(head: AWAY));
            Assert.AreEqual(2, g2.result.stars);
            Assert.AreEqual(3, g2.result.challenges.Count);
        }

        [Test(Description = "练习按已解锁夜数扩充：第 1 夜只练基础，第 10 夜含四个技能")]
        public void Review10_练习按进度扩充()
        {
            var p1 = NightDefs.BuildPractice(1);
            Assert.That(p1.lesson.Exists(s => s.id == "pat") && p1.lesson.Exists(s => s.id == "bucket") && p1.lesson.Exists(s => s.id == "pot"));
            Assert.IsFalse(p1.lesson.Exists(s => s.id == "pray"));
            var p10 = NightDefs.BuildPractice(10);
            foreach (var id in new[] { "shake", "pull", "launch", "pray", "sweep", "shield", "lamp" })
                Assert.That(p10.lesson.Exists(s => s.id == id), id);
            Assert.AreEqual(4, p10.skills.Length);
        }

        [Test(Description = "糖桶最多提 8 秒，到时自动放下")]
        public void Review11_糖桶提起有时长()
        {
            var g = Night(3);
            var handle = new Vector3(JAR.x, JAR.y + 0.32f, JAR.z);
            g.Update(0.02, Frame(head: HEAD, hands: Hands(Hand(Sides.Right, handle)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.AreEqual(Sides.Right, g.bucketHeld);
            var evs = Run(g, Game.BUCKET_HOLD_MAX + 0.2, Frame(head: AWAY, hands: Hands(Hand(Sides.Right, handle))));
            Assert.IsNull(g.bucketHeld);
            Assert.That(Has(evs, GameEventTypes.BucketPut));
        }

        [Test(Description = "护罩结束时把桶边排队的小鬼弹开，不会一拥而上")]
        public void Review12_护罩结束弹开()
        {
            var g = Night(9);
            g.Update(0.02, Frame(head: AWAY, events: Evs(InputEvent.Skill(SkillIds.Shield))));
            var t = Spawn(g, KindIds.Skeleton, pos: new Vector3(JAR.x + 0.15f, TABLE.y, JAR.z));
            t.state = ToyStates.Walk;
            t.path = new List<Vector3> { t.pos, t.pos };
            var evs = Run(g, CONFIG.skills.shield.duration + 0.1, Frame(head: AWAY));
            Assert.That(Has(evs, GameEventTypes.ShieldEnd));
            Assert.That(t.dizzy > 0 || t.state == ToyStates.Falling, "护罩结束时被弹开晕一下");
        }
    }
}
