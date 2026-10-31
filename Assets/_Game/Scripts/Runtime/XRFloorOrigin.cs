// 头显启动时：
//   1) 把追踪原点设成地面（坐着玩时头部实际高度直接对应场景里的座位，不会沉到地板下）；
//   2) 等头部追踪真正就绪后自动校准一次座位：玩家当前朝向 = 场景里正对桌子（-Z），眼高 = 1.15 米。
//      以前没有这一步，启动第一帧头部数据还没就绪，封面按错的朝向摆好就不动了——实机上封面跑到了身后（10-28 反馈）。
//   3) 系统层面重新定位（长按 Home 重置视角等）后，自动再校准一次。
// 校准前保持黑屏，校准完再淡入，看不到画面跳动。
using System.Collections.Generic;
using TrickOrFreeze.UI;
using UnityEngine;
using UnityEngine.XR;

namespace TrickOrFreeze
{
    public class XRFloorOrigin : MonoBehaviour
    {
        public Camera cam;
        public static event System.Action Recentered;

        float retry, wait, settle, pending = -1;
        bool floorDone, recentered;
        readonly List<XRInputSubsystem> subs = new List<XRInputSubsystem>();

        void Start()
        {
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 XRFloorOrigin.Start 开始");
            if (!cam) cam = GetComponentInChildren<Camera>(true);
            if (XRSettings.isDeviceActive && ScreenFade.Instance) ScreenFade.Instance.HoldBlack();
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 XRFloorOrigin.Start 完成");
        }

        void OnDestroy() { foreach (var s in subs) if (s != null) s.trackingOriginUpdated -= OnOriginUpdated; }

        void OnOriginUpdated(XRInputSubsystem s) { if (recentered) pending = 0.35f; }

        void Update()
        {
            if (!XRSettings.isDeviceActive) return;
            float dt = Time.unscaledDeltaTime;
            if (!floorDone)
            {
                retry -= dt;
                if (retry > 0) return;
                retry = 1f;
                SubsystemManager.GetSubsystems(subs);
                foreach (var s in subs)
                {
                    if (s.GetTrackingOriginMode() != TrackingOriginModeFlags.Floor && (s.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) != 0)
                        s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                    s.trackingOriginUpdated += OnOriginUpdated;
                }
                floorDone = true;
                return;
            }
            if (!recentered)
            {
                // 头部追踪就绪：设备报告已追踪，且相机已离开原点（地面模式下头高至少 0.3 米），连续稳定 0.3 秒；最多等 5 秒
                wait += dt;
                bool ok = cam && cam.transform.localPosition.y > 0.3f && HeadTracked();
                settle = ok ? settle + dt : 0;
                if (settle >= 0.3f || wait > 5f)
                {
                    recentered = true;
                    Recenter(cam);
                    if (ScreenFade.Instance) ScreenFade.Instance.FadeIn(0.6f);
                }
                return;
            }
            if (pending > 0)
            {
                pending -= dt;
                if (pending <= 0 && ScreenFade.Instance) ScreenFade.Instance.Run(() => Recenter(cam), 0.2f, 0.4f);
                else if (pending <= 0) Recenter(cam);
            }
        }

        static bool HeadTracked()
        {
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t;
        }

        /// <summary>重新校准：把玩家当前的头部位置与朝向对准座位（眼高 1.15m、面朝 -Z 的桌子）</summary>
        public void Recenter(Camera c)
        {
            if (!c) return;
            var local = c.transform.localPosition;
            var f = c.transform.localRotation * Vector3.forward; f.y = 0;
            float yaw = f.sqrMagnitude > 1e-4f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            transform.localRotation = Quaternion.Euler(0, 180f - yaw, 0);
            var target = new Vector3(0, TrickOrFreeze.Core.Layout.EYE_HEIGHT, TrickOrFreeze.Core.Layout.SEAT_Z);
            transform.localPosition = target - transform.localRotation * local;
            Recentered?.Invoke();
        }
    }
}
