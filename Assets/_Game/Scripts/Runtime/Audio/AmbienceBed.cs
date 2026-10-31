// 夜晚环境声（和音乐分开一层，音量跟“音效”走）：
//   底噪   夜虫 + 微风循环（2D，很轻），封面和游戏里都有
//   坩埚   冒泡循环，3D 挂在坩埚上——只在游戏里响，越靠近越清楚
//   偶发   远处猫头鹰、更远处的狼嚎、铁拱门吱呀——在场景里真实的方位发声，间隔随机，
//          只是“点一下”气氛：音量小、距离远，不会盖过小鬼的声音
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.AudioSys
{
    public class AmbienceBed : MonoBehaviour
    {
        public GameLoop loop;
        public AudioClip nightLoop, bubbleLoop;
        public AudioClip[] owl, wolf, gate;
        public float nightVol = 0.32f, bubbleVol = 0.5f;

        AudioSource bed, bubble, oneShot;
        float tOwl, tWolf, tGate;

        void Start()
        {
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 AmbienceBed.Start 开始");
            bed = Loop2D(nightLoop);
            if (bubbleLoop)
            {
                var go = new GameObject("CauldronBubble"); go.transform.SetParent(transform, false);
                bubble = go.AddComponent<AudioSource>();
                bubble.clip = bubbleLoop; bubble.loop = true; bubble.playOnAwake = false;
                bubble.spatialBlend = 1; bubble.rolloffMode = AudioRolloffMode.Logarithmic; bubble.minDistance = 0.35f; bubble.maxDistance = 5f; bubble.dopplerLevel = 0;
                bubble.volume = 0; bubble.Play();
            }
            var og = new GameObject("AmbOneShot"); og.transform.SetParent(transform, false);
            oneShot = og.AddComponent<AudioSource>();
            oneShot.playOnAwake = false; oneShot.spatialBlend = 1; oneShot.rolloffMode = AudioRolloffMode.Linear; oneShot.minDistance = 5; oneShot.maxDistance = 60; oneShot.dopplerLevel = 0;
            tOwl = Random.Range(12f, 25f); tWolf = Random.Range(45f, 80f); tGate = Random.Range(30f, 60f);
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 AmbienceBed.Start 完成");
        }

        AudioSource Loop2D(AudioClip c)
        {
            if (!c) return null;
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0; s.volume = 0; s.priority = 200;
            s.Play();
            return s;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime, sfx = TrickOrFreeze.UI.GameSettings.Sfx;
            var g = loop ? loop.game : null;
            bool play = g != null && g.phase == Phases.Playing;
            bool paused = loop && loop.paused;
            if (bed) bed.volume = Mathf.MoveTowards(bed.volume, nightVol * sfx * (paused ? 0.6f : 1f), dt * 0.4f);
            if (bubble)
            {
                if (loop) bubble.transform.position = loop.W(Layout.BOX.pos + Vector3.up * Layout.BOX.height);
                bubble.volume = Mathf.MoveTowards(bubble.volume, play && !paused ? bubbleVol * sfx : 0, dt * 0.8f);
            }
            if (paused) return;
            tOwl -= dt; tWolf -= dt; tGate -= dt;
            // 猫头鹰：在左右后方的树上（离座位 12~18 米）
            if (tOwl <= 0) { tOwl = Random.Range(22f, 45f); Shot(owl, Bearing(Random.Range(70f, 160f) * (Random.value < 0.5f ? -1 : 1), Random.Range(12f, 18f), 5f), 0.35f); }
            // 狼嚎：很远（远处庄园后的山上），很轻、很少
            if (tWolf <= 0) { tWolf = Random.Range(70f, 140f); Shot(wolf, Bearing(Random.Range(-40f, 40f), 45f, 8f), 0.22f); }
            // 铁拱门吱呀：正前方的拱门（只在游戏里，风吹的感觉）
            if (tGate <= 0) { tGate = Random.Range(40f, 90f); if (play) Shot(gate, new Vector3(Layout.WINDOW.x, 1.2f, Layout.WINDOW.z), 0.3f); }
        }

        // 方位角（0 = 正前方 -Z，正值往 +X）+ 距离 + 高度 → 规则层坐标
        static Vector3 Bearing(float deg, float dist, float h)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * dist, h, -Mathf.Cos(a) * dist);
        }

        void Shot(AudioClip[] set, Vector3 local, float vol)
        {
            if (set == null || set.Length == 0 || !oneShot) return;
            var c = set[Random.Range(0, set.Length)];
            if (!c) return;
            oneShot.transform.position = loop ? loop.W(local) : local;
            oneShot.pitch = Random.Range(0.95f, 1.05f);
            oneShot.PlayOneShot(c, vol * TrickOrFreeze.UI.GameSettings.Sfx);
        }
    }
}
