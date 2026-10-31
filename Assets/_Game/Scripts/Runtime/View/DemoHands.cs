// 教学“示范手”：一双半透明、发青光的幽灵手套，在教学目标旁循环演示当前这一步该做的动作。
//   grab 捏起 / cauldron 捏起放进坩埚口 / shake 捏起晃一晃 / candy 捡糖放回糖桶 / throw 捏起甩远
//   pull 指着远处的小鬼捏一下往回拉 / launch 捏住等一会儿再朝拱门甩出
//   pray 双掌合十 / sweep 双手合拢后向两侧挥开 / lamp 右手五指张开、手背朝着自己 / shield 双掌朝下一起下按
//   look / residual 只练眼睛，不出手。
// 两种场合：
//   新手教学（第一夜、只在第一次玩）：每一步开始先停 1.2 秒（让玩家听完台词），之后一遍一遍演示，做对了才换下一步；
//   开夜介绍（之后各夜的新本事）：每句话配几遍演示；场上还没有小鬼时，对着桌上一个“假想的小鬼”位置演示。
// 教学/介绍结束、暂停时都收起来。
// 手的骨架用 HandPoses 程序生成，交给 HandView（overrideJoints）去摆手套，所以示范手和玩家自己的手套长得一模一样，只是材质换成幽灵。
using TrickOrFreeze.Core;
using TrickOrFreeze.Input;
using UnityEngine;

namespace TrickOrFreeze.View
{
    [DefaultExecutionOrder(-10)]   // 先摆好关节，同一帧里 HandView 再画手套（不差一帧）
    public class DemoHands : MonoBehaviour
    {
        public GameLoop loop;
        public Camera cam;
        public HandView left, right;
        public Material ghostMat;            // 两只示范手共用（运行时复制一份，淡入淡出直接改它）

        Material mat;
        int lastStep = -2;
        bool briefMode;
        float stepT;
        readonly Vector3[] tmp = new Vector3[HandJoints.Count];
        readonly Vector3[] jl = new Vector3[HandJoints.Count], jr = new Vector3[HandJoints.Count];
        static readonly int ID_Fade = Shader.PropertyToID("_Fade");

        // 一只手这一帧的姿态（规则层坐标）
        struct Pose
        {
            public bool on;
            public Vector3 at;          // 锚点位置：anchorTip 时是拇指尖/食指尖中点，否则是掌心
            public bool anchorTip;
            public Vector3 fingers, dorsal;
            public HandShape shape;
            public bool pinch;
            public float lampK;
        }

