// 界面总管（世界空间，贴纸风）：标题 → 选夜 → 游戏中 HUD（夜名彩带、月亮进度、糖果数、字幕、教学进度点）→ 结算。
// 所有界面都悬在桌子后上方，玩家坐着微抬头就能看到，不挡桌面上的敌人。
// 交互：桌面用鼠标点；头显里看着按钮捏一下手指（或手指直接戳按钮）。所有按钮悬停放大 + 柔光 + 音效。
// 语言切换时整套界面按新语言重建（字体也换：中文用站酷快乐体，英文标题用 Creepster、正文用 Baloo 2）。
using System.Collections.Generic;
using TMPro;
using TrickOrFreeze.Core;
using TrickOrFreeze.UI;
using UnityEngine;
using UnityEngine.UI;

namespace TrickOrFreeze
{
    public class HudPresenter : MonoBehaviour
    {
        public GameLoop loop;
        public UIPointer pointer;
        public TMP_FontAsset fontZh, fontTitleEn, fontEn;
        // 封面与界面美术（Tripo 生成的整套贴图：标题字、南瓜主按钮、奶油票券次按钮、星空纸面板、剪纸关卡卡、图标）
        public CoverStage cover;
        public Sprite logo, btnPrimary, btnSecondary, panel, card;
        public Sprite shopCard;          // 糖果铺升级卡（竖版、不带编号圈和星槽）
        public Sprite[] icons;
        public Sprite[] gestures;            // 手势图标（游戏手套渲染，见 Tools/make_ui_kit.py）

        enum Screen { Title, Nights, Settings, Shop, Playing, Paused, Result }
        Screen screen = Screen.Title;
        RectTransform title, nights, top, subtitle, banner, result, side, settings, pause, coverLogo, coverButtons, shop;
        float practiceBackT = -1;
        readonly List<GameObject> coverUi = new List<GameObject>();
        readonly Dictionary<RectTransform, float> popScale = new Dictionary<RectTransform, float>();
        Image musicBar, sfxBar; TextMeshProUGUI gazeLabel, tutLabel;
        Image subIcon;
        TextMeshProUGUI subText, bannerText, nightText, candyText, moonPct;
        Image moonFill, moonIcon, subBack;
        RectTransform dots;
        WorldButton skipBtn;
        readonly List<Image> dotImgs = new List<Image>();
        float subT, subDur, bannerT, resultDelay = -1;
        NightResult pendingResult;
        int lastCandy = -1;
        float candyDrop;
        float candyPunch;
        readonly Vector3 eye = new Vector3(0, Layout.EYE_HEIGHT, 0);
        // 10-31 暂停页音量条（独立于设置页的 musicBar/sfxBar）
        Image musicBarP, sfxBarP;
        // 10-31 避免语言切换 / 重建时重复给挑战发奖励
        bool _challengesProcessed;
        // 10-31 Fancy() 投影材质缓存：同一 TMP_FontAsset 共用，避免每次 fontMaterial 创建新实例
        static readonly Dictionary<TMP_FontAsset, Material> fancyUnderlayMats = new Dictionary<TMP_FontAsset, Material>();

        TMP_FontAsset Body => Loc.IsZh ? fontZh : fontEn;
        TMP_FontAsset Head => Loc.IsZh ? fontZh : fontTitleEn;

        void Start()
        {
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 HudPresenter.Start 开始");
            // 视线光点：指着按钮时在落点显示一个柔光小圆
            if (pointer && !pointer.cursor)
            {
                var c = new GameObject("GazeCursor").AddComponent<SpriteRenderer>();
                c.sprite = UIKit.Glow(); c.color = new Color(1f, 0.95f, 0.7f, 0.9f); c.sortingOrder = 50;
                c.transform.SetParent(transform, false); c.transform.localScale = Vector3.one * 0.035f;
                pointer.cursor = c.transform;
            }
            Build();
            Loc.Changed += Rebuild;
            Show(Screen.Title);
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 HudPresenter.Start 完成");
        }

        void OnDestroy() { Loc.Changed -= Rebuild; }

        // ---------- 编辑器预览（不进播放模式截图用）：搭出指定界面，截完调用 EditorClear 全部清掉 ----------
        /// <summary>编辑器预览：用给定的结算数据画结算页（截图自查用，不发奖励）</summary>
        public void EditorPreviewResult(NightResult r)
        {
            EditorPreview("Result");
            _challengesProcessed = true;
            FillResult(r);
        }

        public void EditorPreview(string screenName)
        {
            EditorClear();
            Build();
            Show((Screen)System.Enum.Parse(typeof(Screen), screenName));
            foreach (var kv in popScale) if (kv.Key) kv.Key.localScale = Vector3.one * kv.Value;
            if (screenName == "Result")
            {
                // 10-31 预览用模拟结算数据（含挑战、被偷走数、提示）
                var mockChallenges = new System.Collections.Generic.List<ChallengeResult>
                {
                    new ChallengeResult { id = "noLoss", key = "challenge.noLoss", text = "", done = true,  coins = 5 },
                    new ChallengeResult { id = "stars",  key = "challenge.stars",  text = "", done = false, coins = 5, args = new Dictionary<string, string> { { "n", "2" } } },
                    new ChallengeResult { id = "pulls",  key = "challenge.pulls",  text = "", done = true,  coins = 5, args = new Dictionary<string, string> { { "n", "2" } } },
                };
                _challengesProcessed = true; // 预览时不发真实奖励
                FillResult(new NightResult { win = true, stars = 2, candies = 9, lost = 2, next = 1, challenges = mockChallenges });
                result.gameObject.SetActive(true);
            }
        }

        /// <summary>编辑器预览：游戏中界面（字幕 + 手势图标 + 技能栏），不跑游戏循环。</summary>
        public void EditorPreviewHud(Game gm, string stepId, string sayText)
        {
            EditorPreview("Playing");
            subText.text = sayText; subBack.color = UIKit.Cream; subText.alpha = 1;
            var gs = Gesture(GestureFor(stepId) ?? "");
            subIcon.sprite = gs; subIcon.color = Color.white; subIcon.gameObject.SetActive(gs != null);
            subText.rectTransform.anchoredPosition = new Vector2(gs != null ? 60 : 0, 32);
            subText.rectTransform.sizeDelta = new Vector2(gs != null ? 980 : 1080, 126);
            subtitle.gameObject.SetActive(true);
            UpdateSkillDock(gm, 0.016f);
        }

