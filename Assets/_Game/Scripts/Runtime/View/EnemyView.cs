// 敌人视图：跟随规则层 Toy 的位置/朝向/状态，用程序化动画让单网格模型“活”起来：
//   走路：一步一颠（挤压拉伸 + 左右摇摆 + 前倾），步频跟 walkPhase
//   被盯住：瞬间压扁回弹 + 结霜（着色器 _Frost）+ 定格一个随机俏皮姿势（倾斜/扭头）
//   解冻预警：高频抖动
//   被拎着：四肢乱蹬（顶点扭动 _Wiggle）+ 晃动幅度跟手速
//   晕：原地转圈 + 头顶星星
//   蓄力：金光脉动（_Charge）+ 星尾
//   黑暗里：剪影（_Silhouette），只剩发光的眼睛
// 敌人模型都是单网格（Tripo P2），所以动作靠整体变换 + 着色器顶点扭动，不依赖骨骼。
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public class EnemyView : MonoBehaviour
    {
        public int toyId;
        public string kind;
        Transform body;          // 姿态根（挤压拉伸、倾斜作用在这里；本地 +Z = 前进方向）
        Transform model;         // 模型（Tripo 模型正面朝 -X，转 +90° 让正面对准 +Z）
        float yawDeg; bool yawInit;
        LineRenderer silk;       // 吊丝蜘蛛的丝
        float boxT = -1; Vector3 boxFrom;   // 入锅动画：从松手处弧线飞进锅口、沉进魔药
        Renderer[] rends;
        MaterialPropertyBlock mpb;
        float frostK, litK = 1, squash, shakeT, chargeVis, spin;
        bool wasFrozen;
        Vector3 pose;            // 定格姿势：x=前后倾 y=扭头 z=侧倾（度）
        float baseScale = 1;
        GameObject dizzyRing;
        TrailRenderer trail;
        ParticleSystem frostPs;

        static readonly int ID_Frost = Shader.PropertyToID("_Frost");
        static readonly int ID_Charge = Shader.PropertyToID("_Charge");
        static readonly int ID_Sil = Shader.PropertyToID("_Silhouette");
        static readonly int ID_Wiggle = Shader.PropertyToID("_Wiggle");
        static readonly int ID_Phase = Shader.PropertyToID("_WigglePhase");
        static readonly int ID_Stride = Shader.PropertyToID("_Stride"), ID_StridePh = Shader.PropertyToID("_StridePhase"), ID_StrideHip = Shader.PropertyToID("_StrideHip"),
                            ID_StrideFwd = Shader.PropertyToID("_StrideFwd"), ID_StrideQuad = Shader.PropertyToID("_StrideQuad");
        // 迈腿：哪些模型有腿、髋在身高的多少、是不是四足（飞的、飘的、蜘蛛不迈腿）
        bool hasLegs, quad; float hipY, strideK; Vector4 strideFwd;
        static readonly int ID_Flap = Shader.PropertyToID("_Flap"), ID_FlapPh = Shader.PropertyToID("_FlapPhase"), ID_FlapBias = Shader.PropertyToID("_FlapBias");
        float flapPh, flapK;
        Vector3 heldPrev; Vector2 swing, swingVel;
        Transform carry; SpriteRenderer badge; float badgeBase, badgeY, badgeK;
        Vector3 carryPos;        // 捧糖的位置（身前、胸口以下）

        /// <summary>两只捧糖的小手：从身体两侧伸到糖的两边，颜色按敌人种类</summary>
        public void Init(Toy t, GameObject prefab, Material dizzyMat, Material trailMat, GameObject candyPrefab = null, Sprite candyIcon = null)
        {
            toyId = t.id; kind = t.kind;
            body = new GameObject("Pose").transform;
            body.SetParent(transform, false);
            model = Instantiate(prefab, body).transform;
            model.localPosition = Vector3.zero;
            float faceYaw = TrickOrFreeze.View.ModelFacing.Yaw(prefab.name);
            model.localRotation = Quaternion.Euler(0, faceYaw, 0);   // 正面对准行进方向（个别模型正面不正好朝 -X）
            {
                float hipFrac = kind == KindIds.Skeleton ? 0.38f : kind == KindIds.Pumpkin ? 0.36f : kind == KindIds.Cat ? 0.42f : kind == KindIds.Frank ? 0.3f : 0f;
                hasLegs = hipFrac > 0 && t.Kind.mover == Movers.Walk;
                quad = kind == KindIds.Cat;
                hipY = t.Kind.height * hipFrac;
                float yr = faceYaw * Mathf.Deg2Rad;
                strideFwd = new Vector4(-Mathf.Sin(yr), Mathf.Cos(yr), 0, 0);   // 模型空间里的正面方向（x, z）
            }
            baseScale = kind == KindIds.Ghostling ? 0.5f : 1f;   // 小幽灵用大幽灵模型缩小
            body.localScale = Vector3.one * baseScale;
            rends = GetComponentsInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();
            var h = t.Kind.height;
            // 晕眩星环
            dizzyRing = new GameObject("Dizzy");
            dizzyRing.transform.SetParent(transform, false);
            dizzyRing.transform.localPosition = new Vector3(0, h * 1.08f, 0);
            for (int i = 0; i < 3; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                if (Application.isPlaying) Destroy(s.GetComponent<Collider>()); else DestroyImmediate(s.GetComponent<Collider>());
                s.transform.SetParent(dizzyRing.transform, false);
                float a = i / 3f * Mathf.PI * 2;
                s.transform.localPosition = new Vector3(Mathf.Cos(a) * h * 0.25f, 0, Mathf.Sin(a) * h * 0.25f);
                s.transform.localScale = Vector3.one * h * 0.09f;
                s.GetComponent<Renderer>().sharedMaterial = dizzyMat;
            }
            dizzyRing.SetActive(false);
            // 抱着糖：怀里一颗放大的糖 + 头顶一个会跳的糖果图标（10-28 实机反馈：抱没抱糖看不出来）
            if (candyPrefab)
            {
                // 10-30 用户：糖不要贴在脸上；10-31 用户：也不要手，糖悬在肚子前面、留一点空隙。
                // 位置按模型真实大小算：身前（模型前缘 + 糖半径 + 空隙）；蝙蝠吊在身子下面
                var mb = new Bounds(body.position, Vector3.zero); bool mbInit = false;
                foreach (var r in rends) { if (!mbInit) { mb = r.bounds; mbInit = true; } else mb.Encapsulate(r.bounds); }
                // 身体厚度取“朝前方向”的包围盒（翅膀、手臂主要往两侧伸，不影响这个方向）
                var fw = body.forward;
                float front = (Mathf.Abs(fw.x) * mb.extents.x + Mathf.Abs(fw.y) * mb.extents.y + Mathf.Abs(fw.z) * mb.extents.z) / Mathf.Max(1e-4f, body.lossyScale.x);
                // 捧糖的东西挂在 body 下，body 有 baseScale（小幽灵是大幽灵模型缩一半）：位置、大小都按模型自身尺寸算，
                // 否则小幽灵的糖和小手缩进身体里看不见（10-30 用户：小幽灵抱糖没长出手）
                float hWorld = h;
                h = h / Mathf.Max(0.05f, baseScale);
                front = Mathf.Clamp(front, h * 0.18f, h * 0.5f);
                bool flying = t.Kind.mover == Movers.Fly;
                // 10-31 用户：不要手，糖悬在肚子前面、不贴身子，留一点空隙。高度：肚子（黑猫、蜘蛛脸贴地，放在身前低处）
                float carryY = kind == KindIds.Cat || kind == KindIds.Spider ? 0.12f : kind == KindIds.Pumpkin ? 0.28f : kind == KindIds.Skeleton ? 0.32f : 0.38f;
                var holder = new GameObject("CarryCandy").transform; holder.SetParent(body, false);
                holder.localPosition = new Vector3(0, h * carryY, 0);
                var candy = Instantiate(candyPrefab, holder).transform; candy.localPosition = Vector3.zero; candy.localRotation = Quaternion.Euler(0, 0, 25f);
                candy.localScale = Vector3.one * Mathf.Clamp(h * 7f, 1.1f, 2.2f);
                foreach (var r in candy.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // 糖在 body 本地坐标里占多大（半宽、半高、半厚）
                var cb = new Bounds(holder.localPosition, Vector3.zero); bool cbInit = false;
                foreach (var r in candy.GetComponentsInChildren<Renderer>())
                {
                    var wb = r.bounds;
                    for (int ci = 0; ci < 8; ci++)
                    {
                        var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((ci & 1) != 0 ? 1 : -1, (ci & 2) != 0 ? 1 : -1, (ci & 4) != 0 ? 1 : -1));
                        var lp = body.InverseTransformPoint(corner);
                        if (!cbInit) { cb = new Bounds(lp, Vector3.zero); cbInit = true; } else cb.Encapsulate(lp);
                    }
                }
                float gap = h * 0.04f;
                // 身体前缘：在糖要占的那块高度、宽度范围里（再各加一点空隙），模型顶点最靠前的位置——脸、手、衣角往前凸都算进去，
                // 糖放在它前面就碰不到（包围盒会把整只的最前端都算进去，糖就离太远）。
                // 敌人网格在包里不可读：编辑器里量好存进 GameLoop.KindPrefab.bellyFront，运行时直接用
                float belly = bellyOverride > 0 ? bellyOverride : MeasureFront(body, cb.center.y, cb.extents.y + gap, cb.extents.x + gap);
                measuredBelly = belly;
                if (belly > 0) front = belly;
                float candyHalf = cb.extents.z;
                carryPos = flying ? new Vector3(0, -h * 0.35f, h * 0.05f) : new Vector3(0, h * carryY, front + candyHalf + gap);
                candy.localPosition = Vector3.zero;
                holder.localPosition = carryPos;
                carry = holder;
                carry.gameObject.SetActive(false);
                h = hWorld;                                                     // 下面的头顶糖果图标挂在根节点上，用真实身高
            }
            if (candyIcon)
            {
                var go = new GameObject("CandyBadge");
                go.transform.SetParent(transform, false);
                badge = go.AddComponent<SpriteRenderer>();
                badge.sprite = candyIcon;
                badge.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                badgeBase = 0.085f / Mathf.Max(1e-4f, candyIcon.bounds.size.x);
                badgeY = h * 1.4f;
                go.SetActive(false);
            }
            if (t.Kind.mover == Movers.Drop)
            {
                silk = new GameObject("Silk").AddComponent<LineRenderer>();
                silk.transform.SetParent(transform.parent, false);
                silk.useWorldSpace = false; silk.positionCount = 2; silk.widthMultiplier = 0.0035f;
                silk.sharedMaterial = trailMat; silk.startColor = silk.endColor = new Color(0.85f, 0.95f, 1f, 0.9f);
                silk.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 星尾（蓄力扔出时）
            trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.6f; trail.startWidth = h * 0.5f; trail.endWidth = 0;
            trail.sharedMaterial = trailMat; trail.emitting = false;
            trail.colorGradient = new Gradient
            {
                colorKeys = new[] { new GradientColorKey(new Color(1, 0.92f, 0.6f), 0), new GradientColorKey(new Color(1, 0.6f, 0.9f), 1) },
                alphaKeys = new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) },
            };
        }

        void OnDestroy() { if (silk) Destroy(silk.gameObject); }

        /// <summary>编辑器工具用：立刻删掉挂在场景根上的蛛丝（编辑器里删物体不会调 OnDestroy，蛛丝会留在场景里）</summary>
        public void EditorDispose() { if (silk) DestroyImmediate(silk.gameObject); }

        [System.NonSerialized] public float bellyOverride = -1f;   // GameLoop 给：编辑器量好的肚子前缘（模型本地单位）
        [System.NonSerialized] public float measuredBelly = -1f;   // 编辑器里量出来的（GameplayBuilder 读走存起来）

        /// <summary>高度在 cy±halfY、左右在 ±halfX 范围里的模型顶点，最靠前的位置（body 本地 z）；网格不可读时返回 -1</summary>
        static float MeasureFront(Transform body, float cy, float halfY, float halfX)
        {
            float best = -1f; bool any = false;
            foreach (var mf in body.GetComponentsInChildren<MeshFilter>(true))
            {
                var m = mf.sharedMesh;
                if (!m || (!m.isReadable && !Application.isEditor)) continue;
                if (mf.name.StartsWith("LOD1") || mf.name.StartsWith("LOD2") || (mf.transform.parent && (mf.transform.parent.name == "LOD1" || mf.transform.parent.name == "LOD2"))) continue;
                if (mf.GetComponentInParent<EnemyView>() == null) continue;
                if (mf.GetComponentInParent<Transform>() && mf.transform.parent && mf.transform.parent.name == "CarryCandy") continue;
                bool inCandy = false; for (var q = mf.transform; q && q != body; q = q.parent) if (q.name == "CarryCandy") inCandy = true;
                if (inCandy) continue;
                Vector3[] vs;
                try { vs = m.vertices; } catch { continue; }
                var toBody = body.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v in vs)
                {
                    var p = toBody.MultiplyPoint3x4(v);
                    if (Mathf.Abs(p.y - cy) > halfY || Mathf.Abs(p.x) > halfX) continue;
                    if (!any || p.z > best) { best = p.z; any = true; }
                }
            }
            return any ? best : -1f;
        }

        public void Sync(Toy t, float dt, float time, Game g)
        {
            var K = t.Kind;
            if (t.state == ToyStates.Boxed && t.rimWait)
            {
                // 10-31：坩埚满了，坐在锅沿上排队——跳到锅沿、面朝玩家、着急地左右晃（空出一格后走下面的入锅动画）
                boxT = -1;
                transform.localPosition = Vector3.Lerp(transform.localPosition, t.pos, 1f - Mathf.Exp(-dt * 14f));
                var toSeat = new Vector3(0f - t.pos.x, 0f, 0.12f - t.pos.z);
                float seatYaw = Mathf.Atan2(toSeat.x, toSeat.z) * Mathf.Rad2Deg;
                if (!yawInit) { yawDeg = seatYaw; yawInit = true; }
                yawDeg = Mathf.MoveTowardsAngle(yawDeg, seatYaw, 300f * dt);
                transform.localRotation = Quaternion.Euler(0, yawDeg, 0);
                body.localScale = Vector3.one * baseScale;
                body.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(time * 9f + t.id) * 8f);
                foreach (var r in rends) r.enabled = true;
                if (silk) silk.enabled = false;
                if (carry && carry.gameObject.activeSelf) carry.gameObject.SetActive(false);
                if (badge && badge.gameObject.activeSelf) badge.gameObject.SetActive(false);
                return;
            }
            if (t.state == ToyStates.Boxed)
            {
                if (boxT < 0) { boxT = 0; boxFrom = transform.localPosition; }
                boxT += dt / 0.5f;
                float bk = Mathf.Clamp01(boxT);
                var mouth = Layout.BOX.pos + Vector3.up * (Layout.BOX.height + 0.03f);
                var sink = Layout.BOX.pos + Vector3.up * (Layout.BOX.height * 0.4f);
                // 前 45%：抛物线飞到锅口正上方；后 55%：一头栽进锅口、翻着跟头沉进魔药、缩小消失
                Vector3 p;
                if (bk < 0.45f) { float u = bk / 0.45f; p = Vector3.Lerp(boxFrom, mouth, u); p.y += Mathf.Sin(u * Mathf.PI) * Mathf.Max(0.06f, Vector3.Distance(boxFrom, mouth) * 0.35f); }
                else { float u = (bk - 0.45f) / 0.55f; p = Vector3.Lerp(mouth, sink, u * u); }
                transform.localPosition = p;
                float sinkK = bk < 0.45f ? 0f : (bk - 0.45f) / 0.55f;
                body.localScale = Vector3.one * baseScale * Mathf.Lerp(1f, 0.1f, sinkK * sinkK);
                body.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 200f, sinkK) + Mathf.Sin(bk * 18) * 10f * (1 - sinkK), bk * 220f, 0);   // 头朝下栽进去
                foreach (var r in rends) r.enabled = bk < 1f;
                if (silk) silk.enabled = false;
                if (carry && carry.gameObject.activeSelf) carry.gameObject.SetActive(false);   // 入锅时怀里的糖不跟着翻（10-30 审查）
                if (badge && badge.gameObject.activeSelf) badge.gameObject.SetActive(false);
                return;
            }
            boxT = -1;
            transform.localPosition = t.pos;
            bool climbing = false;
            float slopeDeg = 0;
            if ((t.state == ToyStates.Walk || t.state == ToyStates.Flee) && K.mover == Movers.Walk && t.path != null && t.seg + 1 < t.path.Count)
            {
                var a = t.path[t.seg]; var b = t.path[t.seg + 1];
                climbing = Game.IsClimb(a, b);
                slopeDeg = Mathf.Atan2(b.y - a.y, Vec.DistXZ(a, b) + 1e-4f) * Mathf.Rad2Deg;
            }
            // 朝向：规则层 yaw = atan2(dx, dz)（行进方向）。限速平滑转身（最快 240°/s，先快后慢），不会原地抽搐式甩头；
            //   下梯子时背对行进方向（面朝梯子倒着下）；被定住、被拎着时不转（被拎时转过来面对玩家）。
            bool frozenNow = t.frozen && t.state != ToyStates.Held;
            float targetYaw = t.yaw * Mathf.Rad2Deg;   // 上藤下藤都面朝行进方向（藤是坡道，不是竖梯）
            bool heldNow = t.state == ToyStates.Held || t.state == ToyStates.Pulled;
            if (heldNow && Camera.main)
            {
                var toCam = transform.parent.InverseTransformPoint(Camera.main.transform.position) - t.pos;
                if (toCam.x * toCam.x + toCam.z * toCam.z > 1e-4f) targetYaw = Mathf.Atan2(toCam.x, toCam.z) * Mathf.Rad2Deg;
            }
            if (!yawInit) { yawDeg = targetYaw; yawInit = true; }
            if (!frozenNow || heldNow)
            {
                float diff = Mathf.DeltaAngle(yawDeg, targetYaw);
                float step = Mathf.Min(Mathf.Abs(diff), Mathf.Max(60f, Mathf.Abs(diff) * 12f) * dt, 600f * dt);   // 10-29：转身加快（拐角处不再横着走一段）
                yawDeg += Mathf.Sign(diff) * step;
            }
            transform.localRotation = Quaternion.Euler(0, yawDeg, 0);
            if (silk)
            {
                bool hang = t.state != ToyStates.Gone && t.state != ToyStates.Boxed && t.state != ToyStates.Held && t.state != ToyStates.Pulled
                            && t.state != ToyStates.Falling && t.state != ToyStates.Launched && t.state != ToyStates.Stunned && t.spawn != null;
                silk.enabled = hang;
                if (hang) { silk.SetPosition(0, t.spawn.pos); silk.SetPosition(1, t.pos + Vector3.up * K.height * 0.9f); }
            }

            bool frozen = t.frozen && (t.state == ToyStates.Walk || t.state == ToyStates.Steal || t.state == ToyStates.Flee || t.state == ToyStates.Stunned || t.state == ToyStates.Glide);
            if (frozen && !wasFrozen)
            {
                squash = 1;
                pose = new Vector3(Random.Range(-14f, 14f), Random.Range(-35f, 35f), Random.Range(-16f, 16f));
            }
            wasFrozen = frozen;
            frostK = Mathf.MoveTowards(frostK, frozen ? 1 : 0, dt * (frozen ? 8 : 3));
            litK = Mathf.MoveTowards(litK, t.lit ? 1 : 0, dt * 3);
            squash = Mathf.Max(0, squash - dt * 5);

            float ph = t.walkPhase;
            bool walking = (t.state == ToyStates.Walk || t.state == ToyStates.Flee) && !frozen;
            bool held = t.state == ToyStates.Held || t.state == ToyStates.Pulled;
            if (!held) { heldPrev = t.pos; swing = swingVel = Vector2.zero; }     // 刚拎起的第一帧不会因为位置跳变猛甩
            bool falling = t.state == ToyStates.Falling;
            bool dizzy = t.dizzy > 0 && t.state == ToyStates.Stunned;
            bool launched = t.state == ToyStates.Launched;

            // ---- 姿态 ----
            Vector3 euler = Vector3.zero;
            Vector3 scl = Vector3.one;
            float bob = 0;
            if (walking && Mathf.Abs(slopeDeg) > 3f && K.mover == Movers.Walk)
            {
                // 顺着藤的坡度走（10-29 用户：爬藤要顺着坡度，脚不能插进藤里）：
                // 脚底贴着坡面（身体垂直于藤面），只留一点点往前倾的使劲感；前倾多少，身子就抬高多少，脚尖不会陷进去
                float rel = 8f + Mathf.Abs(slopeDeg) * 0.1f;                       // 相对坡面的前倾
                euler = new Vector3(-slopeDeg + rel, 0, Mathf.Sin(ph) * 4);
                bob = K.radius * Mathf.Sin(rel * Mathf.Deg2Rad) + Mathf.Abs(Mathf.Cos(ph)) * K.height * 0.02f;
            }
            else if (walking)
            {
                // 一步一颠：幅度收小，近距离看不晃眼
                // 有腿的：两腿交错经过时身体最高（|cos|），迈到最大时最低，配合着色器里的迈腿
                float step = hasLegs ? Mathf.Abs(Mathf.Cos(ph)) : Mathf.Abs(Mathf.Sin(ph));
                bob = step * K.height * (K.mover == Movers.Fly ? 0.0f : hasLegs ? 0.035f : 0.08f);
                euler = new Vector3(6 + (t.hasCandy ? 6 : 0), 0, Mathf.Sin(ph) * (hasLegs ? 4 : 6));
                scl = new Vector3(1 + (1 - step) * 0.03f, 1 - (1 - step) * 0.035f, 1 + (1 - step) * 0.03f);
                if (K.freeze == FreezeRules.Inverse && t.gazed) euler.x = 22;   // 南瓜头被看着：埋头狂奔
                if (K.mover == Movers.Fly) euler = new Vector3(4, 0, Mathf.Sin(ph * 0.5f) * 8);   // 飞行：轻轻侧摆，不上下颠
            }
            else if (frozen)
            {
                euler = pose;
                // 解冻预警：抖
                bool warn = !t.gazed && t.residual > 0 && t.residual <= (g != null ? g.cfg.freeze.warnSec : 0.4);   // 10-31：读配置，不再写死
                if (warn) { shakeT += dt; euler.z += Mathf.Sin(shakeT * 70) * 7; } else shakeT = 0;
            }
            else if (t.state == ToyStates.Steal)
            {
                euler = new Vector3(10 + Mathf.Sin(ph * 2) * 4, 0, 0);       // 探身向糖桶（11-04：前倾从 18° 收到 10°，配合站位外移，脸不穿进桶壁）
                bob = Mathf.Abs(Mathf.Sin(ph * 2)) * K.height * 0.05f;
            }
            else if (held)
            {
                // 被拎着：像挂在手指下的钟摆——跟着手移动的反方向摆起（有惯性、会慢慢停），
                // 只留很轻的蹬腿；玩家真去晃它（wiggle）时摆幅才变大。
                // 以前是 6Hz、±8~38° 的高频抖动叠全身扭动，实机看像在抽搐（10-28 反馈）。
                var v = dt > 1e-4f ? (t.pos - heldPrev) / dt : Vector3.zero;
                heldPrev = t.pos;
                var lv = transform.parent ? transform.parent.TransformDirection(v) : v;
                lv = transform.InverseTransformDirection(lv);                          // 转到身体朝向（拎起时面朝玩家）
                float w = t.shake != null ? (float)t.shake.wiggle : 0;
                float lim = 14f + w * 16f;
                var target = new Vector2(Mathf.Clamp(-lv.z * 30f, -lim, lim), Mathf.Clamp(lv.x * 30f, -lim, lim));
                var acc = (target - swing) * 40f - swingVel * 9f;                      // 弹簧阻尼：约 1 Hz 的摆、几下就停
                swingVel += acc * dt; swing += swingVel * dt;
                euler = new Vector3(swing.x, Mathf.Sin(time * 1.3f) * 6f, swing.y + Mathf.Sin(time * 2.4f) * 2.5f);
            }
            else if (falling)
            {
                euler = new Vector3(Mathf.Sin(time * 11) * 25, time * 400 % 360, Mathf.Cos(time * 9) * 25);
            }
            else if (dizzy)
            {
                spin += dt * 360;
                euler = new Vector3(-12, spin, Mathf.Sin(time * 6) * 12);
                scl = new Vector3(1.05f, 0.88f, 1.05f);
            }
            else if (t.state == ToyStates.Stunned)
            {
                euler = new Vector3(0, 0, Mathf.Sin(time * 9) * 14);
            }
            else if (t.state == ToyStates.Glide)
            {
                euler = new Vector3(10, Mathf.Sin(time * 1.6f) * 25, Mathf.Sin(time * 1.6f) * 18);
            }
            else if (t.state == ToyStates.Yawn)
            {
                scl = new Vector3(1.05f, 0.92f + Mathf.Sin(time * 2) * 0.04f, 1.05f);
            }
            if (launched) euler = new Vector3(0, time * 900 % 360, time * 700 % 360);
            bool blowing = launched && t.launch != null && t.launch.blow;
            if (blowing) scl *= Mathf.Lerp(1f, 0.02f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.45f * t.launch.endK, t.launch.endK, (float)t.launch.t)));   // 被风卷走：越飞越小，消失在远处
            else if (launched && t.launch != null) scl *= Mathf.Lerp(1f, 0.3f, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.7f * t.launch.endK, t.launch.endK, (float)t.launch.t)));   // 10-31 变星星：最后一段转着缩成一团光，接着原地冒出五角星

            // 压扁回弹（定住瞬间）
            scl = Vector3.Scale(scl, new Vector3(1 + squash * 0.2f, 1 - squash * 0.18f, 1 + squash * 0.2f));
            // 缝合熊单手拖：使劲挣
            if (t.dragged) euler.z += Mathf.Sin(time * 16) * 9;

            float k = Mathf.Min(1, dt * (frozen ? 30 : 12));
            body.localRotation = Quaternion.Slerp(body.localRotation, Quaternion.Euler(euler), k);
            body.localScale = blowing ? scl * baseScale : Vector3.Lerp(body.localScale, scl * baseScale, k);
            body.localPosition = new Vector3(0, bob, 0);

            // ---- 着色器参数 ----
            float chargeK = t.state == ToyStates.Held ? Mathf.Clamp01((float)(t.chargeT / g.cfg.launch.chargeSec)) : launched && !blowing ? 1 : 0;
            float pulse = (t.charged || (launched && !blowing)) ? 0.75f + Mathf.Sin(time * 18) * 0.25f : chargeK * 0.45f;
            chargeVis = Mathf.Lerp(chargeVis, pulse, Mathf.Min(1, dt * 12));
            float wiggle = held ? 0.3f : walking ? (hasLegs ? 0.06f : 0.35f) : 0;  // 被拎着只轻轻蹬腿；有腿的走路靠迈腿，不再整身扭
            strideK = Mathf.MoveTowards(strideK, hasLegs && walking ? 1f : 0f, dt * 6f);
            // 蝙蝠：不用全身扭动，用真正的扇翅（ToonLit _Flap）。飞着扇、被拎着乱扇；被盯住飘落时翅膀张开慢慢扇；定住时翅膀停住
            bool bat = K.mover == Movers.Fly;
            float flapAmp = 0, flapBias = 0.12f;
            if (bat)
            {
                bool gliding = t.state == ToyStates.Glide && !frozen;
                flapAmp = frozen ? 0 : held ? 1.1f : gliding ? 0.35f : launched ? 0 : 0.85f;
                if (gliding) flapBias = 0.3f;
                flapPh += dt * Mathf.PI * 2 * (held ? 9f : gliding ? 2.2f : 6.5f) * (frozen ? 0 : 1);
                flapK = Mathf.MoveTowards(flapK, flapAmp, dt * 4);
                wiggle = 0;
            }
            foreach (var r in rends)
            {
                r.GetPropertyBlock(mpb);
                mpb.SetFloat(ID_Frost, frostK);
                mpb.SetFloat(ID_Charge, chargeVis);
                mpb.SetFloat(ID_Sil, 1 - litK);
                mpb.SetFloat(ID_Wiggle, wiggle);
                mpb.SetFloat(ID_Phase, ph + toyId);
                mpb.SetFloat(ID_Stride, strideK * (quad ? 0.8f : 1f));
                mpb.SetFloat(ID_StridePh, ph);
                mpb.SetFloat(ID_StrideHip, hipY);
                mpb.SetVector(ID_StrideFwd, strideFwd);
                mpb.SetFloat(ID_StrideQuad, quad ? 1 : 0);
                if (bat) { mpb.SetFloat(ID_Flap, flapK); mpb.SetFloat(ID_FlapPh, flapPh); mpb.SetFloat(ID_FlapBias, flapBias); }
                r.SetPropertyBlock(mpb);
            }

            // 抱糖提示
            bool carrying = t.candyCount > 0 && t.state != ToyStates.Gone && t.state != ToyStates.Boxed && t.state != ToyStates.Launched;
            if (carry)
            {
                if (carry.gameObject.activeSelf != carrying) carry.gameObject.SetActive(carrying);
                if (carrying) carry.localPosition = carryPos + Vector3.up * (K.height * 0.015f * Mathf.Sin(time * 6f));
            }
            if (badge)
            {
                badgeK = Mathf.MoveTowards(badgeK, carrying ? 1 : 0, dt * 5);
                bool on = badgeK > 0.01f;
                if (badge.gameObject.activeSelf != on) badge.gameObject.SetActive(on);
                if (on)
                {
                    var bt = badge.transform;
                    bt.localPosition = new Vector3(0, badgeY + Mathf.Abs(Mathf.Sin(time * 3.2f)) * 0.025f, 0);   // 一下一下地跳
                    if (Camera.main) bt.rotation = Camera.main.transform.rotation;
                    float more = 1 + 0.15f * Mathf.Min(3, t.candyCount - 1);                                      // 糖越多图标越大
                    bt.localScale = Vector3.one * badgeBase * more * Mathf.SmoothStep(0, 1, badgeK) * (1 + 0.08f * Mathf.Sin(time * 6.4f));
                }
            }
            dizzyRing.SetActive(t.dizzy > 0 && t.state != ToyStates.Gone);
            if (dizzyRing.activeSelf) dizzyRing.transform.Rotate(0, dt * 300, 0, Space.Self);
            trail.emitting = launched;
            bool visible = t.state != ToyStates.Gone && t.state != ToyStates.Boxed;
            foreach (var r in rends) r.enabled = visible;
        }
    }
}
