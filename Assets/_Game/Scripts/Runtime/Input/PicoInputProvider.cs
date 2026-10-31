// PICO 眼动 + 手部追踪 → InputFrame（规则层所需的每帧数据结构）。
// 所有位置先转到 worldRoot 的局部坐标（与规则层 Layout 一致），XR 校准由外部调整 worldRoot 的 position/rotation。
//
// 关键发现（来自 SDK 源码阅读）：
//   - HandJointLocations 里的 pose.Position 用 .ToVector3() 取值，已经做了 z 取反（OpenXR→Unity）
//   - 眼动数据需要手动 z 取反（和 PXR_EyeGazeCursorDriver.TryGetEyePoseFromPicoSdk 一致）
//   - GetJointLocations 返回的关节位置是 HMD 空间（local-floor），需要通过 XR Origin 转到世界坐标
#if ENABLE_PICO_XR_SDK
using UnityEngine;
using ByteDance.PICO.XR;
using TrickOrFreeze.Core;
using System.Collections.Generic;

namespace TrickOrFreeze.Input
{
    /// <summary>每帧从 PICO 眼动 + 手部追踪读取数据，转换为规则层的 InputFrame。</summary>
    public class PicoInputProvider : MonoBehaviour, IInputProvider
    {
        [Header("世界根节点（规则坐标系）")]
        public Transform worldRoot;
        [Header("XR Origin（提供 HMD 空间→世界的变换）")]
        public Transform xrOrigin;
        [Header("XR 相机（头部）")]
        public Camera xrCamera;
        [Header("捏合阈值")]
        public float pinchOnDist = 0.026f;   // 和 GameConfig.pinchOn 一致（10-29 放宽）
        public float pinchOffDist = 0.04f;

        bool _eyeStarted;
        bool[] _wasPinching = new bool[2];
        // 防抖（10-28 实机：手部追踪会抖，捏着的东西偶尔会“自己掉”）：
        //   松开要持续 releaseHold 秒才算真的松开；追踪短暂丢失 lostGrace 秒内沿用上一帧的手，不算松开
        public float releaseHold = 0.07f, lostGrace = 0.25f;
        float[] _offT = new float[2], _lostT = new float[2];
        HandInput[] _last = new HandInput[2];
        HandAimState[] _aim = new HandAimState[2];
        GestureHand[] _lastGesture = new GestureHand[2];
        float _diagT;
        HandJointLocations[] _jd = new HandJointLocations[2];
        readonly Vector3[][] _joints = { new Vector3[26], new Vector3[26] };
        Vector3?[] _prevPalm = new Vector3?[2];
        float[] _pressCd = new float[2];
        GestureDetector _gestures;
        InputFrame _frame;
        // 10-31 审查：眨眼时眼动无效会立刻换成头部朝向，盯熊要连续 0.8 秒、每眨一次就重来。眼动失效 0.3 秒内沿用最后一次眼动方向（存成相对头部的方向，跟着头转）
        public float eyeHoldSec = 0.3f;
        Vector3 _eyeDirHead, _eyeOffHead; float _eyeLostT = 99f; bool _hasEye;
        float[] _jumpT = new float[2];
        TrickOrFreeze.GameLoop _loop;
        public InputFrame Frame => _frame;
        public bool IsXR => true;

        void Awake()
        {
            _frame = new InputFrame();
            _gestures = new GestureDetector();
            for (int i = 0; i < 2; i++) _jd[i] = new HandJointLocations();
        }

        void Start()
        {
            var info = new EyeTrackingStartInfo { needCalibration = 1, mode = EyeTrackingMode.PXR_ETM_BOTH };
            int r = PXR_MotionTracking.StartEyeTracking(ref info);
            _eyeStarted = r == 0;
            if (!_eyeStarted) Debug.LogWarning($"[TrickOrFreeze] 眼动启动失败 code={r}，降级为头部射线");
        }

