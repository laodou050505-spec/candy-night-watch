// UI 指针：桌面用鼠标射线 + 左键；头显里用眼动射线（没有眼动时退回头部朝向）+ 任一只手捏合，
// 或者直接用手指“戳”到按钮上捏一下。只在菜单/结算等界面显示时检测，避免和游戏里的抓取冲突。
// 视线落点画一个小光点，让玩家知道自己在看哪。
using TrickOrFreeze.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrickOrFreeze.UI
{
    public class UIPointer : MonoBehaviour
    {
        public GameLoop loop;
        public Camera cam;
        public Transform cursor;            // 视线光点（世界空间小精灵）
        public bool active = true;          // 由 HudPresenter 控制：菜单界面开、游戏中只有少量按钮
        WorldButton hover;
        const int UILayerMask = 1 << 5;
        // 10-31 OverlapSphere NonAlloc：复用固定缓冲区，避免每帧 GC 分配
        static readonly Collider[] _overlapBuf = new Collider[8];

        void Update()
        {
            WorldButton hit = null;
            Vector3 hitPos = Vector3.zero;
            bool click = false;
            if (active && loop != null)
            {
                var frame = loop.CurrentInput;
                Ray ray;
                if (loop.IsXR && frame?.gaze != null && frame.gaze.valid)
                    ray = new Ray(loop.W(frame.gaze.origin), loop.worldRoot.TransformDirection(frame.gaze.dir));
                else if (Mouse.current != null && cam != null)
                    ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                else ray = new Ray(cam.transform.position, cam.transform.forward);
                if (Physics.Raycast(ray, out var rh, 8f, UILayerMask, QueryTriggerInteraction.Collide))
                {
                    hit = rh.collider.GetComponentInParent<WorldButton>();
                    hitPos = rh.point;
                }
                if (loop.IsXR)
                {
                    // 手指直接戳到按钮上
                    if (frame != null)
                        foreach (var h in frame.hands)
                        {
                            if (!h.tracked || !h.hasPoint) continue;
                            var p = loop.W(h.point);
                            int cnt = Physics.OverlapSphereNonAlloc(p, 0.015f, _overlapBuf, UILayerMask, QueryTriggerInteraction.Collide);
                            if (cnt > 0) { var b = _overlapBuf[0].GetComponentInParent<WorldButton>(); if (b) { hit = b; hitPos = p; } }
                        }
                    if (frame != null) foreach (var e in frame.events) if (e.type == InputEventTypes.PinchStart) click = true;
                }
                else click = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            }
            if (hit != hover)
            {
                if (hover) hover.SetHover(false);
                hover = hit;
                if (hover) { hover.SetHover(true); Sfx.Instance?.Ui("tick", 0.5f); }
            }
            if (click && hover && hover.interactable) { Sfx.Instance?.Ui("btn"); hover.Click(); }
            if (cursor)
            {
                cursor.gameObject.SetActive(active && hover != null);
                if (hover) { cursor.position = hitPos; cursor.rotation = hover.transform.rotation; }
            }
        }
    }
}
