// 规则层发给表现层的统一事件类型。对应 game.js 里每一个 emit(type, data)。
// 表现层（音效、特效、字幕、UI）只需要按 type 分派。
//
// 字段约定：
//   id      实体 id（敌人 / 糖 / 灯）。0 表示“没有”（实体 id 从 1 开始）
//   strId   字符串 id：技能 id（skill / skillCooling）、教学步骤 id（lessonStep / lessonOk / lessonHint）
//           —— JS 里这两类事件的 id 是字符串，C# 里拆到 strId，避免和实体 id 混成 object
//   其余字段与 JS 同名；JS 里的可选字段在 C# 里用默认值表示“未设置”，用 hasPos 区分 pos 是否有效
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public static class GameEventTypes
    {
        // 一晚的开始与结束
        public const string NightStart = "nightStart";   // night, name(text)
        public const string Dawn = "dawn";               // result
        public const string Fail = "fail";               // result
        public const string HopIn = "hopIn";             // id, pos —— 天亮后敌人跳回去
        public const string Say = "say";                 // textKey, text, dur, args

        // 生成
        public const string Spawn = "spawn";             // id, kind, pos, spawn
        public const string Introduce = "introduce";     // id, kind —— 新敌人首次登场
        public const string Wave = "wave";               // count
        public const string BossIn = "bossIn";
        public const string BossGone = "bossGone";       // id, pos, star

        // 注视 / 光
        public const string GazeOn = "gazeOn";           // id, kind
        public const string ThawWarn = "thawWarn";       // id, pos
        public const string Thaw = "thaw";               // id
        public const string PumpkinRun = "pumpkinRun";   // id, pos
        public const string Revealed = "revealed";       // id, pos —— 进入光里现形
        public const string LampEnd = "lampEnd";         // id, pos
        public const string LampGrab = "lampGrab";       // id, side, pos：捏住南瓜灯
        public const string LampPut = "lampPut";         // id, side, pos：南瓜灯放好

        // 手
        public const string Slip = "slip";               // id, pos
        public const string CloseCall = "closeCall";     // id, pos —— 残留快结束时抓到
        public const string Grab = "grab";               // id, side, by, pos, hasCandy
        public const string GrabCandy = "grabCandy";     // id, side, pos
        public const string FrankGrip = "frankGrip";     // id, side, both
        public const string Split = "split";             // id, kids, pos
        public const string PullStart = "pullStart";     // id, side, isCandy
        public const string PullBreak = "pullBreak";     // id, pos
        public const string Throw = "throw";             // id, pos, speed, boss, hp
        public const string Charged = "charged";         // id, pos
        public const string Launch = "launch";           // id, pos
        public const string Star = "star";               // id, kind, pos
        public const string BlownAway = "blownAway";     // id, kind, pos：被风吹下桌后消失
        public const string Wiggle = "wiggle";           // id, n
        public const string Shaken = "shaken";           // id, pos
        public const string Land = "land";               // id, pos, hard, heavy
        public const string WallHit = "wallHit";         // id, pos —— 被扔/被吹的小鬼撞上花园矮墙或栅栏

        // 糖
        public const string Steal = "steal";             // id, pos, left, kind
        public const string ReachJar = "reachJar";       // id
        public const string CandyDrop = "candyDrop";     // id, candy, pos, reason
        public const string CandyBack = "candyBack";     // id
        public const string CandyLost = "candyLost";     // id, pos
        public const string PickCandy = "pickCandy";     // id, candy, pos
        public const string CandyBounce = "candyBounce"; // id, pos
        public const string CandyLand = "candyLand";     // id, pos

        // 坩埚
        public const string Boxed = "boxed";             // id, pos, used, size
        public const string BoxFull = "boxFull";         // id, need, free, tooBig
        public const string Digested = "digested";       // id, kind, used
        public const string Pat = "pat";                 // count

        // 技能
        public const string Skill = "skill";             // strId, hit, lamp, pos
        public const string SkillCooling = "skillCooling"; // strId, left
        public const string ShieldEnd = "shieldEnd";
        public const string SkillReady = "skillReady";   // strId：技能冷却结束（只对本夜已解锁的技能）

        // 教学
        public const string LessonStep = "lessonStep";   // strId, index, total
        public const string LessonOk = "lessonOk";       // strId
        public const string LessonHint = "lessonHint";   // strId
        public const string LessonDone = "lessonDone";
        public const string PracticeDone = "practiceDone";   // 封面“玩法”练习做完
        public const string BucketLift = "bucketLift", BucketPut = "bucketPut", PotLift = "potLift", PotPut = "potPut";   // side, pos
        public const string BriefStep = "briefStep";     // strId, index, total：开夜介绍说到第几句
        public const string BriefDone = "briefDone";
        public const string PotAssist = "potAssist";     // 扔向坩埚被吸附修正（表现层可以给一道引导光）
        public const string HandHint = "handHint";       // kind(提示动画种类), id

        public static readonly string[] All =
        {
            NightStart, Dawn, Fail, HopIn, Say,
            Spawn, Introduce, Wave, BossIn, BossGone,
            GazeOn, ThawWarn, Thaw, PumpkinRun, Revealed, LampEnd, LampGrab, LampPut,
            Slip, CloseCall, Grab, GrabCandy, FrankGrip, Split, PullStart, PullBreak, Throw, Charged, Launch, Star, BlownAway, Wiggle, Shaken, Land, WallHit,
            Steal, ReachJar, CandyDrop, CandyBack, CandyLost, PickCandy, CandyBounce, CandyLand,
            Boxed, BoxFull, Digested, Pat,
            Skill, SkillCooling, ShieldEnd, SkillReady,
            LessonStep, LessonOk, LessonHint, LessonDone, PracticeDone, BucketLift, BucketPut, PotLift, PotPut, HandHint, BriefStep, BriefDone, PotAssist,
        };
    }

    public class GameEvent
    {
        public string type;

        public int id;              // 实体 id；0 = 无
        public string strId;        // 技能 / 教学步骤 id
        public string kind;         // 敌人种类；handHint 里是提示动画种类
        public bool hasPos;
        public Vector3 pos;
        public string side;         // left / right

        // say
        public string textKey;      // 本地化键
        public string text;         // 中文默认文本（已代入参数）
        public float dur;
        public Dictionary<string, string> args; // 本地化参数；值为 kind id 的参数（键名 kind）需表现层再查 kind.<id>.name

        // 计数 / 数值
        public int count;           // wave / pat
        public int night;           // nightStart：夜编号（1 起）
        public string name;         // nightStart：夜名（中文）
        public string spawn;        // spawn：出生点 id
        public string by;           // grab：gaze | residual | other | twoHand
        public bool hasCandy;       // grab
        public bool both;           // frankGrip：两只手都捏住了
        public int[] kids;          // split：分裂出的小幽灵 id
        public bool isCandy;        // pullStart：拉的是糖（JS 字段名 candy: true）
        public float speed;         // throw
        public bool boss;           // throw：扔的是缝合熊
        public int hp;              // throw：缝合熊剩余血量
        public bool wait;           // boxFull：放不下但坐到锅沿上排队了
        public bool star;           // bossGone：是不是变星星离场
        public int n;               // wiggle：当前换向次数
        public bool hard;           // land：晕着落地
        public bool heavy;          // land：缝合熊落地
        public float left;          // steal：糖桶剩余糖数；skillCooling：剩余冷却秒数
        public int candy;           // candyDrop / pickCandy：糖 id（JS 字段名 candy）
        public string reason;       // candyDrop：shake | sweep | launch | ko
        public int used;            // boxed / digested：坩埚已占格数
        public int size;            // boxed：这只占几格
        public int need;            // boxFull：需要几格
        public int free;            // boxFull：还剩几格
        public bool tooBig;         // boxFull：比坩埚总容量还大
        public int[] hit;           // skill(sweep)：被吹走的敌人 id
        public int lamp;            // skill(lamp)：灯 id
        public int index;           // lessonStep：第几步（0 起）
        public int total;           // lessonStep：总步数
        public NightResult result;  // dawn / fail

        public GameEvent(string type) { this.type = type; }

        public GameEvent At(Vector3 p) { pos = p; hasPos = true; return this; }

        public override string ToString() => $"{type}(id={id}{(strId != null ? ", " + strId : "")}{(text != null ? ", " + text : "")})";
    }
}
