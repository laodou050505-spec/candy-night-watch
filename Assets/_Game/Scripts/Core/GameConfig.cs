// 全部可调参数集中在这里（对应原型 src/logic/config.js 的 CONFIG）。
// 距离单位：米；角度：度；时间：秒。
// 时间类参数（秒、倍率作用于计时器的）用 double：与 JS 的 double 阈值逐位一致，避免 0.8f 之类在边界上多出一帧。
// 字段全部 [Serializable]，后续可以直接包进 ScriptableObject 在 Inspector 里调。
using System;

namespace TrickOrFreeze.Core
{
    [Serializable]
    public class GazeConfig
    {
        public float directConeDeg = 7f;      // 直视锥半角（真实眼动）。10-29 实机：5° 太严，PICO 眼动未单独校准时偏差 2~4°，常常“看着却没算看到”
        public float headRayConeDeg = 8f;     // 用头部射线代替眼动时放宽
        public float snapDeg = 4f;            // 目标外沿再放宽这么多度时吸附到最近敌人
        public double graceSec = 0.22;        // 注视短暂离开不算移开（过滤扫视/抖动）。10-31：0.12 比一次眨眼还短
        public float peripheralHalfDeg = 50f; // 头部视野半角，超出即“视野外”
    }

    [Serializable]
    public class FreezeConfig
    {
        public double residualPerGazeSec = 1.0; // 残留时间 = 注视时长 × 该系数
        public double residualMax = 2.5;
        public double warnSec = 0.4;            // 解冻前抖动 + 咔哒提示
    }

    [Serializable]
    public class SpeedConfig
    {
        public float peripheral = 0.55f; // 相对视野外速度的倍率（玩家大多盯着桌面，桌上的小鬼多在余光里：太低会显得慢吞吞）
        public float outside = 1.0f;
        public float @base = 0.4f;      // 基础移动速度 m/s（视野外）
    }

    [Serializable]
    public class GrabConfig
    {
        public bool requireFreeze = true; // 动着的敌人抓不住（眼先定住、手再抓）。被晃晕/摔晕的可以直接抓
    }

    [Serializable]
    public class HandConfig
    {
        public float pinchOn = 0.026f;        // 拇指-食指距离，小于即捏合。10-29 实机：1.8cm 太严（手部追踪抖动约 1cm），捏住了常识别不到
        public float pinchOff = 0.04f;        // 迟滞，大于才松开
        public float grabRadius = 0.09f;      // 捏合点到敌人 8cm 内吸附
        public float candyGrabRadius = 0.08f;
        public float reach = 0.65f;           // 一臂之内（从玩家胸前算）
        public double pullSec = 0.6;          // 隔空取物用时，期间须持续注视
        public float pullConeDeg = 22f;      // 10-30：15° 太严，飞来途中稍一走神就掉       // 隔空取物途中的注视容差
        public double pullGraceSec = 0.45;
        public float pressSpeed = 0.45f;      // 掌心朝下拍坩埚的向下速度阈值
        public float throwMul = 1.5f;         // 松手时手速 × 该倍率 = 抛出速度
        public float throwMax = 5f;
        public float throwSpeed = 1.2f;       // 超过这个速度算“扔”
    }

    [Serializable]
    public class ShakeConfig
    {
        public float minSpeed = 0.6f;  // 手速超过才计入晃动
        public int reversals = 3;      // 来回换向次数
        public double windowSec = 0.45; // 两次换向的最大间隔
        public double dizzySec = 4.0;  // 被晃晕：放下后原地转圈这么久，期间不用定身也能抓
    }

    // 长按蓄力：捏住不放 chargeSec 后敌人发光，此时甩出去就飞出窗外变成星星（永久消灭）
    [Serializable]
    public class LaunchConfig
    {
        public double chargeSec = 1.2;
        public float minSpeed = 0.6f; // 蓄满后松手时手速至少这么快（10-29 用户：发金光后一丢就变星星，1.5 太难）
        public float flingSpeed = 1.3f; // 10-30：发金光后捏着用力一甩（手速超过这个、不是往下甩）不等松手就直接飞出去
        public double blowSec = 0.95;   // 外挥吹走：飞向远处的时间
        public double flySec = 1.1;   // 飞向窗外的时间
    }

    // 女巫坩埚：收纳 + 慢慢消化
    [Serializable]
    public class CauldronConfig
    {
        public int capacity = 3;          // 格数（缝合熊占 4 格，大幽灵占 2 格，其余 1 格）
        public double digestSec = 6;      // 消化一格需要的时间（按顺序一个一个消化）。10-31：9 秒时后几夜坩埚只能吃下三分之一的小鬼，改 6 秒
        public double stirMul = 2;        // 拍锅沿：接下来 stirSec 秒内消化速度 × stirMul（10-31：×3 时狂拍锅沿成了最优解，收到 ×2）
        public double stirSec = 1.5;
        public double stirCooldown = 1.2;
    }

