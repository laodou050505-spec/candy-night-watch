// 世界空间按钮：被指针（视线/鼠标）指着时放大 + 提亮 + 底下亮一圈柔光；按下时压扁回弹并发出点击音。
// 碰撞体是画布局部空间里的薄盒子（UI 层），由 UIPointer 射线检测。
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TrickOrFreeze.UI
{
    public class WorldButton : MonoBehaviour
    {
        public Image face;
        public Image glow;
        public TextMeshProUGUI label;
        public Color baseColor = Color.white, hoverColor = Color.white;
        public Action onClick;
        public bool interactable = true;
        public float hoverScale = 1.08f;

        bool hovered;
        float hoverK, pressT = 1, dwell;
        Vector3 baseScale;

        public bool Hovered => hovered;

        void Awake() { baseScale = transform.localScale; }

        public void SetHover(bool h) { hovered = h && interactable; }

        public void Click()
        {
            if (!interactable) return;
            pressT = 0;
            onClick?.Invoke();
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            hoverK = Mathf.MoveTowards(hoverK, hovered ? 1 : 0, dt * 7);
            pressT = Mathf.Min(1, pressT + dt * 4);
            // 按下：先压到 0.9 再弹回（阻尼正弦）
            float press = pressT < 1 ? 1 - 0.1f * Mathf.Sin(pressT * Mathf.PI) * (1 - pressT) * 2 : 1;
            float k = Ease(hoverK);
            transform.localScale = baseScale * Mathf.Lerp(1, hoverScale, k) * press;
            if (face) face.color = interactable ? Color.Lerp(baseColor, hoverColor, k) : Color.Lerp(baseColor, new Color(0.55f, 0.52f, 0.6f), 0.7f);
            if (glow) { var c = glow.color; c.a = 0.55f * k; glow.color = c; }
            if (label) label.alpha = interactable ? 1 : 0.55f;
        }

        static float Ease(float x) => 1 - (1 - x) * (1 - x);
    }
}
