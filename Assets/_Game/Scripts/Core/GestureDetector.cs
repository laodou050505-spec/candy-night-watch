// 双手手势识别（纯逻辑，对应原型 src/logic/gestures.js）。每帧喂入双掌位置/法线，吐出 skill 输入事件。
//
// 合十 pray：双掌距离 < prayDist、掌心相对、保持 prayHoldSec。
// 外挥 sweep：双掌先在胸前靠近（< sweepStartDist），然后在 sweepSec 内向两侧张开到 > sweepEndDist，
//             且张开方向主要是水平左右（防止和“举手/放下”混淆）。
// 南瓜灯 lamp（10-30 改，用户：原来的“掌心朝上托住”不舒服、还常被误判）：单手五指伸直张开、手背朝着自己（掌心朝外），
//             几乎不动、保持 lampHoldSec；灯从掌心前飞到前方约 30 厘米处（在桌面上空就压低到桌面上方 12~35 厘米）。
// 护桶 shield：双手掌心朝下、彼此靠近，一起快速向下按。
// 所有手势都要求没有捏合，避免和抓取冲突；有手形数据时，合十、护桶要求不是握拳，南瓜灯要求五指伸直张开。
//
// 输出的 InputEvent 可以直接塞进 InputFrame.events 交给 Game。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Core
{
    public class GestureDetector
    {
        readonly GestureConfig cfg;
        float prayT;
        bool prayFired;
        float armT = -1f;     // 外挥起手时刻
        float armX;
        float time;
        float lockT;          // 触发后短暂锁定，防止一次动作触发两次
        float lampTL, lampTR;
        bool lampFiredL, lampFiredR;
        Vector3? prevL, prevR; // 上一帧掌心位置；null = 上一帧这只手不在
        float shieldCd;
        // 10-31 审查补的防误触状态
        bool sweepBlock;                  // 合十触发后：两手完全分开之前不允许起手外挥（合十后自然分开手会被当成外挥）
        Vector3 armL, armR;               // 外挥起手时两只手的位置（要求两只手各自向外移动）
        float spdL, spdR;                 // 单手速度（平滑后），南瓜灯“静止”判定用
        readonly List<(float t, float yl, float yr)> shieldHist = new List<(float, float, float)>();
        /// <summary>技能是否已学会；没学会的手势不识别、也不占用锁定（null = 全部允许）</summary>
        public Func<string, bool> allowed;
        bool Allowed(string id) => allowed == null || allowed(id);

        public GestureDetector(GestureConfig cfg = null)
        {
            this.cfg = cfg ?? new GestureConfig();
        }

        // 单手手势（托灯）不需要两只手都在
        List<InputEvent> UpdateSingle(float dt, string side, GestureHand H)
        {
            var c = cfg;
            var outList = new List<InputEvent>();
            bool left = side == Sides.Left;
            Vector3? prev = left ? prevL : prevR;
            Vector3? now = H != null ? H.palm : (Vector3?)null;
            if (left) prevL = now; else prevR = now;
            if (H == null || H.pinching || !H.hasPalmN || !Allowed(SkillIds.Lamp))
            {
                SetLampT(left, 0f); SetLampFired(left, false);
                return outList;
            }
            if (H.stale) return outList;   // 沿用的上一帧：不累加计时
            // 手速用平滑值（单帧差分抖动大，正常举着也会忽快忽慢）
            float inst = prev.HasValue ? Vector3.Distance(prev.Value, H.palm) / Mathf.Max(dt, 1e-3f) : 0f;
            float speed = left ? (spdL = Mathf.Lerp(spdL, inst, 0.25f)) : (spdR = Mathf.Lerp(spdR, inst, 0.25f));
            bool spreadOk = !H.hasShape || (H.ext > c.openExt && H.spread > c.lampSpread);
            var outward = H.hasAway ? H.away : Vector3.forward * -1f;
            // 掌心要大致水平朝外（10-31 审查：手悬在桌上、掌心朝下张开准备捏时也满足“背离头部”，会误召灯），手要在桌面以上 15 厘米
            bool facing = Vector3.Dot(H.palmN, outward) > c.lampFacing && H.palmN.y > -0.5f && H.palm.y > Layout.TABLE.y + 0.15f;
            if (spreadOk && facing && speed < c.lampStillSpeed)
            {
                SetLampT(left, LampT(left) + dt);
                if (LampT(left) >= c.lampHoldSec && !LampFired(left))
                {
                    SetLampFired(left, true);
                    var ev = InputEvent.Skill(SkillIds.Lamp, LampSpot(H.palm, H.palmN));
                    ev.from = H.palm + H.palmN * 0.06f;
                    outList.Add(ev);
                }
            }
            else
            {
                // 松开（手指收拢或手翻过来）才能点下一盏，举着不动不会连发
                SetLampT(left, Mathf.Max(0f, LampT(left) - dt * 3f));
                if (!facing || (H.hasShape && H.ext < c.openExt - 0.1f)) SetLampFired(left, false);
            }
            return outList;
        }

        /// <summary>灯停的位置：掌心朝外前方 16 厘米（伸手就能捏住挪走）；在桌面上空时压低到桌面上方 12~35 厘米（照得到桌上的小鬼）</summary>
        public static Vector3 LampSpot(Vector3 palm, Vector3 palmN)
        {
            var dir = palmN; dir.y = Mathf.Min(dir.y, 0.2f); dir = dir.normalized;
            var p = palm + dir * 0.16f;
            if (Layout.OnTable(p, 0.05f)) p.y = Mathf.Clamp(p.y, Layout.TABLE.y + 0.12f, Layout.TABLE.y + 0.35f);
            return p;
        }

        bool Open(GestureHand H) => !H.hasShape || H.ext > cfg.openExt;
        /// <summary>不是握拳（合十、护桶用）：只排除攥着拳头、捏着东西时两手碰到一起</summary>
        bool NotFist(GestureHand H) => !H.hasShape || H.ext > cfg.notFistExt;

        float LampT(bool left) => left ? lampTL : lampTR;
        void SetLampT(bool left, float v) { if (left) lampTL = v; else lampTR = v; }
        bool LampFired(bool left) => left ? lampFiredL : lampFiredR;
        void SetLampFired(bool left, bool v) { if (left) lampFiredL = v; else lampFiredR = v; }

        // 托灯的蓄力进度（给表现层画掌心光）
        public float LampProgress(string side)
        {
            bool left = side == Sides.Left;
            return LampFired(left) ? 0f : (float)Math.Min(1.0, LampT(left) / cfg.lampHoldSec);
        }

        // L/R 可为 null（这只手没追踪到）
        public List<InputEvent> Update(float dt, GestureHand L, GestureHand R)
        {
            time += dt;
            if (lockT > 0) lockT -= dt;
            if (shieldCd > 0) shieldCd -= dt;
            Vector3? pl = prevL, pr = prevR;
            var outList = UpdateSingle(dt, Sides.Left, L);
            outList.AddRange(UpdateSingle(dt, Sides.Right, R));
            if (L == null || R == null || L.pinching || R.pinching)
            {
                prayT = 0;
                prayFired = false;
                armT = -1;
                shieldHist.Clear();
                if (L == null || R == null) sweepBlock = false;
                return outList;
            }
            var c = cfg;
            float d = Vector3.Distance(L.palm, R.palm);
            bool stale = L.stale || R.stale;   // 有一只手是沿用的上一帧：位置不可信，只保持状态、不触发

            // 护桶：双掌朝下、靠近、从桌面以上一起往下按。
            // 10-31 审查：单帧速度抖动大，放下双手休息也会误触——改为 0.15 秒窗口内两手都下降 8 厘米以上、平均速度够快
            if (!stale && Allowed(SkillIds.Shield) && L.hasPalmN && R.hasPalmN && L.palmN.y < -0.7f && R.palmN.y < -0.7f && d < c.shieldDist && NotFist(L) && NotFist(R))
            {
                shieldHist.Add((time, L.palm.y, R.palm.y));
                while (shieldHist.Count > 2 && time - shieldHist[0].t > 0.15f) shieldHist.RemoveAt(0);
                var h0 = shieldHist[0];
                float span = time - h0.t;
                float dropL = h0.yl - L.palm.y, dropR = h0.yr - R.palm.y;
                bool startHigh = Mathf.Min(h0.yl, h0.yr) > Layout.TABLE.y + 0.08f;
                if (span > 0.06f && startHigh && Mathf.Min(dropL, dropR) > 0.08f && Mathf.Min(dropL, dropR) / span > c.shieldSpeed && shieldCd <= 0 && lockT <= 0)
                {
                    shieldCd = 1;
                    lockT = 0.6f;
                    shieldHist.Clear();
                    outList.Add(InputEvent.Skill(SkillIds.Shield));
                }
            }
            else shieldHist.Clear();
            bool facing = L.hasPalmN && R.hasPalmN ? Vector3.Dot(L.palmN, R.palmN) < -0.5f : true;

            // 合十（两只手都要伸直：握着东西、捏着时两手碰到一起不算）
            if (Allowed(SkillIds.Pray) && d < c.prayDist && facing && NotFist(L) && NotFist(R))
            {
                if (!stale) prayT += dt;
                if (prayT >= c.prayHoldSec && !prayFired && lockT <= 0)
                {
                    prayFired = true;
                    sweepBlock = true;
                    lockT = 0.6f;
                    outList.Add(InputEvent.Skill(SkillIds.Pray));
                }
            }
            else if (!stale)
            {
                prayT = 0;
                if (d > c.prayDist * 1.6f) prayFired = false;
            }

            // 外挥：双掌在胸前靠近后，sweepSec 内向两侧张开；两只手都要各自往外移（单手侧伸去拿东西不算）
            if (sweepBlock && d > c.sweepEndDist) sweepBlock = false;   // 合十后两手完全分开过一次，才重新允许外挥
            if (!Allowed(SkillIds.Sweep) || stale) return outList;
            float dx = Mathf.Abs(L.palm.x - R.palm.x);
            if (d < c.sweepStartDist && !sweepBlock && prayT < 0.15f) { armT = time; armX = dx; armL = L.palm; armR = R.palm; }
            else if (armT >= 0)
            {
                if (time - armT > c.sweepSec) armT = -1;
                else if (d > c.sweepEndDist && dx - armX > (c.sweepEndDist - c.sweepStartDist) * 0.7f && lockT <= 0 && !prayFired && !sweepBlock)
                {
                    // 两只手各自沿“远离对方”的方向水平移动了 8 厘米以上
                    var axis = armL - armR; axis.y = 0;
                    if (axis.sqrMagnitude < 1e-6f) axis = Vector3.right;
                    axis.Normalize();
                    float outL = Vector3.Dot(L.palm - armL, axis), outR = Vector3.Dot(R.palm - armR, -axis);
                    if (outL > 0.08f && outR > 0.08f)
                    {
                        armT = -1;
                        lockT = 0.8f;
                        outList.Add(InputEvent.Skill(SkillIds.Sweep));
                    }
                }
            }
            return outList;
        }
    }
}
