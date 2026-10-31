// 对应原型 test/helpers.js：测试用的输入帧构造、种子随机数、常用操作序列。
using System;
using System.Collections.Generic;
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.Tests
{
    public static class H
    {
        public static readonly HeadPose HEAD = new HeadPose(new Vector3(0f, 1.15f, 0f), new Vector3(0f, -0.4f, -1f));
        public static readonly HeadPose AWAY = new HeadPose(HEAD.pos, new Vector3(0f, 0f, 1f));

        // 与 JS 相同的种子算法：seed = (seed * 16807) % 2147483647; return seed / 2147483647。用 long 防溢出
        public static Func<float> Seeded(long seed = 1)
        {
            return () =>
            {
                seed = seed * 16807L % 2147483647L;
                return (float)(seed / 2147483647.0);
            };
        }

        public static Vector3 Center(Toy t) => new Vector3(t.pos.x, t.pos.y + EnemyKinds.Get(t.kind).height * 0.5f, t.pos.z);
        public static GazeRay GazeAt(Vector3 p) => new GazeRay { origin = HEAD.pos, dir = Vec.Norm(p - HEAD.pos), valid = true, source = GazeSources.Eye };
        public static GazeRay GazeToy(Toy t) => GazeAt(Center(t));
        public static GazeRay NO_GAZE => new GazeRay { origin = HEAD.pos, dir = new Vector3(0f, 1f, 0f), valid = true, source = GazeSources.Eye };

        public static InputFrame Frame(HeadPose head = null, GazeRay gaze = null, List<HandInput> hands = null, List<InputEvent> events = null) =>
            new InputFrame { head = head ?? HEAD, gaze = gaze ?? NO_GAZE, hands = hands ?? new List<HandInput>(), events = events ?? new List<InputEvent>() };

        public static HandInput Hand(string side, Vector3 point, bool pinching = true) => new HandInput(side, point, pinching);
        public static List<HandInput> Hands(params HandInput[] hs) => new List<HandInput>(hs);
        public static List<InputEvent> Evs(params InputEvent[] es) => new List<InputEvent>(es);

        static int Steps(double sec) => (int)Math.Round(sec / 0.02, MidpointRounding.AwayFromZero);

        public static List<GameEvent> Run(Game g, double sec, Func<InputFrame> f)
        {
            var outList = new List<GameEvent>();
            int n = Steps(sec);
            for (int i = 0; i < n; i++) outList.AddRange(g.Update(0.02, f()));
            return outList;
        }

        public static List<GameEvent> Run(Game g, double sec, InputFrame f) => Run(g, sec, () => f);

        // 开始某一夜（1 起），跳过教学，关掉自动生成，方便手动摆敌人
        public static Game Night(int n, long seed = 7, bool residual = true)
        {
            var g = new Game(new GameOptions { rand = Seeded(seed), residual = residual });
            g.StartNight(n - 1);
            if (g.lesson != null) g.FinishLesson();
            if (g.brief != null) g.FinishBrief();
            g.Update(0.001, Frame(head: AWAY));
            g.director.t = float.PositiveInfinity;
            g.director.burstT = float.PositiveInfinity;
            g.director.bossAt = float.PositiveInfinity;
            g.pendingSays.Clear();
            return g;
        }

        public static Toy Spawn(Game g, string kind, float atRim = 0f, Vector3? pos = null) =>
            g.SpawnToy(Game.SPAWNS[0], new SpawnOpts { kind = kind, atRim = atRim, pos = pos, silent = true });

        // 盯住 → 捏起（返回捏合点）
        public static Vector3 PickUp(Game g, Toy t, string side = Sides.Right)
        {
            Run(g, 0.3, () => Frame(gaze: GazeToy(t)));
            var point = new Vector3(t.pos.x, t.pos.y + EnemyKinds.Get(t.kind).height * 0.6f, t.pos.z);
            g.Update(0.02, Frame(gaze: GazeToy(t), hands: Hands(Hand(side, point)), events: Evs(InputEvent.PinchStart(side))));
            return point;
        }

        public static Vector3 MoveHand(Game g, string side, Vector3 from, Vector3 to, int steps = 20)
        {
            var p = from;
            for (int i = 1; i <= steps; i++) { p = Vec.Lerp(from, to, (float)i / steps); g.Update(0.02, Frame(hands: Hands(Hand(side, p)))); }
            return p;
        }

        public static List<GameEvent> Release(Game g, string side, Vector3 p) =>
            g.Update(0.02, Frame(hands: Hands(Hand(side, p)), events: Evs(InputEvent.PinchEnd(side))));

        // 快速甩出：沿 dir 以 speed m/s 甩 frames 帧后松手
        public static List<GameEvent> Fling(Game g, string side, Vector3 p, Vector3 dir, float speed = 3f, int frames = 5)
        {
            // 甩的过程中发金光的会直接飞出去（10-30），所以把甩动途中的事件也一起返回
            var all = new List<GameEvent>();
            for (int i = 0; i < frames; i++) { p = p + dir * (speed * 0.02f); all.AddRange(g.Update(0.02, Frame(hands: Hands(Hand(side, p))))); }
            all.AddRange(Release(g, side, p));
            return all;
        }

        public static (Vector3 q, List<GameEvent> evs) Shake(Game g, string side, Vector3 p, int n = 10)
        {
            var q = p;
            var outList = new List<GameEvent>();
            for (int i = 0; i < n; i++)
                for (int k = 0; k < 4; k++)
                {
                    q = q + new Vector3((i % 2 != 0 ? -1 : 1) * 0.035f, 0f, 0f);
                    outList.AddRange(g.Update(0.02, Frame(hands: Hands(Hand(side, q)))));
                }
            return (q, outList);
        }

        public static void GiveCandy(Game g, Toy t) { g.candyJar--; t.candyCount = 1; t.hasCandy = true; g.StartFlee(t); }

        public static bool Has(List<GameEvent> evs, string type) => evs.Exists(e => e.type == type);
        public static string Json(Vector3 p) => $"{{\"x\":{p.x},\"y\":{p.y},\"z\":{p.z}}}";
    }
}
