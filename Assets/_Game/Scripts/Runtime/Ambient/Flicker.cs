// 烛火闪烁：灯光强度/颜色温度噪声抖动；可选同时缩放火焰网格。南瓜灯、蜡烛、灯笼都用它。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class Flicker : MonoBehaviour
    {
        public Light target;
        public Transform flame;             // 火焰网格（可空）
        public float baseIntensity = 1f;
        public float amount = 0.35f;
        public float speed = 9f;
        float seed; Vector3 flameScale;

        void Start()
        {
            if (!target) target = GetComponentInChildren<Light>();
            if (target && baseIntensity <= 0) baseIntensity = target.intensity;
            if (flame) flameScale = flame.localScale;
            seed = Random.value * 100f;
        }

        void Update()
        {
            float t = Time.time * speed;
            float n = Mathf.PerlinNoise(t, seed) * 0.7f + Mathf.PerlinNoise(t * 2.7f, seed + 7) * 0.3f;
            float k = 1 + (n - 0.5f) * 2 * amount;
            if (target) target.intensity = baseIntensity * k;
            if (flame) flame.localScale = new Vector3(flameScale.x * (2 - k) * 0.5f + flameScale.x * 0.5f, flameScale.y * k, flameScale.z * (2 - k) * 0.5f + flameScale.z * 0.5f);
        }
    }
}
