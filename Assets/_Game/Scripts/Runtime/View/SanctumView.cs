// 圣坛桌面表现：把规则层的状态映射到桌上的实物——
//   糖桶里的独立糖果（Candy_*）：按剩余糖数从上往下逐颗消失 / 放回时重新出现（带弹出）
//   糖桶：被偷时跳一下、放回糖时开心地鼓一下；护桶技能时罩一层紫光
//   坩埚汤药：装得越多越亮（_Fill），消化/拍锅沿时沸腾加快（_Boil）
//   坩埚容量灯：锅身正对玩家的一排嵌在锅壁上的圆灯（Slot_*，GameplayBuilder.BuildCauldronSlots 贴着锅身弧面放好），
//              有几格亮几盏（格数随章节、糖果铺升级变化）。空格薄荷绿微光，占用变紫亮，正在消化的格子呼吸闪烁
//   糖桶可以提起来（跟着手走、跟着手腕转，放下就停在那里）；坩埚可以挪位置（跟着规则层 BOX 走）；糖堆随剩余糖数升降，糖都贴在糖堆上
//   消化提示牌：锅口上方一块小圆牌，进度环显示当前这一只消化了多少、中间写“已用/总格数”；满了变红抖一下，空锅时隐去
using System.Collections.Generic;
using TMPro;
using TrickOrFreeze.Core;
using TrickOrFreeze.Input;
using TrickOrFreeze.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TrickOrFreeze.View
{
    public class SanctumView : MonoBehaviour
    {
        public GameLoop loop;
        public Transform bucket;          // Desk_Bucket 根
        public Material glowMat;          // 10-31 糖桶微光：柔光圆片（叠加）
        public Renderer potion;           // Desk_Cauldron/Potion
        public Light bucketLight;         // 糖桶上方暖光（护桶时变紫）
        public Transform cauldron;        // Desk_Cauldron 根
        public TMP_FontAsset font;        // 提示牌数字字体

        readonly List<Renderer> gems = new List<Renderer>();
        Transform bed; Vector3 bedScale; float bedK = 1, bedBaseY;
        readonly List<float> candyY = new List<float>();
        Vector3 bucketHome; float liftK; Vector3 bucketVel;
        RectTransform tag; CanvasGroup tagGroup; Image ring, tagBack; TextMeshProUGUI tagText, tagLabel;
        float tagVis, fullFlash, shake;
        static readonly int ID_Base = Shader.PropertyToID("_BaseColor"), ID_Em = Shader.PropertyToID("_EmissionColor");

        readonly List<Transform> candies = new List<Transform>();
        readonly List<Vector3> candyScale = new List<Vector3>();
        float[] pop;
        float bounce, joy, boil = 1;
        Vector3 bucketScale;
        MaterialPropertyBlock mpb;
        static readonly int ID_Fill = Shader.PropertyToID("_Fill"), ID_Boil = Shader.PropertyToID("_Boil");
        // 提在手里时跟着手腕转（10-30 用户：糖桶和锅拿起来要能转着看）：记下捏住那一刻的手、物体朝向，之后按手的相对转动转物体，绕捏住的点转
        string carryB, carryP; bool rotOkB, rotOkP;
        Quaternion hand0B, hand0P, obj0B, relB = Quaternion.identity, relP = Quaternion.identity;
        Vector3 bucketVelW;
        Renderer[] bucketRs, potRs;

        void Start()
        {
            mpb = new MaterialPropertyBlock();
            if (bucket)
            {
                bucketScale = bucket.localScale;
                foreach (Transform c in bucket) if (c.name.StartsWith("Candy_")) candies.Add(c);
                bed = bucket.Find("Bed");
                if (bed) { bedScale = bed.localScale; bedBaseY = bed.localPosition.y; }
                bucketHome = bucket.localPosition;
                // 10-31 糖桶底部添加一圈淡暖色光晕（Game.JAR_GLOW = 0.26f，黑猫进入范围会现形）
                AddBucketGlow();
                // 先拿走最上面的：按高度从低到高排，可见数 n 时显示前 n 颗
                candies.Sort((a, b) => a.position.y.CompareTo(b.position.y));
                foreach (var c in candies) { candyScale.Add(c.localScale); candyY.Add(c.localPosition.y); }
                pop = new float[candies.Count];
            }
            if (cauldron)
            {
                foreach (var r in cauldron.GetComponentsInChildren<Renderer>(true)) if (r.name.StartsWith("Slot_")) gems.Add(r);
                // 按名字里的序号排：Slot_0 在正中，之后左右交替往外
                gems.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                BuildTag();
            }
        }

        // 10-31 糖桶周围一圈淡暖光（半径 Game.JAR_GLOW = 0.26 米，黑猫进到这圈里会现形）：桌面上一块平放的柔光圆片。
        // 不挂在糖桶下面（以前用无贴图粒子挂在桶上：粒子画成方块，而且“桶底不穿桌”把粒子也算进去，提桶时把桶一路往上推飞走）
        Transform bucketGlow; Renderer bucketGlowR;
        void AddBucketGlow()
        {
            if (!glowMat || !bucket) return;
            var go = new GameObject("BucketGlow");
            go.transform.SetParent(bucket.parent, false);
            var mf = go.AddComponent<MeshFilter>();
            var m = new Mesh { name = "BucketGlowQuad" };
            m.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f) });
            m.SetUVs(0, new List<Vector2> { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
            m.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0); m.RecalculateBounds();
            mf.sharedMesh = m;
            bucketGlowR = go.AddComponent<MeshRenderer>();
            bucketGlowR.sharedMaterial = glowMat;
            bucketGlowR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; bucketGlowR.receiveShadows = false;
            bucketGlow = go.transform;
        }

        void SyncBucketGlow(float time)
        {
            if (!bucketGlow) return;
            // 放在规则层的糖桶位置（黑猫现形按这里算），贴着桌面；直径 = 2 × 0.26 米再加柔边
            var p = loop.W(new Vector3(Layout.JAR.x, Layout.TABLE.y + 0.004f, Layout.JAR.z));
            bucketGlow.position = p;
            bucketGlow.rotation = bucket.parent ? bucket.parent.rotation : Quaternion.identity;
            bucketGlow.localScale = Vector3.one * (0.26f * 2f * 1.35f);
            bucketGlowR.GetPropertyBlock(mpb);
            mpb.SetColor(ID_Base, new Color(1f, 0.72f, 0.36f, 0.16f + 0.03f * Mathf.Sin(time * 1.7f)));
            bucketGlowR.SetPropertyBlock(mpb);
        }

        void BuildTag()
        {
            var eye = loop.W(new Vector3(0, Layout.EYE_HEIGHT, 0));
            tag = UIKit.Canvas(transform, "CauldronTag", loop.W(Layout.BOX.pos + Vector3.up * (Layout.BOX.height + 0.12f)), eye, new Vector2(140, 150));
            tag.localScale = Vector3.one * 0.0006f;
            tagGroup = tag.gameObject.AddComponent<CanvasGroup>();
            tagBack = UIKit.Img(tag, "Back", UIKit.Dot(), new Color(0.16f, 0.09f, 0.22f, 0.82f), new Vector2(0, -8), new Vector2(118, 118));
            ring = UIKit.Img(tag, "Ring", UIKit.Ring(), UIKit.Mint, new Vector2(0, -8), new Vector2(110, 110));
            ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360; ring.fillOrigin = (int)Image.Origin360.Top; ring.fillClockwise = true;
            tagText = UIKit.Text(tag, "Count", font, "0/3", 40, UIKit.Cream, new Vector2(0, -6), new Vector2(110, 60), TextAlignmentOptions.Center, 0.2f);
            tagLabel = UIKit.Text(tag, "Label", font, "", 22, UIKit.Mint, new Vector2(0, 62), new Vector2(160, 34), TextAlignmentOptions.Center, 0.2f);
            tagGroup.alpha = 0;
        }

        void UpdateCauldron(Game gm, float dt)
        {
            int cap = Mathf.Max(1, gm.Capacity), used = gm.cauldron.used;
            int digestSlots = gm.cauldron.queue.Count > 0 ? Mathf.Max(1, gm.cauldron.queue[0].Kind.size) : 0;
            float k = gm.DigestK;
            for (int i = 0; i < gems.Count; i++)
            {
                // 只亮当前格数那么多盏，居中排开（Slot 按奇偶从中间往两边排好了：0 中间、1 右、2 左……）
                bool on = i < cap;
                if (gems[i].gameObject.activeSelf != on) gems[i].gameObject.SetActive(on);
                if (!on) continue;
                bool full = i < used;
                bool digesting = i < digestSlots;
                Color baseC = full ? new Color(0.72f, 0.35f, 1f) : new Color(0.35f, 0.95f, 0.75f);
                float glow = full ? (digesting ? 0.9f + 0.6f * Mathf.Sin(Time.time * 5f) * (1 - k) : 1.1f) : 0.35f;
                gems[i].GetPropertyBlock(mpb);
                mpb.SetColor(ID_Base, baseC);
                mpb.SetColor(ID_Em, baseC * glow);
                gems[i].SetPropertyBlock(mpb);
            }
            if (!tag) return;
            // 只在游戏进行中显示（封面、结算时坩埚里残留的数字不该露出来）
            bool show = loop.game != null && loop.game.phase == Phases.Playing && (used > 0 || fullFlash > 0);
            tagVis = Mathf.MoveTowards(tagVis, show ? 1 : 0, dt * 4);
            tagGroup.alpha = tagVis;
            fullFlash = Mathf.Max(0, fullFlash - dt); shake = Mathf.Max(0, shake - dt * 3);
            ring.fillAmount = digestSlots > 0 ? k : 0;
            ring.color = Color.Lerp(UIKit.Mint, new Color(1f, 0.35f, 0.4f), fullFlash > 0 ? 1 : 0);
            tagText.text = used + "/" + cap;
            tagText.color = used >= cap || fullFlash > 0 ? new Color(1f, 0.55f, 0.55f) : UIKit.Cream;
            tagLabel.text = fullFlash > 0 || used >= cap ? Loc.T("ui.cauldron.full") : Loc.T("ui.cauldron.digesting");
            tagLabel.color = fullFlash > 0 || used >= cap ? new Color(1f, 0.55f, 0.55f) : UIKit.Mint;
            // 坩埚跟着规则层位置走（可以被玩家挪动）；挪动时稍微提起来一点
            // 坩埚：提着时吊在锅耳下（跟手），放下后落回桌面；始终正面朝玩家，提着时随手的移动轻轻摆
            var potTarget = loop.W(gm.potHeld != null ? gm.potCarry : Layout.BOX.pos);
            var yawRot = (loop.worldRoot ? loop.worldRoot.rotation : Quaternion.identity) * Quaternion.Euler(0, Layout.BOX.Yaw, 0);
            if (gm.potHeld != null)
            {
                // 提着：绕捏住的锅耳跟着手腕转；锅底不穿进桌面
                if (carryP != gm.potHeld) { carryP = gm.potHeld; rotOkP = HandRot(gm.potHeld, out hand0P); relP = Quaternion.identity; }
                var rel = rotOkP && HandRot(gm.potHeld, out var hr) ? hr * Quaternion.Inverse(hand0P) : Quaternion.identity;
                relP = Quaternion.Slerp(relP, rel, Mathf.Min(1, dt * 14));
                var earW = loop.W(gm.potCarry + (Layout.BOX.Ear(gm.PotEar) - Layout.BOX.pos));
                var want = earW + relP * (potTarget - earW);
                cauldron.position = Vector3.Lerp(cauldron.position, want, Mathf.Min(1, dt * 25));
                cauldron.rotation = Quaternion.Slerp(cauldron.rotation, relP * yawRot, Mathf.Min(1, dt * 25));
                KeepAboveTable(cauldron, ref potRs);
            }
            else
            {
                carryP = null;
                cauldron.position = Vector3.Lerp(cauldron.position, potTarget, Mathf.Min(1, dt * 14));
                cauldron.rotation = Quaternion.Slerp(cauldron.rotation, yawRot, Mathf.Min(1, dt * 10));
            }
            var basePos = loop.W(Layout.BOX.pos + Vector3.up * (Layout.BOX.height + 0.12f + Mathf.Sin(Time.time * 1.8f) * 0.006f));
            tag.position = basePos + tag.right * Mathf.Sin(shake * 40f) * 0.006f * shake;
        }

        /// <summary>这只手（规则层 joints）在世界里的朝向：手腕→中指根为前、食指根→小指根为横</summary>
        bool HandRot(string side, out Quaternion q)
        {
            q = Quaternion.identity;
            var inp = loop.CurrentInput; if (inp == null || inp.hands == null) return false;
            foreach (var h in inp.hands)
            {
                if (h.side != side || !h.tracked || h.joints == null || h.joints.Length < HandJoints.Count) continue;
                var j = h.joints;
                var fwd = j[HandJoints.MiddleProx] - j[HandJoints.Wrist];
                var lat = j[HandJoints.IndexProx] - j[HandJoints.LittleProx];
                var up = Vector3.Cross(fwd, lat);
                if (fwd.sqrMagnitude < 1e-6f || up.sqrMagnitude < 1e-8f) return false;
                q = (loop.worldRoot ? loop.worldRoot.rotation : Quaternion.identity) * Quaternion.LookRotation(fwd, up);
                return true;
            }
            return false;
        }

        /// <summary>转着看的时候别让东西陷进桌面：整体最低点低于桌面就往上抬</summary>
        float KeepAboveTable(Transform t, ref Renderer[] rs)
        {
            // 只量真正的模型网格（10-31：粒子、光圈之类的包围盒会把桶顶飞）
            if (rs == null) rs = System.Array.FindAll(t.GetComponentsInChildren<Renderer>(true), x => x is MeshRenderer || x is SkinnedMeshRenderer);
            float tableY = loop.W(new Vector3(0, Layout.TABLE.y, 0)).y, lo = float.PositiveInfinity;
            foreach (var r in rs) if (r && r.enabled && r.gameObject.activeInHierarchy) lo = Mathf.Min(lo, r.bounds.min.y);
            if (lo >= tableY) return 0;
            t.position += Vector3.up * (tableY - lo);
            return tableY - lo;
        }

        public void Handle(GameEvent e, GameLoop g)
        {
            switch (e.type)
            {
                case GameEventTypes.Steal: bounce = 1; break;
                case GameEventTypes.CandyBack: joy = 1; break;
                case GameEventTypes.Digested: boil = 2.6f; break;
                case GameEventTypes.Pat: boil = 2.2f; break;
                case GameEventTypes.Boxed: boil = 1.8f; break;
                case GameEventTypes.BoxFull: fullFlash = 1.6f; shake = 1; break;
            }
        }

        Transform candleCluster;

        void Update()
        {
            if (!loop) return;
            var gm = loop.game;
            float dt = Time.deltaTime;
            // 能召唤南瓜灯的夜晚桌上不摆烛台（规则层 CANDLE.on，连同它的烛光一起收掉）；封面时由 CoverStage 管
            if (!candleCluster && loop.worldRoot) candleCluster = loop.worldRoot.Find("Desk_CandleCluster");
            if (candleCluster && gm.phase == Phases.Playing && candleCluster.gameObject.activeSelf != Layout.CANDLE.on) candleCluster.gameObject.SetActive(Layout.CANDLE.on);
            // 糖果可见数：标题/选关时满桶
            int total = Mathf.Max(1, gm.cfg.candy.total);
            int have = gm.phase == Phases.Title ? total : gm.candyJar;
            int n = Mathf.Clamp(Mathf.CeilToInt(candies.Count * have / (float)total), 0, candies.Count);
            if (have <= 0) n = 0;
            // 糖堆跟着剩余糖数降下去（满桶 1 → 空桶 0.3），上面的糖一起降，始终贴在糖堆上（10-29 用户：糖不要悬在桶里）
            bedK = Mathf.MoveTowards(bedK, Mathf.Lerp(0.3f, 1f, have / (float)total), dt * 0.8f);
            if (bed) { var bs = bedScale; bs.y *= bedK; bed.localScale = bs; bed.gameObject.SetActive(have > 0 || bedK > 0.31f); }
            for (int i = 0; i < candies.Count; i++) { var cp = candies[i].localPosition; cp.y = bedBaseY + (candyY[i] - bedBaseY) * bedK; candies[i].localPosition = cp; }
            for (int i = 0; i < candies.Count; i++)
            {
                bool on = i < n;
                if (on && !candies[i].gameObject.activeSelf) { candies[i].gameObject.SetActive(true); pop[i] = 1; }
                else if (!on && candies[i].gameObject.activeSelf) candies[i].gameObject.SetActive(false);
                if (pop[i] > 0) { pop[i] = Mathf.Max(0, pop[i] - dt * 3); candies[i].localScale = candyScale[i] * (1 + 0.5f * Mathf.Sin(pop[i] * Mathf.PI)); }
            }
            if (bucket)
            {
                SyncBucketGlow(Time.time);
                bounce = Mathf.Max(0, bounce - dt * 2.5f); joy = Mathf.Max(0, joy - dt * 2f);
                float hop = Mathf.Sin(bounce * Mathf.PI) * 0.025f;
                float squish = 1 + 0.12f * Mathf.Sin(joy * Mathf.PI * 2) * joy;
                bucket.localScale = new Vector3(bucketScale.x * squish, bucketScale.y / squish, bucketScale.z * squish);
                // 提起来：跟着手；放下：像弹簧一样飘回原位
                liftK = Mathf.MoveTowards(liftK, gm.bucketHeld != null ? 1 : 0, dt * 6);
                // 放下后就待在规则层记的新位置（JAR），不再飘回原处
                var jarLocal = bucket.parent ? bucket.parent.InverseTransformPoint(loop.W(Layout.JAR.pos)) : Layout.JAR.pos;
                var home = new Vector3(jarLocal.x, jarLocal.y + hop, jarLocal.z);
                if (gm.bucketHeld != null)
                {
                    // 提着：绕提手顶（捏住的地方，桶底上方 32 厘米）跟着手腕转；桶底不穿进桌面
                    if (carryB != gm.bucketHeld) { carryB = gm.bucketHeld; rotOkB = HandRot(gm.bucketHeld, out hand0B); obj0B = bucket.rotation; relB = Quaternion.identity; bucketVelW = Vector3.zero; }
                    var rel = rotOkB && HandRot(gm.bucketHeld, out var hr) ? hr * Quaternion.Inverse(hand0B) : Quaternion.identity;
                    relB = Quaternion.Slerp(relB, rel, Mathf.Min(1, dt * 14));
                    var baseW = loop.W(gm.bucketPos);
                    var gripW = loop.W(gm.bucketPos + Vector3.up * 0.32f);
                    var want = gripW + relB * (baseW - gripW);
                    bucket.position = Vector3.SmoothDamp(bucket.position, want, ref bucketVelW, 0.04f);
                    bucket.rotation = Quaternion.Slerp(bucket.rotation, relB * obj0B, Mathf.Min(1, dt * 20));
                    // 顶到桌面时把向下的速度清零，否则平滑跟随继续往下推、和抬起来互相拉扯而抖动（10-30 审查）
                    if (KeepAboveTable(bucket, ref bucketRs) > 0 && bucketVelW.y < 0) bucketVelW.y = 0;
                    bucketVel = Vector3.zero;
                }
                else
                {
                    carryB = null;
                    bucket.localPosition = Vector3.SmoothDamp(bucket.localPosition, home, ref bucketVel, liftK > 0.01f ? 0.18f : 0.02f);
                    bucket.localRotation = Quaternion.Slerp(bucket.localRotation, Quaternion.identity, dt * 6);
                }
            }
            if (potion)
            {
                int cap = Mathf.Max(1, gm.Capacity);
                boil = Mathf.MoveTowards(boil, 1, dt * 0.6f);
                potion.GetPropertyBlock(mpb);
                mpb.SetFloat(ID_Fill, Mathf.Lerp(0.25f, 1f, gm.cauldron.used / (float)cap));
                mpb.SetFloat(ID_Boil, boil);
                potion.SetPropertyBlock(mpb);
            }
            if (cauldron) UpdateCauldron(gm, dt);
            if (bucketLight)
            {
                var c = gm.shieldT > 0 ? new Color(0.7f, 0.5f, 1f) : new Color(1f, 0.72f, 0.4f);
                bucketLight.color = Color.Lerp(bucketLight.color, c, dt * 4);
            }
        }
    }
}
