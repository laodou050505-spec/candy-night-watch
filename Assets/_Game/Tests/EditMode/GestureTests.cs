// 对应原型 test/gestures.test.js（8 项）。
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.Tests
{
    public class GestureTests
    {
        static GestureHand Hand(float x, float y = 0.95f, float z = -0.3f, float nx = 0f, bool pinching = false) =>
            new GestureHand(new Vector3(x, y, z), new Vector3(nx, 0f, 0f), pinching);
        static GestureHand L(float x, bool p = false) => Hand(x, 0.95f, -0.3f, 1f, p);   // 左手掌心朝右
        static GestureHand R(float x, bool p = false) => Hand(x, 0.95f, -0.3f, -1f, p);  // 右手掌心朝左
        static GestureHand Up(float x, float y = 0.95f) => new GestureHand(new Vector3(x, y, -0.3f), new Vector3(0f, 1f, 0f));
        static GestureHand Down(float x, float y = 0.95f) => new GestureHand(new Vector3(x, y, -0.3f), new Vector3(0f, -1f, 0f));

        static List<InputEvent> Feed(GestureDetector g, IEnumerable<(GestureHand l, GestureHand r)> frames)
        {
            var outList = new List<InputEvent>();
            foreach (var (l, r) in frames) outList.AddRange(g.Update(0.02f, l, r));
            return outList;
        }

        static IEnumerable<(GestureHand, GestureHand)> Repeat(int n, Func<int, (GestureHand, GestureHand)> f) => Enumerable.Range(0, n).Select(f);

        [Test(Description = "合十保持 0.3 秒触发一次，不重复触发")]
        public void Gesture01_合十触发一次()
        {
            var g = new GestureDetector();
            var evs = Feed(g, Repeat(60, _ => (L(-0.03f), R(0.03f))));
            Assert.AreEqual(1, evs.Count(e => e.id == SkillIds.Pray));
        }

        [Test(Description = "合十太短不触发；捏合中不触发")]
        public void Gesture02_合十太短或捏合不触发()
        {
            var g = new GestureDetector();
            Assert.AreEqual(0, Feed(g, Repeat(10, _ => (L(-0.03f), R(0.03f)))).Count);
            var g2 = new GestureDetector();
            Assert.AreEqual(0, Feed(g2, Repeat(60, _ => (L(-0.03f, true), R(0.03f)))).Count);
        }

        [Test(Description = "外挥：胸前靠近后 0.4 秒内向两侧张开")]
        public void Gesture03_外挥()
        {
            var g = new GestureDetector();
            var frames = new List<(GestureHand, GestureHand)>();
            for (int i = 0; i < 5; i++) frames.Add((L(-0.1f), R(0.1f)));
            for (int i = 1; i <= 15; i++) { float x = 0.1f + i * 0.024f; frames.Add((L(-x), R(x))); }
            var evs = Feed(g, frames);
            CollectionAssert.AreEqual(new[] { SkillIds.Sweep }, evs.Select(e => e.id).ToArray());
        }

        [Test(Description = "慢慢张开不算外挥")]
        public void Gesture04_慢慢张开不算()
        {
            var g = new GestureDetector();
            var frames = new List<(GestureHand, GestureHand)> { (L(-0.1f), R(0.1f)) };
            for (int i = 1; i <= 80; i++) { float x = 0.1f + i * 0.0035f; frames.Add((L(-x), R(x))); }
            Assert.AreEqual(0, Feed(g, frames).Count);
        }

        [Test(Description = "双手上下分开不算外挥")]
        public void Gesture05_上下分开不算()
        {
            var g = new GestureDetector();
            var frames = new List<(GestureHand, GestureHand)> { (L(-0.05f), R(0.05f)) };
            for (int i = 1; i <= 15; i++) frames.Add((Hand(-0.05f, 0.95f + i * 0.025f, -0.3f, 1f), Hand(0.05f, 0.95f - i * 0.025f, -0.3f, -1f)));
            Assert.AreEqual(0, Feed(g, frames).Count(e => e.id == SkillIds.Sweep));
        }

        // 南瓜灯手形：五指伸直张开、掌心朝外（远离头）
        static GestureHand Lantern(float x, float spread = 1.7f, float ext = 0.95f) =>
            new GestureHand(new Vector3(x, 1.0f, -0.35f), new Vector3(0f, 0f, -1f)) { hasShape = true, ext = ext, spread = spread, away = new Vector3(0f, -0.2f, -1f).normalized, hasAway = true };

        [Test(Description = "南瓜灯：五指张开、手背朝自己静止 0.6 秒，触发一次，灯在手前方")]
        public void Gesture06_南瓜灯()
        {
            var g = new GestureDetector();
            var evs = Feed(g, Repeat(60, _ => ((GestureHand)null, Lantern(0.2f))));
            var lamps = evs.Where(e => e.id == SkillIds.Lamp).ToList();
            Assert.AreEqual(1, lamps.Count);
            Assert.That(lamps[0].pos.HasValue && lamps[0].pos.Value.z < -0.5f, "灯飞到手前方");
            Assert.That(lamps[0].from.HasValue, "带飞出起点");
        }

        [Test(Description = "南瓜灯：手在动、手指没张开、掌心朝上（旧手势）都不触发")]
        public void Gesture07_南瓜灯误触发()
        {
            var g = new GestureDetector();
            Assert.AreEqual(0, Feed(g, Repeat(60, i => ((GestureHand)null, Lantern(0.2f + (i % 2) * 0.01f)))).Count, "手在动");
            g = new GestureDetector();
            Assert.AreEqual(0, Feed(g, Repeat(60, _ => ((GestureHand)null, Lantern(0.2f, spread: 1.1f)))).Count, "手指并拢");
            g = new GestureDetector();
            var up = Up(0.2f); up.hasShape = true; up.ext = 0.95f; up.spread = 1.7f; up.away = new Vector3(0f, -0.2f, -1f).normalized; up.hasAway = true;
            Assert.AreEqual(0, Feed(g, Repeat(60, _ => ((GestureHand)null, up))).Count, "掌心朝上");
        }

        [Test(Description = "合十：实机两掌贴合时手指被算得有点弯（伸直程度约 0.75）也要能识别")]
        public void Gesture07c_手指略弯也能合十()
        {
            var g = new GestureDetector();
            GestureHand Bent(GestureHand h) { h.hasShape = true; h.ext = 0.75f; h.spread = 1.0f; return h; }
            var evs = Feed(g, Repeat(60, _ => (Bent(L(-0.03f)), Bent(R(0.03f)))));
            Assert.AreEqual(1, evs.Count(e => e.id == SkillIds.Pray));
        }

        [Test(Description = "合十：两手握拳碰在一起不算")]
        public void Gesture07b_握拳碰手不合十()
        {
            var g = new GestureDetector();
            GestureHand Fist(GestureHand h) { h.hasShape = true; h.ext = 0.6f; h.spread = 1.0f; return h; }
            var evs = Feed(g, Repeat(60, _ => (Fist(L(-0.03f)), Fist(R(0.03f)))));
            Assert.AreEqual(0, evs.Count(e => e.id == SkillIds.Pray));
        }

        [Test(Description = "护罐：双掌朝下靠近、一起快速下按")]
        public void Gesture08_护桶()
        {
            var g = new GestureDetector();
            var frames = new List<(GestureHand, GestureHand)>();
            for (int i = 0; i < 3; i++) frames.Add((Down(-0.1f, 1.05f), Down(0.1f, 1.05f)));
            for (int i = 1; i <= 6; i++) frames.Add((Down(-0.1f, 1.05f - i * 0.02f), Down(0.1f, 1.05f - i * 0.02f)));
            var evs = Feed(g, frames);
            CollectionAssert.AreEqual(new[] { SkillIds.Shield }, evs.Select(e => e.id).ToArray());
        }
    }
}
