// 自动生成（Tools/make_ui_kit.py icon_bounds）：每个图标可见部分在图片里的范围，x/y 从左下角算，单位为图片宽高的比例。
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.UI
{
    public static class IconBounds
    {
        public static readonly Dictionary<string, Rect> All = new Dictionary<string, Rect>
        {
            { "Icon_Back", new Rect(0.246f, 0.215f, 0.621f, 0.578f) },
            { "Icon_Candy", new Rect(0.055f, 0.262f, 0.898f, 0.480f) },
            { "Icon_Cauldron", new Rect(0.109f, 0.047f, 0.785f, 0.820f) },
            { "Icon_Gear", new Rect(0.062f, 0.039f, 0.871f, 0.898f) },
            { "Icon_Globe", new Rect(0.129f, 0.090f, 0.746f, 0.762f) },
            { "Icon_Hand", new Rect(0.039f, 0.172f, 0.922f, 0.656f) },
            { "Icon_Lock", new Rect(0.203f, 0.105f, 0.594f, 0.699f) },
            { "Icon_Moon", new Rect(0.129f, 0.125f, 0.648f, 0.742f) },
            { "Icon_Pause", new Rect(0.223f, 0.145f, 0.551f, 0.688f) },
            { "Icon_Play", new Rect(0.266f, 0.207f, 0.500f, 0.562f) },
            { "Icon_Retry", new Rect(0.141f, 0.180f, 0.793f, 0.652f) },
            { "Icon_Star", new Rect(0.145f, 0.133f, 0.707f, 0.703f) },
        };

        public static Rect Of(Sprite sp) => sp && All.TryGetValue(sp.name, out var r) ? r : new Rect(0, 0, 1, 1);
    }
}
