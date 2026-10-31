// VR 面板摆放：打开时出现在玩家头部朝向的正前方（距离 1.3m，中心略低于视线 2°，正好避开桌上的糖桶和坩埚），
// 平时不动；玩家转头超过 28° 才缓缓跟过去（0.5 秒左右滑到新位置），不会贴着脸晃。高度按实际头高算，坐着站着都合适。
using UnityEngine;

namespace TrickOrFreeze.UI
{
    public class VrPanel : MonoBehaviour
    {
        public float distance = 1.3f;
        public float elevationDeg = -2f;
        public float followAngle = 28f;
        float yaw, vel;
        bool moving;

        static Transform Head => Camera.main ? Camera.main.transform : null;

        static float HeadYaw(Transform h)
        {
            var f = h.forward; f.y = 0;
            if (f.sqrMagnitude < 1e-4f) f = h.up * -1f;     // 正抬头/低头时用头顶方向兜底
            return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
        }

        void OnEnable() { var h = Head; if (!h) return; yaw = HeadYaw(h); moving = false; Apply(h); }

        void LateUpdate()
        {
            var h = Head; if (!h) return;
            float hy = HeadYaw(h);
            float d = Mathf.DeltaAngle(yaw, hy);
            if (Mathf.Abs(d) > followAngle) moving = true;
            if (moving)
            {
                yaw = Mathf.SmoothDampAngle(yaw, hy, ref vel, 0.18f);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, hy)) < 1.5f) moving = false;
            }
            Apply(h);
        }

        void Apply(Transform h)
        {
            float e = elevationDeg * Mathf.Deg2Rad, y = yaw * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(y) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(y) * Mathf.Cos(e));
            var pos = h.position + dir * distance;
            transform.position = pos;
            // 画布正面（-forward）朝向眼睛，略微后仰正对视线
            transform.rotation = Quaternion.LookRotation(pos - h.position, Vector3.up);
        }
    }
}
