// 世界空间 UI 小工具：程序化生成贴纸风精灵（圆角牌子、斜切彩带、菱形、圆点、星星），以及搭面板/文字/按钮的快捷方法。
// 所有 UI 用 1 单位 = 1 毫米的世界空间画布（画布缩放 0.001），适合 VR 里 1~3 米距离阅读。
// 配色（来自设计文档）：夜紫描边、奶油牌面、南瓜橙、薄荷青、魔法紫、糖果粉。
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrickOrFreeze.UI
{
    public static class UIKit
    {
        public static readonly Color Ink = new Color32(0x2A, 0x18, 0x38, 0xFF);
        public static readonly Color Cream = new Color32(0xFF, 0xF1, 0xD6, 0xFF);
        public static readonly Color Pumpkin = new Color32(0xFF, 0x8A, 0x1F, 0xFF);
        public static readonly Color Mint = new Color32(0x52, 0xE0, 0xC4, 0xFF);
        public static readonly Color Violet = new Color32(0x8F, 0x5B, 0xD9, 0xFF);
        public static readonly Color Candy = new Color32(0xFF, 0x6F, 0xA8, 0xFF);
        public static readonly Color Gold = new Color32(0xFF, 0xD2, 0x4A, 0xFF);
        public static readonly Color NightBlue = new Color32(0x3B, 0x2A, 0x5C, 0xFF);

        static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();
        // 10-31 描边材质按 (字体, 宽度, 颜色) 共享，避免每次 Text() 都 new Material
        static readonly Dictionary<(TMP_FontAsset, float, Color), Material> outlineMats = new Dictionary<(TMP_FontAsset, float, Color), Material>();

        // ---------- 程序化精灵 ----------
        /// <summary>圆角牌子：白色填充 + 夜紫粗描边 + 底部一道暗边（贴纸厚度感），9 宫格可拉伸</summary>
        public static Sprite Panel() => Get("panel", () =>
        {
            const int N = 128, R = 40, O = 9;
            var t = NewTex(N, N);
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float d = RoundRectDist(x + 0.5f, y + 0.5f, N, N, R);    // <0 在里面
                    Color c = Color.clear;
                    if (d < 0)
                    {
                        float a = Mathf.Clamp01(-d);
                        bool rim = d > -O;
                        bool shade = !rim && y < O + 7 && d > -(O + 7);         // 底部厚度
                        c = rim ? Ink : shade ? new Color(0.8f, 0.78f, 0.82f) : Color.white;
                        c.a = a;
                    }
                    px[y * N + x] = c;
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(R + 4, R + 4, R + 4, R + 4));
        });

        /// <summary>斜切两端的彩带（标题、按钮）：白色填充 + 描边，9 宫格横向拉伸</summary>
        public static Sprite Ribbon() => Get("ribbon", () =>
        {
            const int W = 192, H = 96, O = 8, C = 30;
            var t = NewTex(W, H);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    // 两端是燕尾凹口：x < C 时，|y - H/2| 越大越往外，形成 > 形缺口
                    float notch = C * (1 - Mathf.Abs(y - H / 2f) / (H / 2f));
                    float left = x - notch, right = (W - 1 - x) - notch;
                    float d = -Mathf.Min(Mathf.Min(left, right), Mathf.Min(y, H - 1 - y));
                    Color c = Color.clear;
                    if (d < 0) { c = d > -O ? Ink : Color.white; c.a = Mathf.Clamp01(-d); }
                    px[y * W + x] = c;
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(C + 12, 12, C + 12, 12));
        });

        public static Sprite Diamond() => Get("diamond", () => Shape(64, (u, v) => 1 - (Mathf.Abs(u) + Mathf.Abs(v)), 0.16f));
        public static Sprite Dot() => Get("dot", () => Shape(64, (u, v) => 1 - Mathf.Sqrt(u * u + v * v), 0.16f));
        public static Sprite Ring() => Get("ring", () => Shape(128, (u, v) => 1 - Mathf.Abs(Mathf.Sqrt(u * u + v * v) - 0.86f) / 0.12f, 0f));   // 进度环
        public static Sprite Star() => Get("star", () => Shape(96, (u, v) =>
        {
            float a = Mathf.Atan2(v, u), r = Mathf.Sqrt(u * u + v * v);
            float k = Mathf.Cos(5 * (a + Mathf.PI / 2)) * 0.5f + 0.5f;         // 5 个角
            float rr = Mathf.Lerp(0.45f, 1f, Mathf.Pow(k, 3));
            return 1 - r / rr;
        }, 0.14f));
        /// <summary>柔光圆（发光底、指针光点）：中心实、边缘渐隐，无描边</summary>
        public static Sprite Glow() => Get("glow", () =>
        {
            const int N = 64; var t = NewTex(N, N); var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v)); a = a * a;
                    px[y * N + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
        });

        static Sprite Shape(int N, System.Func<float, float, float> f, float outline)
        {
            var t = NewTex(N, N); var px = new Color32[N * N];
            for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2 - 1, v = (y + 0.5f) / N * 2 - 1;
                    float s = f(u, v) * N / 2;   // 像素单位的“离边距离”
                    Color c = Color.clear;
                    if (s > 0) { c = s < outline * N / 2 ? Ink : Color.white; c.a = Mathf.Clamp01(s); }
                    px[y * N + x] = c;
                }
            t.SetPixels32(px); t.Apply();
            return Sprite.Create(t, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100);
        }

        static float RoundRectDist(float x, float y, int w, int h, float r)
        {
            float qx = Mathf.Abs(x - w / 2f) - (w / 2f - r), qy = Mathf.Abs(y - h / 2f) - (h / 2f - r);
            float ox = Mathf.Max(qx, 0), oy = Mathf.Max(qy, 0);
            return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
        }

        static Texture2D NewTex(int w, int h) => new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };

        static Sprite Get(string key, System.Func<Sprite> make)
        {
            if (!cache.TryGetValue(key, out var s) || s == null) cache[key] = s = make();
            return s;
        }

        // ---------- 搭建 ----------
        /// <summary>世界空间画布（1 单位 = 1 毫米），正面朝向 lookFrom 点</summary>
        public static RectTransform Canvas(Transform parent, string name, Vector3 worldPos, Vector3 lookFrom, Vector2 sizeMm)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.WorldSpace; cv.sortingOrder = 10;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizeMm;
            rt.position = worldPos;
            rt.localScale = Vector3.one * 0.001f;
            var fwd = worldPos - lookFrom; fwd.y = 0;      // 画布正面（-forward）朝向玩家；保持竖直
            rt.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            return rt;
        }

        public static RectTransform Node(Transform parent, string name, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static Image Img(Transform parent, string name, Sprite sp, Color c, Vector2 pos, Vector2 size)
        {
            var rt = Node(parent, name, pos, size);
            var im = rt.gameObject.AddComponent<Image>();
            im.sprite = sp; im.color = c; im.type = sp != null && sp.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            im.raycastTarget = false;
            return im;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, string s, float size, Color c, Vector2 pos, Vector2 box,
                                           TextAlignmentOptions align = TextAlignmentOptions.Center, float outline = 0.18f)
        {
            var rt = Node(parent, name, pos, box);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font; t.text = s; t.fontSize = size; t.color = c; t.alignment = align;
            t.enableWordWrapping = true; t.raycastTarget = false;
            t.overflowMode = TextOverflowModes.Overflow;
            if (outline > 0)
            {
                // 10-31 描边材质按 (字体, 宽度, 颜色) 缓存共享；属性直接写材质避免 TMP 内部 null 异常
                var baseMat = t.fontSharedMaterial;
                if (baseMat != null)
                {
                    var key = (font, outline, Ink);
                    if (!outlineMats.TryGetValue(key, out var mat) || mat == null)
                    {
                        mat = new Material(baseMat);
                        mat.EnableKeyword("OUTLINE_ON");
                        mat.SetFloat("_OutlineWidth", outline);
                        mat.SetColor("_OutlineColor", Ink);
                        outlineMats[key] = mat;
                    }
                    t.fontSharedMaterial = mat;
                }
            }
            return t;
        }
    }
}
