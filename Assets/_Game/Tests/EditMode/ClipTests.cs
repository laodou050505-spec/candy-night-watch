// 10-31 防穿模巡检（用户：吹飞可以、回来可以，但一定不能穿模）：用编辑器烘焙的场景占用网格（Resources/solid_grid.bytes），
// 逐帧检查小鬼身体（按模型实际半宽）有没有进到场景摆件里。只有桌面上不查（桌上的糖桶、坩埚、烛台由规则层另行绕开）；
// 顺藤爬、蜘蛛吊丝、刚出生都要查（出生瞬间单独计数，也必须为 0）。
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;
using static TrickOrFreeze.Tests.H;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Tests
{
    public class ClipTests
    {
        static SolidGrid grid;
        static SolidGrid Grid()
        {
            if (grid != null) return grid;
            var ta = Resources.Load<TextAsset>("solid_grid");
            Assert.IsNotNull(ta, "没有 solid_grid.bytes（先 Bake Solid Grid）");
            grid = SolidGrid.FromBytes(ta.bytes);
            return grid;
        }

        class Audit
        {
            public int frames, hits, spawnHits; public readonly StringBuilder log = new StringBuilder(); int shown;
            readonly Dictionary<int, double> seen = new Dictionary<int, double>();
            readonly HashSet<int> hitIds = new HashSet<int>();
            static readonly string[] Check = { ToyStates.Walk, ToyStates.Flee, ToyStates.Steal, ToyStates.Falling, ToyStates.Launched, ToyStates.Glide, ToyStates.Stunned, ToyStates.Pulled };
            public void Frame(Game g, string tag)
            {
                frames++;
                foreach (var t in g.toys)
                {
                    if (!seen.ContainsKey(t.id)) seen[t.id] = g.time;
                    if (System.Array.IndexOf(Check, t.state) < 0) continue;
                    var k = t.Kind;
                    if (t.pos.y >= TABLE.y - 0.02f && OnTable(t.pos)) continue;                          // 桌面上
                    var c = t.pos + Vector3.up * (k.height * 0.5f);
                    if (!grid.SolidSphere(c, k.BodyR * 0.9f)) continue;
                    bool young = g.time - seen[t.id] < 0.6;
                    if (young) { spawnHits++; if (shown++ < 12) log.AppendLine($"  [出生] {tag} {t.kind}#{t.id} {t.state} pos=({t.pos.x:F2},{t.pos.y:F2},{t.pos.z:F2})"); continue; }
                    hits++;
                    if (hitIds.Add(t.id) && shown++ < 12) log.AppendLine($"  {tag} {t.kind}#{t.id} {t.state} pos=({t.pos.x:F2},{t.pos.y:F2},{t.pos.z:F2})" + (t.state == ToyStates.Launched ? $" blow={t.launch.blow} k={t.launch.t:F2}/{t.launch.endK:F2}" : ""));
                }
            }
        }

        static Game NewNight(int n, long seed, bool director)
        {
            var g = new Game(new GameOptions { rand = Seeded(seed) });
            g.solid = Grid();
            g.StartNight(n - 1);
            if (g.lesson != null) g.FinishLesson();
            if (g.brief != null) g.FinishBrief();
            if (!director) { g.Update(0.001, Frame(head: AWAY)); g.director.t = float.PositiveInfinity; g.director.burstT = float.PositiveInfinity; g.director.bossAt = float.PositiveInfinity; g.pendingSays.Clear(); }
            return g;
        }

        [Test(Description = "十二夜挂机跑完（出生、走、飞、偷、逃、首领）：身体不进场景摆件")]
        public void Clip01_十二夜挂机()
        {
            Grid();
            var a = new Audit();
            for (int i = 1; i <= 12; i++)
                for (long seed = 3; seed <= 4; seed++)
                {
                    var g = NewNight(i, seed, true);
                    for (int f = 0; f < 170 / 0.02 && g.phase == Phases.Playing; f++)
                    {
                        if (g.candyJar < 6) g.candyJar = 12;
                        g.Update(0.02, Frame(head: AWAY));
                        a.Frame(g, $"夜{i}");
                    }
                }
            Assert.AreEqual(0, a.hits + a.spawnHits, $"穿模 {a.hits} 帧（检查 {a.frames} 帧；出生瞬间 {a.spawnHits}）：\n{a.log}");
        }

        [Test(Description = "从桌沿往各个方向扔、落地后走回来：身体不进场景摆件")]
        public void Clip02_扔飞落地走回()
        {
            Grid();
            var a = new Audit();
            foreach (var kind in new[] { KindIds.Skeleton, KindIds.Pumpkin, KindIds.Cat, KindIds.Ghostling, KindIds.Frank })
            foreach (float speed in new[] { 1.5f, 3f, 4.5f })
                for (int ang = 0; ang < 360; ang += 20)
                {
                    var g = NewNight(12, 5 + ang, false);
                    float rad = ang * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(rad), 0, -Mathf.Cos(rad));
                    var start = new Vector3(TABLE.center.x + dir.x * 0.9f, TABLE.y + 0.25f, TABLE.center.z + dir.z * 0.9f);
                    var t = Spawn(g, kind, pos: start);
                    t.state = ToyStates.Falling; t.pos = start; t.vel = dir * speed + Vector3.up * 1.2f; t.dizzy = 1.2;
                    for (int f = 0; f < 8 / 0.02; f++) { if (g.candyJar < 6) g.candyJar = 12; g.Update(0.02, Frame(head: AWAY)); a.Frame(g, $"扔 {kind} {ang}° {speed}m/s"); }
                }
            Assert.AreEqual(0, a.hits, $"穿模 {a.hits} 帧（检查 {a.frames} 帧）：\n{a.log}");
        }

        [Test(Description = "隔空取物：桌外地上、桌下、桌上的小鬼被拉到手里，途中不进场景摆件")]
        public void Clip05_隔空拉来()
        {
            Grid();
            var a = new Audit();
            int n = 0;
            for (int ang = -150; ang <= 150; ang += 25)
                foreach (float rr in new[] { 0.7f, 1.6f, 2.2f })
                {
                    var g = NewNight(3, 31 + n++, false);
                    float rad = ang * Mathf.Deg2Rad;
                    var p = new Vector3(TABLE.center.x + Mathf.Sin(rad) * rr, 0f, TABLE.center.z - Mathf.Cos(rad) * rr);
                    if (Grid().SolidSphere(p + Vector3.up * 0.08f, 0.04f)) continue;
                    var t = Spawn(g, KindIds.Skeleton, pos: p);
                    t.state = ToyStates.Walk; t.path = new List<Vector3> { p, p };
                    var hand = new Vector3(0f, 1.0f, -0.45f);
                    for (int f = 0; f < 15; f++) g.Update(0.02, Frame(head: HEAD, gaze: GazeToy(t)));
                    g.Update(0.02, Frame(head: HEAD, gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hand)), events: Evs(InputEvent.PinchStart(Sides.Right))));
                    if (t.state != ToyStates.Pulled) continue;
                    for (int f = 0; f < 1.5 / 0.02 && t.state == ToyStates.Pulled; f++) { g.Update(0.02, Frame(head: HEAD, gaze: GazeToy(t), hands: Hands(Hand(Sides.Right, hand)))); a.Frame(g, $"拉 {ang}° r{rr}"); }
                }
            Assert.Greater(a.frames, 50, "拉的场景太少");
            Assert.AreEqual(0, a.hits, $"穿模 {a.hits} 帧（检查 {a.frames} 帧）：\n{a.log}");
        }

        [Test(Description = "边盯边玩（会定身、蝙蝠被盯会滑翔落下、合十全场定住）：第 5/8/11/12 夜不穿模")]
        public void Clip06_盯着玩()
        {
            Grid();
            var a = new Audit();
            foreach (int night in new[] { 5, 8, 11, 12 })
            {
                var g = NewNight(night, 41 + night, true);
                Toy look = null; double nextLook = 0, nextPray = 6;
                for (int f = 0; f < 150 / 0.02 && g.phase == Phases.Playing; f++)
                {
                    if (g.candyJar < 6) g.candyJar = 12;
                    if (g.time > nextLook || look == null || look.state == ToyStates.Gone)
                    {
                        var live = g.toys.Where(x => x.state != ToyStates.Gone && x.state != ToyStates.Boxed).ToList();
                        look = live.Count > 0 ? live[(f / 7) % live.Count] : null; nextLook = g.time + 0.8;
                    }
                    var evs = new List<InputEvent>();
                    if (g.time > nextPray) { evs.Add(InputEvent.Skill(SkillIds.Pray)); nextPray = g.time + 9; }
                    g.Update(0.02, Frame(head: HEAD, gaze: look != null ? GazeToy(look) : NO_GAZE, events: evs));
                    a.Frame(g, $"夜{night}盯");
                }
            }
            Assert.AreEqual(0, a.hits, $"穿模 {a.hits} 帧（检查 {a.frames} 帧；出生瞬间 {a.spawnHits}）：\n{a.log}");
        }

        [Test(Description = "外挥吹走：桌上、桌下、地上的小鬼往各个方向飞走，途中不进场景摆件")]
        public void Clip03_吹走()
        {
            Grid();
            var a = new Audit();
            var starts = new List<Vector3>();
            for (int ang = 0; ang < 360; ang += 30)
            {
                float rad = ang * Mathf.Deg2Rad;
                starts.Add(new Vector3(TABLE.center.x + Mathf.Sin(rad) * 0.8f, TABLE.y, TABLE.center.z - Mathf.Cos(rad) * 0.8f));   // 桌上
                starts.Add(new Vector3(TABLE.center.x + Mathf.Sin(rad) * 0.7f, 0f, TABLE.center.z - Mathf.Cos(rad) * 0.7f));       // 桌下
                starts.Add(new Vector3(TABLE.center.x + Mathf.Sin(rad) * 1.6f, 0f, TABLE.center.z - Mathf.Cos(rad) * 1.6f));       // 桌外地上
            }
            foreach (var kind in new[] { KindIds.Skeleton, KindIds.Bat, KindIds.Ghost })
                for (int i = 0; i < starts.Count; i++)
                {
                    var g = NewNight(7, 11 + i, false);
                    var t = Spawn(g, kind, pos: starts[i]);
                    if (Grid().SolidSphere(t.pos + Vector3.up * (t.Kind.height * 0.5f), t.Kind.BodyR * 0.9f)) { g.toys.Remove(t); continue; }   // 起点本身在摆件里的跳过
                    t.state = ToyStates.Walk;
                    var dir = new Vector3(t.pos.x, 0, t.pos.z - 0.15f).normalized;
                    g.BlowForTest(t, dir);
                    for (int f = 0; f < 2.0 / 0.02 && t.state == ToyStates.Launched; f++) { g.Update(0.02, Frame(head: AWAY)); a.Frame(g, $"吹 {kind} 起点{i}"); }
                }
            Assert.AreEqual(0, a.hits, $"穿模 {a.hits} 帧（检查 {a.frames} 帧）：\n{a.log}");
        }

        [Test(Description = "甩成星星：从桌边各处、各种方向甩出，飞向铁拱门途中不进场景摆件")]
        public void Clip04_甩星星()
        {
            Grid();
            var a = new Audit();
            int n = 0;
            for (int ang = -60; ang <= 60; ang += 20)
                foreach (var v in new[] { new Vector3(0, 0.4f, -1), new Vector3(1, 0.3f, -0.5f), new Vector3(-1, 0.3f, -0.5f), new Vector3(0, 1, 0), new Vector3(0, 0.2f, 1) })
                {
                    var g = NewNight(4, 21 + n++, false);
                    float rad = ang * Mathf.Deg2Rad;
                    var hand = new Vector3(Mathf.Sin(rad) * 0.55f, 1.0f, -0.35f - Mathf.Cos(rad) * 0.2f);
                    var t = Spawn(g, KindIds.Skeleton, pos: hand);
                    g.LaunchForTest(t, v.normalized * 2.5f);
                    for (int f = 0; f < 2.5 / 0.02 && t.state == ToyStates.Launched; f++) { g.Update(0.02, Frame(head: AWAY)); a.Frame(g, $"星 {ang}° {v}"); }
                }
            Assert.AreEqual(0, a.hits, $"穿模 {a.hits} 帧（检查 {a.frames} 帧）：\n{a.log}");
        }
    }
}
