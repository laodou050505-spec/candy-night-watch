// 一帧的输入（对应 game.js 文件头的 InputFrame 约定）。各输入源负责转换到“世界局部坐标”。
//   head:  头部位置与朝向，决定余光/视野外
//   gaze:  注视射线：eye | head | mouse
//   hands: 每只手的捏合状态与捏合点（拇指尖和食指尖的中点）
//   events: pinchStart / pinchEnd / press / skill
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public static class Sides
    {
        public const string Left = "left", Right = "right";
        public static string Other(string side) => side == Left ? Right : Left;
    }

    public static class GazeSources { public const string Eye = "eye", Head = "head", Mouse = "mouse"; }

    public static class InputEventTypes
    {
        public const string PinchStart = "pinchStart";
        public const string PinchEnd = "pinchEnd";
        public const string Press = "press";   // 拍坩埚
        public const string Skill = "skill";   // 手势技能（GestureDetector 产出）
    }

    public class HeadPose
    {
        public Vector3 pos;
        public Vector3 fwd;
        public HeadPose() { }
        public HeadPose(Vector3 pos, Vector3 fwd) { this.pos = pos; this.fwd = fwd; }
    }

    public class GazeRay
    {
        public Vector3 origin;
        public Vector3 dir;
        public bool valid = true;
        public string source = GazeSources.Eye;
    }

    public class HandInput
    {
        public string side;
        public bool tracked = true;
        public bool pinching;
        public Vector3 point;          // 捏合点
        public bool hasPoint = true;   // JS 里 point 可能缺省；缺省时不计入手速历史
        public bool stale;             // 10-31：追踪短暂丢失/跳变时沿用的上一帧（位置不可信，不计入手速历史）
        public float lampK;            // 托灯蓄力进度 0~1（表现层画掌心光用，规则层不读）
        public Vector3[] joints;       // 26 个手部关节（OpenXR 顺序，世界根坐标），表现层画手套用；规则层不读，可为空

        public HandInput() { }
        public HandInput(string side, Vector3 point, bool pinching = true)
        {
            this.side = side; this.point = point; this.pinching = pinching;
        }
    }

    public class InputEvent
    {
        public string type;
        public string side;    // pinchStart / pinchEnd
        public Vector3? pos;   // press 的位置；skill(lamp) 的灯位置（可空）
        public Vector3? from;  // skill(lamp)：灯是从哪儿（掌心前）飞出去的，表现层用
        public string id;      // skill 的技能 id

        public static InputEvent PinchStart(string side) => new InputEvent { type = InputEventTypes.PinchStart, side = side };
        public static InputEvent PinchEnd(string side) => new InputEvent { type = InputEventTypes.PinchEnd, side = side };
        public static InputEvent Press(Vector3 pos) => new InputEvent { type = InputEventTypes.Press, pos = pos };
        public static InputEvent Skill(string id, Vector3? pos = null) => new InputEvent { type = InputEventTypes.Skill, id = id, pos = pos };
    }

    public class InputFrame
    {
        public HeadPose head;                              // 可空
        public GazeRay gaze;                               // 可空
        public List<HandInput> hands = new List<HandInput>();
        public List<InputEvent> events = new List<InputEvent>();
    }

    // 手势检测器的单手输入：掌心位置 + 掌心法线 + 是否在捏合
    public class GestureHand
    {
        public Vector3 palm;
        public Vector3 palmN;
        public bool hasPalmN = true;  // JS 里 palmN 可能缺省
        public bool pinching;
        // 手形（PICO 由关节算出；桌面调试没有 → hasShape=false，相关条件视为满足）
        public bool hasShape;
        public float ext;              // 四指伸直程度：指根→指尖直线距离 / 三节骨长之和（伸直≈1，握拳≈0.5）
        public float spread;           // 四指张开程度：食指尖到小指尖 / 食指根到小指根（并拢≈1.1，张开≥1.6）
        public Vector3 away;           // 头 → 掌心的方向（判断“手背朝着自己、掌心朝外”）
        public bool hasAway;
        public bool stale;             // 10-31：沿用的上一帧（手已经不在了，手势计时不再累加）

        public GestureHand() { }
        public GestureHand(Vector3 palm, Vector3 palmN, bool pinching = false)
        {
            this.palm = palm; this.palmN = palmN; this.pinching = pinching;
        }
    }
}
