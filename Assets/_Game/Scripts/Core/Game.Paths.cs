// 出生点与寻路（对应 game.js 顶部的 SPAWNS / SKY_SPAWNS / DROP_SPAWNS 与 planTo* 系列函数）。
// 敌人从花园里的棺材、衣柜、墓碑、南瓜堆钻出来，走到桌腿旁的南瓜藤，顺着藤爬上桌，再走向南瓜糖桶。
using System.Collections.Generic;
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        // 地面出生点：花园里四件“会冒出小鬼”的摆件跟前（朝桌子那一侧）。GameLoop 开局时按场景里摆件的实际位置重算（SetGroundSpawns），
        // 这里是与当前场景一致的默认值，规则测试也用它。
        public static SpawnPoint[] SPAWNS =
        {
            new SpawnPoint("coffin", "小棺材", new Vector3(-2.48f, 0f, -0.83f), -100),
            new SpawnPoint("wardrobe", "獠牙衣柜", new Vector3(2.2f, 0f, -1.02f), 100),
            new SpawnPoint("grave", "墓碑", new Vector3(-1.28f, 0f, -3.24f), -40),
            new SpawnPoint("pumpkins", "南瓜堆", new Vector3(1.28f, 0f, -3.24f), 40),
        };

        public static void SetGroundSpawns(SpawnPoint[] spawns) { if (spawns != null && spawns.Length > 0) SPAWNS = spawns; }

        // 飞行敌人的出生点：正前方铸铁拱门外、左墙外（墓园上空）、右墙外（南瓜田上空）
        public static readonly SpawnPoint[] SKY_SPAWNS =
        {
            new SpawnPoint("gate", "拱门", new Vector3(0f, 2.0f, -5.4f), 0),
            new SpawnPoint("left-high", "墓园上空", new Vector3(-5.0f, 2.6f, -2.6f), -100),   // 飞向糖桶时从藤架两根立柱中间穿过
            new SpawnPoint("right-high", "南瓜田上空", new Vector3(5.0f, 2.4f, -2.6f), 100),
        };

        // 蜘蛛的垂降点：糖桶前方那根藤架椽子底下（玩家要抬头才看得到它垂下来），沿椽子排开四个点
        public static readonly SpawnPoint[] DROP_SPAWNS = BuildDropSpawns();

        static SpawnPoint[] BuildDropSpawns()
        {
            float[] xs = { -0.36f, -0.12f, 0.12f, 0.36f };
            var list = new SpawnPoint[xs.Length];
            for (int i = 0; i < xs.Length; i++)
                list[i] = new SpawnPoint($"rafter-{i}", "藤架椽子", new Vector3(JAR.x + xs[i], PERGOLA.rafterY, -1.45f), xs[i] < 0 ? -40 : 40);
            return list;
        }

        const float CANDY_R = 0.018f;
        const float TABLE_SLAB = 0.06f;   // 桌板厚度（拎着的东西不会被压进桌板里）

        // 四条桌腿旁各长一根南瓜藤：从桌外 1.8m 的地面拱起，弯着搭过桌沿（比桌沿唇边高出几厘米，不碰桌子），落到桌面上。
        // 小鬼顺着藤顶往上爬；藤的网格（GameplayBuilder）和这里的爬行路线用同一组剖面，严丝合缝。
        // 剖面点：x = 到桌心的水平距离，y = 藤顶面（小鬼脚底）高度。桌沿外缘最宽 r≈1.26（y 0.64~0.76），唇边顶 0.752（r 1.2）。
        public static readonly float[] LEGS = { -100, -40, 40, 100 };
        public static readonly Vector2[] VINE =
        {
            new Vector2(1.80f, 0.00f), new Vector2(1.72f, 0.15f), new Vector2(1.63f, 0.31f), new Vector2(1.54f, 0.47f),
            new Vector2(1.45f, 0.62f), new Vector2(1.37f, 0.75f), new Vector2(1.28f, 0.83f), new Vector2(1.19f, 0.835f),
            new Vector2(1.11f, 0.80f), new Vector2(1.04f, 0.75f), new Vector2(0.98f, 0.72f),
        };
        public const float VINE_R = 0.034f;    // 藤的粗细（半径）：藤中心线 = 顶面往下 VINE_R
        static Vector3 AtRadius(float a, float r, float y) { float rad = a * Mathf.Deg2Rad; return new Vector3(TABLE.center.x + Mathf.Sin(rad) * r, y, TABLE.center.z - Mathf.Cos(rad) * r); }
        public static Vector3 VinePoint(float a, int i) => AtRadius(a, VINE[i].x, VINE[i].y);
        public static Vector3 LegFloor(float a) => VinePoint(a, 0);                               // 藤根（地面）
        static Vector3 LegTop(float a) => VinePoint(a, VINE.Length - 1);                         // 藤梢落在桌面上
        // 顺着藤的中间各点（不含两端）：上行从藤根往藤梢，下行反过来
        static IEnumerable<Vector3> VineMid(float a, bool up)
        {
            if (up) for (int i = 1; i < VINE.Length - 1; i++) yield return VinePoint(a, i);
            else for (int i = VINE.Length - 2; i >= 1; i--) yield return VinePoint(a, i);
        }
        static List<Vector3> Seq(Vector3 p, Vector3 first, IEnumerable<Vector3> mid, Vector3 last, params Vector3[] tail)
        {
            var l = new List<Vector3> { p, first }; l.AddRange(mid); l.Add(last); l.AddRange(tail); return l;
        }

        public static bool IsOnTable(Vector3 p) => p.y >= TABLE.y - 0.01f && OnTable(p);

        // 糖桶旁的停靠点（从 from 方向靠近）
        static Vector3 JarApproach(Vector3 from, float radius)
        {
            var d = Vec.Norm(new Vector3(from.x - JAR.x, 0f, from.z - JAR.z));
            float r = JAR.outer + radius + 0.012f;   // 11-04：按桶肚实际外壳站位（原来按 0.11 算，偷糖前倾时脸穿进桶壁）
            return new Vector3(JAR.x + d.x * r, TABLE.y, JAR.z + d.z * r);
        }

        static float NearestLeg(Vector3 p)
        {
            float best = LEGS[0];
            float bd = float.PositiveInfinity;
            foreach (var a in LEGS)
            {
                float d = Vec.DistXZ(p, LegFloor(a));
                if (d < bd) { bd = d; best = a; }
            }
            return best;
        }

        // 路线绕障：地面上绕开玩家座位（不从玩家腿下穿过），桌面上绕开南瓜灯烛台和坩埚（不从烛台、锅身里穿过去）
        const float SEAT_R = 0.55f;
        static List<Vector3> AvoidSeat(List<Vector3> path)
        {
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = path[i]; var b = path[i + 1];
                bool ground = a.y < 0.05f && b.y < 0.05f;
                bool table = Mathf.Abs(a.y - TABLE.y) < 0.03f && Mathf.Abs(b.y - TABLE.y) < 0.03f;
                if (!ground && !table) continue;
                if (ground) { if (Detour(path, i, Vector2.zero, SEAT_R, 0f)) i++; continue; }
                if (CANDLE.on && Detour(path, i, new Vector2(CANDLE.x, CANDLE.z), 0.27f, TABLE.y)) { i++; continue; }
                if (Detour(path, i, new Vector2(BOX.x, BOX.z), BOX.half + 0.1f, TABLE.y)) i++;
            }
            return path;
        }

        // 线段 path[i]→path[i+1] 穿过圆（中心 c、半径 R）时，在它的外侧插一个绕行点；返回是否插入
        static bool Detour(List<Vector3> path, int i, Vector2 c, float R, float y)
        {
            var a = path[i]; var b = path[i + 1];
            var a2 = new Vector2(a.x, a.z) - c; var ab = new Vector2(b.x - a.x, b.z - a.z);
            float L2 = ab.sqrMagnitude; if (L2 < 1e-6f) return false;
            if (a2.magnitude < R || (new Vector2(b.x, b.z) - c).magnitude < R) return false;   // 端点本来就在圆里（比如目标就在旁边），不绕
            float t = Mathf.Clamp01(Vector2.Dot(-a2, ab) / L2);
            var closest = a2 + ab * t;
            if (closest.magnitude >= R || t <= 0f || t >= 1f) return false;
            var side = new Vector2(-ab.y, ab.x).normalized;
            if (Vector2.Dot(side, closest) < 0) side = -side;
            var d = c + side * (R + 0.08f);
            path.Insert(i + 1, new Vector3(d.x, y, d.y));
            return true;
        }

        // 通用寻路：桌面 ↔ 地面之间必须经过桌腿上的蛛网梯
        public static List<Vector3> PlanTo(Vector3 p, Vector3 target)
        {
            bool a = IsOnTable(p);
            bool b = IsOnTable(target);
            if (a == b) return AvoidSeat(new List<Vector3> { p, target });
            if (!a && b)
            {
                float leg = NearestLeg(p);
                return AvoidSeat(Seq(p, LegFloor(leg), VineMid(leg, true), LegTop(leg), target));
            }
            float leg2 = NearestLeg(target);
            return AvoidSeat(Seq(p, LegTop(leg2), VineMid(leg2, false), LegFloor(leg2), target));
        }

        public static List<Vector3> PlanToJar(Vector3 p, float radius = 0.065f)
        {
            if (IsOnTable(p)) return new List<Vector3> { p, JarApproach(p, radius) };
            float a = NearestLeg(p);
            var top = LegTop(a);
            return AvoidSeat(Seq(p, LegFloor(a), VineMid(a, true), top, JarApproach(top, radius)));
        }

        public static List<Vector3> PlanToSpawn(Vector3 p, SpawnPoint spawn)
        {
            var ground = new Vector3(spawn.pos.x, 0f, spawn.pos.z);
            if (IsOnTable(p)) return AvoidSeat(Seq(p, LegTop(spawn.legAngle), VineMid(spawn.legAngle, false), LegFloor(spawn.legAngle), ground));
            return AvoidSeat(new List<Vector3> { p, ground });
        }

        // 飞行路径：从当前点直飞到糖桶上方，再下降
        static List<Vector3> FlyToJar(Vector3 p, EnemyKind k)
        {
            var above = new Vector3(JAR.x + (p.x > 0 ? 0.12f : -0.12f), k.flyY, JAR.z - 0.05f);
            return OutFromUnder(p, new Vector3[] { above, new Vector3(above.x, TABLE.y + JAR.height + 0.05f, above.z) });   // 悬停在桶口上方，不卡在桶沿里
        }

        static List<Vector3> FlyAway(Vector3 p, SpawnPoint spawn)
        {
            var path = OutFromUnder(p, new Vector3[0]);
            var last = path[path.Count - 1];
            path.Add(new Vector3(last.x, 1.2f, last.z)); path.Add(spawn.pos);
            return path;
        }

        /// <summary>
        /// 在桌子底下（被扔到地上、落在桌板投影里）的东西要先水平钻出桌沿，再起飞 / 往上爬。
        /// 10-29 实机：蝙蝠、幽灵从桌下直线飞向糖桶，蜘蛛从桌下顺丝爬回椽子，都从桌板里穿了出来。
        /// 往远离玩家的方向钻（玩家坐在桌子 +Z 一侧）。
        /// </summary>
        static List<Vector3> OutFromUnder(Vector3 p, Vector3[] rest)
        {
            var path = new List<Vector3> { p };
            if (p.y < TABLE.y - 0.01f && Vec.DistXZ(p, TABLE.center) < TABLE.radius + 0.1f)
            {
                var d = new Vector3(p.x - TABLE.center.x, 0, p.z - TABLE.center.z);
                if (d.sqrMagnitude < 1e-4f) d = new Vector3(p.x >= 0 ? 1 : -1, 0, 0);
                d = Vec.Norm(d);
                if (d.z > 0.35f) d = Vec.Norm(new Vector3(d.x >= 0 ? 1 : -1, 0, 0.2f));   // 不朝玩家那边钻
                path.Add(new Vector3(TABLE.center.x + d.x * (TABLE.radius + 0.18f), p.y, TABLE.center.z + d.z * (TABLE.radius + 0.18f)));
            }
            path.AddRange(rest);
            return path;
        }

        // 蜘蛛：从天花板垂直降到桌面（糖桶旁），偷到糖再沿丝爬回去
        static List<Vector3> DropPath(Vector3 p) => new List<Vector3> { p, new Vector3(p.x, TABLE.y, p.z) };
        // 沿自己的丝爬回椽子上的挂点（丝从挂点斜拉到蜘蛛身上，爬回去就是顺着丝走）
        static List<Vector3> ClimbBack(Vector3 p, SpawnPoint spawn) => OutFromUnder(p, new[] { spawn.pos });

        /// <summary>
        /// 防穿模（10-31）：路线里穿过场景摆件的线段改成绕行。地面线段（两端都贴地）在地面占用图上 A* 绕；
        /// 空中线段（飞行、离开）在粗 3D 网格上绕；爬藤、桌面上、蜘蛛顺丝上下的线段按设计走，不处理。
        /// </summary>
        List<Vector3> Safe(List<Vector3> path, Toy t)
        {
            if (solid == null || path == null || path.Count < 2) return path;
            var K = t.Kind;
            if (K.mover == Movers.Drop)
            {
                // 蜘蛛顺丝上下：线段本身是竖直的丝，只把终点（椽子上的挂点）挪到椽子下面，不爬进木梁里
                path[path.Count - 1] = HangPoint(path[path.Count - 1], K);
                return path;
            }
            var up = Vector3.up * (K.height * 0.5f);
            float r = K.BodyR;
            bool big = K.BodyR > 0.15f;
            var outL = new List<Vector3> { path[0] };
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var a = outL[outL.Count - 1]; var b = path[i + 1];
                bool groundSeg = a.y < 0.05f && b.y < 0.05f;
                bool tableSeg = Mathf.Abs(a.y - TABLE.y) < 0.03f && Mathf.Abs(b.y - TABLE.y) < 0.03f && OnTable(a, -0.05f) && OnTable(b, -0.05f);
                bool climb = IsClimb(a, b) && !(K.mover == Movers.Fly);
                List<Vector3> det = null;
                if (groundSeg) det = solid.GroundDetour(a, b, big);
                else if (!tableSeg && !climb && K.mover == Movers.Fly)
                {
                    var d3 = solid.AirDetour(a + up, b + up, r, 3.0f);
                    if (d3 != null) { det = new List<Vector3>(); foreach (var q in d3) det.Add(q - up); }
                }
                if (det != null) outL.AddRange(det); else outL.Add(b);
            }
            return outL;
        }

        public const float SPIDER_REST_Y = 1.3f;

        /// <summary>蜘蛛的挂点：从椽子上的点往下挪，直到身体离开木梁（10-31：原来出生在木梁正中，一出来身体嵌在梁里）</summary>
        Vector3 HangPoint(Vector3 p, EnemyKind k)
        {
            if (solid == null) return p;
            var up = Vector3.up * (k.height * 0.5f);
            for (float dy = 0; dy <= 0.6f; dy += 0.01f)
            {
                var q = p - Vector3.up * dy;
                if (!solid.SolidSphere(q + up, k.BodyR + 0.01f)) return q;
            }
            return p - Vector3.up * 0.6f;
        }

        public static float PathLength(List<Vector3> path)
        {
            float l = 0f;
            for (int i = 1; i < path.Count; i++) l += Vector3.Distance(path[i - 1], path[i]);
            return l;
        }
    }
}
