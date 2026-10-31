// 关卡：12 夜，分 3 章（对应原型 config.js 的 CHAPTERS 与 NIGHTS）。
// 新手教学（lesson）只有第一夜有，而且只在玩家第一次玩时出现：一步一步做对了才往下走，教学期间不计时。
// 之后各夜新加的本事（晃、甩、拉、合十……）不再做互动教学，只在开夜时用 brief 依次说一句、配示范手演示。
//
// lesson 步骤：{ id, say, spawn? }，完成条件由 Game.LessonDone 实现；brief 步骤只用 { id, say }
// mix = 本夜敌人配比（保持声明顺序，抽签时按顺序累减，和 JS 的 Object.entries 一致）
// feature = 本夜新登场的敌人（教学结束后第一只单独出场）
//
// 所有中文文本同时带本地化键：night.<id>.name / night.<id>.intro / night.<id>.tip.<i> / lesson.<夜id>.<步骤id> / chapter.<id>.name
using System;
using System.Collections.Generic;

namespace TrickOrFreeze.Core
{
    [Serializable]
    public class ChapterDef
    {
        public int id;
        public string name;
        public string nameKey;
        public int[] nights;
    }

    [Serializable]
    public class LessonSpawn
    {
        public string kind = KindIds.Skeleton;
        public float? rim;        // 桌沿角度；null = 按数量在 -40°~40° 之间均匀排开
        public float? slow;       // 教学敌人的速度倍率；null = 0.6
        public bool withCandy;    // 出生时就抱着一颗糖并往回跑
        public int count = 1;
        public bool far;          // 放在桌沿更远处（练隔空取物）
        public bool nearJar;      // 放在糖桶两侧
    }

    [Serializable]
    public class LessonStep
    {
        public string id;
        public string say;        // 中文默认文本
        public string sayKey;     // 本地化键
        public LessonSpawn spawn; // 可为 null

        // JS 在 lessonStep() 里给步骤对象挂 needToy；这里按 id 静态算出，语义相同
        public bool NeedToy => Array.IndexOf(NeedToyIds, id) >= 0;
        static readonly string[] NeedToyIds = { "look", "residual", "grab", "cauldron", "shake", "throw", "pull", "launch" };   // pat/bucket/pot/技能 不需要教学敌人
    }

    [Serializable]
    public class BurstDef { public double every; public int count; }

    [Serializable]
    public class BossDef { public double at; }

    [Serializable]
    public class TipDef { public double t; public string text; public string textKey; public float dur = 4f; }

    [Serializable]
    public class MixEntry
    {
        public string kind;
        public float weight;
        public MixEntry(string kind, float weight) { this.kind = kind; this.weight = weight; }
    }

    [Serializable]
    public class NightDef
    {
        public int id;
        public string name;
        public string nameKey;
        public double duration;
        public int maxActive;
        public double[] spawnEvery;     // [lo, hi]
        public float speedMul = 1f;
        public string[] skills = new string[0];
        public List<MixEntry> mix = new List<MixEntry>();
        public string feature;          // null = 无
        public BurstDef burst;          // null = 无
        public BossDef boss;            // null = 无
        public bool dark;
        public string intro;            // null = 无
        public string introKey;
        public List<TipDef> tips = new List<TipDef>();
        public List<LessonStep> lesson; // null = 本夜没有新手教学（只有第一夜有）
        public List<LessonStep> brief;  // 开夜介绍：本夜新本事的一句话说明，按顺序念完就开始（不等玩家做）

        public bool HasLesson => lesson != null && lesson.Count > 0;
        public bool HasBrief => brief != null && brief.Count > 0;
    }

    public static class NightDefs
    {
        public static readonly ChapterDef[] CHAPTERS =
        {
            new ChapterDef { id = 1, name = "第一章 · 南瓜灯亮起", nameKey = "chapter.1.name", nights = new[] { 1, 2, 3, 4 } },
            new ChapterDef { id = 2, name = "第二章 · 午夜狂欢", nameKey = "chapter.2.name", nights = new[] { 5, 6, 7, 8 } },
            new ChapterDef { id = 3, name = "第三章 · 不给糖就捣蛋", nameKey = "chapter.3.name", nights = new[] { 9, 10, 11, 12 } },
        };

        public static readonly NightDef[] NIGHTS = Build();

