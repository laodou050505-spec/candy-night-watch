// 对应原型 test/lesson.test.js（5 项）。
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;
using static TrickOrFreeze.Tests.H;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Tests
{
    public class LessonTests
    {
        static Game Start(int n) { var g = new Game(new GameOptions { rand = Seeded(5) }); g.StartNight(n - 1); return g; }
        static Toy LessonToy(Game g) => g.lesson.toys.Where(t => t.state != ToyStates.Gone).Last();
        static string StepId(Game g) => g.lesson.steps[g.lesson.step].id;

        static bool Until(Game g, Func<bool> pred, double sec, Func<InputFrame> f)
        {
            for (int i = 0; i < sec / 0.02; i++) { g.Update(0.02, f()); if (pred()) return true; }
            return false;
        }
        static bool Until(Game g, Func<bool> pred, double sec, InputFrame f) => Until(g, pred, sec, () => f);

        [Test(Description = "每夜都有新东西（新手教学 / 开夜介绍 / 新敌人 / 波次 / 首领），新东西不会悄悄出现")]
        public void Lesson01_每夜都有新东西()
        {
            foreach (var n in NightDefs.NIGHTS) Assert.That(n.HasLesson || n.HasBrief || n.feature != null || n.boss != null || n.burst != null, $"夜{n.id}");
            // 只有第一夜有互动式新手教学；之后的新本事都在解锁那一夜开场时介绍
            Assert.That(NightDefs.NIGHTS[0].HasLesson);
            foreach (var n in NightDefs.NIGHTS.Skip(1)) Assert.That(!n.HasLesson, $"夜{n.id} 不该再有互动教学");
            var taught = new HashSet<string>(NightDefs.NIGHTS.SelectMany(n => (n.lesson ?? new List<LessonStep>()).Concat(n.brief ?? new List<LessonStep>()).Select(s => s.id)));
            foreach (var s in new[] { "pray", "sweep", "shield", "lamp", "shake", "throw", "launch", "cauldron", "pull" }) Assert.That(taught.Contains(s), s);
        }

        [Test(Description = "第一夜教学：看 → 残留 → 捏 → 放坩埚；教学期间不计时，完成后才开始")]
        public void Lesson02_第一夜教学()
        {
            var g = Start(1);
            Run(g, 2.5, Frame(head: AWAY));
            Assert.AreEqual("look", StepId(g));
            Assert.AreEqual(0f, g.clock);
            var t = LessonToy(g);
            Assert.That(Until(g, () => StepId(g) == "residual", 3, () => Frame(gaze: GazeToy(t))));
            Assert.That(Until(g, () => StepId(g) == "grab", 3, Frame()));
            Run(g, 0.2, () => Frame(gaze: GazeToy(t)));
            var p = new Vector3(t.pos.x, t.pos.y + 0.12f, t.pos.z);
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, p)), events: Evs(InputEvent.PinchStart(Sides.Right))));
            Assert.That(Until(g, () => StepId(g) == "cauldron", 1, Frame(hands: Hands(Hand(Sides.Right, p)))));
            var q = MoveHand(g, Sides.Right, p, new Vector3(BOX.x, BOX.y + 0.3f, BOX.z), 20);
            Release(g, Sides.Right, q);
            Run(g, 0.1, Frame());
            Assert.AreEqual(true, g.lesson.done);
            Assert.AreEqual(true, g.clockRunning);
            Assert.AreEqual(true, g.clock < 0.2f);
        }

        [Test(Description = "教学卡住不动：14 秒后重复提示，不会自动跳过")]
        public void Lesson03_卡住重复提示()
        {
            var g = Start(1);
            var evs = Run(g, 18, Frame(head: AWAY));
            Assert.That(Has(evs, GameEventTypes.LessonHint));
            Assert.AreEqual(false, g.lesson.done);
            Assert.AreEqual(0f, g.clock);
        }

        [Test(Description = "第二夜开夜介绍：依次说两句（不出教学敌人、不等玩家做），说完才开始计时出怪")]
        public void Lesson04_第二夜开夜介绍()
        {
            var g = Start(2);
            Assert.IsNull(g.lesson);
            Assert.IsNotNull(g.brief);
            var evs = Run(g, 3.5, Frame(head: AWAY));
            Assert.AreEqual("shake", g.brief.Current.id);
            Assert.That(evs.Exists(e => e.type == GameEventTypes.BriefStep && e.strId == "shake"));
            Assert.AreEqual(0f, g.clock);
            Assert.AreEqual(0, g.toys.Count);
            Assert.That(Until(g, () => g.brief.Current != null && g.brief.Current.id == "candy", 8, Frame(head: AWAY)));
            Assert.That(Until(g, () => g.brief.done, 8, Frame(head: AWAY)));
            Assert.AreEqual(true, g.clockRunning);
            Assert.That(Until(g, () => g.toys.Count > 0, 3, Frame(head: AWAY)), "介绍完很快开始出怪");
        }

        [Test(Description = "新手教学只在第一次：完成过以后第一夜直接开始，不再教")]
        public void Lesson05_教学只出现一次()
        {
            var g = new Game(new GameOptions { rand = Seeded(5) }) { tutorial = false };
            g.StartNight(0);
            Assert.IsNull(g.lesson);
            Assert.IsNull(g.brief);
            Assert.AreEqual(true, g.clockRunning);
            Assert.That(Until(g, () => g.toys.Count > 0, 4, Frame(head: AWAY)));
        }

        [Test(Description = "开夜介绍可以跳过：跳过后立刻开始计时")]
        public void Lesson06_跳过开夜介绍()
        {
            var g = Start(5);
            Run(g, 3.5, Frame(head: AWAY));
            Assert.AreEqual("pray", g.brief.Current.id);
            g.FinishBrief();
            Run(g, 0.1, Frame());
            Assert.AreEqual(true, g.brief.done);
            Assert.AreEqual(true, g.clockRunning);
            Assert.That(g.clock > 0);
        }
    }
}
