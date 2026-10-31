// 摆动：挂着的灯笼、旗串、藤蔓尖绕枢轴来回摆（枢轴 = 物体自身原点，放在挂点上）。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class Swayer : MonoBehaviour
    {
        public Vector3 axis = new Vector3(0, 0, 1);
        public float angle = 6f;
        public float period = 3.2f;
        public float gust = 0.35f;          // 阵风：叠加一层慢速噪声
        float phase; Quaternion baseRot;

        void Start() { baseRot = transform.localRotation; phase = Random.value * 10f; }

        void Update()
        {
            float t = Time.time;
            float a = Mathf.Sin(t * Mathf.PI * 2 / period + phase) * angle;
            a += (Mathf.PerlinNoise(t * 0.15f, phase) - 0.5f) * 2 * angle * gust;
            transform.localRotation = Quaternion.AngleAxis(a, axis.normalized) * baseRot;
        }
    }
}
