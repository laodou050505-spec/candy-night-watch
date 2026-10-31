// 卡通手套（奶油白手套 + 南瓜橙袖口），由手部追踪的 26 个关节实时驱动。
// 10-29 第三版（用户：结构太简单、弯手时关节露出来）：整只手套是一张带骨骼蒙皮的网格（SkinnedMeshRenderer）——
//   掌部是从手腕到指根放样出来的圆角软垫（手背鼓、掌心平），四根手指和拇指是从掌心里长出来的软管，
//   指根埋在掌垫里、拇指根和掌侧鼓包（大鱼际）连成一体；指节处微微收窄、指节中段微微鼓起，像布料裹着；
//   手背三道凸起的缝线。顶点按到各关节的距离平滑分配骨骼权重，弯曲时由骨骼平滑带动，关节处不会断开、也不会露缝。
//   网格在运行时按标准手型生成一次；之后每帧只更新 16 根骨骼（手掌 1 + 每指 3 节），骨长按玩家真实手指长度拉伸。
// 10-30 第四版（用户：手套有点粗糙，要更精致）：细分加密（截面 22 段、手指 40 段）；整只手套有 UV、贴细针织布纹；
//   袖口改为蒙皮网格里的一圈罗纹针织袖口（贴着手腕、口沿往外卷一道边），不再是一个大喇叭；
//   手背三道缝线贴着手背曲面、两头收尖，颜色改成浅一号的灰紫，不再像浮在上面的黑条；描边细一半，手指间不再出现内描线。
// 状态反馈（没有触觉，全靠看）：
//   捏合：整只手套泛一点暖光（刚捏上时闪一下）；南瓜灯蓄力（五指张开、手背朝自己）：手套变成发光的南瓜橙，手背亮起南瓜鬼脸
//   手伸进坩埚里：手套慢慢被烫红、冒一股热气、滋啦一声；拿出来以后慢慢褪回奶油色
//   追踪丢失：手套 0.25 秒内缩回手腕处消失；重新追踪到时从手腕处长出来
using System.Collections.Generic;
using TrickOrFreeze.Core;
using TrickOrFreeze.Input;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public partial class HandView : MonoBehaviour
    {
        public GameLoop loop;
        public bool right = true;
        public Mesh palmMesh, cuffMesh, segMesh, ballMesh;          // palmMesh / segMesh / ballMesh 已不用（保留字段，搭建脚本还在赋值）
        public Material gloveMat, palmMat, cuffMat;
        /// <summary>预览/示范用：不为空时直接用这组关节（世界根坐标），不读输入。</summary>
        // 不能序列化：公开数组在头显上会被还原成空数组（不是 null），之前因此每帧在 Array.Copy 报错、手套从来没显示过（10-29 实机日志）
        [System.NonSerialized] public Vector3[] overrideJoints;
        public bool overridePinch;
        public float overrideLampK;
        public float heat;               // 0..1 被坩埚烫红的程度
        /// <summary>南瓜灯蓄力时手背上亮起的南瓜鬼脸（发光贴花材质）</summary>
        public Material lampFaceMat;
        Transform face; Renderer faceR;

        /// <summary>诊断用：手套这一帧是否显示、手腕位置（规则层坐标）、是否拿到了追踪数据。</summary>
        public bool DiagVisible => root && root.gameObject.activeInHierarchy;
        public Vector3 DiagWrist => J[HandJoints.Wrist];
        public bool DiagTracked { get; private set; }
        /// <summary>烫手了（规则层坐标）：Sfx 播滋啦声、SceneFx 冒热气，各自订阅</summary>
        public static System.Action<Vector3> SceneFxBurn;

        // 每根手指的关节链（拇指从掌骨根起，四指从指根起），以及每节用哪根骨骼
        static readonly int[][] Chains =
        {
            new[] { HandJoints.ThumbMeta, HandJoints.ThumbProx, HandJoints.ThumbDist, HandJoints.ThumbTip },
            new[] { HandJoints.IndexProx, HandJoints.IndexInter, HandJoints.IndexDist, HandJoints.IndexTip },
            new[] { HandJoints.MiddleProx, HandJoints.MiddleInter, HandJoints.MiddleDist, HandJoints.MiddleTip },
            new[] { HandJoints.RingProx, HandJoints.RingInter, HandJoints.RingDist, HandJoints.RingTip },
            new[] { HandJoints.LittleProx, HandJoints.LittleInter, HandJoints.LittleDist, HandJoints.LittleTip },
        };
        // 卡通手套的“香肠手指”半径：指根 → 指尖
        // 10-30 用户：手指粘在一起。真实指根间距只有 16~20 毫米，原来半径 15~17 毫米的“香肠手指”在距离场里融成一整块；改细到相邻手指刚好分开
        static readonly float[] RootR = { 0.0128f, 0.0104f, 0.0108f, 0.0102f, 0.0092f }, TipR = { 0.0116f, 0.0098f, 0.0102f, 0.0096f, 0.0087f };
        const int BONES = 1 + 5 * 3;
        const int RING = 22, ALONG = 40, CAP = 8;

        Transform root;
        Transform[] bones;
        SkinnedMeshRenderer skin;
        Material stitchMat;
        MaterialPropertyBlock mpb;
        readonly Vector3[] J = new Vector3[HandJoints.Count];
        Vector3[] restJ;
        float[] restLen;                 // 每根骨骼静止时的长度（按玩家真实骨长拉伸用）
        float vis, pinchGlow, flash, lampGlow;
        bool wasPinch, haveLast, burnt;
        Color baseCol = Color.white, cuffCol = Color.white;
        static readonly int ID_Em = Shader.PropertyToID("_EmissionColor"), ID_Base = Shader.PropertyToID("_BaseColor");

        void Awake()
        {
            Build();
            // 10-31 读取已装备的手套皮肤并应用到 baseCol；订阅设置变化以便铺商店里装备时实时更新
            ApplySkin();
            TrickOrFreeze.UI.GameSettings.Changed += ApplySkin;
        }

        void OnDestroy() { TrickOrFreeze.UI.GameSettings.Changed -= ApplySkin; }

        void ApplySkin()
        {
            // 10-31 手套皮肤颜色：default=奶白（保持材质原色），pumpkin=南瓜橙，ghost=幽灵青，night=夜紫
            string skin = UnityEngine.PlayerPrefs.GetString("tof_skin", "default");
            switch (skin)
            {
                case "pumpkin": baseCol = new Color(1f, 0.54f, 0.12f); break;
                case "ghost":   baseCol = new Color(0.32f, 0.87f, 0.77f); break;
                case "night":   baseCol = new Color(0.44f, 0.34f, 0.6f); break;
                default:        baseCol = gloveMat && gloveMat.HasProperty(ID_Base) ? gloveMat.GetColor(ID_Base) : Color.white; break;
            }
        }

        void Build()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            if (root) return;
            var old = transform.Find("Glove");
            if (old) { if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject); }
            root = new GameObject("Glove").transform;
            root.SetParent(transform, false);
            if (gloveMat && gloveMat.HasProperty(ID_Base)) baseCol = gloveMat.GetColor(ID_Base);
            if (cuffMat && cuffMat.HasProperty(ID_Base)) cuffCol = cuffMat.GetColor(ID_Base);


            // 骨骼：全部直接挂在手套根下（不嵌套，拉伸一节不影响别的节）
            bones = new Transform[BONES];
            for (int i = 0; i < BONES; i++) { bones[i] = new GameObject("B" + i).transform; bones[i].SetParent(root, false); }

            // 标准手型（手指伸直、略张开）生成网格
            restJ = RestPose(right);
            var restM = new Matrix4x4[BONES];
            restLen = new float[BONES];
            BoneFrames(restJ, restM, restLen);
            // 网格：优先用编辑器里预先生成好的（GameplayBuilder 烘焙成资源），没有就现场生成一次并缓存（同一只手、有无袖口共用）
            Mesh mesh;
            var key = (right, cuffMesh != null);
            if (bakedSkin && bakedSkin.vertexCount > 0) mesh = bakedSkin;
            else if (!meshCache.TryGetValue(key, out mesh) || !mesh) { mesh = MakeMesh(restJ, restM); meshCache[key] = mesh; }

            var sg = new GameObject("Skin"); sg.transform.SetParent(root, false);
            skin = sg.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = root;
            skin.updateWhenOffscreen = true;
            skin.quality = SkinQuality.Bone4;
            skin.forceMatrixRecalculationPerRender = true;                          // 编辑器预览 / 截图时也按当前骨骼刷新
            skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 手的影子落在桌上反而晃眼
            if (gloveMat)
            {
                stitchMat = new Material(gloveMat) { name = "GloveStitch" };
                if (stitchMat.HasProperty(ID_Base)) stitchMat.SetColor(ID_Base, new Color(0.58f, 0.5f, 0.66f));
                if (stitchMat.HasProperty("_OutlineWidth")) stitchMat.SetFloat("_OutlineWidth", 0f);
            }
            // 子网格：0 手套 / 1 缝线 / 2 袖口（示范手没有袖口：cuffMesh 为空时不生成袖口）
            skin.sharedMaterials = new[] { gloveMat, stitchMat ? stitchMat : gloveMat, cuffMat ? cuffMat : gloveMat };
            root.gameObject.SetActive(false);
        }

        public Mesh bakedSkin;           // 预先生成的手套网格（含骨骼绑定姿势）；为空时运行时生成
        static readonly Dictionary<(bool, bool), Mesh> meshCache = new Dictionary<(bool, bool), Mesh>();

        static Vector3[] RestPose(bool right)
        {
            var j = HandPoses.Build(right, Vector3.zero, Quaternion.identity, new HandShape { index = 0.02f, middle = 0.02f, ring = 0.03f, little = 0.04f, thumbCurl = 0.05f });
            SpreadFingers(j, right);
            return j;
        }

        Mesh MakeMesh(Vector3[] rj, Matrix4x4[] restM)
        {
            var mesh = BuildSkinMesh(rj, restM);
            var bind = new Matrix4x4[BONES];
            for (int i = 0; i < BONES; i++) bind[i] = restM[i].inverse;
            mesh.bindposes = bind;
            return mesh;
        }

        /// <summary>编辑器烘焙用：按当前 right / cuffMesh 生成一张新的手套网格（距离场提取约 1 秒，不放在头显启动时做）</summary>
        public Mesh BakeSkinMesh()
        {
            var rj = RestPose(right);
            var m = new Matrix4x4[BONES]; var l = new float[BONES];
            BoneFrames(rj, m, l);
            return MakeMesh(rj, m);
        }

        /// <summary>
        /// 生成网格用的标准手型把四指再张开一些（食指、无名指、小指绕指根往外转），拇指也再离开食指一点：
        /// 距离场手套的手指在静止手型里不能互相贴着，否则表面会粘在一起，握拳时被拉出破面。只影响网格生成，骨骼照样跟着真实手指走。
        /// </summary>
        static void SpreadFingers(Vector3[] j, bool right)
        {
            var dorsal = HandPoses.Dorsal(j, right);
            var mid = j[HandJoints.MiddleProx];
            int[][] fingers = { new[] { HandJoints.IndexProx, HandJoints.IndexInter, HandJoints.IndexDist, HandJoints.IndexTip },
                                new[] { HandJoints.RingProx, HandJoints.RingInter, HandJoints.RingDist, HandJoints.RingTip },
                                new[] { HandJoints.LittleProx, HandJoints.LittleInter, HandJoints.LittleDist, HandJoints.LittleTip },
                                new[] { HandJoints.ThumbProx, HandJoints.ThumbDist, HandJoints.ThumbTip } };
            float[] ang = { 9f, 7f, 14f, 10f };
            for (int f = 0; f < fingers.Length; f++)
            {
                var c = fingers[f]; var pivot = f == 3 ? j[HandJoints.ThumbMeta] : j[c[0]];
                var away = Vector3.ProjectOnPlane(pivot - mid, dorsal);
                if (f == 3) away = Vector3.ProjectOnPlane(j[HandJoints.ThumbProx] - j[HandJoints.IndexProx], dorsal);
                // 选转动方向：让指尖离中指更远
                var tip = j[c[c.Length - 1]];
                var qa = Quaternion.AngleAxis(ang[f], dorsal);
                float sgn = Vector3.Dot(qa * (tip - pivot) - (tip - pivot), away) >= 0 ? 1 : -1;
                var q = Quaternion.AngleAxis(ang[f] * sgn, dorsal);
                for (int k = f == 3 ? 0 : 1; k < c.Length; k++) j[c[k]] = pivot + q * (j[c[k]] - pivot);
            }
        }

        // ---------- 骨骼标架 ----------
        // 0 = 手掌（手腕 → 四指根中点，上方向 = 手背）；之后每根手指 3 节：上方向沿手指平行移动（不扭转、握拳时也稳定）
        void BoneFrames(Vector3[] j, Matrix4x4[] m, float[] len)
        {
            var dorsal = HandPoses.Dorsal(j, right);
            var knuckle = (j[HandJoints.IndexProx] + j[HandJoints.LittleProx]) * 0.5f;
            var f0 = knuckle - j[HandJoints.Wrist];
            len[0] = f0.magnitude;
            m[0] = Matrix4x4.TRS(j[HandJoints.Wrist], Quaternion.LookRotation(f0.normalized, dorsal), Vector3.one);
            for (int f = 0; f < 5; f++)
            {
                var c = Chains[f];
                var up = dorsal;
                for (int s = 0; s < 3; s++)
                {
                    var d = j[c[s + 1]] - j[c[s]];
                    float l = d.magnitude; if (l < 1e-5f) { d = Vector3.forward * 1e-3f; l = 1e-3f; }
                    var dn = d / l;
                    up = Vector3.ProjectOnPlane(up, dn); if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.up, dn);
                    up.Normalize();
                    int b = 1 + f * 3 + s;
                    len[b] = l;
                    m[b] = Matrix4x4.TRS(j[c[s]], Quaternion.LookRotation(dn, up), Vector3.one);
                }
            }
        }

        // ---------- 网格生成（静止手型下，手套根坐标） ----------
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<BoneWeight> w = new List<BoneWeight>(); public readonly List<int> t0 = new List<int>(), t1 = new List<int>(), t2 = new List<int>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public Vector2 nextUV;
            public void Add(Vector3 p, Vector3 nr, BoneWeight bw) { v.Add(p); n.Add(nr); w.Add(bw); uv.Add(nextUV); }
        }

        static BoneWeight BW(params (int b, float w)[] ws)
        {
            // 取最大的 4 个并归一化
            var l = new List<(int b, float w)>();
            foreach (var x in ws) if (x.w > 1e-4f) { int k = l.FindIndex(y => y.b == x.b); if (k >= 0) l[k] = (x.b, l[k].w + x.w); else l.Add(x); }
            l.Sort((a, c) => c.w.CompareTo(a.w));
            while (l.Count > 4) l.RemoveAt(l.Count - 1);
            float s = 0; foreach (var x in l) s += x.w;
            if (s <= 0) return new BoneWeight { boneIndex0 = 0, weight0 = 1 };
            var bw = new BoneWeight();
            if (l.Count > 0) { bw.boneIndex0 = l[0].b; bw.weight0 = l[0].w / s; }
            if (l.Count > 1) { bw.boneIndex1 = l[1].b; bw.weight1 = l[1].w / s; }
            if (l.Count > 2) { bw.boneIndex2 = l[2].b; bw.weight2 = l[2].w / s; }
            if (l.Count > 3) { bw.boneIndex3 = l[3].b; bw.weight3 = l[3].w / s; }
            return bw;
        }

        Mesh BuildSkinMesh(Vector3[] j, Matrix4x4[] m)
        {
            var mb = new MB();
            var dorsal = HandPoses.Dorsal(j, right);
            var wrist = j[HandJoints.Wrist];
            var knuckle = (j[HandJoints.IndexProx] + j[HandJoints.LittleProx]) * 0.5f;
            var F = (knuckle - wrist).normalized;
            var U = Vector3.ProjectOnPlane(dorsal, F).normalized;
            var S = Vector3.Cross(U, F);                                       // 指向小指或食指一侧（左右手相反，不影响）
            float L = Vector3.Distance(wrist, knuckle);
            // 四个指根在手掌横向上的位置
            float[] mcpS = new float[4];
            for (int f = 0; f < 4; f++) mcpS[f] = Vector3.Dot(j[Chains[f + 1][0]] - wrist, S);
            float sMin = Mathf.Min(mcpS[0], mcpS[3]), sMax = Mathf.Max(mcpS[0], mcpS[3]);

            // 1) 手套主体：一整张光滑曲面（10-30 第五版，用户：拇指根在掌心那一侧很怪、整体不够精致）。
            //    以前是掌垫、五根软管、大鱼际软垫几块网格插在一起，交界处各自画描边，拇指根一圈黑线和鼓包、虎口有尖缝。
            //    现在把手掌、手指、拇指写成距离场，按“平滑并集”融在一起（拇指根自然鼓成大鱼际、指根之间有一点指蹼），
            //    再用 Surface Nets 提取表面：一张封闭的网格，没有接缝、没有内描边；法线取距离场梯度，处处光滑。
            var sdf = new GloveSdf(j, right, mcpS, sMin, sMax, wrist, S, U, F, L, Chains, RootR, TipR);
            int bodyStart = mb.v.Count;
            sdf.Mesh(mb, mb.t0);
            // UV：在手的坐标系里按法线主方向做立方投影（针织纹很细，投影接缝看不出来），约 7 厘米一个循环
            for (int i = bodyStart; i < mb.v.Count; i++)
            {
                var d = mb.v[i] - wrist; var n = mb.n[i];
                float x = Vector3.Dot(d, S), y = Vector3.Dot(d, U), z = Vector3.Dot(d, F);
                float ax = Mathf.Abs(Vector3.Dot(n, S)), ay = Mathf.Abs(Vector3.Dot(n, U)), az = Mathf.Abs(Vector3.Dot(n, F));
                mb.uv[i] = (ay >= ax && ay >= az ? new Vector2(x, z) : ax >= az ? new Vector2(y, z) : new Vector2(x, y)) * 14f;
            }

            // 3) 手背三道缝线：从手背上方往下投到曲面上，贴着曲面走，两头收尖
            for (int k = 0; k < 3; k++)
            {
                float lat = (mcpS[k] + mcpS[k + 1]) * 0.5f;
                var pts = new List<Vector3>(); var rs = new List<float>();
                for (int a = 0; a <= 16; a++)
                {
                    float u = a / 16f, t = Mathf.Lerp(0.34f, 0.84f, u);
                    var p = sdf.Project(wrist + F * (t * L) + S * lat + U * 0.05f, -U);
                    pts.Add(p + sdf.Normal(p) * 0.0003f);
                    rs.Add(0.00105f * Mathf.Sin(Mathf.PI * Mathf.Clamp(u * 1.15f, 0, 1)) + 0.0002f);
                }
                Strip(mb, pts, rs, U, BW((0, 1)));
            }
            // 4) 袖口：罗纹针织的一圈，贴着手腕（比腕部截面大一圈），口沿往外卷一道圆边；只跟手掌骨走
            if (cuffMesh != null)
            {
                int CR = 36, CA = 14;
                int cStart = mb.v.Count;
                for (int a = 0; a <= CA; a++)
                {
                    float u = a / (float)CA;                                     // 0 = 伸进掌垫下面，1 = 卷边
                    float t = Mathf.Lerp(-0.08f, -0.62f, u);
                    float hw = 0.036f + 0.004f * u, th = 0.023f + 0.003f * u;
                    float roll = Mathf.Sin(Mathf.InverseLerp(0.78f, 1f, u) * Mathf.PI) * 0.0045f;   // 口沿外卷
                    var c = wrist + F * (t * L) + U * 0.001f;
                    for (int i = 0; i < CR; i++)
                    {
                        float ang = i / (float)CR * Mathf.PI * 2, ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
                        float rib = 1 + 0.012f * Mathf.Cos(ang * 30) * (1 - Mathf.InverseLerp(0.7f, 1f, u));   // 细罗纹，口沿处收平（不然边缘像齿轮）
                        float ex = 2f / 2.6f;
                        float x = Mathf.Sign(ca) * Mathf.Pow(Mathf.Abs(ca), ex) * (hw + roll) * rib;
                        float y = Mathf.Sign(sa) * Mathf.Pow(Mathf.Abs(sa), ex) * (th + roll) * rib;
                        var nr = (S * ca / hw + U * sa / th).normalized;
                        mb.nextUV = new Vector2(i / (float)CR * 4f, u * 1.2f);
                        mb.Add(c + S * x + U * y, nr, BW((0, 1)));
                    }
                }
                for (int a = 0; a < CA; a++)
                    for (int i = 0; i < CR; i++)
                    {
                        int p0 = cStart + a * CR + i, p1 = cStart + a * CR + (i + 1) % CR, p2 = p0 + CR, p3 = p1 + CR;
                        Tri(mb.t2, p0, p2, p1); Tri(mb.t2, p1, p2, p3);   // 袖口沿 -F 方向生成，绕序和掌垫相反
                    }
                // 袖口内壁（从袖口里往里看不是空的）：往里缩一点、反向
                int inStart = mb.v.Count;
                for (int a = CA - 3; a <= CA; a++)
                    for (int i = 0; i < CR; i++)
                    {
                        int src = cStart + a * CR + i; var p = mb.v[src]; var c = wrist + F * (Mathf.Lerp(-0.08f, -0.62f, a / (float)CA) * L);
                        mb.nextUV = mb.uv[src];
                        mb.Add(c + (p - c) * 0.9f, -mb.n[src], BW((0, 1)));
                    }
                for (int a = 0; a < 3; a++)
                    for (int i = 0; i < CR; i++)
                    {
                        int p0 = inStart + a * CR + i, p1 = inStart + a * CR + (i + 1) % CR, p2 = p0 + CR, p3 = p1 + CR;
                        Tri(mb.t2, p0, p1, p2); Tri(mb.t2, p1, p3, p2);
                    }
                // 袖口里面封一层（从袖口开口往里看是布，不是空心）
                CloseRing(mb, inStart, CR, wrist + F * (Mathf.Lerp(-0.08f, -0.62f, (CA - 3) / (float)CA) * L), -F, BW((0, 1)), true);
                // CloseRing 写进的是 t0（手套色），挪到袖口子网格
                { int n = CR * 3; var tail = mb.t0.GetRange(mb.t0.Count - n, n); mb.t0.RemoveRange(mb.t0.Count - n, n); mb.t2.AddRange(tail); }
                // 卷边：外沿和内壁之间封上
                int outer = cStart + CA * CR, inner = inStart + 3 * CR;
                for (int i = 0; i < CR; i++) { int o0 = outer + i, o1 = outer + (i + 1) % CR, i0 = inner + i, i1 = inner + (i + 1) % CR; Tri(mb.t2, o0, i0, o1); Tri(mb.t2, o1, i0, i1); }
            }

            var mesh = new Mesh { name = "GloveSkin_" + (right ? "R" : "L") };
            mesh.SetVertices(mb.v); mesh.SetNormals(mb.n); mesh.SetUVs(0, mb.uv);
            mesh.boneWeights = mb.w.ToArray();
            mesh.subMeshCount = 3;
            mesh.SetTriangles(mb.t0, 0); mesh.SetTriangles(mb.t1, 1); mesh.SetTriangles(mb.t2, 2);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void Tri(List<int> t, int a, int b, int c) { t.Add(a); t.Add(b); t.Add(c); }

        static void CloseRing(MB mb, int start, int n, Vector3 center, Vector3 nr, BoneWeight bw, bool front)
        {
            int ci = mb.v.Count; mb.Add(center, nr, bw);
            for (int i = 0; i < n; i++) { if (front) Tri(mb.t0, start + i, start + (i + 1) % n, ci); else Tri(mb.t0, start + (i + 1) % n, start + i, ci); }
        }

        // 缝线：沿折线的一条细圆管（第二个子网格），每个点自己的半径（两头收尖）
        static void Strip(MB mb, List<Vector3> pts, List<float> rs, Vector3 up, BoneWeight bw)
        {
            int ring = 8, start = mb.v.Count;
            for (int s = 0; s < pts.Count; s++)
            {
                var tn = (s < pts.Count - 1 ? pts[s + 1] - pts[s] : pts[s] - pts[s - 1]).normalized;
                var nr = Vector3.ProjectOnPlane(up, tn).normalized; var bn = Vector3.Cross(tn, nr);
                for (int i = 0; i < ring; i++)
                {
                    float a = i / (float)ring * Mathf.PI * 2; var d = nr * Mathf.Cos(a) + bn * Mathf.Sin(a);
                    mb.nextUV = Vector2.zero; mb.Add(pts[s] + d * rs[s] * (Mathf.Cos(a) < 0 ? 0.4f : 1f), d, bw);   // 下半圈压扁贴住手背
                }
            }
            for (int s = 0; s < pts.Count - 1; s++)
                for (int i = 0; i < ring; i++)
                {
                    int p0 = start + s * ring + i, p1 = start + s * ring + (i + 1) % ring, p2 = p0 + ring, p3 = p1 + ring;
                    Tri(mb.t1, p0, p1, p2); Tri(mb.t1, p1, p3, p2);
                }
        }

        // ---------- 每帧 ----------
        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>按当前输入（或 overrideJoints）摆一帧。编辑器预览可直接调用。</summary>
        public void Tick(float dt)
        {
            Build();
            bool tracked = false, pinching = false; float lampK = 0;
            if (overrideJoints != null && overrideJoints.Length >= HandJoints.Count)
            {
                System.Array.Copy(overrideJoints, J, HandJoints.Count);
                tracked = true; pinching = overridePinch; lampK = overrideLampK;
            }
            else if (loop != null && loop.CurrentInput != null && loop.CurrentInput.hands != null)
            {
                string side = right ? Sides.Right : Sides.Left;
                foreach (var h in loop.CurrentInput.hands)
                {
                    if (h.side != side) continue;
                    if (h.tracked && h.joints != null && h.joints.Length >= HandJoints.Count)
                    {
                        System.Array.Copy(h.joints, J, HandJoints.Count);
                        tracked = true; pinching = h.pinching; lampK = h.lampK;
                    }
                    break;
                }
            }
            DiagTracked = tracked;
            if (tracked) haveLast = true;
            vis = Mathf.MoveTowards(vis, tracked ? 1 : 0, dt / 0.25f);
            root.gameObject.SetActive(haveLast && vis > 0.001f);
            if (!root.gameObject.activeSelf) return;
            Pose(Mathf.SmoothStep(0, 1, vis));

            if (pinching && !wasPinch) flash = 1;
            wasPinch = pinching;
            flash = Mathf.MoveTowards(flash, 0, dt * 4);
            pinchGlow = Mathf.MoveTowards(pinchGlow, pinching ? 1 : 0, dt * 8);
            lampGlow = Mathf.MoveTowards(lampGlow, lampK, dt * 4);
            // 烫手：指尖伸进锅口里（锅沿以下、魔药面附近）慢慢变红，拿出来慢慢褪色
            bool inPot = false;
            var bp = Layout.BOX.pos;
            foreach (var c in Chains)
            {
                var tip = J[c[c.Length - 1]];
                if (Vec.DistXZ(tip, bp) < Layout.BOX.mouth && tip.y < bp.y + Layout.BOX.height - 0.01f && tip.y > bp.y + 0.03f) { inPot = true; break; }
            }
            heat = Mathf.MoveTowards(heat, inPot ? 1 : 0, dt * (inPot ? 2.2f : 0.35f));
            if (inPot && heat > 0.35f && !burnt) { burnt = true; SceneFxBurn?.Invoke(J[HandJoints.IndexTip]); }
            if (heat < 0.1f) burnt = false;

            var hot = Color.Lerp(baseCol, new Color(1f, 0.32f, 0.24f), Mathf.SmoothStep(0, 1, heat));
            // 南瓜灯蓄力（五指张开、手背朝自己）：手套渐渐变成南瓜橙、自己发光，手背上亮起一张南瓜鬼脸——整只手变成一盏南瓜灯
            float lk = Mathf.SmoothStep(0, 1, lampGlow);
            hot = Color.Lerp(hot, new Color(1f, 0.56f, 0.2f), lk * 0.85f);
            var em = new Color(1f, 0.25f, 0.1f) * (heat * heat * 0.8f) + new Color(1f, 0.62f, 0.25f) * (pinchGlow * 0.12f + flash * 0.35f) + new Color(1f, 0.48f, 0.1f) * (lk * (0.75f + 0.15f * Mathf.Sin(Time.time * 11f)));
            UpdateLampFace(lk);
            mpb.Clear();
            mpb.SetColor(ID_Base, hot); mpb.SetColor(ID_Em, em);
            skin.SetPropertyBlock(mpb, 0);
            mpb.Clear(); mpb.SetColor(ID_Em, em * 0.5f);
            if (lk > 0.01f) mpb.SetColor(ID_Base, Color.Lerp(new Color(0.58f, 0.5f, 0.66f), hot, lk));   // 蓄力时缝线和手套同色，不穿过鬼脸
            skin.SetPropertyBlock(mpb, 1);
            mpb.Clear(); mpb.SetColor(ID_Base, cuffCol); mpb.SetColor(ID_Em, em * 0.3f);
            skin.SetPropertyBlock(mpb, 2);
        }

        void UpdateLampFace(float lk)
        {
            if (!lampFaceMat) return;
            if (!face)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                if (Application.isPlaying) Destroy(q.GetComponent<Collider>()); else DestroyImmediate(q.GetComponent<Collider>());
                q.name = "LampFace"; face = q.transform; face.SetParent(root, false);
                faceR = q.GetComponent<Renderer>(); faceR.sharedMaterial = lampFaceMat; faceR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            bool on = lk > 0.01f;
            if (face.gameObject.activeSelf != on) face.gameObject.SetActive(on);
            if (!on) return;
            // 贴在手背正中（手背表面上方一点点），脸的“上方”朝手指
            var dorsal = HandPoses.Dorsal(J, right);
            var along = (J[HandJoints.MiddleProx] - J[HandJoints.Wrist]).normalized;
            face.localPosition = Vector3.Lerp(J[HandJoints.MiddleProx], J[HandJoints.Wrist], 0.45f) + dorsal * 0.026f;
            face.localRotation = Quaternion.LookRotation(-dorsal, along);
            face.localScale = Vector3.one * (0.058f * (0.6f + 0.4f * lk) * (1f + 0.04f * Mathf.Sin(Time.time * 9f)));
            faceR.GetPropertyBlock(mpb);
            mpb.SetColor(ID_Base, new Color(1f, 0.85f, 0.35f, lk));
            faceR.SetPropertyBlock(mpb);
        }

        readonly Matrix4x4[] curM = new Matrix4x4[BONES];
        readonly float[] curLen = new float[BONES];

        void Pose(float k)
        {
            var wrist = J[HandJoints.Wrist];
            // 消失/出现时整只手缩向手腕
            var Jk = new Vector3[HandJoints.Count];
            for (int i = 0; i < Jk.Length; i++) Jk[i] = Vector3.LerpUnclamped(wrist, J[i], k);
            if ((Jk[HandJoints.MiddleProx] - wrist).sqrMagnitude < 1e-8f) return;
            BoneFrames(Jk, curM, curLen);
            for (int b = 0; b < BONES; b++)
            {
                var m = curM[b];
                bones[b].localPosition = m.GetColumn(3);
                bones[b].localRotation = m.rotation;
                // 骨长按玩家真实手指拉伸（只沿骨骼方向），限制在 0.6~1.6 倍，追踪抖动时不至于把手指拉成面条
                float sZ = Mathf.Clamp(curLen[b] / Mathf.Max(1e-4f, restLen[b]), 0.6f, 1.6f) * (b == 0 ? 1f : 1f);
                bones[b].localScale = new Vector3(1, 1, sZ) * Mathf.Max(0.001f, k);
            }
        }
    }
}
