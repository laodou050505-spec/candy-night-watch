// 敌人种类（万圣节主题），对应原型 src/logic/kinds.js 的 KINDS。每种敌人逼玩家换一种眼手用法。
//
// freeze 规则：
//   gaze    直视定身，移开后残留（基础规则）
//   inverse 反着来：被直视时会跑；在余光里（视野内但没被直视）才僵住，不残留
//   stare   要连续盯满 stareSec 才定住，残留上限更短
// needsLight：只有在光里（南瓜灯/掌心灯）才能被注视选中
// mover：
//   walk  地面 → 桌腿蛛网梯 → 桌面
//   fly   空中直线飞，被定住时缓缓飘落
//   drop  从天花板吊丝垂降到糖桶旁，偷到糖再爬回去；被定住时吊在丝上不动
// size：坩埚里占几格
//
// JS 里可选字段用 ?? 取默认值；这里用 0 表示“未设置”（stealSec、residualMax），由 Game 回退到 GameConfig。
using System;
using System.Collections.Generic;

namespace TrickOrFreeze.Core
{
    public static class Movers { public const string Walk = "walk", Fly = "fly", Drop = "drop"; }
    public static class FreezeRules { public const string Gaze = "gaze", Inverse = "inverse", Stare = "stare"; }

    public static class KindIds
    {
        public const string Skeleton = "skeleton", Bat = "bat", Spider = "spider", Pumpkin = "pumpkin",
            Cat = "cat", Ghost = "ghost", Ghostling = "ghostling", Frank = "frank";
    }

    [Serializable]
    public class EnemyKind
    {
        public string id;
        public string label;             // 中文名（原型里的 label）
        public string nameKey;           // 本地化键：kind.<id>.name
        public float height;
        public float radius;
        /// <summary>防穿模用的身体半径：按模型实际量出来的水平半宽（10-31：熊的胳膊、蝙蝠的翅膀比规则半径大，用规则半径会蹭进摆件）</summary>
        public float bodyR;
        public float BodyR => bodyR > 0 ? bodyR : radius;
        public float speed = 1f;
        public int size = 1;             // 坩埚格数
        public string mover = Movers.Walk;
        public string freeze = FreezeRules.Gaze;
        public int steal = 1;            // 一次偷几颗才走
        public float flyY;               // fly：巡航高度
        public float glide;              // fly：被定住时的飘落速度
        public float dropY;              // drop：垂降起点高度
        public float gazedSpeed;         // inverse：被直视时的速度倍率
        public double stealSec;          // 0 = 用 GameConfig.candy.stealSec
        public bool needsLight;
        public int splits;               // 被抓时分裂出几只
        public string splitInto;
        public double stareSec;          // stare：要盯多久才定住
        public double residualMax;       // 0 = 用 GameConfig.freeze.residualMax
        public bool twoHand;             // 双手才能举起
        public int hp = 1;               // 扔几次离场
        public float sweepMul = 1f;      // 被狂风推开的比例
        public string intro;             // 首次出场字幕（中文默认文本），空 = 无
        public string introKey;          // 本地化键：kind.<id>.intro

        public bool HasIntro => !string.IsNullOrEmpty(intro);
    }

    public static class EnemyKinds
    {
        // 10-31 从各敌人预制体量出来的水平半宽（米），见 EnemyView 模型；留 0.5 厘米余量
        static readonly Dictionary<string, float> BODY_R = new Dictionary<string, float>
        {
            { KindIds.Skeleton, 0.072f }, { KindIds.Bat, 0.107f }, { KindIds.Spider, 0.083f }, { KindIds.Pumpkin, 0.103f },
            { KindIds.Cat, 0.072f }, { KindIds.Ghost, 0.102f }, { KindIds.Ghostling, 0.056f }, { KindIds.Frank, 0.26f },
        };   // 必须在 All 之前初始化（静态字段按书写顺序初始化）

        public static readonly Dictionary<string, EnemyKind> All = Build();

