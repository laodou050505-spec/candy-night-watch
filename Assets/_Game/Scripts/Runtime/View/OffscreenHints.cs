// 视野外提示：小鬼从侧面、身后（或桌下）往糖桶靠近、而玩家没在看它时，
// 在视野边缘浮出一个南瓜橙的小箭头，指向它所在的方向（10-28 分析：桌子周围 360° 都会来小鬼，余光外的很难发现）。
//   · 只提示“正在过来偷糖 / 抱着糖跑”的小鬼，被定住的、晕着的、拎在手里的都不提示
//   · 最多同时 3 个，按离糖桶由近到远排；箭头离开视野中心约 32°、略低于视线，在 1 米远处，跟着头平滑移动
//   · 小鬼进入视野中心 50° 以内就淡出；箭头轻轻呼吸，离糖桶越近越亮、呼吸越快
using System.Collections.Generic;
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public class OffscreenHints : MonoBehaviour
    {
        public GameLoop loop;
        public Camera cam;
        public Sprite arrow;
        public int max = 3;
        public float showBeyondDeg = 50f, edgeDeg = 32f, dist = 1.0f, size = 0.045f;

        class Pip { public SpriteRenderer sr; public float a; public Vector3 dir; public int toy; }
        readonly List<Pip> pips = new List<Pip>();
        readonly List<Toy> cand = new List<Toy>();

        void Start()
        {
            if (!cam) cam = Camera.main;
            for (int i = 0; i < max; i++)
            {
                var go = new GameObject("Hint" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = arrow; sr.color = new Color(1, 1, 1, 0);
                sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sr.sortingOrder = 50;
                pips.Add(new Pip { sr = sr, toy = -1 });
            }
        }

        void LateUpdate()
        {
            var g = loop ? loop.game : null;
            if (!cam || g == null || arrow == null) return;
            float dt = Time.deltaTime;
            var head = cam.transform;
            var fwd = head.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            cand.Clear();
            if (g.phase == Phases.Playing && !loop.paused)
                foreach (var t in g.toys)
                {
                    if (t.frozen || t.dizzy > 0) continue;
                    if (t.state != ToyStates.Walk && t.state != ToyStates.Steal && t.state != ToyStates.Flee && t.state != ToyStates.Glide) continue;
                    var w = loop.W(t.pos) - head.position; w.y = 0;
                    if (w.sqrMagnitude < 1e-4f || Vector3.Angle(fwd, w) < showBeyondDeg) continue;
                    cand.Add(t);
                }
            cand.Sort((a, b) => Vec.DistXZ(a.pos, Layout.JAR.pos).CompareTo(Vec.DistXZ(b.pos, Layout.JAR.pos)));
            for (int i = 0; i < pips.Count; i++)
            {
                var p = pips[i];
                Toy t = i < cand.Count ? cand[i] : null;
                float target = t != null ? 1 : 0;
                p.a = Mathf.MoveTowards(p.a, target, dt * (target > 0 ? 3f : 2f));
                p.sr.enabled = p.a > 0.01f;
                if (t == null) { if (!p.sr.enabled) p.toy = -1; continue; }
                // 箭头位置：从视线正前方往小鬼方向转 edgeDeg 度，放在 1 米远、略低于视线
                var w = loop.W(t.pos) - head.position; w.y = 0; w.Normalize();
                float side = Mathf.Sign(Vector3.Cross(fwd, w).y);
                var d = Quaternion.AngleAxis(side * edgeDeg, Vector3.up) * fwd;
                d = (d + Vector3.down * 0.12f).normalized;
                if (p.toy != t.id) { p.dir = d; p.toy = t.id; }
                p.dir = Vector3.Slerp(p.dir, d, 1 - Mathf.Exp(-dt * 8f));
                var tr = p.sr.transform;
                tr.position = head.position + p.dir * dist;
                // 面朝玩家，箭头指向侧面（小鬼在身后时稍微往后翻）
                tr.rotation = Quaternion.LookRotation(tr.position - head.position, Vector3.up);
                float behind = Mathf.Clamp01((Vector3.Angle(fwd, w) - 90f) / 90f);
                tr.Rotate(0, 0, side > 0 ? -behind * 35f : 180f + behind * 35f, Space.Self);     // 贴图箭头朝右；小鬼在左边就转 180°
                float near = 1 - Mathf.Clamp01((Vec.DistXZ(t.pos, Layout.JAR.pos) - 0.3f) / 1.6f);   // 离糖桶越近越急
                float pulse = 1 + (0.08f + 0.1f * near) * Mathf.Sin(Time.time * (4f + 5f * near));
                tr.localScale = Vector3.one * size / Mathf.Max(1e-4f, arrow.bounds.size.x) * pulse;
                p.sr.color = new Color(1, 1, 1, p.a * (0.6f + 0.4f * near));
            }
        }
    }
}