        void Start()
        {
            if (ghostMat)
            {
                mat = new Material(ghostMat);
                foreach (var hv in new[] { left, right })
                {
                    if (!hv) continue;
                    hv.gloveMat = hv.palmMat = hv.cuffMat = mat;
                    foreach (var r in hv.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = mat;
                }
            }
            if (!cam) cam = Camera.main;
        }

        void LateUpdate()
        {
            var g = loop ? loop.game : null;
            var L = g?.lesson;
            var B = g?.brief;
            LessonStep st = null; int stepNo = -2;
            if (loop.paused) { }
            else if (L != null && !L.done && L.Current != null) { st = L.Current; stepNo = L.step; briefMode = false; }
            else if (B != null && B.Current != null) { st = B.Current; stepNo = 1000 + B.step; briefMode = true; }
            if (st == null) { Hide(); lastStep = -2; return; }
            if (stepNo != lastStep) { lastStep = stepNo; stepT = 0; }
            stepT += Time.deltaTime;

            float delay = briefMode ? 0.6f : 1.2f; const float gap = 0.5f;
            float period = Period(st.id);
            if (period <= 0 || stepT < delay) { Hide(); return; }
            float c = (stepT - delay) % (period + gap);
            if (c > period) { Hide(); return; }
            float u = c / period;

            Pose pl = default, pr = default;
            if (!Sample(st.id, u, g, ref pl, ref pr)) { Hide(); return; }
            // 每遍开头淡入、结尾淡出（各动作自己决定什么时候开始淡出）
            float fade = Mathf.Clamp01(u / 0.08f) * Mathf.Clamp01((1 - u) / FadeTail(st.id));
            if (mat) mat.SetFloat(ID_Fade, Mathf.SmoothStep(0, 1, fade));
            Apply(left, false, pl, jl);
            Apply(right, true, pr, jr);
        }

        /// <summary>编辑器预览：不跑游戏循环，直接把某一步第 u（0..1）成的姿态摆出来。</summary>
        public bool EditorPose(Game g, string id, float u, bool brief = false)
        {
            briefMode = brief;
            Pose pl = default, pr = default;
            bool ok = Sample(id, u, g, ref pl, ref pr);
            Apply(left, false, ok ? pl : default, jl);
            Apply(right, true, ok ? pr : default, jr);
            if (left) left.Tick(1); if (right) right.Tick(1);
            return ok;
        }

        void Hide()
        {
            if (left) left.overrideJoints = null;
            if (right) right.overrideJoints = null;
        }

        static float Period(string id)
        {
            switch (id)
            {
                case "grab": return 2.8f;
                case "cauldron": case "candy": return 3.6f;
                case "shake": return 3.0f;
                case "throw": return 2.8f;
                case "pull": return 3.0f;
                case "launch": return 3.8f;
                case "pray": return 3.0f;
                case "sweep": return 2.6f;
                case "lamp": return 3.4f;
                case "shield": return 2.8f;
                default: return 0;      // look / residual
            }
        }

        // 从第几成开始淡出（越小淡出越晚）
        static float FadeTail(string id) => id == "throw" || id == "launch" ? 0.22f : 0.15f;

        static float E(float t) => Mathf.SmoothStep(0, 1, Mathf.Clamp01(t));
        static float Seg(float u, float a, float b) => Mathf.Clamp01((u - a) / (b - a));

        // ---------- 各步骤的动作 ----------
        bool Sample(string id, float u, Game g, ref Pose pl, ref Pose pr)
        {
            var head = loop.worldRoot.InverseTransformPoint(cam ? cam.transform.position : new Vector3(0, 1.15f, 0));
            var fwd = cam ? loop.worldRoot.InverseTransformDirection(cam.transform.forward) : Vector3.back;
            fwd.y = 0; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.back;
            var side = Vector3.Cross(Vector3.up, fwd);          // 玩家的右手方向
            var chest = head + fwd * 0.36f + Vector3.down * 0.3f;

            switch (id)
            {
                case "grab": case "cauldron": case "shake": case "throw": case "launch":
                {
                    var toy = LessonToy(g);
                    // 小鬼还在地上 / 太远够不着：教学时先不演示（不然示范手会伸到地上去），等它爬上桌走近；
                    // 开夜介绍时对着桌上一个假想的小鬼演示
                    Vector3 tp;
                    if (toy != null && InReach(head, toy.pos)) tp = toy.pos;
                    else if (briefMode) tp = Imaginary(head, fwd, side);
                    else return false;
                    var grip = tp + Vector3.up * (Layout.TOY_HEIGHT * 0.8f);   // 捏头顶
                    pr = PinchPose(head, grip);
                    var lift = grip + Vector3.up * 0.18f;
                    if (id == "grab")
                    {
                        var hover = grip + Vector3.up * 0.16f - fwd * 0.06f;
                        if (u < 0.35f) { pr.at = Vector3.Lerp(hover, grip, E(u / 0.35f)); pr.shape = HandShape.Lerp(HandShape.Open, HandShape.Relaxed, E(u / 0.35f)); }
                        else if (u < 0.5f) Pinching(ref pr, grip, Seg(u, 0.35f, 0.5f));
                        else { Pinching(ref pr, Vector3.Lerp(grip, lift, E(Seg(u, 0.5f, 0.8f))), 1); }
                        return true;
                    }
                    // 其余四种：先捏起、提起来
                    if (u < 0.15f) { Pinching(ref pr, grip, Seg(u, 0, 0.15f)); return true; }
                    if (u < 0.3f) { Pinching(ref pr, Vector3.Lerp(grip, lift, E(Seg(u, 0.15f, 0.3f))), 1); return true; }
                    if (id == "cauldron")
                    {
                        var mouth = Layout.BOX.pos + Vector3.up * (Layout.BOX.height + 0.16f);
                        DropInto(ref pr, lift, mouth, u);
                    }
                    else if (id == "shake")
                    {
                        float k = Seg(u, 0.3f, 0.8f);
                        float amp = 0.07f * Mathf.Sin(k * Mathf.PI);   // 晃动幅度先大后小，两端不跳
                        Pinching(ref pr, lift + side * amp * Mathf.Sin(k * Mathf.PI * 2 * 5), 1);
                    }
                    else if (id == "throw")
                    {
                        // 往回收一点蓄势 → 猛地往外甩（远离坩埚的一侧），甩到一半松手
                        var wind = lift - fwd * 0.08f + Vector3.up * 0.03f;
                        var away = new Vector3(Mathf.Sign(grip.x - Layout.BOX.x + 1e-3f), 0, 0);
                        var outDir = (fwd * 0.6f + away * 0.5f + Vector3.up * 0.55f).normalized;
                        var far = lift + outDir * 0.42f;
                        if (u < 0.42f) Pinching(ref pr, Vector3.Lerp(lift, wind, E(Seg(u, 0.3f, 0.42f))), 1);
                        else
                        {
                            float k = Seg(u, 0.42f, 0.62f);
                            Pinching(ref pr, Vector3.LerpUnclamped(wind, far, k * k * (2 - k)), 1 - Seg(u, 0.5f, 0.58f));
                        }
                    }
                    else // launch：捏着不动等它发光，再朝铁拱门方向甩出去
                    {
                        var gate = new Vector3(Layout.WINDOW.x, Layout.WINDOW.y, Layout.WINDOW.z);
                        var dir = (gate - lift).normalized;
                        if (u < 0.66f) Pinching(ref pr, lift + Vector3.up * 0.004f * Mathf.Sin(u * 60), 1);   // 轻轻颤一下：在“憋劲”
                        else
                        {
                            float k = Seg(u, 0.66f, 0.82f);
                            Pinching(ref pr, Vector3.LerpUnclamped(lift - dir * 0.05f, lift + dir * 0.36f, k * k * (2 - k)), 1 - Seg(u, 0.74f, 0.8f));
                        }
                    }
                    return true;
                }
                case "candy":
                {
                    Vector3 grip;
                    var candy = RestCandy(g);
                    if (candy != null && InReach(head, candy.pos)) grip = candy.pos + Vector3.up * 0.03f;
                    else
                    {
                        var toy = LessonToy(g);
                        if (toy != null && InReach(head, toy.pos)) grip = toy.pos + Vector3.up * (Layout.TOY_HEIGHT * 0.8f);
                        else if (briefMode) grip = Imaginary(head, fwd, side) + Vector3.up * 0.03f;
                        else return false;
                    }
                    pr = PinchPose(head, grip);
                    var lift = grip + Vector3.up * 0.18f;
                    if (u < 0.15f) { Pinching(ref pr, grip, Seg(u, 0, 0.15f)); return true; }
                    if (u < 0.3f) { Pinching(ref pr, Vector3.Lerp(grip, lift, E(Seg(u, 0.15f, 0.3f))), 1); return true; }
                    DropInto(ref pr, lift, Layout.JAR.pos + Vector3.up * (Layout.JAR.height + 0.14f), u);
                    return true;
                }
                case "pull":
                {
                    var toy = LessonToy(g);
                    if (toy == null && !briefMode) return false;
                    var target = (toy != null ? toy.pos : Layout.RimPoint(0, 0.06f)) + Vector3.up * 0.08f;
                    var basePt = chest + side * 0.12f + Vector3.up * 0.06f;
                    var f = (target - basePt).normalized;
                    pr = new Pose { on = true, anchorTip = true, fingers = f, dorsal = Ortho(Vector3.up, f), shape = HandShape.Open };
                    var reach = basePt + f * 0.14f;
                    if (u < 0.3f) pr.at = Vector3.Lerp(basePt, reach, E(u / 0.3f));
                    else if (u < 0.42f) Pinching(ref pr, reach, Seg(u, 0.3f, 0.42f));
                    else Pinching(ref pr, Vector3.Lerp(reach, basePt - fwd * 0.12f + Vector3.up * 0.05f, E(Seg(u, 0.42f, 0.62f))), 1);
                    return true;
                }
                case "pray":
                {
                    // 双掌从两侧合拢、掌心相对、指尖朝上微微前倾，合住停一会儿
                    float gap = Mathf.Lerp(0.32f, 0.05f, E(Seg(u, 0.05f, 0.4f)));
                    var f = (Vector3.up + fwd * 0.35f).normalized;
                    pr = PalmPose(chest + side * gap * 0.5f, f, side, HandShape.Open);
                    pl = PalmPose(chest - side * gap * 0.5f, f, -side, HandShape.Open);
                    return true;
                }
                case "sweep":
                {
                    // 双手在胸前靠拢（掌心朝外）→ 半秒内向两侧挥开，带一点往前的弧
                    float k = E(Seg(u, 0.3f, 0.52f));
                    float gap = Mathf.Lerp(0.14f, 0.82f, k);
                    var c = chest + fwd * (0.06f * Mathf.Sin(k * Mathf.PI));
                    var f = (Vector3.up + fwd * 0.3f).normalized;
                    pr = PalmPose(c + side * gap * 0.5f, f, (-side - fwd * 0.6f).normalized, HandShape.Open);
                    pl = PalmPose(c - side * gap * 0.5f, f, (side - fwd * 0.6f).normalized, HandShape.Open);
                    return true;
                }
                case "lamp":
                {
                    // 右手举到胸前偏右：从掌心朝内转成手背朝着自己（掌心朝前），五指张开，停住——手套渐渐变成发光的南瓜灯
                    var at = chest + side * 0.12f + fwd * 0.04f + Vector3.up * 0.06f;
                    float k = E(Seg(u, 0.05f, 0.3f));
                    var dorsal = Vector3.Slerp(side, -fwd, k);
                    var sh = HandShape.Open; sh.spread = k;
                    pr = PalmPose(at, (Vector3.up + fwd * 0.15f).normalized, dorsal, sh);
                    pr.lampK = Seg(u, 0.34f, 0.8f);
                    return true;
                }
                case "shield":
                {
                    // 双掌朝下、并排，一起快速往下按
                    float k = Seg(u, 0.32f, 0.48f);
                    var c = chest + Vector3.up * 0.12f + Vector3.down * (0.24f * k * k);
                    pr = PalmPose(c + side * 0.12f, fwd, Vector3.up, HandShape.Open);
                    pl = PalmPose(c - side * 0.12f, fwd, Vector3.up, HandShape.Open);
                    return true;
                }
            }
            return false;
        }

        // 捏起来之后挪到某个口子上方，松手（cauldron / candy 共用；u 从 0.3 开始）
        static void DropInto(ref Pose p, Vector3 from, Vector3 over, float u)
        {
            float k = E(Seg(u, 0.3f, 0.62f));
            var pos = Vector3.Lerp(from, over, k) + Vector3.up * (0.08f * Mathf.Sin(k * Mathf.PI));   // 拱一下，不贴着桌面拖
            Pinching(ref p, pos, 1 - Seg(u, 0.66f, 0.76f));
        }

        static void Pinching(ref Pose p, Vector3 at, float k)
        {
            p.at = at;
            k = Mathf.Clamp01(k);
            p.shape = HandShape.Lerp(HandShape.Relaxed, HandShape.Pinch, E(k));
            p.pinch = k > 0.85f;
        }

        // 从上往下捏：指尖朝前下方，手背朝上
        static Pose PinchPose(Vector3 head, Vector3 grip)
        {
            var flat = grip - head; flat.y = 0;
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.back;
            var f = (flat * 0.55f + Vector3.down * 0.85f).normalized;
            return new Pose { on = true, anchorTip = true, at = grip, fingers = f, dorsal = Ortho(Vector3.up, f), shape = HandShape.Relaxed };
        }

        static Pose PalmPose(Vector3 at, Vector3 fingers, Vector3 dorsal, HandShape s) =>
            new Pose { on = true, anchorTip = false, at = at, fingers = fingers.normalized, dorsal = Ortho(dorsal, fingers), shape = s };

        static Vector3 Ortho(Vector3 v, Vector3 f)
        {
            var o = v - f * Vector3.Dot(v, f);
            return o.sqrMagnitude > 1e-5f ? o.normalized : Vector3.Cross(f, Vector3.right).normalized;
        }

        void Apply(HandView hv, bool isRight, Pose p, Vector3[] buf)
        {
            if (!hv) return;
            if (!p.on) { hv.overrideJoints = null; return; }
            var rot = Quaternion.LookRotation(p.fingers, p.dorsal);
            // 先在原点摆一遍，量出锚点相对手腕的偏移，再把手腕挪到让锚点正好落在目标上
            HandPoses.Build(isRight, Vector3.zero, Quaternion.identity, p.shape, tmp);
            var anchor = p.anchorTip ? (tmp[HandJoints.ThumbTip] + tmp[HandJoints.IndexTip]) * 0.5f : tmp[HandJoints.Palm];
            HandPoses.Build(isRight, p.at - rot * anchor, rot, p.shape, buf);
            // 规则层坐标 → 手套所在节点的本地坐标
            for (int i = 0; i < buf.Length; i++) buf[i] = hv.transform.InverseTransformPoint(loop.worldRoot.TransformPoint(buf[i]));
            hv.overrideJoints = buf;
            hv.overridePinch = p.pinch;
            hv.overrideLampK = p.lampK;
        }

        static bool InReach(Vector3 head, Vector3 p)
        {
            var d = p - head; d.y = 0;
            return p.y > Layout.TABLE.y - 0.05f && d.magnitude < 1.05f;
        }

        // 开夜介绍时场上还没有小鬼：假想它站在玩家面前偏左的空桌面上（糖桶在正前方会挡住，右手边是坩埚）
        static Vector3 Imaginary(Vector3 head, Vector3 fwd, Vector3 side)
        {
            var p = head + fwd * 0.42f - side * 0.2f;
            return new Vector3(p.x, Layout.TABLE.y, p.z);
        }

        static Toy LessonToy(Game g)
        {
            var L = g.lesson;
            if (L == null) return null;
            for (int i = L.toys.Count - 1; i >= 0; i--)
            {
                var t = L.toys[i];
                if (t.state != ToyStates.Gone && t.state != ToyStates.Boxed && t.state != ToyStates.Launched) return t;
            }
            return null;
        }

        static Candy RestCandy(Game g)
        {
            foreach (var c in g.candies) if (c.state == CandyStates.Rest) return c;
            return null;
        }
    }
}