        static EnemyKind K(string id, string label, Action<EnemyKind> init)
        {
            var k = new EnemyKind { id = id, label = label, nameKey = $"kind.{id}.name" };
            init(k);
            if (BODY_R.TryGetValue(id, out var br)) k.bodyR = br;
            if (k.HasIntro) k.introKey = $"kind.{id}.intro";
            return k;
        }

        static Dictionary<string, EnemyKind> Build()
        {
            var list = new[]
            {
                K(KindIds.Skeleton, "小骷髅", k => { k.height = 0.2f; k.radius = 0.065f; k.speed = 1.0f; k.size = 1; k.mover = Movers.Walk; k.freeze = FreezeRules.Gaze; k.steal = 1; }),
                K(KindIds.Bat, "蝙蝠", k => { k.height = 0.1f; k.radius = 0.09f; k.speed = 1.1f; k.size = 1; k.mover = Movers.Fly; k.freeze = FreezeRules.Gaze; k.steal = 1; k.flyY = 1.08f; k.glide = 0.28f;
                    k.intro = "蝙蝠会飞！盯住它，它会慢慢飘落，趁机隔空捏过来。"; }),
                K(KindIds.Spider, "吊丝蜘蛛", k => { k.height = 0.09f; k.radius = 0.06f; k.speed = 0.9f; k.size = 1; k.mover = Movers.Drop; k.freeze = FreezeRules.Gaze; k.steal = 1; k.dropY = 2.3f;
                    k.intro = "蜘蛛从天花板垂下来了！抬头看住它，捏住就能扯断丝。"; }),
                K(KindIds.Pumpkin, "南瓜头小丑", k => { k.height = 0.22f; k.radius = 0.07f; k.speed = 0.9f; k.gazedSpeed = 1.5f; k.size = 1; k.mover = Movers.Walk; k.freeze = FreezeRules.Inverse; k.steal = 1;
                    k.intro = "南瓜头反着来：盯着它看它会跑！眼睛看旁边、用余光照着它，伸手去抓——手凑近了它就不敢动。"; }),
                K(KindIds.Cat, "黑猫", k => { k.height = 0.13f; k.radius = 0.07f; k.speed = 1.2f; k.stealSec = 0.5; k.size = 1; k.mover = Movers.Walk; k.freeze = FreezeRules.Gaze; k.needsLight = true; k.steal = 1;
                    k.intro = "黑猫只在光里现形。五指张开、手背朝自己，点一盏南瓜灯。"; }),
                K(KindIds.Ghost, "大幽灵", k => { k.height = 0.2f; k.radius = 0.08f; k.speed = 0.8f; k.size = 2; k.mover = Movers.Walk; k.freeze = FreezeRules.Gaze; k.steal = 1; k.splits = 2; k.splitInto = KindIds.Ghostling;
                    k.intro = "大幽灵一抓就分成三只小幽灵！"; }),
                K(KindIds.Ghostling, "小幽灵", k => { k.height = 0.1f; k.radius = 0.045f; k.speed = 1.3f; k.size = 1; k.mover = Movers.Walk; k.freeze = FreezeRules.Gaze; k.steal = 1; }),
                K(KindIds.Frank, "缝合熊", k => { k.height = 0.42f; k.radius = 0.14f; k.speed = 0.75f; k.stealSec = 1.4; k.mover = Movers.Walk; k.freeze = FreezeRules.Stare; k.stareSec = 0.8; k.residualMax = 1.6;
                    k.twoHand = true; k.steal = 3; k.hp = 2; k.sweepMul = 0.35f; k.size = 4; // 比坩埚还大：只能扔
                    k.intro = "缝合熊来了！要盯久一点才能定住，再用两只手一起捏住扔出去。"; }),
            };
            var d = new Dictionary<string, EnemyKind>();
            foreach (var k in list) d[k.id] = k;
            return d;
        }

        // JS: KINDS[kind]（找不到时 JS 会直接报错；这里退回小骷髅）
        public static EnemyKind Get(string id) => id != null && All.TryGetValue(id, out var k) ? k : All[KindIds.Skeleton];
    }
}
