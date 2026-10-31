// 对应原型 src/logic/vec.js。大部分直接用 UnityEngine.Vector3 运算，
// 这里只保留和 Unity 内置行为不同、必须与 JS 保持一致的几个函数。
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public static class Vec
    {
        // 与 JS 一致：长度为 0 时按 1 处理（返回零向量）；不像 Vector3.normalized 那样对 <1e-5 的短向量直接归零。
        public static Vector3 Norm(Vector3 a)
        {
            float l = a.magnitude;
            if (l == 0f) l = 1f;
            return new Vector3(a.x / l, a.y / l, a.z / l);
        }

        public static float DistXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        // 不夹取的线性插值（JS 的 lerp 不夹 t）
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(
            a.x + (b.x - a.x) * t,
            a.y + (b.y - a.y) * t,
            a.z + (b.z - a.z) * t);

        // 两方向夹角（度），先各自归一化再夹取 acos 的输入
        public static float AngleDeg(Vector3 a, Vector3 b)
        {
            float c = Vector3.Dot(Norm(a), Norm(b));
            return Mathf.Acos(Mathf.Max(-1f, Mathf.Min(1f, c))) * 180f / Mathf.PI;
        }

        public static float Clamp(float v, float lo, float hi) => Mathf.Max(lo, Mathf.Min(hi, v));
    }
}
