// 规则层里的运行时实体（JS 里都是普通对象字面量，这里写成类）。
// 字段全部 public，方便表现层只读和测试直接摆状态，与原型测试的用法一致。
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public static class ToyStates
    {
        public const string Walk = "walk", Steal = "steal", Flee = "flee", Stunned = "stunned", Falling = "falling",
            Pulled = "pulled", Glide = "glide", Held = "held", Boxed = "boxed", Launched = "launched", Gone = "gone", Yawn = "yawn";
    }

    public static class CandyStates
    {
        public const string Falling = "falling", Rest = "rest", Held = "held", Pulled = "pulled", Gone = "gone";
    }

    public static class Zones { public const string Direct = "direct", Peripheral = "peripheral", Outside = "outside"; }

    public static class Phases { public const string Title = "title", Playing = "playing", Result = "result"; }

    // 出生点
    public class SpawnPoint
    {
        public string id;
        public string label;
        public Vector3 pos;
        public float legAngle;  // 离它最近的桌腿角度
        public SpawnPoint(string id, string label, Vector3 pos, float legAngle) { this.id = id; this.label = label; this.pos = pos; this.legAngle = legAngle; }
    }

    // 隔空取物进行中
    public class PullState
    {
        public Vector3 from;
        public Vector3 ctrl;      // 飞来路线的控制点：桌面以下出发时先绕到桌沿外、升到桌面以上再飞过来（不穿桌子）
        public double t;
        public string side;
        public double lost;
    }

    // 被拿着时的晃动检测
    public class ShakeState
    {
        public Vector3? lastDir;
        public List<double> flips = new List<double>();
        public double wiggle;
    }

    // 缝合熊：两只手各自捏住的相对偏移；null = 这只手没捏
    public class Grips
    {
        public Vector3? left, right;
        public Vector3? Get(string side) => side == Sides.Left ? left : right;
        public void Set(string side, Vector3? v) { if (side == Sides.Left) left = v; else right = v; }
        public bool Both => left.HasValue && right.HasValue;
        public bool None => !left.HasValue && !right.HasValue;
    }

    // 蓄力甩出后的飞行
    public class LaunchState
    {
        public double t;
        public Vector3 from;
        public Vector3 dir;
        public Vector3 to;
        public bool blow;         // 被外挥吹走：飞向远处、越飞越小消失（不是变星星）
        public Vector3 w1, w2;    // 吹走的折线路点：w1 = 从桌下钻出桌沿（不在桌下时 = from），w2 = 竖直升到桌面以上
        public float endK = 1f;   // 10-31 防穿模：飞到这里就结束（之后会撞上场景摆件）；表现层按它算缩小
    }

    public class Goal
    {
        public const string Jar = "jar", CandyGoal = "candy";
        public string type = Jar;
        public Candy candy;
        public static Goal ToJar() => new Goal { type = Jar };
    }

    // 敌人和糖共有的物理部分（clampRoom、拿着、隔空取物都用得到）
    public abstract class Body
    {
        public int id;
        public string state;
        public Vector3 pos;
        public Vector3 vel;
        public PullState pull;
    }

    public class Candy : Body
    {
        public int claimedBy; // 0 = 没人认领
    }

    public class Toy : Body
    {
        public string kind;
        public SpawnPoint spawn;
        public float yaw;
        public List<Vector3> path;
        public int seg;
        public Goal goal = Goal.ToJar();
        public bool hasCandy;
        public int candyCount;
        public float speedMul = 1f;
        public double residual;
        public double gazeAccum;
        public double lastDirect = double.NegativeInfinity;
        public bool gazed;
        public bool scared;     // 南瓜头：被盯久了受惊逃跑（手伸到跟前时吓得不敢动）
        public string zone = Zones.Outside;
        public bool frozen;
        public bool warned;
        public double slipCd;
        public double lostAt = -10;   // 拿着它的手追踪丢失、它掉下去的时刻（这之后一会儿不许被隔空取走，免得一抬另一只手它就飞过去）
        public double stun;
        public double dizzy;
        public double stealT;
        public double replanT;
        public float walkPhase;
        public ShakeState shake;
        public bool lit = true;
        public int hp = 1;
        public int splitsLeft;
        public Grips grips;     // 缝合熊
        public double chargeT;  // 被捏住不晃的累计时间（蓄力）
        public bool charged;
        public bool rimWait;      // 10-31：坩埚满了，坐在锅沿上排队（状态仍是 Boxed，不占格）
        public int rimSlot;
        public bool lesson;     // 教学用敌人
        public bool noSteal;    // 教学期间不偷糖
        public bool knockout;   // 缝合熊被扔够次数：晕完就离场
        public bool blown;      // 被狂风吹：越过桌面直接落地
        public bool potAim;           // 被“扔锅吸附”修正过轨迹：飞行途中不被锅壁弹开
        public bool dragged;    // 缝合熊被单手拖着
        public bool respawnDrop;// 蜘蛛：爬回天花板后换点重新垂降
        public float stareK;    // 缝合熊盯住的蓄力进度 0~1
        public LaunchState launch;
        public double yawnT;
        public double? goneAt;

        public EnemyKind Kind => EnemyKinds.Get(kind);
    }

    public class Lamp
    {
        public int id;
        public Vector3 pos;
        public Vector3 from;      // 从哪儿飞出来的（掌心前），表现层飞出动画用
        public string heldBy;     // 捏着它挪位置的手（null = 没被拿着）
        public float radius;
        public double t;
        public float dur;
    }

    public class HeldItem
    {
        public bool isToy;
        public Body obj;
        public Toy Toy => obj as Toy;
        public Candy Candy => obj as Candy;
        public static HeldItem OfToy(Toy t) => new HeldItem { isToy = true, obj = t };
        public static HeldItem OfCandy(Candy c) => new HeldItem { isToy = false, obj = c };
    }

    public class SkillState { public double cd; }

    public class CauldronState
    {
        public List<Toy> queue = new List<Toy>(); // 按放入顺序消化
        public List<Toy> rim = new List<Toy>();   // 锅沿上排队等空格的（最多 RIM_MAX 只）
        public double fullSayT = -99;              // 上次说“坩埚满了”的时间（15 秒内不重复说）
        public int used;                           // 已占格数
        public double progress;                    // 当前这一只的消化进度（秒）
        public double stirT;
        public double stirCd;
    }

    public class GameStats
    {
        public int boxed, digested, stars, close, recovered, pulls, thrown, shaken, skills, split, bossDown, lamps, blownAway, lost, boxFull, pats, bucketLifts, potMoves;
        public Dictionary<string, int> skillUse = new Dictionary<string, int>();

        public int SkillUse(string id) => skillUse.TryGetValue(id, out var n) ? n : 0;

        public GameStats Clone()
        {
            var s = (GameStats)MemberwiseClone();
            s.skillUse = new Dictionary<string, int>(skillUse);
            return s;
        }
    }

    public class NightResult
    {
        public bool win;
        public int candies;
        public int stars;
        public GameStats stats;
        public int next;      // 下一夜的索引（0 起）
        public bool allDone;
        public int lost;                  // 本夜被偷走的糖
        public bool dawnFail;             // 撑到了天亮，但剩糖少于 3 颗（不是被偷光）
        public List<ChallengeResult> challenges = new List<ChallengeResult>();   // 本夜挑战（完成首次给糖果币，由表现层/存档发放）
        public string tipKey, tip;        // 没守住时给的一条小技巧（本地化键 + 中文兜底）
    }

    /// <summary>每夜挑战：id 全局唯一（存档用 night+id），text 为中文兜底，key 为本地化键，args 代入 {n}</summary>
    public class ChallengeResult
    {
        public string id, key, text;
        public Dictionary<string, string> args = new Dictionary<string, string>();
        public bool done;
        public int coins;
    }

    public class PendingSay
    {
        public double t;
        public string key;
        public string text;
        public float dur;
    }

    public class DirectorState
    {
        // 倒计时用 double：与 JS 一样按 dt 逐帧递减，float 误差会让出怪晚一帧
        public double t;
        public List<double> recentLoss = new List<double>();
        public double burstT;
        public double bossAt;
        public HashSet<string> introduced = new HashSet<string>();
        public List<(double t, string kind)> returns = new List<(double t, string kind)>();   // 10-31：被风吹走的小鬼过一阵从远处回来
        public bool featured;
    }

    public class LessonFlag
    {
        public double? last;
        public double seen;
    }

    public class LessonState
    {
        public List<LessonStep> steps;
        public int step = -1;
        public double t;
        public double sayAt;
        public bool done;
        public List<Toy> toys = new List<Toy>();
        public GameStats @base;
        public LessonFlag flag = new LessonFlag();

        public LessonStep Current => step >= 0 && step < steps.Count ? steps[step] : null;
    }

    // 开夜介绍：每句 BRIEF_LINE 秒，按顺序说完就开始
    public class BriefState
    {
        public List<LessonStep> steps;
        public int step = -1;
        public double t;
        public bool done;
        public LessonStep Current => !done && step >= 0 && step < steps.Count ? steps[step] : null;
    }

    // spawnToy 的可选参数
    public class SpawnOpts
    {
        public string kind;
        public float? atRim;
        public Vector3? pos;
        public float? speedMul;
        public bool lesson;
        public bool silent;
    }

    // 构造参数（JS: new Game({ config, rand, residual, requireFreeze })）
    public class GameOptions
    {
        public GameConfig config;
        public System.Func<float> rand;
        public bool residual = true;
        public bool? requireFreeze;
    }
}
