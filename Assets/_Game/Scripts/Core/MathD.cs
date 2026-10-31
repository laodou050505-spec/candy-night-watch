// 双精度数学辅助。
// 原型 JS 全程 double。规则层的“时间类”量（计时器、残留、进度、dt）用 double 存储与累加，
// 这样在“累加后与阈值比较”的边界上帧数判定与 JS 一致；空间量（位置、速度）用 Vector3（float）。
using System;
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public static class MathD
    {
        public static double Max(double a, double b) => a > b ? a : b;
        public static double Min(double a, double b) => a < b ? a : b;
        public static double Max(double a, double b, double c) => Max(Max(a, b), c);

        // Vector3 × double：先按 double 算标量再转 float
        public static Vector3 Mul(Vector3 v, double k) => new Vector3((float)(v.x * k), (float)(v.y * k), (float)(v.z * k));
    }
}