        /// <summary>
        /// 封面“玩法”练习（10-29 用户：玩法介绍要让玩家动手学、少大段文字）：一只慢吞吞的小骷髅，
        /// 跟着示范手一步一步做（看住 → 捏住 → 丢进锅 → 攥着发金光再甩成星星），每步只有一句很短的话，
        /// 不计时、不会输、不偷糖，做完回封面。
        /// </summary>
        public static readonly NightDef PRACTICE = BuildPractice(1);

        /// <summary>
        /// 封面“玩法”练习。10-31 审查：原来只有“看、捏、丢锅、甩星星”四步，拍锅沿、提桶、挪锅、残留定身、晃糖、隔空取、手势技能都没教。
        /// 现在按玩家已经打到的夜数，把那之前学过的本事都练一遍（unlockedNight = 已解锁的最高夜编号，1 起）
        /// </summary>
        public static NightDef BuildPractice(int unlockedNight)
        {
            var n = N(0, "玩法练习", 9999, 0, 9999, 9999, 0.6f);
            n.nameKey = "practice.name";
            n.mix = Mix(KindIds.Skeleton, 1);
            LessonStep P(string id, string say, LessonSpawn sp = null) => new LessonStep { id = id, say = say, sayKey = "practice." + id, spawn = sp };
            var l = new List<LessonStep>
            {
                P("look", "盯着它，它就不敢动", new LessonSpawn { kind = KindIds.Skeleton, rim = 0, slow = 0.35f }),
                P("residual", "移开目光，它还会僵一会儿：盯得越久，僵得越久"),
                P("grab", "趁它僵住，捏住它"),
                P("cauldron", "丢进坩埚"),
                P("pat", "手掌在坩埚上方往下拍一拍，它消化得更快"),
                P("bucket", "捏住糖桶的提手，可以把糖桶提起来"),
                P("pot", "捏住坩埚的锅耳，可以把坩埚挪到顺手的地方"),
            };
            if (unlockedNight >= 2)
            {
                l.Add(P("shake", "它抱着糖！定住它，捏起来左右晃一晃", new LessonSpawn { kind = KindIds.Skeleton, rim = -20, slow = 0.35f, withCandy = true }));
                l.Add(P("candy", "把掉出来的糖捡回糖桶"));
            }
            if (unlockedNight >= 3)
            {
                l.Add(P("pull", "远处那只：盯住它，把手伸到身前捏一下，它会飞过来", new LessonSpawn { kind = KindIds.Skeleton, rim = 35, slow = 0.3f, far = true }));
                l.Add(P("throw", "捏着它用力一甩，把它丢远"));
            }
            if (unlockedNight >= 4)
                l.Add(P("launch", "再来一只：捏住别动，等它发金光，一甩！", new LessonSpawn { kind = KindIds.Skeleton, rim = 25, slow = 0.35f }));
            var sk = new List<string>();
            if (unlockedNight >= 5) { sk.Add(SkillIds.Pray); l.Add(P("pray", "双手合十：全场的小鬼都会僵住")); }
            if (unlockedNight >= 7) { sk.Add(SkillIds.Sweep); l.Add(P("sweep", "双手放在胸前，再快速向两边挥开")); }
            if (unlockedNight >= 9) { sk.Add(SkillIds.Shield); l.Add(P("shield", "双手掌心朝下，在糖桶上方一起往下按")); }
            if (unlockedNight >= 10) { sk.Add(SkillIds.Lamp); l.Add(P("lamp", "五指张开、手背朝自己，举着别动，点亮南瓜灯")); }
            n.skills = sk.ToArray();
            n.lesson = l;
            return n;
        }

        static List<MixEntry> Mix(params object[] kv)
        {
            var l = new List<MixEntry>();
            for (int i = 0; i < kv.Length; i += 2) l.Add(new MixEntry((string)kv[i], Convert.ToSingle(kv[i + 1])));
            return l;
        }

        static LessonStep Step(int night, string id, string say, LessonSpawn spawn = null) =>
            new LessonStep { id = id, say = say, sayKey = $"lesson.{night}.{id}", spawn = spawn };

        static LessonStep Brief(int night, string id, string say) =>
            new LessonStep { id = id, say = say, sayKey = $"brief.{night}.{id}" };

        static NightDef N(int id, string name, double duration, int maxActive, double lo, double hi, float speedMul)
        {
            return new NightDef { id = id, name = name, nameKey = $"night.{id}.name", duration = duration, maxActive = maxActive, spawnEvery = new[] { lo, hi }, speedMul = speedMul };
        }

