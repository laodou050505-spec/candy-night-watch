// 群飞（蝙蝠群 / 小幽灵群）：像真正的鸟群一样整群行动——
//   领头沿一条平滑游荡的航线飞（椭圆 + 高度起伏 + 缓慢摆动，不会急转），
//   成员在领头的局部坐标系里保持松散队形（左右、上下、前后错开），队形位置随时间缓慢漂移；
//   成员带惯性地追自己的队形点（有滞后，转弯时外侧的会被甩开再追上），机头朝向平滑后的速度方向，转弯时向内侧倾；
//   扑翼和滑翔交替：一段扇翅（各自频率接近但略有差别）后滑翔片刻，整群节奏相近但不完全同步。
// 模型都是单网格。蝙蝠（wingFlap）：翅膀用 ToonLit 的 _Flap 绕肩部上下扇（身体不扭），下扇时身体微微上提；
// 滑翔时翅膀平伸略抬、几乎不动。其他（小幽灵）：用 _Wiggle 轻轻飘摆。
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class FlockOrbit : MonoBehaviour
    {
        public GameObject prefab;
        public int count = 9;
        public float radius = 14f;
        public float radiusZScale = 0.8f;
        public float height = 6f;
        public float heightWave = 1.6f;
        public float speed = 0.09f;          // 领头绕航线的圈速（圈/秒）
        public float scale = 6f;
        public float spread = 1.2f;          // 队形半宽（米）
        public float flap = 9f;              // 扇翅频率（Hz）
        public bool faceTravel = true;
        public bool modelFrontMinusX = true; // Tripo 模型正面朝模型 -X
        public bool wingFlap = false;        // 蝙蝠：真正的扇翅（翼展沿模型 Z）

        class Member
        {
            public Transform t; public Renderer[] rs; public MaterialPropertyBlock mpb;
            public Vector3 slot;              // 队形位置（领头局部坐标：x 横向、y 上下、z 前后，负值在后）
            public float seed, flapHz, phase, glideT, flapT, wing;
            public Vector3 pos, vel; public float bank;
        }
        readonly List<Member> members = new List<Member>();
        float leaderU;
        Quaternion faceFix = Quaternion.identity;   // 模型正面 → 行进方向

        void Start()
        {
            if (!prefab) return;
            leaderU = Random.value;
            faceFix = Quaternion.Euler(0, modelFrontMinusX ? TrickOrFreeze.View.ModelFacing.Yaw(prefab.name) : 0f, 0);
            for (int i = 0; i < count; i++)
            {
                var go = Instantiate(prefab, transform);
                go.name = prefab.name + "_" + i;
                go.transform.localScale = Vector3.one * scale * Random.Range(0.85f, 1.1f);
                var rs = go.GetComponentsInChildren<Renderer>();
                foreach (var r in rs) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // 松散的楔形队形：越往后越散开
                float back = -i * spread * 0.55f;
                float side = (i == 0 ? 0 : (i % 2 == 0 ? 1 : -1) * Mathf.Ceil(i / 2f)) * spread * 0.6f;
                var m = new Member
                {
                    t = go.transform, rs = rs, mpb = new MaterialPropertyBlock(),
                    slot = new Vector3(side + Random.Range(-0.3f, 0.3f) * spread, Random.Range(-0.4f, 0.4f) * spread, back + Random.Range(-0.3f, 0.3f) * spread),
                    seed = Random.Range(0f, 100f), flapHz = flap * Random.Range(0.9f, 1.1f), phase = Random.Range(0f, 6.28f),
                    flapT = Random.Range(1.5f, 3f),
                };
                m.pos = Leader(leaderU) + m.slot; m.vel = Vector3.zero;
                members.Add(m);
            }
        }

        // 领头航线：椭圆 + 慢速的半径摆动和高度起伏（导数连续，没有急转）
        Vector3 Leader(float u)
        {
            float a = u * Mathf.PI * 2;
            float r = radius * (1 + 0.12f * Mathf.Sin(a * 2 + 1.3f));
            return new Vector3(Mathf.Cos(a) * r, height + Mathf.Sin(a * 2 + 0.7f) * heightWave, Mathf.Sin(a) * r * radiusZScale);
        }

        void Update()
        {
            if (members.Count == 0) return;
            float dt = Mathf.Min(Time.deltaTime, 0.05f), t = Time.time;
            leaderU += speed * dt;
            var lp = Leader(leaderU);
            var fwd = (Leader(leaderU + 0.004f * Mathf.Sign(speed)) - lp).normalized;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, fwd).normalized;
            foreach (var m in members)
            {
                // 队形点随时间缓慢漂移（柏林噪声），队形不会僵硬
                var drift = new Vector3(Mathf.PerlinNoise(m.seed, t * 0.15f) - 0.5f, Mathf.PerlinNoise(m.seed + 7, t * 0.12f) - 0.5f, Mathf.PerlinNoise(m.seed + 13, t * 0.1f) - 0.5f) * spread * 0.8f;
                var s = m.slot + drift;
                var target = lp + right * s.x + Vector3.up * s.y + fwd * s.z;
                // 弹簧阻尼追队形点：有惯性、有滞后
                var acc = (target - m.pos) * 2.2f - m.vel * 1.6f;
                var prevV = m.vel;
                m.vel += acc * dt;
                m.pos += m.vel * dt;
                // 扇翅 / 滑翔交替
                m.flapT -= dt;
                if (m.flapT <= 0) { if (m.glideT > 0) { m.glideT = 0; m.flapT = Random.Range(1.5f, 3.2f); } else { m.glideT = Random.Range(0.6f, 1.4f); m.flapT = m.glideT; } }
                bool gliding = m.glideT > 0;
                m.wing = Mathf.MoveTowards(m.wing, gliding ? 0.08f : 0.6f, dt * 2f);
                m.phase += dt * m.flapHz * Mathf.PI * 2 * (gliding ? (wingFlap ? 0.08f : 0.2f) : 1f);
                // 下扇（相位 π/2→3π/2 翅膀往下）时身体被托起一点
                var bob = Vector3.up * (wingFlap ? -Mathf.Sin(m.phase) * 0.012f * m.wing : Mathf.Sin(m.phase) * 0.012f * m.wing) * scale;
                m.t.localPosition = m.pos + bob;
                if (faceTravel && m.vel.sqrMagnitude > 1e-4f)
                {
                    var dir = m.vel.normalized;
                    // 转弯侧倾：水平速度方向的转向率
                    var h0 = new Vector3(prevV.x, 0, prevV.z); var h1 = new Vector3(m.vel.x, 0, m.vel.z);
                    float turn = h0.sqrMagnitude > 1e-6f ? Vector3.SignedAngle(h0, h1, Vector3.up) / Mathf.Max(dt, 1e-3f) : 0;
                    m.bank = Mathf.Lerp(m.bank, Mathf.Clamp(-turn * 0.35f, -35f, 35f), dt * 3f);
                    var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 0, m.bank);
                    m.t.localRotation = Quaternion.Slerp(m.t.localRotation, rot * faceFix, dt * 6f);
                }
                if (wingFlap)
                {
                    m.mpb.SetFloat("_Wiggle", 0);
                    m.mpb.SetFloat("_Flap", m.wing * 1.45f);                 // 扇动时约 ±50°
                    m.mpb.SetFloat("_FlapPhase", m.phase);
                    m.mpb.SetFloat("_FlapBias", 0.12f);                      // 翅膀略往上抬（滑翔姿态）
                }
                else
                {
                    m.mpb.SetFloat("_Wiggle", m.wing);
                    m.mpb.SetFloat("_WigglePhase", m.phase);
                }
                foreach (var r in m.rs) r.SetPropertyBlock(m.mpb);
            }
        }
    }
}
