// 浮空烛海：子物体（每支蜡烛）各自缓慢上下浮动、轻微摇晃。单个 Update 统一驱动，避免 140 个组件各自 Update。
// 天亮时（SetDawn 1）蜡烛按随机顺序逐一熄灭（火焰缩没）。
// 火苗下若有名为 "Halo" 的光晕面片（封面用）：始终朝向镜头，随火苗一起明灭。
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class CandleSea : MonoBehaviour
    {
        public float amplitude = 0.18f;
        public float period = 6.5f;
        struct C { public Transform t; public Vector3 p; public Quaternion r; public float ph, sp, off; public Transform flame; public Vector3 fs; public Transform halo; public Vector3 hs; }
        readonly List<C> cs = new List<C>();
        float dawn;
        Camera cam;

        void Start()
        {
            foreach (Transform t in transform)
            {
                Transform fl = null;
                foreach (Transform ch in t) if (ch.name.StartsWith("Candle_Flame")) fl = ch;
                Transform halo = null;
                foreach (var ch in t.GetComponentsInChildren<Transform>(true)) if (ch.name == "Halo") halo = ch;
                cs.Add(new C { t = t, p = t.localPosition, r = t.localRotation, ph = Random.value * 6.283f, sp = Random.Range(0.8f, 1.25f), off = Random.value, flame = fl, fs = fl ? fl.localScale : Vector3.one, halo = halo, hs = halo ? halo.localScale : Vector3.one });
            }
        }

        public void SetDawn(float k) => dawn = Mathf.Clamp01(k);

        void Update()
        {
            float t = Time.time * Mathf.PI * 2 / period;
            for (int i = 0; i < cs.Count; i++)
            {
                var c = cs[i];
                float w = t * c.sp + c.ph;
                c.t.localPosition = c.p + Vector3.up * (Mathf.Sin(w) * amplitude);
                c.t.localRotation = Quaternion.Euler(Mathf.Sin(w * 0.7f) * 3f, 0, Mathf.Cos(w * 0.6f) * 3f) * c.r;
                if (c.flame)
                {
                    float on = dawn <= c.off ? 1f : Mathf.Clamp01(1 - (dawn - c.off) * 12f);
                    float flick = 1 + (Mathf.PerlinNoise(Time.time * 9f, c.ph) - 0.5f) * 0.35f;
                    c.flame.localScale = new Vector3(c.fs.x, c.fs.y * flick, c.fs.z) * on;
                    if (c.halo)
                    {
                        if (cam == null) cam = Camera.main;
                        if (cam) c.halo.rotation = cam.transform.rotation;
                        c.halo.localScale = c.hs * (0.85f + (flick - 1) * 0.8f);
                    }
                }
            }
        }
    }
}