        static NightDef[] Build()
        {
            const string S = KindIds.Skeleton;
            var nights = new List<NightDef>();
            NightDef n;

            // ---------------- 第一章：学会眼和手 ----------------
            n = N(1, "第一夜 · 守夜人", 80, 3, 5, 8, 0.85f);   // 10-31：重玩时 75 秒、6~9 秒一只太闲
            n.mix = Mix(S, 1);
            n.lesson = new List<LessonStep>
            {
                Step(1, "look", "万圣夜，小骷髅们想偷你的糖。看着它——被你盯着，它就不敢动。", new LessonSpawn { kind = S, rim = 0, slow = 0.4f }),
                Step(1, "residual", "移开视线，它还会僵一会儿；脚下的光圈转完它才会动。"),
                Step(1, "grab", "趁它僵住，用拇指和食指捏住它。动着的抓不住哦。"),
                Step(1, "cauldron", "把它丢进右手边的坩埚里，坩埚会慢慢把它消化掉。"),
            };
            nights.Add(n);

            n = N(2, "第二夜 · 偷糖贼", 90, 4, 5, 7.5, 0.9f);
            n.mix = Mix(S, 1);
            n.brief = new List<LessonStep>
            {
                Brief(2, "shake", "新招：小鬼偷到糖会抱着跑。先定住它，捏起来左右用力晃一晃，糖就掉出来了。"),
                Brief(2, "candy", "掉出来的糖捡起来，放回南瓜糖桶。被晃晕的小鬼可以随便抓。"),
            };
            nights.Add(n);

            n = N(3, "第三夜 · 丢远一点", 100, 5, 4.5, 7, 1.0f);
            n.mix = Mix(S, 1);
            n.brief = new List<LessonStep>
            {
                Brief(3, "throw", "坩埚满了也不用干等：捏住用力一甩，把它丢远。"),
                Brief(3, "pull", "远处的够不着？盯着它，再捏一下手指，它会飞到你手里。"),
            };
            nights.Add(n);

            n = N(4, "第四夜 · 变成星星", 110, 5, 4, 6.2, 1.05f);
            // 10-31 审查：前四夜只有小骷髅（平台期），第五夜又一下子加三样——蝙蝠提前到这一夜少量登场（坩埚 +1 格也挪到这一夜）
            n.mix = Mix(S, 4, KindIds.Bat, 1);
            n.feature = KindIds.Bat;
            n.brief = new List<LessonStep>
            {
                Brief(4, "launch", "新招：捏住它别松手，等它发光，再用力甩出去——它会飞出花园的铁拱门，变成天上的星星，再也不回来。"),
            };
            nights.Add(n);

            // ---------------- 第二章：手势与新敌人 ----------------
            n = N(5, "第五夜 · 蝙蝠夜", 120, 6, 3.4, 5.2, 1.1f);
            n.skills = new[] { SkillIds.Pray };
            n.mix = Mix(S, 3, KindIds.Bat, 1);
            n.brief = new List<LessonStep>
            {
                Brief(5, "pray", "新技能：双手合十，全场都会僵住，之后还会变慢。来得太多时就用它。"),
            };
            nights.Add(n);

            n = N(6, "第六夜 · 幽灵分身", 125, 6, 3.2, 4.8, 1.12f);
            n.skills = new[] { SkillIds.Pray };
            n.mix = Mix(S, 3, KindIds.Bat, 1, KindIds.Ghost, 1);
            n.feature = KindIds.Ghost;
            n.brief = new List<LessonStep>
            {
                Brief(6, "ghost", "大幽灵一抓就分成三只小幽灵，各占坩埚一格。坩埚快满时，先合十定住它们再一只只收拾。"),
            };
            nights.Add(n);

            n = N(7, "第七夜 · 一拥而上", 135, 7, 2.8, 4.2, 1.15f);
            n.burst = new BurstDef { every = 32, count = 3 };
            n.skills = new[] { SkillIds.Pray, SkillIds.Sweep };
            n.mix = Mix(S, 3, KindIds.Bat, 1, KindIds.Ghost, 1);
            n.brief = new List<LessonStep>
            {
                Brief(7, "sweep", "新技能：双手放在胸前，再快速向两边挥开，一阵风把身边的小鬼全吹飞。"),
            };
            nights.Add(n);

            n = N(8, "第八夜 · 蛛网天花板", 135, 7, 2.7, 4.0, 1.15f);
            n.burst = new BurstDef { every = 34, count = 3 };   // 10-31：原来和第七夜参数一样却没有波次，反而更容易
            n.skills = new[] { SkillIds.Pray, SkillIds.Sweep };
            n.mix = Mix(S, 3, KindIds.Spider, 2, KindIds.Bat, 1, KindIds.Ghost, 1);
            n.feature = KindIds.Spider;
            n.brief = new List<LessonStep>
            {
                Brief(8, "spider", "蜘蛛会从藤架上垂下来。抬眼看住它，再伸手隔空把它扯下来。"),
            };
            nights.Add(n);

            // ---------------- 第三章：用余光、点灯、打首领 ----------------
            n = N(9, "第九夜 · 南瓜头", 140, 7, 2.8, 4.2, 1.15f);
            n.burst = new BurstDef { every = 35, count = 3 };
            n.skills = new[] { SkillIds.Pray, SkillIds.Sweep, SkillIds.Shield };
            n.mix = Mix(S, 3, KindIds.Pumpkin, 2, KindIds.Bat, 1, KindIds.Spider, 1);
            n.feature = KindIds.Pumpkin;
            n.brief = new List<LessonStep>
            {
                Brief(9, "shield", "新技能：双手掌心朝下，在糖桶上方一起往下按，月光会罩住糖桶。"),
            };
            nights.Add(n);

            // 10-31 审查：第十夜原来黑夜、黑猫、南瓜灯三样同时来，是个断崖。黑猫挪到第十一夜；黑夜本身更难，节奏放缓一点
            n = N(10, "第十夜 · 熄灯", 145, 6, 3.0, 4.4, 1.08f);
            n.dark = true;
            n.skills = new[] { SkillIds.Pray, SkillIds.Sweep, SkillIds.Shield, SkillIds.Lamp };
            n.mix = Mix(S, 3, KindIds.Pumpkin, 1, KindIds.Bat, 1);
            n.brief = new List<LessonStep>
            {
                Brief(10, "lamp", "今晚停电，黑暗里看不见、也定不住小鬼。新技能：五指张开、手背朝自己，点亮南瓜灯。"),
            };
            nights.Add(n);

            n = N(11, "第十一夜 · 百鬼夜行", 150, 8, 2.5, 3.7, 1.16f);
            n.burst = new BurstDef { every = 32, count = 4 };
            n.skills = new[] { SkillIds.Pray, SkillIds.Sweep, SkillIds.Shield, SkillIds.Lamp };
            n.mix = Mix(S, 3, KindIds.Bat, 1, KindIds.Ghost, 1, KindIds.Spider, 1, KindIds.Pumpkin, 1, KindIds.Cat, 2);
            n.feature = KindIds.Cat;
            n.brief = new List<LessonStep>
            {
                Brief(11, "cat", "今晚黑猫也来了，它只在光里现形。把南瓜灯挪到糖桶边上，再把合十、挥风、护桶轮流用起来。"),
            };
            nights.Add(n);

            // 10-31 审查：最后一夜原来比第十一夜还松、首领第 20 秒就出场，收尾虎头蛇尾。强度不低于第十一夜，首领在 55% 进度压轴登场
            n = N(12, "第十二夜 · 缝合熊", 160, 8, 2.4, 3.6, 1.18f);
            n.burst = new BurstDef { every = 36, count = 4 };
            n.skills = new[] { SkillIds.Pray, SkillIds.Sweep, SkillIds.Shield, SkillIds.Lamp };
            n.mix = Mix(S, 3, KindIds.Bat, 1, KindIds.Pumpkin, 1, KindIds.Ghost, 1, KindIds.Cat, 1, KindIds.Spider, 1);
            n.boss = new BossDef { at = 88 };
            n.intro = "最后一夜。大家都来了……还有个大家伙。";
            n.introKey = "night.12.intro";
            nights.Add(n);

            // tips 字段原型里没有任何一夜用到，结构保留；键名约定 night.<id>.tip.<序号>
            foreach (var nd in nights)
                for (int i = 0; i < nd.tips.Count; i++)
                    if (string.IsNullOrEmpty(nd.tips[i].textKey)) nd.tips[i].textKey = $"night.{nd.id}.tip.{i}";

            return nights.ToArray();
        }
    }
}
