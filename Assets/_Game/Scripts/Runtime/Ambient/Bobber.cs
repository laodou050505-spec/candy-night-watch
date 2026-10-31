// 漂浮：上下起伏 + 缓慢自转 + 轻微摇摆。浮岛、浮空蜡烛、卫星岛上的建筑都用它，让整个场景“活着”。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class Bobber : MonoBehaviour
    {
        public float amplitude = 0.25f;     // 上下起伏幅度（米）
        public float period = 7f;           // 起伏周期（秒）
        public float yawSpeed = 0f;         // 自转速度（度/秒）
        public float tilt = 0.8f;           // 摇摆角度（度）
        public float phase = -1f;           // <0 时按位置自动错开
        Vector3 basePos; Quaternion baseRot;

        void Start()
        {
            basePos = transform.localPosition; baseRot = transform.localRotation;
            if (phase < 0) phase = Mathf.Repeat(basePos.x * 0.37f + basePos.z * 0.61f, 1f) * Mathf.PI * 2;
        }

        void Update()
        {
            float t = Time.time * Mathf.PI * 2 / period + phase;
            transform.localPosition = basePos + Vector3.up * Mathf.Sin(t) * amplitude;
            transform.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.7f) * tilt, Time.time * yawSpeed, Mathf.Cos(t * 0.53f) * tilt) * baseRot;
        }
    }
}