        /// <summary>每帧由 GameLoop 调用，更新 Frame。</summary>
        public void Tick(float dt)
        {
            _frame.hands.Clear();
            _frame.events.Clear();

            // === 头部 ===
            var camT = xrCamera.transform;
            _frame.head = new HeadPose(ToLocal(camT.position), ToLocalDir(camT.forward));

            // === 眼动 ===
            if (_eyeStarted && TryEyeGaze(out var eOrigin, out var eDir))
            {
                _frame.gaze = new GazeRay { origin = ToLocal(eOrigin), dir = ToLocalDir(eDir), valid = true, source = GazeSources.Eye };
                _eyeDirHead = Quaternion.Inverse(camT.rotation) * eDir;
                _eyeOffHead = Quaternion.Inverse(camT.rotation) * (eOrigin - camT.position);
                _eyeLostT = 0; _hasEye = true;
            }
            else if (_hasEye && (_eyeLostT += dt) < eyeHoldSec)
            {
                // 眨眼/短暂丢失：沿用最后一次眼动方向（跟着头转）
                var o = camT.position + camT.rotation * _eyeOffHead;
                _frame.gaze = new GazeRay { origin = ToLocal(o), dir = ToLocalDir(camT.rotation * _eyeDirHead), valid = true, source = GazeSources.Eye };
            }
            else
                _frame.gaze = new GazeRay { origin = _frame.head.pos, dir = _frame.head.fwd, valid = true, source = GazeSources.Head };

            // === 双手 ===
            var gestureL = (GestureHand)null;
            var gestureR = (GestureHand)null;
            bool[] okArr = new bool[2];
            for (int hi = 0; hi < 2; hi++)
            {
                var type = hi == 0 ? HandType.HandLeft : HandType.HandRight;
                okArr[hi] = PXR_HandTracking.GetJointLocations(type, ref _jd[hi]) && _jd[hi].isActive > 0 && _jd[hi].jointLocations != null;
            }
            // 左右手纠正（10-31 审查：双手交叉时系统偶尔把左右标反，手里的东西瞬移到另一只手）：
            // 两只手都在、上一帧也都在时，交换后和上一帧的位置明显更接近，就把两只手的数据对调
            if (okArr[0] && okArr[1] && _last[0] != null && _last[1] != null && !_last[0].stale && !_last[1].stale)
            {
                Vector3 W(int i) => ToLocal(XrToWorld(_jd[i].jointLocations[(int)HandJoint.JointWrist].pose.Position.ToVector3()));
                Vector3 w0 = W(0), w1 = W(1), l0 = _last[0].point, l1 = _last[1].point;
                float direct = Vector3.Distance(w0, l0) + Vector3.Distance(w1, l1);
                float swapped = Vector3.Distance(w0, l1) + Vector3.Distance(w1, l0);
                if (swapped < direct - 0.2f) { var tmp = _jd[0]; _jd[0] = _jd[1]; _jd[1] = tmp; }
            }
            for (int hi = 0; hi < 2; hi++)
            {
                var type = hi == 0 ? HandType.HandLeft : HandType.HandRight;
                string side = hi == 0 ? Sides.Left : Sides.Right;
                bool tracked = okArr[hi];

                // 两只手靠得很近（合十、护桶）时互相遮挡，追踪掉得更久：宽限延长到 0.6 秒（10-30 实机：两只手合不到一起，合十识别不出）
                float grace = lostGrace;
                if (_last[0] != null && _last[1] != null && Vector3.Distance(_last[0].point, _last[1].point) < 0.2f) grace = Mathf.Max(lostGrace, 0.6f);
                if (_wasPinching[hi]) grace = Mathf.Max(grace, 0.8f);   // 捏着东西时追踪短暂丢失：不松手（10-30 实机：左手拿着的小鬼一抬右手就跑过去）
                if (!tracked && _last[hi] != null && _lostT[hi] + dt < grace)
                {
                    _lostT[hi] += dt;                       // 短暂丢失：沿用上一帧，手里的东西不掉
                    _last[hi].stale = true;                 // 位置不可信：不计入手速历史
                    _frame.hands.Add(_last[hi]);
                    if (_lastGesture[hi] != null) _lastGesture[hi].stale = true;   // 手势沿用上一帧但不再累加计时
                    if (hi == 0) gestureL = _lastGesture[0]; else gestureR = _lastGesture[1];   // 合十计时不被打断
                    continue;
                }
                if (!tracked)
                {
                    _last[hi] = null; _lastGesture[hi] = null;
                    if (_wasPinching[hi]) { _frame.events.Add(InputEvent.PinchEnd(side)); _wasPinching[hi] = false; }
                    _frame.hands.Add(new HandInput { side = side, tracked = false, hasPoint = false });
                    _prevPalm[hi] = null;
                    continue;
                }

                // 关节位置：ToVector3() 已做 z 取反；再通过 XR Origin 转到世界坐标
                Vector3 JW(HandJoint j) => XrToWorld(_jd[hi].jointLocations[(int)j].pose.Position.ToVector3());
                var thumb = JW(HandJoint.JointThumbTip);
                var index = JW(HandJoint.JointIndexTip);
                var wrist = JW(HandJoint.JointWrist);
                var midProx = JW(HandJoint.JointMiddleProximal);
                var idxProx = JW(HandJoint.JointIndexProximal);
                var pinkyProx = JW(HandJoint.JointLittleProximal);

                var pointW = (thumb + index) * 0.5f;
                var pointL = ToLocal(pointW);
                // 捏着东西时单帧跳了 15 厘米以上：多半是追踪抖了一下或认错了手，先沿用上一帧（最多 0.15 秒，持续跳就接受新位置）
                if (_wasPinching[hi] && _last[hi] != null && Vector3.Distance(pointL, _last[hi].point) > 0.15f && _jumpT[hi] < 0.15f)
                {
                    _jumpT[hi] += dt;
                    _last[hi].stale = true;
                    _frame.hands.Add(_last[hi]);
                    if (hi == 0) gestureL = _lastGesture[0]; else gestureR = _lastGesture[1];
                    continue;
                }
                _jumpT[hi] = 0;

                // 捏合（迟滞）。10-30 实机：“捏了没捏上”——只看拇指食指尖的距离，追踪抖动、手指被遮挡时常判不出。
                // 再加 PICO 系统自己的捏合判定（机器学习，对遮挡更稳）：系统说在捏、且指尖距离不太离谱时也算
                float d = Vector3.Distance(thumb, index);
                bool wasPinch = _wasPinching[hi];
                bool sys = PXR_HandTracking.GetAimState(type, ref _aim[hi]) &&
                           ((_aim[hi].aimStatus & HandAimStatus.AimIndexPinching) != 0 || _aim[hi].touchStrengthRay > 0.85f);
                bool raw = wasPinch ? (d <= pinchOffDist || (sys && d < 0.06f)) : (d < pinchOnDist || (sys && d < 0.045f));
                _offT[hi] = wasPinch && !raw ? _offT[hi] + dt : 0;
                bool pinch = wasPinch ? (raw || _offT[hi] < releaseHold) : raw;
                if (pinch && !wasPinch) _frame.events.Add(InputEvent.PinchStart(side));
                else if (!pinch && wasPinch) _frame.events.Add(InputEvent.PinchEnd(side));
                _wasPinching[hi] = pinch;

                // 掌心 + 法线
                var palmW = Vector3.Lerp(midProx, wrist, 0.3f);
                var palmL = ToLocal(palmW);
                var a = ToLocalDir(midProx - wrist);
                var b = ToLocalDir(pinkyProx - idxProx);
                // 掌心法线（指向掌心外侧）。Unity 是左手坐标系：右手 a×b 指向手背，所以右手取 b×a、左手取 a×b
                // （原型在 three.js 右手坐标系里是反过来的，直接照搬会让“掌心朝上/朝下”的手势全部判反）
                var palmN = hi == 0 ? Vector3.Cross(a, b).normalized : Vector3.Cross(b, a).normalized;

                // 手势检测器的输入
                var gh = new GestureHand(palmL, palmN, pinch);
                // 手形：四指伸直程度、张开程度；头 → 掌心方向（南瓜灯要“手背朝着自己”）
                {
                    float extSum = 0;
                    var fj = new[] { (HandJoint.JointIndexProximal, HandJoint.JointIndexIntermediate, HandJoint.JointIndexDistal, HandJoint.JointIndexTip),
                                     (HandJoint.JointMiddleProximal, HandJoint.JointMiddleIntermediate, HandJoint.JointMiddleDistal, HandJoint.JointMiddleTip),
                                     (HandJoint.JointRingProximal, HandJoint.JointRingIntermediate, HandJoint.JointRingDistal, HandJoint.JointRingTip),
                                     (HandJoint.JointLittleProximal, HandJoint.JointLittleIntermediate, HandJoint.JointLittleDistal, HandJoint.JointLittleTip) };
                    foreach (var (p0, p1, p2, p3) in fj)
                    {
                        Vector3 a0 = JW(p0), a1 = JW(p1), a2 = JW(p2), a3 = JW(p3);
                        float bone = Vector3.Distance(a0, a1) + Vector3.Distance(a1, a2) + Vector3.Distance(a2, a3);
                        extSum += bone > 1e-4f ? Vector3.Distance(a0, a3) / bone : 0;
                    }
                    gh.hasShape = true;
                    gh.ext = extSum / 4f;
                    float knuckles = Vector3.Distance(idxProx, pinkyProx);
                    gh.spread = knuckles > 1e-4f ? Vector3.Distance(index, JW(HandJoint.JointLittleTip)) / knuckles : 1f;
                    var away = palmL - _frame.head.pos;
                    if (away.sqrMagnitude > 1e-6f) { gh.away = away.normalized; gh.hasAway = true; }
                }
                if (hi == 0) gestureL = gh; else gestureR = gh;
                _lastGesture[hi] = gh;

                // 按盖：掌心朝下、在坩埚上方、快速向下
                _pressCd[hi] = Mathf.Max(0, _pressCd[hi] - dt);
                if (!pinch && palmN.y < -0.6f && _pressCd[hi] <= 0 && _prevPalm[hi].HasValue)
                {
                    float vy = (palmL.y - _prevPalm[hi].Value.y) / Mathf.Max(dt, 1e-3f);
                    float dx2 = palmL.x - Layout.BOX.x, dz2 = palmL.z - Layout.BOX.z;
                    float boxR = Layout.BOX.half + 0.07f;
                    if (dx2 * dx2 + dz2 * dz2 < boxR * boxR && palmL.y < Layout.BOX.y + Layout.BOX.height + 0.12f && -vy > 0.45f)
                    {
                        _pressCd[hi] = 0.6f;
                        _frame.events.Add(InputEvent.Press(palmL));
                    }
                }
                _prevPalm[hi] = palmL;

                var joints = _joints[hi];
                for (int j = 0; j < joints.Length; j++) joints[j] = ToLocal(JW((HandJoint)j));
                var hin = new HandInput
                {
                    side = side, tracked = true, pinching = pinch, point = pointL, hasPoint = true, joints = joints
                };
                _frame.hands.Add(hin);
                _last[hi] = hin; _lostT[hi] = 0;
            }

            // === 手势技能 ===
            // 诊断：两手靠近时每 0.5 秒打一行（距离、掌心相对、手指伸直程度、是否追踪到），合十识别不出时看 logcat 的 [TOF-Gest]
            _diagT -= dt;
            if (Debug.isDebugBuild && gestureL != null && gestureR != null && _diagT <= 0 && Vector3.Distance(gestureL.palm, gestureR.palm) < 0.25f)
            {
                _diagT = 0.5f;
                Debug.Log($"[TOF-Gest] d={Vector3.Distance(gestureL.palm, gestureR.palm):F3} face={Vector3.Dot(gestureL.palmN, gestureR.palmN):F2} extL={gestureL.ext:F2} extR={gestureR.ext:F2} pinch={gestureL.pinching}/{gestureR.pinching} lost={_lostT[0]:F2}/{_lostT[1]:F2}");
            }
            // 没学会的技能手势不识别（也不占用识别锁，免得挡住已学会的手势）
            if (!_loop) _loop = FindAnyObjectByType<TrickOrFreeze.GameLoop>();
            _gestures.allowed = id => _loop == null || _loop.game == null || _loop.game.skills.ContainsKey(id);
            var gestureEvts = _gestures.Update(dt, gestureL, gestureR);
            foreach (var e in gestureEvts)
                _frame.events.Add(e);

            // 南瓜灯蓄力进度（给表现层画掌心光、掌心前长出小南瓜灯）
            if (_frame.hands.Count > 0) _frame.hands[0].lampK = _gestures.LampProgress(Sides.Left);
            if (_frame.hands.Count > 1) _frame.hands[1].lampK = _gestures.LampProgress(Sides.Right);
        }

