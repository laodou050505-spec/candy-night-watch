// 氛围粒子（代码里建，免得手调 Inspector）：
//   落叶  橙/红/棕的小叶片在圣坛周围慢慢旋转飘落
//   萤火  青绿色光点在环廊附近游荡、呼吸明灭
//   鬼火  紫色小火团从岛边缘的云海里缓缓升起再消散
// 粒子材质用 URP 粒子着色器（加法/透明），贴图用程序生成的软圆点。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class AmbientParticles : MonoBehaviour
    {
        public float areaRadius = 9f;

        public bool coverMode;            // 封面：只在漂浮小布景周围飘一些暖橙/淡紫的光点（本地坐标盒子）
        public Vector3 coverBox = new Vector3(2.4f, 1.4f, 1.6f);

        void Start()
        {
            var dot = SoftDot(64);
            if (coverMode)
            {
                Make("CoverMotes", dot, true, 7, coverBox, Vector3.zero,
                    life: 7, size: (0.012f, 0.026f), vel: new Vector3(0, 0.03f, 0), noise: 0.06f, spin: 0, blink: true,
                    colors: new[] { new Color(1f, 0.62f, 0.25f) * 2.2f, new Color(0.75f, 0.55f, 1f) * 2.0f, new Color(1f, 0.85f, 0.45f) * 2.0f });
                return;
            }
            var leaf = LeafTex(64);
            // 10-28 实机：余光里一直有东西飘会让人不舒服——数量减到原来的一半以下，萤火只在离座位 4 米外的一圈里飘
            Make("Leaves", leaf, false, 4, new Vector3(areaRadius * 2, 1, areaRadius * 2), new Vector3(0, 7, 0),
                life: 12, size: (0.07f, 0.12f), vel: new Vector3(0.15f, -0.35f, 0.05f), noise: 0.35f, spin: 120,
                colors: new[] { new Color(1f, 0.55f, 0.15f), new Color(0.9f, 0.3f, 0.12f), new Color(0.65f, 0.38f, 0.2f), new Color(1f, 0.75f, 0.25f) });
            var ff = Make("Fireflies", dot, true, 14, new Vector3(areaRadius * 1.8f, 2.2f, areaRadius * 1.8f), new Vector3(0, 1.4f, 0),
                life: 6, size: (0.025f, 0.05f), vel: Vector3.zero, noise: 0.15f, spin: 0, blink: true,
                colors: new[] { new Color(0.55f, 1f, 0.7f) * 2.2f, new Color(0.65f, 1f, 0.95f) * 2.0f });
            // 外圈发射：半径 areaRadius 的扁球壳，只从外侧 55% 厚度里出（内圈约 4 米留空，桌面附近不飘萤火）
            var fsh = ff.shape; fsh.shapeType = ParticleSystemShapeType.Sphere; fsh.radius = areaRadius; fsh.radiusThickness = 0.55f; fsh.scale = new Vector3(1, 0.12f, 1);
            ff.Clear(); ff.Simulate(6f, true, true); ff.Play();     // 按新形状重新预热，开场就不会有一团萤火挤在桌边
            Make("WillOWisps", dot, true, 6, new Vector3(9f, 0.6f, 12f), new Vector3(-15f, 1.0f, -1f),       // 鬼火：在山坡墓园上方飘
                life: 9, size: (0.25f, 0.5f), vel: new Vector3(0, 0.45f, 0), noise: 0.3f, spin: 0, blink: true,
                colors: new[] { new Color(0.75f, 0.45f, 1f) * 2.4f, new Color(0.45f, 1f, 0.75f) * 2.0f });
        }

        ParticleSystem Make(string name, Texture2D tex, bool additive, float rate, Vector3 box, Vector3 center, float life, (float, float) size,
                  Vector3 vel, float noise, float spin, Color[] colors, bool blink = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = center;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.prewarm = true; main.duration = life;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
            main.startSpeed = 0;
            main.startSize = new ParticleSystem.MinMaxCurve(size.Item1, size.Item2);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var grad = new Gradient();
            var keys = new GradientColorKey[colors.Length];
            for (int i = 0; i < colors.Length; i++) keys[i] = new GradientColorKey(colors[i], i / Mathf.Max(1f, colors.Length - 1));
            grad.SetKeys(keys, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 1) });
            main.startColor = new ParticleSystem.MinMaxGradient(grad) { mode = ParticleSystemGradientMode.RandomColor };
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = rate;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = box;
            var vo = ps.velocityOverLifetime; vo.enabled = true; vo.space = ParticleSystemSimulationSpace.World;
            vo.x = new ParticleSystem.MinMaxCurve(vel.x * 0.5f, vel.x * 1.5f);
            vo.y = new ParticleSystem.MinMaxCurve(vel.y * 0.7f, vel.y * 1.3f);
            vo.z = new ParticleSystem.MinMaxCurve(vel.z * 0.5f, vel.z * 1.5f);
            var nz = ps.noise; nz.enabled = true; nz.strength = noise; nz.frequency = 0.35f; nz.scrollSpeed = 0.2f; nz.quality = ParticleSystemNoiseQuality.Medium;
            if (spin > 0) { var ro = ps.rotationOverLifetime; ro.enabled = true; ro.z = new ParticleSystem.MinMaxCurve(-spin * Mathf.Deg2Rad, spin * Mathf.Deg2Rad); }
            var col = ps.colorOverLifetime; col.enabled = true;
            var a = new Gradient();
            if (blink)
                a.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.15f), new GradientAlphaKey(0.2f, 0.4f), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0.3f, 0.8f), new GradientAlphaKey(0, 1) });
            else
                a.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.1f), new GradientAlphaKey(1, 0.85f), new GradientAlphaKey(0, 1) });
            col.color = a;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh2 = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(sh2);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", additive ? 2 : 0);   // 2 = Additive, 0 = Alpha
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)(additive ? UnityEngine.Rendering.BlendMode.One : UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha));
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            ps.Play();
            return ps;
        }

        static Texture2D SoftDot(int n)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                    float a = Mathf.Clamp01(1 - d); a = a * a * (3 - 2 * a);
                    float core = Mathf.Clamp01(1 - d * 3f);
                    t.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(a * 0.8f + core)));
                }
            t.Apply(); return t;
        }

        static Texture2D LeafTex(int n)
        {
            // 枫叶状剪影：极坐标下 5 瓣的半径函数 + 叶柄
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n - 0.5f, (y + 0.5f) / n - 0.45f) * 2;
                    float ang = Mathf.Atan2(p.y, p.x), r = p.magnitude;
                    float lobe = 0.62f + 0.3f * Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2.5f + 1.57f)), 0.6f);
                    float edge = Mathf.Clamp01((lobe - r) * 18);
                    bool stem = Mathf.Abs(p.x) < 0.04f && p.y < -0.2f && p.y > -0.85f;
                    float vein = Mathf.Abs(p.x) < 0.025f && p.y > -0.2f ? 0.75f : 1f;
                    float a = Mathf.Max(edge, stem ? 1 : 0);
                    t.SetPixel(x, y, new Color(vein, vein, vein, a));
                }
            t.Apply(); return t;
        }
    }
}
