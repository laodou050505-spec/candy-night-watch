// 全屏淡入淡出（VR 里换场景时先黑下来再亮起，不会突然跳变）：贴在相机前的一块黑幕
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace TrickOrFreeze.UI
{
    public class ScreenFade : MonoBehaviour
    {
        public static ScreenFade Instance { get; private set; }
        CanvasGroup group;

        public static ScreenFade Create(Camera cam)
        {
            var go = new GameObject("ScreenFade", typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(cam.transform, false);
            var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = cam.nearClipPlane + 0.01f; cv.sortingOrder = 1000;
            var img = new GameObject("Black", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.color = Color.black; img.raycastTarget = false;
            var rt = img.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var f = go.AddComponent<ScreenFade>();
            return f;
        }

        void Awake() { Instance = this; group = GetComponent<CanvasGroup>(); group.alpha = 0; group.blocksRaycasts = false; }

        /// <summary>立刻全黑（启动时等座位校准用）</summary>
        public void HoldBlack() { StopAllCoroutines(); if (group) group.alpha = 1; }

        /// <summary>从当前黑度淡入到全亮</summary>
        public void FadeIn(float sec = 0.5f) { StopAllCoroutines(); StartCoroutine(CoIn(sec)); }

        IEnumerator CoIn(float b)
        {
            float a0 = group.alpha;
            for (float t = 0; t < b; t += Time.unscaledDeltaTime) { group.alpha = a0 * (1 - t / b); yield return null; }
            group.alpha = 0;
        }

        /// <summary>黑下来 → 执行 mid → 亮起来</summary>
        public void Run(System.Action mid, float outSec = 0.35f, float inSec = 0.45f) => StartCoroutine(Co(mid, outSec, inSec));

        IEnumerator Co(System.Action mid, float a, float b)
        {
            for (float t = 0; t < a; t += Time.unscaledDeltaTime) { group.alpha = t / a; yield return null; }
            group.alpha = 1;
            mid?.Invoke();
            yield return null; yield return null;
            for (float t = 0; t < b; t += Time.unscaledDeltaTime) { group.alpha = 1 - t / b; yield return null; }
            group.alpha = 0;
        }
    }
}
