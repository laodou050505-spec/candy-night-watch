// 程序化手部骨架：按 OpenXR 26 关节顺序生成一只手的关节位置。用途：桌面调试时画手套、手套预览图、教学示范手。
// 手的本地坐标：+Z 指向手指，+Y 是手背，原点在手腕。右手拇指在本地 -X 一侧，左手镜像（Unity 左手坐标系）。
using UnityEngine;

namespace TrickOrFreeze.Input
{
    public static class HandJoints
    {
        public const int Palm = 0, Wrist = 1;
        public const int ThumbMeta = 2, ThumbProx = 3, ThumbDist = 4, ThumbTip = 5;
        public const int IndexMeta = 6, IndexProx = 7, IndexInter = 8, IndexDist = 9, IndexTip = 10;
        public const int MiddleMeta = 11, MiddleProx = 12, MiddleInter = 13, MiddleDist = 14, MiddleTip = 15;
        public const int RingMeta = 16, RingProx = 17, RingInter = 18, RingDist = 19, RingTip = 20;
        public const int LittleMeta = 21, LittleProx = 22, LittleInter = 23, LittleDist = 24, LittleTip = 25;
        public const int Count = 26;
        // 四指：起始关节下标（Meta），之后依次 Prox/Inter/Dist/Tip
        public static readonly int[] FingerBase = { IndexMeta, MiddleMeta, RingMeta, LittleMeta };
    }

    public struct HandShape
    {
        public float index, middle, ring, little;   // 四指弯曲 0 伸直 … 1 握拳
        public float thumbCurl;                     // 拇指弯曲
        public float pinch;                         // 0 … 1：拇指尖贴到食指尖
        public float spread;                        // 0 … 1：五指额外张开（南瓜灯手势示范用）
        public static HandShape Open => new HandShape { index = 0.08f, middle = 0.1f, ring = 0.12f, little = 0.14f, thumbCurl = 0.05f };
        public static HandShape Relaxed => new HandShape { index = 0.25f, middle = 0.32f, ring = 0.38f, little = 0.42f, thumbCurl = 0.2f };
        public static HandShape Pinch => new HandShape { index = 0.42f, middle = 0.45f, ring = 0.5f, little = 0.55f, thumbCurl = 0.3f, pinch = 1f };
        public static HandShape Fist => new HandShape { index = 0.95f, middle = 0.95f, ring = 0.95f, little = 0.95f, thumbCurl = 0.8f };
        public static HandShape Lerp(HandShape a, HandShape b, float t) => new HandShape
        {
            index = Mathf.Lerp(a.index, b.index, t), middle = Mathf.Lerp(a.middle, b.middle, t), ring = Mathf.Lerp(a.ring, b.ring, t),
            little = Mathf.Lerp(a.little, b.little, t), thumbCurl = Mathf.Lerp(a.thumbCurl, b.thumbCurl, t), pinch = Mathf.Lerp(a.pinch, b.pinch, t), spread = Mathf.Lerp(a.spread, b.spread, t),
        };
    }

    public static class HandPoses
    {
        // 右手、伸直时的指根（掌指关节）位置与三节指骨长度（中等成人手，米）
        static readonly Vector3[] Mcp = { new Vector3(-0.024f, 0f, 0.088f), new Vector3(-0.004f, 0.002f, 0.092f), new Vector3(0.015f, 0f, 0.086f), new Vector3(0.031f, -0.004f, 0.077f) };
        static readonly Vector3[] Cmc = { new Vector3(-0.012f, 0f, 0.02f), new Vector3(-0.002f, 0.001f, 0.02f), new Vector3(0.008f, 0f, 0.02f), new Vector3(0.016f, -0.002f, 0.018f) };
        static readonly float[][] Len = { new[] { 0.040f, 0.024f, 0.022f }, new[] { 0.044f, 0.028f, 0.023f }, new[] { 0.041f, 0.026f, 0.022f }, new[] { 0.032f, 0.019f, 0.020f } };
        static readonly float[] Splay = { -6f, 0f, 5f, 11f };   // 四指张开角（绕手背法线，度；正值往小指侧）
        static readonly float[] SplayWide = { -13f, -2f, 9f, 18f }; // spread=1 时额外张开

