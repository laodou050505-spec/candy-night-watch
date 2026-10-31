// 发光芯随烛火呼吸：轻微缩放（材质共享，不改颜色，避免每个实例新建材质）。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class GlowPulse : MonoBehaviour
    {
        public float amount = 0.06f;
        Vector3 s0; float seed;
        void Start() { s0 = transform.localScale; seed = Random.value * 50; }
        void Update() { transform.localScale = s0 * (1 + (Mathf.PerlinNoise(Time.time * 6, seed) - 0.5f) * 2 * amount); }
    }
}