    [Serializable]
    public class PraySkillConfig
    {
        public string label = "月光合十";
        public double cooldown = 16;     // 10-31：合十太弱（只定 2 秒），改 3 秒、冷却 16
        public double freezeSec = 3.0;
        public double slowSec = 4.0;
        public double slowMul = 0.5;
    }

    [Serializable]
    public class SweepSkillConfig
    {
        public string label = "狂风挥散";
        public double cooldown = 28;      // 10-31：吹走的小鬼 10 秒后会回来，冷却也拉长一点
        public float radius = 2.4f;
        public float push = 2.6f;
        public float lift = 1.4f;
        public double dizzySec = 1.6;
    }

    [Serializable]
    public class LampSkillConfig
    {
        public string label = "掌心南瓜灯";
        public double cooldown = 8;       // 10-31：冷却等于持续时间时“最多 2 盏”永远用不上
        public float radius = 0.55f;        // 10-30 灯改成从掌心飞到前方桌面上空，照得更开一些
        public double duration = 12;
        public int max = 2;
        public double slowMul = 0.6;
    }

    [Serializable]
    public class ShieldSkillConfig
    {
        public string label = "月光护桶";
        public double cooldown = 26;
        public double duration = 6;
    }

    // 手势技能（有冷却）。每夜的 skills 列表决定解锁
    [Serializable]
    public class SkillsConfig
    {
        public PraySkillConfig pray = new PraySkillConfig();
        public SweepSkillConfig sweep = new SweepSkillConfig();
        public LampSkillConfig lamp = new LampSkillConfig();
        public ShieldSkillConfig shield = new ShieldSkillConfig();

        // JS 里是 cfg.skills[id].cooldown，这里按 id 分派
        public double Cooldown(string id)
        {
            switch (id)
            {
                case SkillIds.Pray: return pray.cooldown;
                case SkillIds.Sweep: return sweep.cooldown;
                case SkillIds.Lamp: return lamp.cooldown;
                case SkillIds.Shield: return shield.cooldown;
                default: return 0f;
            }
        }
    }

    [Serializable]
    public class GestureConfig
    {
        public float prayDist = 0.12f;       // 双掌距离（10-30 实机：两掌贴合时追踪不稳，8.5 厘米太严）
        public double prayHoldSec = 0.3;     // 合十保持时长
        public float sweepStartDist = 0.3f;  // 外挥起手：双掌靠近
        public float sweepEndDist = 0.52f;   // 外挥结束：双掌张开（10-31：0.62 坐着要把手肘大幅外展，MR 里侧面还可能碰到真实物体）
        public double sweepSec = 0.55;       // 必须在这么短时间内挥开
        public double lampHoldSec = 0.6;     // 单手五指张开、手背朝着自己（掌心朝外）、保持不动
        public float lampStillSpeed = 0.15f; // 手速低于这个才算“举稳了”
        public float openExt = 0.86f;        // 南瓜灯：手指算“伸直”的伸直程度
        public float notFistExt = 0.65f;     // 合十、护桶只排除握拳（10-30 实机：两掌贴在一起时追踪常把手指算弯，0.86 太严，合十识别不出来）
        public float lampSpread = 1.45f;     // 南瓜灯要求五指张开的程度
        public float lampFacing = 0.5f;      // 掌心朝外：掌心法线和“头→掌心”方向的点积
        public float shieldDist = 0.35f;     // 下按：双手掌心朝下、相距不超过这个
        public float shieldSpeed = 0.5f;     // 双手同时向下的速度
    }

    [Serializable]
    public class CandyConfig
    {
        public int total = 12;
        public double stealSec = 0.8;
    }

    [Serializable]
    public class DirectorConfig
    {
        public int maxActive = 10;
    }

    [Serializable]
    public class GameConfig
    {
        public GazeConfig gaze = new GazeConfig();
        public FreezeConfig freeze = new FreezeConfig();
        public SpeedConfig speed = new SpeedConfig();
        public GrabConfig grab = new GrabConfig();
        public HandConfig hand = new HandConfig();
        public ShakeConfig shake = new ShakeConfig();
        public LaunchConfig launch = new LaunchConfig();
        public CauldronConfig cauldron = new CauldronConfig();
        public SkillsConfig skills = new SkillsConfig();
        public GestureConfig gesture = new GestureConfig();
        public CandyConfig candy = new CandyConfig();
        public DirectorConfig director = new DirectorConfig();

        // 默认配置（等价于 JS 的全局 CONFIG）。每次返回新实例，避免被某局游戏改动后污染别的局。
        public static GameConfig CreateDefault() => new GameConfig();
    }

    public static class SkillIds
    {
        public const string Pray = "pray";
        public const string Sweep = "sweep";
        public const string Lamp = "lamp";
        public const string Shield = "shield";
    }
}
