// 桌面调试输入（编辑器里没戴头显时用）：鼠标 = 视线，按住左键 = 右手捏合（手跟着鼠标在桌面上方移动，限制在一臂之内），
// Shift + 左键 = 双手一起捏；右键/F = 拍锅沿；空格 合十、W 外挥、S 护桶、L 托灯；Q/E 转头。与原型 desktop.js 一致。
using System.Collections.Generic;
using TrickOrFreeze.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrickOrFreeze.Input
{
    public class DesktopInputProvider : MonoBehaviour, IInputProvider
    {
        public Transform worldRoot;
        public Camera cam;
        float yaw = 180f, pitch = -22f;   // 规则层玩家面朝 -Z
        Vector3 handPoint = new Vector3(-0.2f, 0.9f, -0.35f);      // 玩家面朝 -Z 时右手在 -X 一侧（Unity 左手坐标系）
        static readonly Vector3 LeftOffset = new Vector3(0.24f, 0, 0.02f);
        readonly Vector3[] jointsR = new Vector3[HandJoints.Count], jointsL = new Vector3[HandJoints.Count];
        float pinchR, pinchL;
        float lift;
        bool down, both, pendingStart, pendingEnd;
        readonly InputFrame frame = new InputFrame();
        public InputFrame Frame => frame;
        public bool IsXR => false;

        public void Tick(float dt)
        {
            frame.hands.Clear();
            frame.events.Clear();
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null) return;

            float turn = (kb.qKey.isPressed || kb.leftArrowKey.isPressed ? -1 : 0) + (kb.eKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0);
            yaw = Mathf.Clamp(yaw + turn * dt * 100f, 0f, 360f);
            cam.transform.localPosition = new Vector3(0, Layout.EYE_HEIGHT, Layout.SEAT_Z);
            cam.transform.localRotation = Quaternion.Euler(-pitch, yaw, 0);

            var ray = cam.ScreenPointToRay(ms.position.ReadValue());
            var o = ToLocal(ray.origin); var d = ToLocalDir(ray.direction);

            if (ms.leftButton.wasPressedThisFrame) { down = true; pendingStart = true; both = kb.shiftKey.isPressed; }
            if (ms.leftButton.wasReleasedThisFrame && down) { down = false; pendingEnd = true; }
            if (ms.rightButton.wasPressedThisFrame || kb.fKey.wasPressedThisFrame) frame.events.Add(InputEvent.Press(handPoint));
            if (kb.spaceKey.wasPressedThisFrame) frame.events.Add(InputEvent.Skill("pray"));
            if (kb.wKey.wasPressedThisFrame) frame.events.Add(InputEvent.Skill("sweep"));
            if (kb.sKey.wasPressedThisFrame) frame.events.Add(InputEvent.Skill("shield"));
            if (kb.lKey.wasPressedThisFrame) { var le = InputEvent.Skill("lamp", GestureDetector.LampSpot(handPoint + Vector3.up * 0.1f, Vector3.forward * -1f)); le.from = handPoint; frame.events.Add(le); }

            // 手：鼠标射线与“桌面上方 9cm”平面求交，拿着东西时抬高
            lift = Mathf.Lerp(lift, down ? 0.14f : 0.09f, Mathf.Min(1, dt * 10));
            float planeY = Layout.TABLE.y + lift;
            if (d.y < -0.01f)
            {
                float t = (planeY - o.y) / d.y;
                var p = o + d * t;
                var off = p - Layout.CHEST;
                float reach = 0.65f;
                if (off.magnitude > reach) p = Layout.CHEST + off.normalized * reach;
                handPoint = Vector3.Lerp(handPoint, p, Mathf.Min(1, dt * 25));
            }

            bool leftActive = both && (down || pendingEnd);
            if (pendingStart) { if (both) frame.events.Add(InputEvent.PinchStart(Sides.Left)); frame.events.Add(InputEvent.PinchStart(Sides.Right)); pendingStart = false; }
            if (pendingEnd) { if (both) frame.events.Add(InputEvent.PinchEnd(Sides.Left)); frame.events.Add(InputEvent.PinchEnd(Sides.Right)); pendingEnd = false; both = false; }
            // 手套：按捏合程度生成整只手的关节，让捏合点正好落在 handPoint 上
            pinchR = Mathf.MoveTowards(pinchR, down ? 1 : 0, dt * 8);
            var hr = new HandInput(Sides.Right, handPoint, down) { joints = PoseHand(true, handPoint, pinchR, jointsR) };
            frame.hands.Add(hr);
            bool showL = leftActive || kb.shiftKey.isPressed;
            pinchL = Mathf.MoveTowards(pinchL, leftActive && down ? 1 : 0, dt * 8);
            var lp = handPoint + LeftOffset;
            frame.hands.Add(new HandInput(Sides.Left, lp, leftActive && down) { tracked = showL, hasPoint = showL, joints = PoseHand(false, lp, pinchL, jointsL) });

            frame.head = new HeadPose(ToLocal(cam.transform.position), ToLocalDir(cam.transform.forward));
            frame.gaze = new GazeRay { origin = o, dir = d, valid = true, source = GazeSources.Mouse };
        }

        public Vector3 HandWorld(string side) => worldRoot.TransformPoint(side == Sides.Left ? handPoint + LeftOffset : handPoint);

        // 桌面模拟的手：掌心朝下略朝内、手指朝前下方，捏合点对准 point
        static Vector3[] PoseHand(bool right, Vector3 point, float pinch, Vector3[] buf)
        {
            var fwd = new Vector3(right ? 0.25f : -0.25f, -0.45f, -1f).normalized;
            var rot = Quaternion.LookRotation(fwd, new Vector3(right ? -0.3f : 0.3f, 1f, 0.2f));
            var shape = HandShape.Lerp(HandShape.Relaxed, HandShape.Pinch, pinch);
            HandPoses.Build(right, Vector3.zero, rot, shape, buf);
            var off = (buf[HandJoints.ThumbTip] + buf[HandJoints.IndexTip]) * 0.5f;
            for (int i = 0; i < buf.Length; i++) buf[i] += point - off;
            return buf;
        }
        Vector3 ToLocal(Vector3 w) => worldRoot ? worldRoot.InverseTransformPoint(w) : w;
        Vector3 ToLocalDir(Vector3 w) => worldRoot ? worldRoot.InverseTransformDirection(w) : w;
    }

    public interface IInputProvider
    {
        InputFrame Frame { get; }
        bool IsXR { get; }
        void Tick(float dt);
    }
}
