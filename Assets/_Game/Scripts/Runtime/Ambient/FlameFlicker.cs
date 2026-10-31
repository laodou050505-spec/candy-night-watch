// 火苗：独立网格，绕底部（灯芯处）做噪声缩放与轻微摆动，模拟烛火跳动。火苗网格原点在蜡烛底部，
// 所以按网格包围盒找到火苗底部作为枢轴：运行时把网格挂到一个位于火苗底部的枢轴节点下。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class FlameFlicker : MonoBehaviour
    {
        public float amount = 0.18f, speed = 9f, sway = 6f;
        Transform pivot; Vector3 s0; float seed;

        void Start()
        {
            seed = Random.value * 100f;
            var mf = GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return;
            var b = mf.sharedMesh.bounds;
            var baseLocal = new Vector3(b.center.x, b.min.y, b.center.z);
            pivot = new GameObject(name + "_Pivot").transform;
            pivot.SetParent(transform.parent, false);
            pivot.localPosition = transform.localPosition + transform.localRotation * Vector3.Scale(baseLocal, transform.localScale);
            pivot.localRotation = transform.localRotation;
            transform.SetParent(pivot, true);
            s0 = pivot.localScale;
        }

        void Update()
        {
            if (!pivot) return;
            float t = Time.time * speed;
            float n = Mathf.PerlinNoise(t, seed) - 0.5f, m = Mathf.PerlinNoise(seed, t * 0.7f) - 0.5f;
            pivot.localScale = new Vector3(s0.x * (1 - n * amount * 0.6f), s0.y * (1 + n * amount * 2f), s0.z * (1 - n * amount * 0.6f));
            pivot.localRotation = Quaternion.Euler(m * sway, 0, (Mathf.PerlinNoise(t * 0.5f, seed + 3) - 0.5f) * sway);
        }
    }
}
