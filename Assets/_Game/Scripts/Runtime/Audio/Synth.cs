// 程序化音频合成：所有音效与背景音乐都在启动时合成为 AudioClip（无外部音频资源，零版权风险）。
// 乐器：
//   Bell    八音盒/钢片琴（正弦 + 非整数泛音 + 指数衰减）
//   Pluck   拨弦（Karplus-Strong）
//   Pad     柔和合成垫（三把轻微失谐的锯齿 + 低通）
//   Noise   带通噪声（风、刷、嘶）
//   Bloop   卡通“啵”（快速滑音正弦）
using System;
using UnityEngine;

namespace TrickOrFreeze.AudioSys
{
    public static class Synth
    {
        public const int SR = 44100;

        public static AudioClip Make(string name, float seconds, Action<float[]> fill, bool loop = false)
        {
            int n = Mathf.CeilToInt(seconds * SR);
            var data = new float[n];
            fill(data);
            Normalize(data, 0.92f);
            var clip = AudioClip.Create(name, n, 1, SR, false);
            clip.SetData(data, 0);
            return clip;
        }

        public static void Normalize(float[] d, float peak)
        {
            float m = 1e-6f;
            for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
            float k = peak / m;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }

        public static float Midi(float note) => 440f * Mathf.Pow(2f, (note - 69f) / 12f);

        // 八音盒音色：基频 + 2.76x / 5.4x / 8.9x 非谐泛音（钢片的金属感）
        public static void Bell(float[] d, float t0, float freq, float dur, float vol, float bright = 1f)
        {
            int s0 = (int)(t0 * SR), n = (int)(dur * SR);
            float[] ratios = { 1f, 2.76f, 5.4f, 8.93f };
            float[] amps = { 1f, 0.45f * bright, 0.22f * bright, 0.08f * bright };
            float[] decays = { 2.2f, 4.5f, 8f, 14f };
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                if (s0 + i < 0) continue;
                float t = i / (float)SR;
                float atk = Mathf.Min(1f, t / 0.003f);
                float v = 0;
                for (int h = 0; h < 4; h++) v += amps[h] * Mathf.Sin(2 * Mathf.PI * freq * ratios[h] * t) * Mathf.Exp(-decays[h] * t / Mathf.Max(dur, 0.2f) * 2.2f);
                d[s0 + i] += v * atk * vol;
            }
        }

        public static void Pluck(float[] d, float t0, float freq, float dur, float vol, float damp = 0.996f, int seed = 1)
        {
            int s0 = (int)(t0 * SR), n = (int)(dur * SR);
            int period = Mathf.Max(2, (int)(SR / freq));
            var buf = new float[period];
            var rng = new System.Random(seed);
            for (int i = 0; i < period; i++) buf[i] = (float)(rng.NextDouble() * 2 - 1);
            int idx = 0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                if (s0 + i < 0) { idx = (idx + 1) % period; continue; }
                int nxt = (idx + 1) % period;
                float y = buf[idx];
                buf[idx] = (buf[idx] + buf[nxt]) * 0.5f * damp;
                idx = nxt;
                d[s0 + i] += y * vol;
            }
        }

        public static void Pad(float[] d, float t0, float freq, float dur, float vol, float attack = 0.4f, float release = 0.8f)
        {
            int s0 = (int)(t0 * SR), n = (int)((dur + release) * SR);
            float lp = 0, k = 0.06f;
            float[] det = { 0.996f, 1f, 1.004f };
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                if (s0 + i < 0) continue;
                float t = i / (float)SR;
                float env = t < attack ? t / attack : t < dur ? 1 : Mathf.Max(0, 1 - (t - dur) / release);
                float v = 0;
                foreach (var dt in det) { float ph = (freq * dt * t) % 1f; v += ph * 2 - 1; }
                lp += (v / 3f - lp) * k;
                d[s0 + i] += lp * env * vol;
            }
        }

        // 带通噪声：简易双极点共振滤波
        public static void Noise(float[] d, float t0, float dur, float vol, float centerHz, float q = 2f, float attack = 0.005f, float fadePow = 2f, int seed = 3, float sweepTo = -1)
        {
            int s0 = (int)(t0 * SR), n = (int)(dur * SR);
            var rng = new System.Random(seed);
            float y1 = 0, y2 = 0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                if (s0 + i < 0) continue;
                float t = i / (float)n;
                float fc = sweepTo > 0 ? Mathf.Lerp(centerHz, sweepTo, t) : centerHz;
                float w = 2 * Mathf.PI * fc / SR;
                float r = Mathf.Clamp(1 - w / (2 * q), 0.5f, 0.9995f);
                float x = (float)(rng.NextDouble() * 2 - 1);
                float y = x * (1 - r) + 2 * r * Mathf.Cos(w) * y1 - r * r * y2;
                y2 = y1; y1 = y;
                float env = Mathf.Min(1, i / (attack * SR + 1)) * Mathf.Pow(1 - t, fadePow);
                d[s0 + i] += y * env * vol * 6;
            }
        }

        // 滑音正弦（卡通“啵”“咻”）
        public static void Bloop(float[] d, float t0, float f0, float f1, float dur, float vol, float shape = 1f, bool square = false)
        {
            int s0 = (int)(t0 * SR), n = (int)(dur * SR);
            double ph = 0;
            for (int i = 0; i < n && s0 + i < d.Length; i++)
            {
                if (s0 + i < 0) continue;
                float t = i / (float)n;
                float f = Mathf.Lerp(f0, f1, Mathf.Pow(t, shape));
                ph += f / SR;
                float s = Mathf.Sin((float)(ph * 2 * Math.PI));
                if (square) s = Mathf.Sign(s) * 0.5f + s * 0.5f;
                float env = Mathf.Min(1, i / (0.004f * SR)) * Mathf.Pow(1 - t, 1.6f);
                d[s0 + i] += s * env * vol;
            }
        }
    }
}
