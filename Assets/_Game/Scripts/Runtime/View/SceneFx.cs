// 游戏事件 → 粒子特效。所有粒子用一个通用的“星屑”粒子系统按需 Emit（颜色/速度/大小可调），避免每种特效一套资源。
// 关键特效：
//   定身    霜花一圈（青白）            grab      手边一团软光
//   入坩埚  绿色魔药泡泡上涌            消化      咕嘟大泡泡 + 小骷髅头形状的烟（用粒子大小渐变近似）
//   晃掉糖  糖果色碎屑                  偷糖      粉色糖屑 + 糖桶跳一下
//   变星星  金色爆闪 + 星尾 + 夜空新星   合十      青白冲击环 + 漫天霜尘
//   外挥    暖色风尘向两侧扫开          护桶      紫色月光罩合拢
//   教学完成 金色彩带纸屑
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public class SceneFx : MonoBehaviour
    {
        public ParticleSystem sparks;     // 通用星屑（加法混合，带拖尾）
        public ParticleSystem puffs;      // 通用软烟团（alpha 混合，变大淡出）
        public ParticleSystem ring;       // 冲击环（单粒子，大小从 0 → R）
        public Transform candyBucket, cauldron;
        public SkyStars skyStars;

        static readonly Color Frost = new Color(0.72f, 0.92f, 1f), Gold = new Color(1f, 0.82f, 0.35f), Potion = new Color(0.5f, 1f, 0.55f),
            Pink = new Color(1f, 0.45f, 0.7f), Violet = new Color(0.72f, 0.55f, 1f), Warm = new Color(1f, 0.7f, 0.4f);

        Vector3 W(GameLoop g, Vector3 p) => g.W(p);

        // 手伸进坩埚被烫：冒一股白色热气 + 几颗绿色魔药飞沫
        void OnEnable() { HandView.SceneFxBurn += OnBurn; }
        void OnDisable() { HandView.SceneFxBurn -= OnBurn; }
        void OnBurn(Vector3 local)
        {
            var gl = FindAnyObjectByType<GameLoop>(); if (!gl) return;
            var w = gl.W(local + Vector3.up * 0.04f);
            Puff(w, new Color(1f, 1f, 1f, 0.55f), 6, 0.05f, 0.25f);
            Burst(w, new Color(0.5f, 1f, 0.5f), 8, 0.25f, 0.8f, 0.008f, 0.6f);
        }

        public void Burst(Vector3 worldPos, Color c, int n, float speed, float up, float size = 0.012f, float life = 0.8f)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = worldPos;
                var d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y) * up + 0.1f;
                ep.velocity = d * speed;
                ep.startColor = Color.Lerp(c, Color.white, Random.Range(0f, 0.3f));
                ep.startSize = size * Random.Range(0.6f, 1.4f);
                ep.startLifetime = life * Random.Range(0.6f, 1.2f);
                sparks.Emit(ep, 1);
            }
        }

        void Puff(Vector3 worldPos, Color c, int n, float size, float speed)
        {
            var ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < n; i++)
            {
                ep.position = worldPos + Random.insideUnitSphere * size * 0.3f;
                ep.velocity = (Random.insideUnitSphere + Vector3.up * 0.6f) * speed;
                ep.startColor = c;
                ep.startSize = size * Random.Range(0.7f, 1.3f);
                ep.startLifetime = Random.Range(0.8f, 1.4f);
                puffs.Emit(ep, 1);
            }
        }

        void Ring(Vector3 worldPos, Color c, float radius)
        {
            var ep = new ParticleSystem.EmitParams { position = worldPos, startColor = c, startSize = radius * 2, startLifetime = 0.6f, velocity = Vector3.zero };
            ring.Emit(ep, 1);
        }

        public void Handle(GameEvent e, GameLoop g)
        {
            var game = g.game;
            Vector3 P(float up = 0) => W(g, e.pos + Vector3.up * up);
            float H(int id) { var t = game.toys.Find(x => x.id == id); return t != null ? t.Kind.height : 0.2f; }
            switch (e.type)
            {
                case GameEventTypes.GazeOn:
                {
                    var t = game.toys.Find(x => x.id == e.id);
                    if (t != null && t.residual <= 0) { Burst(W(g, t.pos + Vector3.up * t.Kind.height * 0.5f), Frost, 14, 0.25f, 0.6f, 0.008f, 0.6f); }
                    break;
                }
                case GameEventTypes.Slip: Burst(P(0.1f), Pink, 8, 0.3f, 0.6f); break;
                case GameEventTypes.Grab: Burst(P(H(e.id) * 0.6f), Frost, 12, 0.25f, 0.4f, 0.01f, 0.5f); break;
                case GameEventTypes.CloseCall: Burst(P(0.2f), Gold, 24, 0.5f, 1f); break;
                case GameEventTypes.Boxed:
                    // 小鬼先沿弧线飞到锅口（EnemyView 入锅动画约 0.24 秒到锅口），一头栽进去的那一下才溅起魔药
                    StartCoroutine(Later(0.24f, () =>
                    {
                        Burst(W(g, Layout.BOX.pos + Vector3.up * Layout.BOX.height), Potion, 30, 0.35f, 1.2f, 0.012f, 1f);
                        Puff(W(g, Layout.BOX.pos + Vector3.up * Layout.BOX.height), new Color(0.45f, 0.9f, 0.5f, 0.6f), 4, 0.06f, 0.08f);
                    }));
                    break;
                case GameEventTypes.Digested:
                    // 咕嘟：大泡泡 + 冒出一缕骷髅烟
                    Puff(W(g, Layout.BOX.pos + Vector3.up * (Layout.BOX.height + 0.04f)), new Color(0.6f, 1f, 0.6f, 0.75f), 6, 0.09f, 0.12f);
                    Burst(W(g, Layout.BOX.pos + Vector3.up * Layout.BOX.height), Potion, 20, 0.2f, 2f, 0.014f, 1.4f);
                    break;
                case GameEventTypes.Pat: Ring(W(g, Layout.BOX.pos + Vector3.up * (Layout.BOX.height + 0.01f)), new Color(0.6f, 1f, 0.6f, 0.8f), 0.18f); break;
                case GameEventTypes.BoxFull: Burst(W(g, Layout.BOX.pos + Vector3.up * Layout.BOX.height), new Color(1f, 0.4f, 0.4f), 16, 0.4f, 1f); break;
                case GameEventTypes.Shaken: Burst(P(0.2f), new Color(1f, 0.95f, 0.6f), 14, 0.25f, 0.5f); break;
                case GameEventTypes.CandyDrop: Burst(P(), Pink, 22, 0.4f, 0.8f, 0.01f, 0.7f); break;
                case GameEventTypes.CandyBack: Burst(W(g, Layout.JAR.pos + Vector3.up * Layout.JAR.height), new Color(1f, 0.7f, 0.85f), 26, 0.3f, 1.2f); break;
                case GameEventTypes.Steal: Burst(W(g, Layout.JAR.pos + Vector3.up * (Layout.JAR.height + 0.02f)), Pink, 16, 0.3f, 1f); break;
                case GameEventTypes.Charged: { var t = game.toys.Find(x => x.id == e.id); if (t != null) Burst(W(g, t.pos + Vector3.up * t.Kind.height * 0.5f), Gold, 20, 0.2f, 0.5f, 0.01f, 0.6f); break; }
                case GameEventTypes.Launch: Burst(P(0.1f), Gold, 34, 0.7f, 0.8f, 0.012f, 0.8f); Ring(P(0.1f), new Color(1f, 0.85f, 0.4f, 0.8f), 0.3f); break;
                case GameEventTypes.Star:
                    // 10-31：原地炸开一团金色火花，冒出一颗五角星飞上夜空（SkyStars）
                    Burst(P(0.1f), Gold, 46, 1.1f, 0.7f, 0.014f, 0.9f); Ring(P(0.1f), new Color(1f, 0.9f, 0.5f, 0.9f), 0.45f);
                    skyStars?.AddStar(e.kind, P(0.1f));
                    break;
                case GameEventTypes.NightStart: skyStars?.Clear(); break;
                case GameEventTypes.Land: if (e.heavy) Puff(P(), new Color(0.7f, 0.6f, 0.8f, 0.5f), 6, 0.15f, 0.25f); break;
                case GameEventTypes.WallHit: Puff(P(0.05f), new Color(0.75f, 0.7f, 0.8f, 0.6f), 7, 0.12f, 0.25f); Burst(P(0.1f), Gold, 8, 0.25f, 0.6f); break;   // 撞上矮墙：一团石灰 + 冒金星
                case GameEventTypes.Split: Burst(P(0.1f), new Color(0.9f, 0.9f, 1f), 30, 0.6f, 0.8f); Puff(P(0.1f), new Color(0.95f, 0.95f, 1f, 0.7f), 5, 0.08f, 0.2f); break;
                case GameEventTypes.Revealed: Burst(P(0.06f), Violet, 8, 0.2f, 0.4f); break;
                case GameEventTypes.HopIn: Burst(P(0.1f), Gold, 10, 0.3f, 1f); break;
                // 10-31 新增：糖被偷走——紫色烟雾
                case GameEventTypes.CandyLost: if (e.hasPos) Puff(P(), Violet, 8, 0.08f, 0.15f); else Puff(W(g, Layout.JAR.pos + Vector3.up * Layout.JAR.height), Violet, 8, 0.08f, 0.15f); break;
                // 10-31 解冻：高速小冰屑
                case GameEventTypes.Thaw: if (e.hasPos) { Burst(P(0.1f), Frost, 16, 0.35f, 0.7f, 0.007f, 0.5f); Ring(P(0.08f), new Color(0.72f, 0.92f, 1f, 0.6f), 0.1f); } break;
                // 10-31 首次 Introduce：聚光高亮（用 Gold Burst 加白色光晕）
                case GameEventTypes.Introduce:
                {
                    var toy = game.toys.Find(x => x.id == e.id);
                    if (toy != null)
                    {
                        var wp = W(g, toy.pos + Vector3.up * toy.Kind.height * 0.5f);
                        Burst(wp, Gold, 30, 0.4f, 0.5f, 0.012f, 1f);
                        Ring(wp, new Color(1f, 0.9f, 0.5f, 0.7f), 0.5f);
                    }
                    break;
                }
                // 10-31 护桶结束：紫色消散
                case GameEventTypes.ShieldEnd: Puff(W(g, Layout.JAR.pos + Vector3.up * (Layout.JAR.height + 0.05f)), Violet, 10, 0.12f, 0.2f); break;
                case GameEventTypes.BossGone: Burst(P(0.2f), Gold, 80, 0.9f, 1f, 0.016f, 1.4f); break;
                case GameEventTypes.Skill:
                    switch (e.strId)
                    {
                        case "pray":
                            Ring(W(g, new Vector3(0, Layout.TABLE.y + 0.05f, Layout.TABLE.center.z)), new Color(0.75f, 0.95f, 1f, 0.9f), 2.6f);
                            Burst(W(g, new Vector3(0, Layout.TABLE.y + 0.3f, -0.9f)), Frost, 140, 1.2f, 0.6f, 0.01f, 1.6f);
                            break;
                        case "sweep":
                            for (int s = -1; s <= 1; s += 2)
                            {
                                var ep = new ParticleSystem.EmitParams();
                                for (int i = 0; i < 70; i++)
                                {
                                    ep.position = W(g, new Vector3(s * 0.15f, Layout.TABLE.y + 0.15f, -0.45f));
                                    ep.velocity = worldRotate(g, new Vector3(s * Random.Range(1.5f, 3f), Random.Range(0.1f, 0.6f), Random.Range(-1.2f, -0.2f)));
                                    ep.startColor = Color.Lerp(Warm, Color.white, Random.value * 0.4f);
                                    ep.startSize = Random.Range(0.006f, 0.016f);
                                    ep.startLifetime = Random.Range(0.6f, 1.1f);
                                    sparks.Emit(ep, 1);
                                }
                            }
                            break;
                        case "lamp": if (e.hasPos) { Burst(P(), new Color(1f, 0.85f, 0.5f), 40, 0.35f, 0.5f); Ring(P(), new Color(1f, 0.8f, 0.4f, 0.7f), 0.42f); } break;
                        case "shield": Burst(W(g, Layout.JAR.pos + Vector3.up * (Layout.JAR.height + 0.05f)), Violet, 60, 0.45f, 0.4f, 0.01f, 1.2f); break;
                    }
                    break;
                case GameEventTypes.LessonOk: Burst(W(g, new Vector3(0, Layout.TABLE.y + 0.35f, -0.75f)), Gold, 40, 0.5f, 0.6f, 0.014f, 1.1f); break;
                case GameEventTypes.Dawn:
                    Burst(W(g, new Vector3(0, Layout.TABLE.y + 0.5f, -1.2f)), Gold, 160, 1.4f, 1f, 0.016f, 2.2f);
                    break;
            }
        }

        Vector3 worldRotate(GameLoop g, Vector3 v) => g.worldRoot.TransformDirection(v);

        static System.Collections.IEnumerator Later(float sec, System.Action a) { yield return new WaitForSeconds(sec); a(); }
    }
}