        public void EditorClear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) DestroyImmediate(transform.GetChild(i).gameObject);
            foreach (var g in coverUi) if (g) DestroyImmediate(g);
            coverUi.Clear(); popScale.Clear(); dotImgs.Clear();
            if (cover) cover.Exit();
        }

        void Rebuild()
        {
            var keep = screen;
            foreach (Transform c in transform) Destroy(c.gameObject);
            foreach (var g in coverUi) if (g) Destroy(g);
            coverUi.Clear(); popScale.Clear();
            dotImgs.Clear();
            Build();
            Show(keep);
            if (keep == Screen.Result && pendingResult != null) FillResult(pendingResult);
        }

        Vector3 Wp(Vector3 local) => loop ? loop.W(local) : local;

        // ================= 搭建 =================
        void Build()
        {
            BuildTitle();
            BuildNights();
            BuildSettings();
            BuildShop();
            BuildHud();
            BuildPause();
            BuildResult();
        }

        WorldButton Button(Transform parent, string name, string text, Vector2 pos, Vector2 size, Color col, float font, bool ribbon, System.Action click)
        {
            var root = UIKit.Node(parent, name, pos, size);
            var glow = UIKit.Img(root, "Glow", UIKit.Glow(), new Color(col.r, col.g, col.b, 0), Vector2.zero, size * 1.35f);
            var face = UIKit.Img(root, "Face", ribbon ? UIKit.Ribbon() : UIKit.Panel(), col, Vector2.zero, size);
            var label = UIKit.Text(root, "Label", Body, text, font, ribbon ? UIKit.Cream : UIKit.Ink, new Vector2(0, 3), size - new Vector2(40, 10),
                                   TextAlignmentOptions.Center, ribbon ? 0.22f : 0f);
            var b = root.gameObject.AddComponent<WorldButton>();
            b.face = face; b.glow = glow; b.label = label; b.baseColor = col; b.hoverColor = Color.Lerp(col, Color.white, 0.25f); b.onClick = click;
            var bc = root.gameObject.AddComponent<BoxCollider>();
            bc.size = new Vector3(size.x, size.y, 20); bc.isTrigger = true;
            return b;
        }

        // 菜单面板（标题、选夜、结算）：1 画布单位 = 0.72mm，标题面板约 0.8m × 0.58m，1.3m 外看约 35° × 25°
        const float SheetScale = 0.00072f;

        RectTransform Sheet(string name, Vector3 local, Vector2 size, Color tint)
        {
            var cv = UIKit.Canvas(transform, name, Wp(local), Wp(eye), size);
            cv.localScale = Vector3.one * SheetScale;
            cv.gameObject.AddComponent<VrPanel>().elevationDeg = 6f;   // 出现在玩家头部朝向的正前方（略高于视线，越过桌上的糖桶），转头超过 28° 才慢慢跟随
            UIKit.Img(cv, "Back", UIKit.Panel(), tint, Vector2.zero, size);
            return cv;
        }

        // ---------- 美术工具 ----------
        Sprite Gesture(string n) { if (gestures != null) foreach (var sp in gestures) if (sp && sp.name == "Gesture_" + n) return sp; return null; }

        // 教学 / 开夜介绍每一步对应的手势图标（look / residual 只用眼睛，不显示手）
        static string GestureFor(string step)
        {
            switch (step)
            {
                case "grab": case "cauldron": case "candy": return "Pinch";
                case "shake": return "Shake";
                case "throw": return "Throw";
                case "pull": return "Pull";
                case "launch": return "Launch";
                case "pray": return "Pray";
                case "sweep": return "Sweep";
                case "shield": return "Shield";
                case "lamp": return "Lamp";
                default: return null;
            }
        }

        Sprite Icon(string n) { if (icons != null) foreach (var sp in icons) if (sp && sp.name == "Icon_" + n) return sp; return null; }

        // 挂在封面锚点下的画布（锚点已朝向玩家）
        RectTransform AnchorCanvas(Transform anchor, string name, Vector2 size, float scale)
        {
            var cv = UIKit.Canvas(anchor, name, anchor.position, anchor.position - anchor.forward, size);
            cv.localPosition = Vector3.zero; cv.localRotation = Quaternion.identity; cv.localScale = Vector3.one * scale;
            coverUi.Add(cv.gameObject);
            return cv;
        }

        // 设计字：竖向渐变 + 深紫描边 + 下投影（标题、按钮字都用它）
        TextMeshProUGUI Fancy(Transform parent, string name, TMP_FontAsset font, string text, float size, Color top, Color bottom, Vector2 pos, Vector2 box, float outline = 0.26f)
        {
            var t = UIKit.Text(parent, name, font, text, size, Color.white, pos, box, TextAlignmentOptions.Center, outline);
            // 平涂单色 + 墨色描边 + 硬边投影（不用渐变：和整套剪纸贴纸风格一致，也不显得廉价）
            t.enableVertexGradient = false;
            t.color = top;
            // 10-31 共用投影材质，不再每次 fontMaterial 实例化新 Material（类似 UIKit.outlineMats）；加 null 防护
            if (!fancyUnderlayMats.TryGetValue(font, out var m) || m == null)
            {
                var baseM = t.fontSharedMaterial;
                m = baseM != null ? new Material(baseM) : null;
                if (m != null)
                {
                    m.EnableKeyword("UNDERLAY_ON");
                    m.SetColor("_UnderlayColor", new Color(0.09f, 0.04f, 0.14f, 0.55f));
                    m.SetFloat("_UnderlayOffsetX", 0.35f); m.SetFloat("_UnderlayOffsetY", -0.7f); m.SetFloat("_UnderlaySoftness", 0f);
                    fancyUnderlayMats[font] = m;
                }
            }
            if (m != null) t.fontSharedMaterial = m;
            return t;
        }

        // 贴图按钮：底图 + 可选图标 + 设计字；悬停放大发光，按下压扁回弹
        WorldButton ArtButton(Transform parent, string name, string text, Vector2 pos, Vector2 size, Sprite sp, bool primary, float font, System.Action click, Sprite icon = null)
        {
            var root = UIKit.Node(parent, name, pos, size);
            var glow = UIKit.Img(root, "Glow", UIKit.Glow(), new Color(1f, 0.78f, 0.4f, 0), new Vector2(0, -size.y * 0.04f), new Vector2(size.x * 1.15f, size.y * 1.3f));
            var face = UIKit.Img(root, "Face", sp, Color.white, Vector2.zero, size); face.preserveAspect = true;
            float tx = 0;
            if (icon)
            {
                float ih = size.y * (primary ? 0.5f : 0.56f);
                UIKit.Img(root, "Icon", icon, Color.white, new Vector2(-size.x * 0.3f, size.y * 0.03f), new Vector2(ih, ih)).preserveAspect = true;
                tx = size.x * 0.095f;
            }
            var label = primary
                ? Fancy(root, "Label", Head, text, font, new Color(1f, 0.98f, 0.9f), new Color(1f, 0.86f, 0.55f), new Vector2(tx, size.y * 0.05f), new Vector2(size.x * 0.66f, size.y * 0.6f), 0.3f)
                : UIKit.Text(root, "Label", Body, text, font, UIKit.Ink, new Vector2(tx, size.y * 0.02f), new Vector2(size.x * 0.66f, size.y * 0.6f), TextAlignmentOptions.Center, 0f);
            var b = root.gameObject.AddComponent<WorldButton>();
            b.face = face; b.glow = glow; b.label = label; b.baseColor = Color.white; b.hoverColor = new Color(1f, 0.95f, 0.85f); b.onClick = click;
            var bc = root.gameObject.AddComponent<BoxCollider>();
            bc.size = new Vector3(size.x * 0.92f, size.y * 0.75f, 20); bc.isTrigger = true;
            return b;
        }

        // 换场景都走淡入淡出（VR 里不会突然跳变）
        void Fade(System.Action a) { if (ScreenFade.Instance) ScreenFade.Instance.Run(a); else a(); }
        void StartNightFaded(int idx) { Sfx.Instance?.Ui("btn"); Fade(() => loop.StartNight(idx)); }
        void ToTitleFaded(Screen next = Screen.Title) { Fade(() => { loop.paused = false; loop.BackToTitle(); Show(next); }); }

        // ---------- 封面（标题）----------
        void BuildTitle()
        {
            if (!cover) { BuildTitleLegacy(); return; }
            coverLogo = AnchorCanvas(cover.logoAnchor, "UI_Logo", new Vector2(1536, 820), 0.00082f);
            UIKit.Img(coverLogo, "Halo", UIKit.Glow(), new Color(0.55f, 0.35f, 1f, 0.32f), new Vector2(0, 40), new Vector2(1750, 900));
            UIKit.Img(coverLogo, "Logo", logo, Color.white, new Vector2(0, 60), new Vector2(1536, 651)).preserveAspect = true;
            UIKit.Img(coverLogo, "SubBack", UIKit.Ribbon(), new Color(0.19f, 0.11f, 0.3f, 0.92f), new Vector2(0, -330), new Vector2(1500, 112));   // 副标题垫一条深紫缎带，压在花园背景上也看得清
            Fancy(coverLogo, "Sub", Body, Loc.T("game.subtitle"), Loc.IsZh ? 52 : 46, new Color(1f, 0.95f, 0.85f), new Color(0.75f, 1f, 0.92f), new Vector2(0, -330), new Vector2(1400, 90), 0.22f);
            var lb = coverLogo.gameObject.AddComponent<TrickOrFreeze.Ambient.Bobber>(); lb.amplitude = 0.012f; lb.period = 5f; lb.tilt = 0.4f;

            coverButtons = AnchorCanvas(cover.buttonAnchor, "UI_CoverButtons", new Vector2(900, 1080), 0.0008f);
            // 第一次玩：只有"开始守夜"（直接进第一夜）+ 设置；打过第一夜以后："继续第 N 夜" + "选择夜晚"（10-28 反馈：一上来就选关不合理）
            int next = Mathf.Clamp(Progress.Unlocked, 0, NightDefs.NIGHTS.Length - 1);
            bool fresh = Progress.Unlocked <= 0;
            string startLabel = fresh ? Loc.T("ui.start") : Loc.T("ui.continue", null, new System.Collections.Generic.Dictionary<string, string> { { "n", (next + 1).ToString() } });
            float y0 = fresh ? 300 : 370;
            ArtButton(coverButtons, "Start", startLabel, new Vector2(0, y0), new Vector2(660, 300), btnPrimary, true, Loc.IsZh ? 72 : 56, () => StartNightFaded(next), Icon("Play"));
            float y = y0 - 230;
            if (!fresh) { ArtButton(coverButtons, "Nights", Loc.T("ui.nights"), new Vector2(0, y), new Vector2(560, 180), btnSecondary, false, Loc.IsZh ? 56 : 44, () => { Sfx.Instance?.Ui("btn"); Show(Screen.Nights); }, Icon("Moon")); y -= 185; }
            // 玩法（动手练一遍，10-29 用户：玩中学）+ 糖果铺（用剩下的糖换升级）
            ArtButton(coverButtons, "Practice", Loc.T("ui.practice"), new Vector2(-200, y), new Vector2(390, 170), btnSecondary, false, Loc.IsZh ? 50 : 34, () => { Sfx.Instance?.Ui("btn"); Fade(() => loop.StartPractice()); }, Icon("Hand"));
            ArtButton(coverButtons, "Shop", Loc.T("ui.shop"), new Vector2(200, y), new Vector2(390, 170), btnSecondary, false, Loc.IsZh ? 50 : 34, () => { Sfx.Instance?.Ui("btn"); FillShop(); Show(Screen.Shop); }, Icon("Candy"));
            y -= 180;
            // 10-30 用户：封面按钮不能只有图标没有字
            ArtButton(coverButtons, "Settings", Loc.T("ui.settings"), new Vector2(-285, y), new Vector2(270, 140), btnSecondary, false, Loc.IsZh ? 38 : 30, () => { Sfx.Instance?.Ui("btn"); Show(Screen.Settings); }, Icon("Gear"));
            ArtButton(coverButtons, "Lang", Loc.T("ui.lang"), new Vector2(0, y), new Vector2(270, 140), btnSecondary, false, Loc.IsZh ? 34 : 38, () => Loc.SetLang(Loc.IsZh ? "en" : "zh"), Icon("Globe"));
            ArtButton(coverButtons, "Quit", Loc.T("ui.quit"), new Vector2(285, y), new Vector2(270, 140), btnSecondary, false, Loc.IsZh ? 38 : 30, () => Application.Quit(), Icon("Back"));
            UIKit.Text(coverButtons, "Hint", Body, Loc.T("ui.pinchHint"), 36, new Color(1, 1, 1, 0.7f), new Vector2(0, -460), new Vector2(880, 60), TextAlignmentOptions.Center, 0);
        }

        // 没有封面布景时的后备标题面板（老样式）
        void BuildTitleLegacy()
        {
            title = Sheet("UI_Title", new Vector3(0, 1.3f, -1.7f), new Vector2(1120, 800), new Color(0.23f, 0.16f, 0.36f, 0.94f));
            UIKit.Text(title, "Title", Head, Loc.T("game.title"), Loc.IsZh ? 104 : 96, UIKit.Cream, new Vector2(0, 292), new Vector2(860, 160), outline: 0.24f);
            Button(title, "Start", Loc.T("ui.start"), new Vector2(0, -250), new Vector2(560, 128), UIKit.Pumpkin, 54, true, () => Show(Screen.Nights));
        }

        // 封面里的面板（选夜、设置）：挂在 panelAnchor 下，底图是星空纸面板
        RectTransform CoverPanel(string name, string head)
        {
            RectTransform p;
            if (cover) p = AnchorCanvas(cover.panelAnchor, name, new Vector2(1280, 843), 0.00095f);
            else p = Sheet(name, new Vector3(0, 1.3f, -1.8f), new Vector2(1280, 843), Color.white);
            popScale[p] = p.localScale.x;
            UIKit.Img(p, "Panel", panel, Color.white, Vector2.zero, new Vector2(1280, 843)).preserveAspect = true;
            Fancy(p, "Head", Head, head, Loc.IsZh ? 60 : 54, new Color(1f, 0.92f, 0.6f), new Color(1f, 0.62f, 0.2f), new Vector2(0, 272), new Vector2(800, 90), 0.28f);   // 避开面板顶上的弯月装饰
            return p;
        }

        // ---------- 选择夜晚：三章 × 四夜，剪纸关卡卡 ----------
        void BuildNights()
        {
            nights = CoverPanel("UI_Nights", Loc.T("ui.nights"));
            Color[] chapterCol = { UIKit.Pumpkin, UIKit.Mint, UIKit.Candy };
            int unlocked = Progress.Unlocked;
            // 卡片上数字圆与星槽的位置（来自 Card_layout.json，相对卡片中心，单位：卡片宽高）
            var badge = new Vector2(0.143f - 0.5f, 0.551f - 0.5f); float[] starX = { 0.349f - 0.5f, 0.493f - 0.5f, 0.637f - 0.5f }; float starY = 0.226f - 0.5f;
            var cs = new Vector2(250, 146);
            for (int c = 0; c < NightDefs.CHAPTERS.Length; c++)
            {
                var ch = NightDefs.CHAPTERS[c];
                float y = 125 - c * 176;
                UIKit.Text(nights, "Chapter" + c, Body, Loc.T(ch.nameKey, ch.name), 30, chapterCol[c], new Vector2(-130, y + 80), new Vector2(800, 40), TextAlignmentOptions.Left, 0.18f);
                for (int k = 0; k < ch.nights.Length; k++)
                {
                    int idx = ch.nights[k] - 1;
                    var nd = NightDefs.NIGHTS[idx];
                    bool open = idx <= unlocked;
                    var pos = new Vector2(-405 + k * 270, y - 8);
                    var full = Loc.T(nd.nameKey, nd.name);
                    int dot = full.IndexOf('·');
                    string shortName = dot >= 0 ? full.Substring(dot + 1).Trim() : full;
                    var root = UIKit.Node(nights, "Night" + idx, pos, cs);
                    var glow = UIKit.Img(root, "Glow", UIKit.Glow(), new Color(1f, 0.8f, 0.4f, 0), Vector2.zero, cs * 1.25f);
                    var face = UIKit.Img(root, "Face", card, open ? Color.white : new Color(0.62f, 0.58f, 0.7f), Vector2.zero, cs); face.preserveAspect = true;
                    UIKit.Text(root, "Num", fontTitleEn, (idx + 1).ToString(), 40, chapterCol[c], new Vector2(badge.x * cs.x, badge.y * cs.y + 2), new Vector2(60, 50), TextAlignmentOptions.Center, 0.2f);
                    var nameT = UIKit.Text(root, "Name", Body, open ? shortName : Loc.T("ui.locked"), Loc.IsZh ? 28 : 22, UIKit.Ink, new Vector2(26, open ? 18 : 26), new Vector2(150, open ? 70 : 40), TextAlignmentOptions.Center, 0);
                    nameT.enableAutoSizing = true; nameT.fontSizeMin = 16; nameT.fontSizeMax = Loc.IsZh ? 28 : 22;   // 10-30：长关卡名自动缩字，不出框
                    int st = Progress.Stars(idx);
                    for (int sIdx = 0; sIdx < 3; sIdx++)
                        UIKit.Img(root, "Star" + sIdx, Icon("Star"), sIdx < st ? Color.white : new Color(1, 1, 1, 0), new Vector2(starX[sIdx] * cs.x, starY * cs.y), new Vector2(30, 30)).preserveAspect = true;
                    if (!open) UIKit.Img(root, "Lock", Icon("Lock"), Color.white, new Vector2(26, -24), new Vector2(44, 44)).preserveAspect = true;   // 10-30：锁放到文字正下方（原来压着"未解锁"）
                    var b = root.gameObject.AddComponent<WorldButton>();
                    b.face = face; b.glow = glow; b.baseColor = face.color; b.hoverColor = Color.Lerp(face.color, Color.white, 0.3f); b.interactable = open;
                    b.onClick = () => StartNightFaded(idx);
                    var bc = root.gameObject.AddComponent<BoxCollider>(); bc.size = new Vector3(cs.x * 0.9f, cs.y * 0.85f, 20); bc.isTrigger = true;
                }
            }
            ArtButton(nights, "Back", Loc.T("ui.back"), new Vector2(0, -362), new Vector2(240, 96), btnSecondary, false, 32, () => { Sfx.Instance?.Ui("back"); Show(Screen.Title); }, Icon("Back"));
        }

        // ---------- 设置 ----------
        void BuildSettings()
        {
            settings = CoverPanel("UI_Settings", Loc.T("ui.settings"));
            Image Bar(string n, float y, string label, System.Func<float> get, System.Action<float> set)
            {
                UIKit.Text(settings, n + "Label", Body, label, 40, UIKit.Cream, new Vector2(-330, y), new Vector2(300, 70), TextAlignmentOptions.Left, 0.18f);
                UIKit.Img(settings, n + "Track", UIKit.Panel(), new Color(0.12f, 0.07f, 0.18f, 0.95f), new Vector2(110, y), new Vector2(380, 44));
                var fill = UIKit.Img(settings, n + "Fill", UIKit.Panel(), UIKit.Mint, new Vector2(110 - 190, y), new Vector2(380 * get(), 44));
                fill.rectTransform.pivot = new Vector2(0, 0.5f); fill.rectTransform.anchoredPosition = new Vector2(110 - 190, y);
                ArtButton(settings, n + "Minus", "-", new Vector2(-150, y), new Vector2(120, 80), btnSecondary, false, 48, () => { set(get() - 0.1f); Sfx.Instance?.Ui("btn"); });
                ArtButton(settings, n + "Plus", "+", new Vector2(370, y), new Vector2(120, 80), btnSecondary, false, 48, () => { set(get() + 0.1f); Sfx.Instance?.Ui("btn"); });
                return fill;
            }
            musicBar = Bar("Music", 210, Loc.T("ui.music"), () => GameSettings.Music, v => GameSettings.Music = v);
            sfxBar = Bar("Sfx", 112, Loc.T("ui.sfx"), () => GameSettings.Sfx, v => GameSettings.Sfx = v);
            UIKit.Text(settings, "GazeLabel", Body, Loc.T("ui.gazeDot"), 40, UIKit.Cream, new Vector2(-330, 18), new Vector2(300, 70), TextAlignmentOptions.Left, 0.18f);
            var gz = ArtButton(settings, "Gaze", GameSettings.GazeDot ? Loc.T("ui.on") : Loc.T("ui.off"), new Vector2(110, 18), new Vector2(260, 88), btnSecondary, false, 36, () => { GameSettings.GazeDot = !GameSettings.GazeDot; Sfx.Instance?.Ui("btn"); });
            gazeLabel = gz.label as TextMeshProUGUI;
            // 新手教学：默认只在第一次玩时出现；打开后下一次第一夜会再教一遍
            UIKit.Text(settings, "TutLabel", Body, Loc.T("ui.tutorial"), 40, UIKit.Cream, new Vector2(-330, -78), new Vector2(300, 70), TextAlignmentOptions.Left, 0.18f);
            var tb = ArtButton(settings, "Tutorial", GameSettings.TutorialDone ? Loc.T("ui.off") : Loc.T("ui.on"), new Vector2(110, -78), new Vector2(260, 88), btnSecondary, false, 36, () => { GameSettings.TutorialDone = !GameSettings.TutorialDone; Sfx.Instance?.Ui("btn"); });
            tutLabel = tb.label as TextMeshProUGUI;
            ArtButton(settings, "Recenter", Loc.T("ui.recenter"), new Vector2(0, -185), new Vector2(480, 112), btnPrimary, true, Loc.IsZh ? 40 : 34, () =>
            {
                Sfx.Instance?.Ui("btn");
                var xr = Object.FindAnyObjectByType<XRFloorOrigin>(FindObjectsInactive.Include);
                if (xr && pointer && pointer.cam) Fade(() => { xr.Recenter(pointer.cam); if (cover && cover.Active) { cover.Exit(); cover.Enter(); } });
            });
            ArtButton(settings, "Back", Loc.T("ui.back"), new Vector2(0, -300), new Vector2(240, 96), btnSecondary, false, 32, () => { Sfx.Instance?.Ui("back"); Show(Screen.Title); }, Icon("Back"));
        }

        // ---------- 糖果铺：用糖果币买永久升级（10-29 用户）----------
        void BuildShop()
        {
            shop = CoverPanel("UI_Shop", Loc.T("ui.shop"));
            FillShop();
        }

        void FillShop()
        {
            if (!shop) return;
            for (int i = shop.childCount - 1; i >= 0; i--) { var c = shop.GetChild(i); if (c.name != "Panel" && c.name != "Head") Destroy(c.gameObject); }
            // 现有糖果币（右上角）
            // 10-31 重排：糖果币放到标题右边（原来压在第三张卡右上角）
            UIKit.Img(shop, "CoinIcon", Icon("Candy"), Color.white, new Vector2(445, 272), new Vector2(60, 60)).preserveAspect = true;
            UIKit.Text(shop, "Coins", Body, "× " + Progress.Coins, 42, UIKit.Gold, new Vector2(475 + 85, 272), new Vector2(170, 60), TextAlignmentOptions.Left, 0.18f);
            // 卡片：竖版贴纸（ShopCard），从上到下：图标（圆垫里）→ 名字 → 说明 → 等级圆点 → 价格按钮，互不重叠
            var cs = new Vector2(330, 360);
            for (int i = 0; i < Shop.All.Length; i++)
            {
                string id = Shop.All[i];
                int lv = Shop.Level(id), max = Shop.Max(id), cost = Shop.NextCost(id);
                var pos = new Vector2(-370 + i * 370, 32);
                var root = UIKit.Node(shop, "Up_" + id, pos, cs);
                UIKit.Img(root, "Face", shopCard ? shopCard : card, Color.white, Vector2.zero, cs).preserveAspect = true;
                // 圆垫中心在卡片顶部往下 23% 处（ShopCard 画的位置），图标放在里面
                UIKit.Img(root, "Icon", Icon(id == Shop.Cauldron ? "Cauldron" : id == Shop.Magnet ? "Candy" : "Star"), Color.white, new Vector2(0, cs.y * 0.27f), new Vector2(84, 84)).preserveAspect = true;
                UIKit.Text(root, "Name", Body, Loc.T("shop." + id), Loc.IsZh ? 36 : 30, UIKit.Ink, new Vector2(0, 26), new Vector2(290, 52), TextAlignmentOptions.Center, 0);
                UIKit.Text(root, "Desc", Body, Loc.T("shop." + id + ".desc"), Loc.IsZh ? 24 : 20, new Color(0.3f, 0.2f, 0.38f), new Vector2(0, -18), new Vector2(280, 46), TextAlignmentOptions.Center, 0);
                for (int k = 0; k < max; k++)
                    UIKit.Img(root, "Pip" + k, UIKit.Dot(), k < lv ? UIKit.Pumpkin : new Color(0.3f, 0.2f, 0.38f, 0.3f), new Vector2((k - (max - 1) * 0.5f) * 34, -62), new Vector2(22, 22));
                bool canBuy = cost > 0 && Progress.Coins >= cost;
                string label = cost < 0 ? Loc.T("shop.max") : cost.ToString();
                var b = ArtButton(root, "Buy", label, new Vector2(0, -122), new Vector2(240, 92), canBuy ? btnPrimary : btnSecondary, canBuy, 36, () =>
                {
                    if (Shop.Buy(id)) { Sfx.Instance?.Ui("star"); FillShop(); }
                    else Sfx.Instance?.Ui("back");
                }, cost > 0 ? Icon("Candy") : null);
                b.interactable = cost > 0;
            }
            UIKit.Text(shop, "Rule", Body, Loc.T("shop.rule"), 24, new Color(1, 1, 1, 0.62f), new Vector2(0, -170), new Vector2(1080, 40), TextAlignmentOptions.Center, 0);
            // 10-31 手套外观：购买一次后永久拥有，可随时切换。一排胶囊按钮（左边色点），装备中的用主按钮样式高亮
            UIKit.Text(shop, "GloveHead", Body, Loc.T("shop.glove"), 26, UIKit.Gold, new Vector2(0, -210), new Vector2(800, 38), TextAlignmentOptions.Center, 0.14f);
            string[] skinIds  = { "default", "pumpkin", "ghost", "night" };
            int[]    skinCosts = { 0, 20, 30, 40 };
            string equippedSkin = PlayerPrefs.GetString("tof_skin", "default");
            for (int si = 0; si < skinIds.Length; si++)
            {
                string skinId = skinIds[si]; int skinCost = skinCosts[si];
                bool owned   = skinId == "default" || PlayerPrefs.GetInt("tof_skin_" + skinId + "_owned", 0) == 1;
                bool equipped = skinId == equippedSkin;
                var bpos = new Vector2(-418 + si * 286, -268);
                string name = Loc.T("shop.glove." + skinId);
                bool canAfford = Progress.Coins >= skinCost;
                string label = owned ? name : name + "  " + skinCost;
                var sb = ArtButton(shop, "Skin_" + skinId, label, bpos, new Vector2(280, 88), equipped ? btnPrimary : btnSecondary, equipped, Loc.IsZh ? 28 : 22, () =>
                {
                    if (equipped) return;
                    if (!owned)
                    {
                        if (Progress.Coins < skinCost) { Sfx.Instance?.Ui("back"); return; }
                        Progress.Coins -= skinCost; PlayerPrefs.SetInt("tof_skin_" + skinId + "_owned", 1);
                        Sfx.Instance?.Ui("star");
                    }
                    else Sfx.Instance?.Ui("btn");
                    PlayerPrefs.SetString("tof_skin", skinId); PlayerPrefs.Save();
                    GameSettings.NotifyChanged(); FillShop();
                });
                sb.interactable = !equipped && (owned || canAfford);
                Color swatch = skinId == "pumpkin" ? new Color(1f, 0.54f, 0.12f) : skinId == "ghost" ? new Color(0.32f, 0.87f, 0.77f) : skinId == "night" ? new Color(0.44f, 0.34f, 0.6f) : new Color(0.98f, 0.95f, 0.88f);
                UIKit.Img(shop, "Swatch_" + skinId, UIKit.Dot(), new Color(0.16f, 0.1f, 0.24f), bpos + new Vector2(-112, 0), new Vector2(46, 46));
                UIKit.Img(shop, "SwatchC_" + skinId, UIKit.Dot(), swatch, bpos + new Vector2(-112, 0), new Vector2(36, 36));
            }
            ArtButton(shop, "Back", Loc.T("ui.back"), new Vector2(0, -368), new Vector2(240, 96), btnSecondary, false, 32, () => { Sfx.Instance?.Ui("back"); Show(Screen.Title); }, Icon("Back"));
        }

        // ---------- 暂停（游戏中，从侧边"暂停"按钮打开）----------
        void BuildPause()
        {
            pause = Sheet("UI_Pause", new Vector3(0, 1.3f, -1.7f), new Vector2(1280, 843), Color.clear);
            popScale[pause] = pause.localScale.x;
            UIKit.Img(pause, "Panel", panel, Color.white, Vector2.zero, new Vector2(1280, 843)).preserveAspect = true;
            Fancy(pause, "Head", Head, Loc.T("ui.paused"), Loc.IsZh ? 72 : 64, new Color(1f, 0.92f, 0.6f), new Color(1f, 0.62f, 0.2f), new Vector2(0, 255), new Vector2(800, 110), 0.28f);
            ArtButton(pause, "Resume", Loc.T("ui.resume"), new Vector2(0, 128), new Vector2(560, 180), btnPrimary, true, Loc.IsZh ? 56 : 46, () => { Sfx.Instance?.Ui("btn"); loop.paused = false; Show(Screen.Playing); }, Icon("Play"));
            // 10-31 暂停页加音量调节 + 语言切换，复用 BuildSettings 同款滑块
            Image PBar(string n, float y, string label, System.Func<float> get, System.Action<float> set)
            {
                UIKit.Text(pause, n + "Label", Body, label, 34, UIKit.Cream, new Vector2(-300, y), new Vector2(240, 52), TextAlignmentOptions.Left, 0.14f);
                UIKit.Img(pause, n + "Track", UIKit.Panel(), new Color(0.12f, 0.07f, 0.18f, 0.95f), new Vector2(110, y), new Vector2(380, 38));
                var fill = UIKit.Img(pause, n + "Fill", UIKit.Panel(), UIKit.Mint, new Vector2(110 - 190, y), new Vector2(380 * get(), 38));
                fill.rectTransform.pivot = new Vector2(0, 0.5f); fill.rectTransform.anchoredPosition = new Vector2(110 - 190, y);
                ArtButton(pause, n + "Minus", "-", new Vector2(-148, y), new Vector2(92, 64), btnSecondary, false, 42, () => { set(get() - 0.1f); Sfx.Instance?.Ui("btn"); });
                ArtButton(pause, n + "Plus", "+", new Vector2(368, y), new Vector2(92, 64), btnSecondary, false, 42, () => { set(get() + 0.1f); Sfx.Instance?.Ui("btn"); });
                return fill;
            }
            musicBarP = PBar("PMus", -8, Loc.T("ui.music"), () => GameSettings.Music, v => GameSettings.Music = v);
            sfxBarP   = PBar("PSfx", -76, Loc.T("ui.sfx"),   () => GameSettings.Sfx,   v => GameSettings.Sfx   = v);
            // 10-31 暂停页语言切换（中文/英文），避免玩家去封面翻设置
            ArtButton(pause, "LangP", Loc.T("ui.lang"), new Vector2(0, -156), new Vector2(340, 80), btnSecondary, false, Loc.IsZh ? 34 : 38, () => Loc.SetLang(Loc.IsZh ? "en" : "zh"), Icon("Globe"));
            // 10-31 Restart：loop.paused = false 移入淡出回调（之前在 fade 前重置会导致短暂闪现游戏画面）
            ArtButton(pause, "Restart", Loc.T("ui.restart"), new Vector2(-220, -288), new Vector2(380, 124), btnSecondary, false, 38, () => { Sfx.Instance?.Ui("btn"); Fade(() => { loop.paused = false; loop.StartNight(loop.game.nightIndex); }); }, Icon("Retry"));
            ArtButton(pause, "Title",   Loc.T("ui.toTitle"),  new Vector2( 220, -288), new Vector2(380, 124), btnSecondary, false, 38, () => { Sfx.Instance?.Ui("back"); ToTitleFaded(); }, Icon("Back"));
        }

        /// <summary>应用切到后台 / 失焦时自动弹出暂停（由 GameLoop.OnApplicationPause/Focus 调用）</summary>
        public void OpenPause()
        {
            if (screen != Screen.Playing) return;
            Show(Screen.Paused);
        }

        void BuildHud()
        {
            top = UIKit.Canvas(transform, "UI_Top", Wp(new Vector3(0, 1.43f, -2.55f)), Wp(eye), new Vector2(1500, 200));
            UIKit.Img(top, "NightRibbon", UIKit.Ribbon(), UIKit.Violet, new Vector2(-470, 20), new Vector2(520, 110));
            nightText = UIKit.Text(top, "Night", Body, "", 38, UIKit.Cream, new Vector2(-470, 24), new Vector2(440, 90), outline: 0.2f);
            // 月亮进度：深色槽 + 金色填充 + 一颗沿槽移动的月亮
            UIKit.Img(top, "MoonTrack", UIKit.Panel(), new Color(0.2f, 0.14f, 0.32f, 0.95f), new Vector2(110, 20), new Vector2(560, 60));
            moonFill = UIKit.Img(top, "MoonFill", UIKit.Panel(), UIKit.Gold, new Vector2(110, 20), new Vector2(560, 60));
            moonFill.rectTransform.pivot = new Vector2(0, 0.5f); moonFill.rectTransform.anchoredPosition = new Vector2(110 - 280, 20);
            var moonSp = Icon("Moon");
            moonIcon = UIKit.Img(top, "Moon", moonSp ? moonSp : UIKit.Dot(), moonSp ? Color.white : new Color(1f, 0.96f, 0.8f), new Vector2(-170, 20), new Vector2(92, 92));
            moonIcon.preserveAspect = true;
            moonPct = UIKit.Text(top, "MoonLabel", Body, Loc.T("ui.moon"), 26, UIKit.Cream, new Vector2(110, -40), new Vector2(400, 40), outline: 0.12f);
            // 糖果数
            // 糖果数：和月亮槽同一种深紫底，金边；图标用整套界面的糖果图
            UIKit.Img(top, "CandyRim", UIKit.Panel(), UIKit.Gold, new Vector2(540, 20), new Vector2(312, 122));
            UIKit.Img(top, "CandyBack", UIKit.Panel(), new Color(0.2f, 0.14f, 0.32f, 0.97f), new Vector2(540, 20), new Vector2(300, 110));
            var candySp = Icon("Candy");
            var candyIco = UIKit.Img(top, "CandyIcon", candySp ? candySp : UIKit.Dot(), candySp ? Color.white : UIKit.Pumpkin, new Vector2(452, 22), new Vector2(104, 104));
            candyIco.preserveAspect = true;
            candyText = UIKit.Text(top, "Candy", fontTitleEn, "12", 66, UIKit.Gold, new Vector2(580, 24), new Vector2(160, 90), outline: 0.22f);
            // 侧边小按钮（离开、跳过教学），放在左前方，远离桌面
            side = UIKit.Canvas(transform, "UI_Side", Wp(new Vector3(-0.74f, 1.14f, -1.38f)), Wp(eye), new Vector2(300, 260));   // 10-30：从偏 31° 挪到约 25°，不用转身
            // 10-31 按钮文字应该是"暂停"（ui.pause），不是"已暂停"（ui.paused）
            ArtButton(side, "Pause", Loc.T("ui.pause"), new Vector2(0, 60), new Vector2(270, 108), btnSecondary, false, 32, () => { Sfx.Instance?.Ui("btn"); loop.paused = true; Show(Screen.Paused); }, Icon("Pause"));
            skipBtn = ArtButton(side, "Skip", Loc.T("ui.skip"), new Vector2(0, -60), new Vector2(270, 108), btnSecondary, false, 28, () => loop.SkipLesson(), Icon("Play"));
            // 字幕彩带 + 教学进度点
            // 字幕彩带：桌面上方、视线略下方（约 -4°），从糖桶上方越过去，不被桌上的东西挡住
            // 10-30 分析：原来字幕在 2 米外、字高 3 厘米，头显里只有 0.8° 视角（舒适阅读要 1.3° 以上），又压在花哨的铁门和庄园上。
            // 挪近到 1.45 米、放在桌面远沿上方（视线自然下垂的位置），字号加大到约 1.5°；条子不透明，字是深墨色
            subtitle = UIKit.Canvas(transform, "UI_Subtitle", Wp(new Vector3(0, 0.98f, -1.33f)), Wp(eye), new Vector2(1300, 220));
            subtitle.localScale = Vector3.one * 0.0009f;
            subBack = UIKit.Img(subtitle, "Back", UIKit.Ribbon(), UIKit.Cream, new Vector2(0, 30), new Vector2(1260, 150));
            subText = UIKit.Text(subtitle, "Text", Body, "", 54, UIKit.Ink, new Vector2(0, 32), new Vector2(1100, 126), outline: 0);
            // 10-30 用户：提示要在两行以内、不能超出框——放不下时自动缩字（最小 38 号），框高只够两行
            subText.enableAutoSizing = true; subText.fontSizeMin = 38; subText.fontSizeMax = 54; subText.enableWordWrapping = true; subText.lineSpacing = -8;
            subIcon = UIKit.Img(subtitle, "Gesture", null, Color.white, new Vector2(-640, 34), new Vector2(170, 170)); subIcon.preserveAspect = true; subIcon.gameObject.SetActive(false);
            BuildSkillDock();
            dots = UIKit.Node(subtitle, "Dots", new Vector2(0, -62), new Vector2(600, 40));
            // 开夜大标题
            banner = UIKit.Canvas(transform, "UI_Banner", Wp(new Vector3(0, 1.3f, -1.85f)), Wp(eye), new Vector2(1400, 260));
            bannerText = UIKit.Text(banner, "Text", Head, "", Loc.IsZh ? 96 : 88, UIKit.Cream, Vector2.zero, new Vector2(1350, 220), outline: 0.26f);
        }

        void BuildResult()
        {
            result = Sheet("UI_Result", new Vector3(0, 1.28f, -1.7f), new Vector2(1280, 843), Color.clear);
            popScale[result] = result.localScale.x;
        }

        void FillResult(NightResult r)
        {
            for (int ci = result.childCount - 1; ci >= 0; ci--) { var co = result.GetChild(ci).gameObject; if (Application.isPlaying) Destroy(co); else DestroyImmediate(co); }
            bool win = r.win;
            UIKit.Img(result, "Panel", panel, Color.white, Vector2.zero, new Vector2(1280, 843)).preserveAspect = true;
            string head = r.allDone ? Loc.T("result.allDone") : win ? Loc.T("result.dawn")
                        : r.dawnFail ? Loc.T("result.failFew", null, new Dictionary<string, string> { { "n", r.candies.ToString() } }) : Loc.T("result.fail");
            Fancy(result, "Head", Head, head, Loc.IsZh ? 76 : 66, win ? new Color(1f, 0.95f, 0.6f) : new Color(0.85f, 0.85f, 1f), win ? new Color(1f, 0.6f, 0.15f) : new Color(0.6f, 0.55f, 0.85f), new Vector2(0, 262), new Vector2(1000, 120), 0.28f);
            // 10-31 重排：星星 → 剩糖 → 本夜统计 → 糖果币 → 挑战（三行）→ 小技巧（没守住时）→ 按钮，自上而下互不重叠、不出面板
            for (int st = 0; st < 3; st++)
            {
                var img = UIKit.Img(result, "Star" + st, Icon("Star"), st < r.stars ? Color.white : new Color(0.3f, 0.25f, 0.4f, 0.9f), new Vector2(-170 + st * 170, 125 + (st == 1 ? 18 : 0)), new Vector2(124, 124));
                img.preserveAspect = true;
                img.gameObject.AddComponent<PopIn>().delay = 0.25f + st * 0.25f;
            }
            var args = new Dictionary<string, string> { { "count", r.candies.ToString() } };
            UIKit.Img(result, "CandyIcon", Icon("Candy"), Color.white, new Vector2(-215, 22), new Vector2(72, 72)).preserveAspect = true;
            UIKit.Text(result, "Candies", Body, Loc.T("result.candies", null, args), 44, UIKit.Cream, new Vector2(-150 + 290, 22), new Vector2(580, 70), TextAlignmentOptions.Left, 0.18f);
            // 本夜统计：被偷走 · 消化 · 星星 · 隔空拉
            if (r.stats != null)
            {
                var sa = new Dictionary<string, string> { { "lost", r.lost.ToString() }, { "digested", r.stats.digested.ToString() }, { "stars", r.stats.stars.ToString() }, { "pulls", r.stats.pulls.ToString() } };
                UIKit.Text(result, "Stats", Body, Loc.T("result.stats", null, sa), 28, new Color(0.88f, 0.78f, 1f, 0.9f), new Vector2(0, -28), new Vector2(1060, 40), TextAlignmentOptions.Center, 0.12f);
            }
            // 糖果币：超过这一夜最好成绩的部分（Progress.Record 算好）
            {
                string coinLine = Progress.LastEarned > 0 ? Loc.T("result.coins", null, new Dictionary<string, string> { { "n", Progress.LastEarned.ToString() }, { "total", Progress.Coins.ToString() } })
                                 : win ? Loc.T("result.noCoins", null, new Dictionary<string, string> { { "best", Progress.BestCandies(loop != null && loop.game != null ? loop.game.nightIndex : 0).ToString() } }) : "";
                if (coinLine.Length > 0)
                {
                    var ct = UIKit.Text(result, "Coins", Body, coinLine, 28, Progress.LastEarned > 0 ? UIKit.Gold : new Color(1, 1, 1, 0.6f), new Vector2(0, -68), new Vector2(1060, 40), TextAlignmentOptions.Center, 0.16f);
                    ct.enableAutoSizing = true; ct.fontSizeMin = 20; ct.fontSizeMax = 28;
                }
            }
            // 挑战：最多 3 条，左边圆点（完成=薄荷绿实心，未完成=灰色空心环），首次达成在右边写 +N 糖果币
            if (r.challenges != null && r.challenges.Count > 0)
            {
                int night = loop != null && loop.game != null ? loop.game.nightIndex : 0;
                if (!_challengesProcessed)
                {
                    _challengesProcessed = true;
                    foreach (var ch in r.challenges)
                    {
                        if (!ch.done || ch.coins <= 0) continue;
                        string ck = "tof_ch_" + night + "_" + ch.id;
                        if (!PlayerPrefs.HasKey(ck)) { PlayerPrefs.SetInt(ck, 1); Progress.Coins += ch.coins; }
                    }
                }
                UIKit.Text(result, "ChHead", Body, Loc.T("result.challenges"), 26, new Color(1, 1, 1, 0.55f), new Vector2(-230, -104), new Vector2(500, 34), TextAlignmentOptions.Left, 0.12f);
                for (int ci = 0; ci < Mathf.Min(r.challenges.Count, 3); ci++)
                {
                    var ch = r.challenges[ci];
                    float cy = -146 - ci * 44;
                    bool first = ch.done && !PlayerPrefs.HasKey("tof_ch_" + night + "_" + ch.id + "_shown");
                    if (first) PlayerPrefs.SetInt("tof_ch_" + night + "_" + ch.id + "_shown", 1);
                    if (ch.done) UIKit.Img(result, "ChDot" + ci, UIKit.Dot(), UIKit.Mint, new Vector2(-462, cy), new Vector2(24, 24));
                    else UIKit.Img(result, "ChDot" + ci, UIKit.Ring(), new Color(1, 1, 1, 0.35f), new Vector2(-462, cy), new Vector2(26, 26));
                    string chText = Loc.T(ch.key, ch.text, ch.args);   // 按当前语言显示（中文兜底）
                    UIKit.Text(result, "ChText" + ci, Body, chText, 28, ch.done ? UIKit.Cream : new Color(1, 1, 1, 0.4f), new Vector2(-110, cy), new Vector2(660, 36), TextAlignmentOptions.Left, 0).enableWordWrapping = false;
                    if (ch.done && ch.coins > 0 && first)
                        UIKit.Text(result, "ChCoin" + ci, Body, Loc.T("result.coin.reward", null, new Dictionary<string, string> { { "n", ch.coins.ToString() } }), 26, UIKit.Gold, new Vector2(330, cy), new Vector2(260, 36), TextAlignmentOptions.Right, 0);
                }
            }
            // 没守住：一条小技巧
            if (!win && !string.IsNullOrEmpty(r.tipKey))
            {
                var tipT = UIKit.Text(result, "Tip", Body, Loc.T(r.tipKey, r.tip), 26, new Color(1f, 0.85f, 0.55f, 0.85f), new Vector2(0, -278), new Vector2(1060, 40), TextAlignmentOptions.Center, 0.1f);
                tipT.enableAutoSizing = true; tipT.fontSizeMin = 18; tipT.fontSizeMax = 26;
            }
            bool hasNext = win && !r.allDone;
            if (hasNext) ArtButton(result, "Next",  Loc.T("ui.next"),  new Vector2(285, -355), new Vector2(380, 124), btnPrimary,   true,  Loc.IsZh ? 46 : 38, () => StartNightFaded(r.next), Icon("Play"));
            ArtButton(result, "Retry", Loc.T("ui.retry"), new Vector2(hasNext ? -60 : 150, -355), new Vector2(300, 108), btnSecondary, false, 34, () => StartNightFaded(loop != null ? loop.game.nightIndex : 0), Icon("Retry"));
            ArtButton(result, "Menu",  Loc.T("ui.menu"),  new Vector2(hasNext ? -360 : -190, -355), new Vector2(hasNext ? 240 : 300, 108), btnSecondary, false, 32, () => ToTitleFaded(Screen.Nights), Icon("Moon"));
        }

        void Show(Screen s)
        {
            screen = s;
            bool coverMode = s == Screen.Title || s == Screen.Nights || s == Screen.Settings || s == Screen.Shop;
            if (cover) { if (coverMode) cover.Enter(); else cover.Exit(); }
            if (title) title.gameObject.SetActive(s == Screen.Title);
            if (coverLogo) coverLogo.gameObject.SetActive(s == Screen.Title);
            if (coverButtons) coverButtons.gameObject.SetActive(s == Screen.Title);
            nights.gameObject.SetActive(s == Screen.Nights);
            settings.gameObject.SetActive(s == Screen.Settings);
            if (shop) shop.gameObject.SetActive(s == Screen.Shop);
            pause.gameObject.SetActive(s == Screen.Paused);
            result.gameObject.SetActive(s == Screen.Result);
            bool play = s == Screen.Playing;
            top.gameObject.SetActive(play);
            if (skillDock && !play) skillDock.gameObject.SetActive(false);
            side.gameObject.SetActive(play);
            subtitle.gameObject.SetActive(play && subT < subDur);
            banner.gameObject.SetActive(play && bannerT > 0);
            if (pointer) pointer.active = true;
            // 出现动画：从 0.85 倍弹到 1（在 Update 里插值）
            foreach (var kv in popScale) if (kv.Key && kv.Key.gameObject.activeSelf) kv.Key.localScale = Vector3.one * kv.Value * 0.85f;
        }

        // ================= 事件 =================
        public void Handle(GameEvent e, GameLoop g)
        {
            switch (e.type)
            {
                case GameEventTypes.NightStart:
                    pendingResult = null; resultDelay = -1;
                    Show(Screen.Playing);
                    nightText.text = Loc.T(g.game.night.nameKey, g.game.night.name);
                    bannerText.text = nightText.text; bannerT = 3.2f; banner.gameObject.SetActive(true);
                    subT = subDur = 0; subtitle.gameObject.SetActive(false);
                    BuildDots(g.game.lesson != null ? g.game.lesson.steps.Count : g.game.brief != null && g.game.brief.steps.Count > 1 ? g.game.brief.steps.Count : 0);
                    break;
                case GameEventTypes.SkillCooling:
                    foreach (var sl in skillSlots) if (sl.id == e.strId) sl.deny = 1.8f;
                    break;
                case GameEventTypes.Say:
                    if (e.textKey == g.game.night?.nameKey) break;      // 夜名由大标题显示
                    subText.text = Loc.T(e.textKey, e.text, e.args);
                    {
                        var st = g.game.lesson != null && !g.game.lesson.done ? g.game.lesson.Current : g.game.brief != null ? g.game.brief.Current : null;
                        var gn = st != null && st.sayKey == e.textKey ? GestureFor(st.id) : null;
                        var gs = gn != null ? Gesture(gn) : null;
                        subIcon.sprite = gs; subIcon.gameObject.SetActive(gs != null);
                        subText.rectTransform.anchoredPosition = new Vector2(gs != null ? 60 : 0, 32);
                        subText.rectTransform.sizeDelta = new Vector2(gs != null ? 980 : 1080, 126);
                    }
                    subT = 0; subDur = Mathf.Max(2.5f, e.dur);
                    subtitle.gameObject.SetActive(true);
                    // 10-31 say 音效只在 Sfx.cs Handle() 里播（移除此处重复调用）
                    break;
                case GameEventTypes.LessonStep:
                case GameEventTypes.BriefStep:
                    for (int i = 0; i < dotImgs.Count; i++) dotImgs[i].color = i < e.index ? UIKit.Gold : i == e.index ? UIKit.Pumpkin : new Color(1, 1, 1, 0.35f);
                    break;
                case GameEventTypes.PracticeDone:
                    practiceBackT = 2.8f;
                    break;
                case GameEventTypes.LessonDone:
                case GameEventTypes.BriefDone:
                    foreach (var d in dotImgs) d.color = UIKit.Gold;
                    break;
                case GameEventTypes.Dawn:
                case GameEventTypes.Fail:
                    pendingResult = e.result;
                    _challengesProcessed = false; // 10-31 新夜晚结算到来时重置，保证每夜挑战奖励只发一次
                    if (!g.game.practice) Progress.Record(g.game.nightIndex, e.result);
                    resultDelay = e.type == GameEventTypes.Dawn ? 2.6f : 1.8f;
                    break;
            }
        }

        // ---------- 技能栏：左手边一列，显示本夜已解锁的手势技能（图标 = 要做的手势）和冷却 ----------
        class SkillSlot { public string id; public RectTransform root; public Image icon, ring, back; public TextMeshProUGUI name, cdText; public float pop, deny; public bool wasReady = true; }
        readonly List<SkillSlot> skillSlots = new List<SkillSlot>();
        RectTransform skillDock;
        static readonly string[] SkillOrder = { SkillIds.Pray, SkillIds.Sweep, SkillIds.Shield, SkillIds.Lamp };
        static string SkillGesture(string id) => id == SkillIds.Pray ? "Pray" : id == SkillIds.Sweep ? "Sweep" : id == SkillIds.Shield ? "Shield" : "Lamp";

        void BuildSkillDock()
        {
            // 左手边、桌面略高处（右手边是坩埚；余光里扫一眼就能看到冷却好了没），面朝玩家
            skillDock = UIKit.Canvas(transform, "UI_Skills", Wp(new Vector3(0.74f, 1.12f, -1.38f)), Wp(eye), new Vector2(260, 760));   // 10-30：从偏 35° 挪到约 25°，余光就能看到
            skillSlots.Clear();
            for (int i = 0; i < SkillOrder.Length; i++)
            {
                var sl = new SkillSlot { id = SkillOrder[i] };
                sl.root = UIKit.Node(skillDock, "Skill_" + sl.id, new Vector2(0, 285 - i * 190), new Vector2(240, 180));
                sl.back = UIKit.Img(sl.root, "Back", UIKit.Dot(), new Color(0.16f, 0.1f, 0.24f, 0.88f), new Vector2(0, 14), new Vector2(150, 150));
                sl.ring = UIKit.Img(sl.root, "Ring", UIKit.Ring(), UIKit.Mint, new Vector2(0, 14), new Vector2(160, 160));
                sl.ring.type = Image.Type.Filled; sl.ring.fillMethod = Image.FillMethod.Radial360; sl.ring.fillOrigin = (int)Image.Origin360.Top; sl.ring.fillClockwise = true;
                sl.icon = UIKit.Img(sl.root, "Icon", Gesture(SkillGesture(sl.id)), Color.white, new Vector2(0, 16), new Vector2(124, 124)); sl.icon.preserveAspect = true;
                sl.name = UIKit.Text(sl.root, "Name", Body, Loc.T("skill." + sl.id + ".name"), 24, UIKit.Cream, new Vector2(0, -78), new Vector2(240, 36), TextAlignmentOptions.Center, 0.2f);
                // 10-31：冷却中还去用：技能槽抖一下，中间显示还剩几秒
                sl.cdText = UIKit.Text(sl.root, "Cd", fontTitleEn, "", 54, UIKit.Cream, new Vector2(0, 16), new Vector2(150, 80), TextAlignmentOptions.Center, 0.25f);
                sl.cdText.gameObject.SetActive(false);
                skillSlots.Add(sl);
            }
        }

        void UpdateSkillDock(Game gm, float dt)
        {
            if (!skillDock) return;
            bool any = false;
            int row = 0;
            foreach (var sl in skillSlots)
            {
                bool has = gm.skills != null && gm.skills.TryGetValue(sl.id, out var st);
                sl.root.gameObject.SetActive(has);
                if (!has) continue;
                any = true;
                gm.skills.TryGetValue(sl.id, out var s);
                sl.root.anchoredPosition = new Vector2(0, 285 - row++ * 190);
                double max = gm.cfg.skills.Cooldown(sl.id);
                float k = max > 0 ? Mathf.Clamp01(1f - (float)(s.cd / max)) : 1f;
                bool ready = s.cd <= 0;
                sl.ring.fillAmount = k;
                sl.ring.color = ready ? UIKit.Mint : new Color(0.6f, 0.55f, 0.7f, 0.9f);
                sl.icon.color = ready ? Color.white : new Color(0.55f, 0.52f, 0.62f, 0.85f);
                if (ready && !sl.wasReady) sl.pop = 1;          // 冷却好了：弹一下
                sl.wasReady = ready;
                sl.pop = Mathf.MoveTowards(sl.pop, 0, dt * 2.5f);
                sl.root.localScale = Vector3.one * (1 + 0.18f * Mathf.Sin(sl.pop * Mathf.PI));
                sl.deny = ready ? 0 : Mathf.MoveTowards(sl.deny, 0, dt);
                bool showCd = sl.deny > 0;
                if (sl.cdText.gameObject.activeSelf != showCd) sl.cdText.gameObject.SetActive(showCd);
                if (showCd)
                {
                    sl.cdText.text = Mathf.CeilToInt((float)s.cd).ToString();
                    sl.root.anchoredPosition += new Vector2(Mathf.Sin(sl.deny * 60f) * 9f * Mathf.Clamp01(sl.deny - 1f), 0);
                }
            }
            if (skillDock.gameObject.activeSelf != any) skillDock.gameObject.SetActive(any);
        }

        void BuildDots(int n)
        {
            foreach (Transform c in dots) Destroy(c.gameObject);
            dotImgs.Clear();
            for (int i = 0; i < n; i++)
                dotImgs.Add(UIKit.Img(dots, "Dot" + i, UIKit.Diamond(), new Color(1, 1, 1, 0.35f), new Vector2((i - (n - 1) / 2f) * 48, 0), new Vector2(34, 34)));
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (screen == Screen.Playing && loop && loop.game.night != null)
            {
                var gm = loop.game;
                float m = gm.MoonProgress;
                moonFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(60, 560 * m), 60);
                moonIcon.rectTransform.anchoredPosition = new Vector2(110 - 280 + 560 * m, 20);
                if (gm.candyJar != lastCandy)
                {
                    // 10-31：多了是金色弹一下；少了（被偷走）红闪 + 往下一沉
                    if (lastCandy >= 0) { if (gm.candyJar < lastCandy) candyDrop = 1; else candyPunch = 1; }
                    lastCandy = gm.candyJar; candyText.text = gm.candyJar.ToString();
                }
                candyPunch = Mathf.MoveTowards(candyPunch, 0, dt * 3);
                candyDrop = Mathf.MoveTowards(candyDrop, 0, dt * 1.6f);
                candyText.rectTransform.localScale = Vector3.one * (1 + 0.35f * Mathf.Sin(candyPunch * Mathf.PI) - 0.12f * Mathf.Sin(candyDrop * Mathf.PI));
                candyText.rectTransform.anchoredPosition = new Vector2(580, 24 - 22f * Mathf.Sin(candyDrop * Mathf.PI));
                candyText.color = Color.Lerp(UIKit.Gold, new Color(1f, 0.3f, 0.36f), candyDrop);
                bool teaching = (gm.lesson != null && !gm.lesson.done) || (gm.brief != null && !gm.brief.done);
                UpdateSkillDock(gm, dt);
                skipBtn.gameObject.SetActive(teaching);
                // 字幕淡入淡出
                if (subDur > 0)
                {
                    subT += dt;
                    float a = Mathf.Clamp01(subT * 5) * Mathf.Clamp01((subDur - subT) * 3);
                    subBack.color = new Color(UIKit.Cream.r, UIKit.Cream.g, UIKit.Cream.b, a);
                    subText.alpha = a;
                    subIcon.color = new Color(1, 1, 1, a);
                    if (subT > subDur) { subtitle.gameObject.SetActive(dotImgs.Count > 0 && teaching); subText.alpha = 0; subBack.color = Color.clear; }
                }
                if (bannerT > 0)
                {
                    bannerT -= dt;
                    float a = Mathf.Clamp01((3.2f - bannerT) * 3) * Mathf.Clamp01(bannerT * 1.5f);
                    bannerText.alpha = a;
                    banner.localScale = Vector3.one * 0.001f * (0.9f + 0.1f * Mathf.Clamp01((3.2f - bannerT) * 4));
                    if (bannerT <= 0) banner.gameObject.SetActive(false);
                }
                if (practiceBackT > 0)
                {
                    practiceBackT -= dt;
                    if (practiceBackT <= 0) ToTitleFaded();
                }
                if (resultDelay > 0)
                {
                    resultDelay -= dt;
                    if (resultDelay <= 0 && pendingResult != null) { FillResult(pendingResult); Show(Screen.Result); }
                }
            }
            // 面板出现时从 0.85 弹到 1
            foreach (var kv in popScale)
                if (kv.Key && kv.Key.gameObject.activeSelf) kv.Key.localScale = Vector3.Lerp(kv.Key.localScale, Vector3.one * kv.Value, dt * 10);
            // 设置页：音量条与开关文字跟着设置走；视线光点开关
            if (screen == Screen.Settings && musicBar)
            {
                musicBar.rectTransform.sizeDelta = new Vector2(Mathf.Max(8, 380 * GameSettings.Music), 44);
                sfxBar.rectTransform.sizeDelta = new Vector2(Mathf.Max(8, 380 * GameSettings.Sfx), 44);
                if (gazeLabel) gazeLabel.text = GameSettings.GazeDot ? Loc.T("ui.on") : Loc.T("ui.off");
                if (tutLabel) tutLabel.text = GameSettings.TutorialDone ? Loc.T("ui.off") : Loc.T("ui.on");
            }
            // 10-31 暂停页也同步音量条
            if (screen == Screen.Paused && musicBarP)
            {
                musicBarP.rectTransform.sizeDelta = new Vector2(Mathf.Max(8, 380 * GameSettings.Music), 38);
                sfxBarP.rectTransform.sizeDelta   = new Vector2(Mathf.Max(8, 380 * GameSettings.Sfx),   38);
            }
            if (pointer && pointer.cursor) pointer.cursor.gameObject.SetActive(GameSettings.GazeDot);
        }
    }

    /// <summary>结算星星依次弹出</summary>
    public class PopIn : MonoBehaviour
    {
        public float delay;
        float t; bool rang;
        void OnEnable() { t = 0; rang = false; transform.localScale = Vector3.zero; }
        void Update()
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01((t - delay) * 3);
            float s = k <= 0 ? 0 : 1 + 0.3f * Mathf.Sin(k * Mathf.PI) * (1 - k);
            transform.localScale = Vector3.one * s * Mathf.SmoothStep(0, 1, k * 1.5f);
            if (k > 0 && !rang) { rang = true; Sfx.Instance?.Ui("star", 0.4f); }
        }
    }

    /// <summary>存档：已解锁到第几夜、每夜最好星数、每夜最多剩下几颗糖、糖果币</summary>
    public static class Progress
    {
        public static int Unlocked => PlayerPrefs.GetInt("tof_unlocked", 0);
        public static int Stars(int night) => PlayerPrefs.GetInt("tof_stars_" + night, 0);
        public static int BestCandies(int night) => PlayerPrefs.GetInt("tof_best_" + night, 0);
        public static int Coins { get => PlayerPrefs.GetInt("tof_coins", 0); set { PlayerPrefs.SetInt("tof_coins", Mathf.Max(0, value)); PlayerPrefs.Save(); } }
        /// <summary>上一次结算拿到的糖果币（结算页显示用）</summary>
        public static int LastEarned { get; private set; }
        public static bool LastBest { get; private set; }

        public static void Record(int night, NightResult r)
        {
            if (r == null) return;
            // 糖果币（10-29 用户）：只有天亮守住时，剩下的糖比这一夜以前最好的成绩多，才把多出来的部分换成币——
            // 比如这一夜上次剩 10 颗、这次剩 12 颗，只多给 2 枚；重复刷同一夜拿不到更多
            LastEarned = 0; LastBest = false;
            if (r.win)
            {
                int best = BestCandies(night);
                if (r.candies > best)
                {
                    LastEarned = r.candies - best; LastBest = true;
                    PlayerPrefs.SetInt("tof_best_" + night, r.candies);
                    PlayerPrefs.SetInt("tof_coins", Coins + LastEarned);
                }
            }
            if (r.stars > Stars(night)) PlayerPrefs.SetInt("tof_stars_" + night, r.stars);
            if (r.win) PlayerPrefs.SetInt("tof_unlocked", Mathf.Max(Unlocked, Mathf.Min(NightDefs.NIGHTS.Length - 1, night + 1)));
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// 糖果铺：用糖果币买永久升级。价格按"正常玩能攒到"定：12 夜每夜最多 12 枚、全部买齐要 130 枚，
    /// 一般玩家每夜守住 7~9 颗，一轮打完能买下大部分，想全部买齐就得回头把几夜打得更好。
    /// </summary>
    public static class Shop
    {
        public const string Cauldron = "cauldron", Magnet = "magnet", Digest = "digest";
        public static readonly string[] All = { Cauldron, Digest, Magnet };
        public static int[] Costs(string id) => id == Cauldron ? new[] { 25, 45 } : id == Digest ? new[] { 15, 30 } : new[] { 15 };
        public static int Max(string id) => Costs(id).Length;
        public static int Level(string id) => Mathf.Clamp(PlayerPrefs.GetInt("tof_up_" + id, 0), 0, Max(id));
        public static int NextCost(string id) { int l = Level(id); var c = Costs(id); return l < c.Length ? c[l] : -1; }
        public static bool Buy(string id)
        {
            int cost = NextCost(id);
            if (cost < 0 || Progress.Coins < cost) return false;
            Progress.Coins -= cost;
            PlayerPrefs.SetInt("tof_up_" + id, Level(id) + 1);
            PlayerPrefs.Save();
            return true;
        }
    }
}