        // ---------- 眼动 ----------
        bool TryEyeGaze(out Vector3 origin, out Vector3 dir)
        {
            origin = Vector3.zero; dir = Vector3.forward;
            bool isTracking = false;
            var state = new EyeTrackingState();
            if (PXR_MotionTracking.GetEyeTrackingState(ref isTracking, ref state) != 0 || !isTracking) return false;

            // 必须声明要位置和朝向（和 PICO 官方 PXR_EyeGazeCursorDriver 一致）；之前传 0，注视数据常常无效，游戏一直退回用头部朝向（10-29 实机“眼手很难识别”）
            var getInfo = new EyeTrackingDataGetInfo { displayTime = 0, flags = EyeTrackingDataGetFlags.PXR_EYE_POSITION | EyeTrackingDataGetFlags.PXR_EYE_ORIENTATION };
            var data = new EyeTrackingData { eyeDatas = new PerEyeData[(int)PerEyeUsage.EyeCount] };
            for (int i = 0; i < data.eyeDatas.Length; i++) data.eyeDatas[i].SetVersion(PXR_MotionTracking.PXR_EYE_TRACKING_API_VERSION);
            if (PXR_MotionTracking.GetEyeTrackingData(ref getInfo, ref data) != 0) return false;

            var combined = data.eyeDatas[(int)PerEyeUsage.Combined];
            if (combined.isPoseValid == 0) return false;

            // PICO 眼动返回 OpenXR 原始坐标，手动 z 取反（和 PXR_EyeGazeCursorDriver 一致）
            var rawOrigin = new Vector3(combined.pose.position.x, combined.pose.position.y, -combined.pose.position.z);
            var rawRot = new Quaternion(combined.pose.orientation.x, combined.pose.orientation.y, -combined.pose.orientation.z, -combined.pose.orientation.w);
            // HMD 空间 → 世界坐标
            origin = XrToWorld(rawOrigin);
            dir = xrOrigin ? xrOrigin.TransformDirection(rawRot * Vector3.forward) : rawRot * Vector3.forward;
            return true;
        }

        // ---------- 坐标转换 ----------
        /// <summary>HMD 本地空间（local-floor）→ 世界坐标</summary>
        Vector3 XrToWorld(Vector3 xrLocal) => xrOrigin ? xrOrigin.TransformPoint(xrLocal) : xrLocal;

        /// <summary>世界坐标 → 规则层局部坐标</summary>
        Vector3 ToLocal(Vector3 worldPos) => worldRoot ? worldRoot.InverseTransformPoint(worldPos) : worldPos;
        Vector3 ToLocalDir(Vector3 worldDir) => worldRoot ? worldRoot.InverseTransformDirection(worldDir) : worldDir;
    }
}
#endif