        /// <summary>生成 26 个关节（世界坐标）。right=true 为右手。</summary>
        public static Vector3[] Build(bool right, Vector3 wristPos, Quaternion wristRot, HandShape s, Vector3[] outJoints = null)
        {
            var J = outJoints != null && outJoints.Length == HandJoints.Count ? outJoints : new Vector3[HandJoints.Count];
            float mx = right ? 1f : -1f;                       // 左手：本地 X 镜像
            Vector3 W(Vector3 local) => wristPos + wristRot * new Vector3(local.x * mx, local.y, local.z);
            J[HandJoints.Wrist] = W(Vector3.zero);
            J[HandJoints.Palm] = W(new Vector3(-0.004f, 0f, 0.048f));
            float[] curls = { s.index, s.middle, s.ring, s.little };
            for (int f = 0; f < 4; f++)
            {
                int b = HandJoints.FingerBase[f];
                J[b] = W(Cmc[f]);
                var p = Mcp[f];
                J[b + 1] = W(p);
                // 指向：先绕 Y 张开，再逐节绕 X 往掌心（-Y）弯
                var splay = Quaternion.Euler(0, (Splay[f] + SplayWide[f] * s.spread) * (1 - curls[f] * 0.6f), 0);
                float[] ang = { curls[f] * 72f, curls[f] * 98f, curls[f] * 62f };
                float acc = 0;
                for (int k = 0; k < 3; k++)
                {
                    acc += ang[k];
                    var dir = splay * (Quaternion.Euler(acc, 0, 0) * Vector3.forward);
                    p += dir * Len[f][k];
                    J[b + 2 + k] = W(p);
                }
            }
            // 拇指：从掌根斜向前、往掌心下方伸出；捏合时指尖贴向食指尖
            var t0 = new Vector3(-0.02f, -0.008f, 0.024f);
            var tDir = Quaternion.Euler(18f + s.thumbCurl * 25f - s.spread * 10f, -38f + s.thumbCurl * 30f - s.spread * 14f, 0) * Vector3.forward;
            var t1 = t0 + tDir * 0.036f;
            var bend = Quaternion.Euler(12f + s.thumbCurl * 40f, 10f + s.thumbCurl * 25f, 0);
            var t2 = t1 + bend * tDir * 0.031f;
            var t3 = t2 + bend * bend * tDir * 0.027f;
            if (s.pinch > 0)
            {
                // 食指尖（本地）作为目标：拇指尖贴过去，中间两节按比例跟着收
                var inv = Quaternion.Inverse(wristRot);
                var it = inv * (J[HandJoints.IndexTip] - wristPos); it.x *= mx;
                var target = it + new Vector3(-0.004f, -0.006f, -0.002f);
                float k = Mathf.SmoothStep(0, 1, s.pinch);
                var t3p = Vector3.Lerp(t3, target, k);
                var mid = Vector3.Lerp(t1, t3p, 0.55f) + new Vector3(-0.012f, -0.006f, 0) * k;
                t2 = Vector3.Lerp(t2, mid, k);
                t3 = t3p;
            }
            J[HandJoints.ThumbMeta] = W(t0); J[HandJoints.ThumbProx] = W(t1); J[HandJoints.ThumbDist] = W(t2); J[HandJoints.ThumbTip] = W(t3);
            return J;
        }

        /// <summary>手背法线（指向手背外侧）：由“手腕→中指根”和“食指根→小指根”求得，左右手方向一致。</summary>
        public static Vector3 Dorsal(Vector3[] J, bool right)
        {
            var a = J[HandJoints.MiddleProx] - J[HandJoints.Wrist];
            var b = J[HandJoints.LittleProx] - J[HandJoints.IndexProx];
            // Unity 左手坐标系：右手 a×b 指向手背，左手相反
            var n = right ? Vector3.Cross(a, b) : Vector3.Cross(b, a);
            return n.normalized;
        }
    }
}
