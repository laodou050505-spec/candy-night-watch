// 场景布局（世界坐标，玩家坐在原点，面朝 -Z）。对应原型 src/logic/layout.js。
// 逻辑与渲染共用同一份布局，1 单位 = 1 米。
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public static class Layout
    {
        public const float EYE_HEIGHT = 1.15f;                 // 坐姿眼高（XR 中会按实际头高校准整个世界）
        public const float SEAT_Z = 0.12f;                     // 座位往后挪（10-29 用户：离桌子太近——圆桌前沿原本离身体只有 15 厘米）
        public static readonly Vector3 CHEST = new Vector3(0f, 0.92f, -0.12f);

        // 庄园后花园：四周是看得见的石栏杆矮墙（左右、身后）和正前方的铸铁栅栏 + 拱门。
        // 被扔、被吹的小鬼和糖最远只到墙根，撞墙时规则层发 WallHit（表现层：咚一声 + 灰尘），不设看不见的墙。
        public static class ROOM
        {
            public const float minX = -5.75f, maxX = 5.75f, minZ = -4.6f, maxZ = 4.3f, height = 3.2f;
        }

        // 藤架：蜘蛛从椽子底下垂丝（椽子沿 X 方向，每 0.95m 一根，底面高 2.735m）
        public static class PERGOLA
        {
            public const float rafterY = 2.72f;
            public static readonly float[] rafterZ = { -4.3f, -3.35f, -2.4f, -1.45f, -0.5f, 0.45f, 1.4f };
        }

        public static class TABLE
        {
            public static readonly Vector3 center = new Vector3(0f, 0f, -1.3f); // 只用 x、z
            public const float radius = 1.15f;
            public const float y = 0.72f;                      // 桌面高度
        }

        // 南瓜糖桶（守护目标）
        public static class JAR
        {
            // 10-30 用户：糖桶提起来放到哪就在哪（只要在桌上）——位置是可变的，规则层、表现层都读这里
            public const float DefaultX = 0f, DefaultZ = -0.68f;
            public static float x = DefaultX, z = DefaultZ;
            public static Vector3 pos => new Vector3(x, y, z);
            public static void Set(float nx, float nz) { x = nx; z = nz; }
            public static void Reset() { x = DefaultX; z = DefaultZ; }
            public const float y = TABLE.y, radius = 0.11f, height = 0.22f;   // 糖桶放大 1.3 倍（场景里 Desk_Bucket 同步缩放）
            public const float outer = 0.145f;   // 11-04：桶肚最宽处的实际外壳半径（实测 0.140），小鬼靠桶站位按它算，脸不扎进桶壁
            public const float scale = 1.3f;
        }

        // 女巫坩埚（收纳并慢慢消化敌人）：在玩家右手边。
        // 注意 Unity 是左手坐标系：玩家面朝 -Z 时右手在 -X 一侧（原型 three.js 是右手坐标系，+X 才是右边，数值不能照搬）
        public static class BOX
        {
            // 10-28 实机：原来 (-0.42,-0.5) 离玩家 0.65 米，坐着伸手偏远 → 挪到 0.52 米（右前方、胸口高度，伸手最顺；再近锅就压到桌沿外了）
            // 10-29 用户：坩埚可以用手拿起来挪位置——位置是可变的（规则层、表现层都读这里）。默认位置见 DefaultX/DefaultZ
            public const float DefaultX = -0.34f, DefaultZ = -0.4f;
            public static float x = DefaultX, z = DefaultZ;
            public static Vector3 pos => new Vector3(x, y, z);
            public static void Set(float nx, float nz) { x = nx; z = nz; }
            public static void Reset() { x = DefaultX; z = DefaultZ; }
            // 10-29 用户：锅太大 → 1.35 倍缩到 1.05 倍；10-30 一度缩到 0.8 倍（后查明满屏是装饰大锅被搬上桌）。
            // half = 锅身最大半径，mouth = 锅口内径（从这里扔进去才算入锅），height = 锅沿顶高
            // 10-30 查明“满屏”其实是表现层错把装饰大锅搬上了桌，用户又说 0.8 倍偏小 → 回到 1.05 倍（10-29 认可过的大小）
            public const float scale = 1.05f;
            public const float y = TABLE.y, half = 0.111f * scale, mouth = 0.086f * scale, height = 0.1625f * scale;
            /// <summary>提起坩埚时最多离桌面这么高（只是“提起来挪个位置”，不让它被端到脸前挡住视线）</summary>
            public const float LIFT_MAX = 0.32f;                    // 10-30：拿起来能转着看，抬高上限放宽到 32 厘米（水平位置仍限制在桌面上）
            // 两只锅耳（锅身两侧的铁环）：坩埚本地坐标（正面朝玩家，X 朝两侧），只有捏住锅耳才能把锅提起来
            public const float EAR_X = 0.124f * scale, EAR_Y = 0.108f * scale;    // 与 GameplayBuilder.SeatCauldronEars 量出的环心一致
            /// <summary>坩埚朝向：正面对着玩家（座位在原点）</summary>
            public static float Yaw => Mathf.Atan2(-x, -z) * Mathf.Rad2Deg;
            public static Vector3 Ear(int side)
            {
                float a = Yaw * Mathf.Deg2Rad, lx = side * EAR_X;
                return new Vector3(x + Mathf.Cos(a) * lx, y + EAR_Y, z - Mathf.Sin(a) * lx);
            }
        }

        public const float TOY_HEIGHT = 0.2f;  // 基础敌人身高（小骷髅）
        public const float TOY_RADIUS = 0.065f;

        // 桌上的南瓜灯：一小圈常亮的光（黑猫在光里才现形）
        public static class CANDLE
        {
            public static readonly Vector3 pos = new Vector3(0.5f, TABLE.y, -0.95f);    // 玩家左前方
            public const float x = 0.5f, y = TABLE.y, z = -0.95f, radius = 0.36f;
            /// <summary>这一夜桌上有没有烛台（10-30 用户：能召唤南瓜灯的夜晚，桌上就不该再有烛台）。由 Game.StartNight 设置</summary>
            public static bool on = true;
        }

        // 窗口 = 正前方铸铁拱门：蓄力扔出的敌人从拱门飞出花园，变成夜空里的星星
        public static class WINDOW
        {
            public const float x = 0f, y = 1.75f, z = -4.8f, w = 1.6f, h = 1.2f;
        }

        // 桌沿上某个角度的点（角度以桌心为原点，0 指向远离玩家的 -Z 方向，正值向 +X）
        public static Vector3 RimPoint(float angleDeg, float inset = 0.04f)
        {
            float a = angleDeg * Mathf.PI / 180f;
            float r = TABLE.radius - inset;
            return new Vector3(TABLE.center.x + Mathf.Sin(a) * r, TABLE.y, TABLE.center.z - Mathf.Cos(a) * r);
        }

        public static bool OnTable(Vector3 p, float margin = 0f)
        {
            return Vec.DistXZ(p, TABLE.center) <= TABLE.radius - margin;
        }
    }
}
