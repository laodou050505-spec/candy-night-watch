// 天上的过客：骑扫帚的女巫每隔一段时间从月亮前横穿夜空（大弧线，掠过月亮时形成经典剪影），
// 身后拖一串星尘；飞到另一边后隐藏，等待下一次。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class SkyFlyer : MonoBehaviour
    {
        public Transform rider;              // 女巫预制体实例
        public Vector3 from = new Vector3(-60, 14, -45), via = new Vector3(0, 20, -50), to = new Vector3(60, 12, -40);
        public float duration = 14f;
        public float interval = 38f;
        public float firstDelay = 6f;
        public ParticleSystem trail;
        public float bobAmp = 0.6f;          // 上下起伏幅度（封面小布景里要小得多）
        // 模型正面相对模型 +Z 的偏转角。H4_WitchOnBroom 的正面（脸、扫帚头）朝模型 -Z → 180。
        // 10-29 用户：女巫飞的方向必须和她面朝的方向一致（之前按“正面朝 +X”转了 -90°，她是横着飞的）。
        public float modelYaw = 180f;
        float timer;

        void Start() { timer = -firstDelay; if (rider) rider.gameObject.SetActive(false); }

        static Vector3 Bez(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);

        void Update()
        {
            if (!rider) return;
            timer += Time.deltaTime;
            if (timer < 0) return;
            if (timer > duration + interval) timer = 0;
            bool flying = timer <= duration;
            if (rider.gameObject.activeSelf != flying) rider.gameObject.SetActive(flying);
            if (trail) { var em = trail.emission; em.enabled = flying; }
            if (!flying) return;
            float u = timer / duration;
            var p = Bez(from, via, to, u) + Vector3.up * Mathf.Sin(timer * 2.2f) * bobAmp;
            var n = Bez(from, via, to, Mathf.Min(1, u + 0.01f));
            rider.localPosition = p;
            var dir = (n - p); dir.y *= 0.3f;
            if (dir.sqrMagnitude > 1e-6f) rider.localRotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, modelYaw, 0) * Quaternion.Euler(0, 0, Mathf.Sin(timer * 1.6f) * 8);
            if (trail) trail.transform.position = rider.position;
        }
    }
}
