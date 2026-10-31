// 光（桌上南瓜灯 + 掌心灯）与注视定身。
using System.Collections.Generic;
using UnityEngine;
using static TrickOrFreeze.Core.Layout;

namespace TrickOrFreeze.Core
{
    public partial class Game
    {
        // ---------- 光：桌上南瓜灯 + 掌心灯；熄灯夜只有光里的看得见 ----------
        public const float JAR_GLOW = 0.26f;   // 糖桶微光半径（表现层画一圈暖光）

        public bool LightAt(Vector3 p)
        {
            if (CANDLE.on && Vec.DistXZ(p, CANDLE.pos) <= CANDLE.radius && Mathf.Abs(p.y - CANDLE.y) < 0.5f) return true;
            // 10-31 审查：糖桶自己有一圈糖果微光——黑猫不会在看不见的地方把糖偷走
            if (Vec.DistXZ(p, JAR.pos) <= JAR_GLOW && Mathf.Abs(p.y - JAR.y) < 0.35f) return true;
            foreach (var l in lamps) if (Vector3.Distance(p, l.pos) <= l.radius) return true;
            return false;
        }

        void UpdateLight()
        {
            foreach (var t in toys)
            {
                var k = t.Kind;
                bool needs = k.needsLight || dark;
                bool was = t.lit;
                t.lit = !needs || LightAt(Center(t)) || t.state == ToyStates.Held || t.state == ToyStates.Pulled;
                if (t.lit && !was && ACTIVE.Contains(t.state)) { var e = Emit(GameEventTypes.Revealed).At(t.pos); e.id = t.id; }
            }
        }

        void UpdateLamps(double dt)
        {
            foreach (var l in lamps) l.t -= dt;
            foreach (var l in lamps) if (l.t <= 0) { var e = Emit(GameEventTypes.LampEnd).At(l.pos); e.id = l.id; }
            lamps.RemoveAll(l => l.t <= 0);
        }

        public bool InLamp(Vector3 p)
        {
            foreach (var l in lamps) if (Vector3.Distance(p, l.pos) <= l.radius) return true;
            return false;
        }

        // ---------- 注视 ----------
        void UpdateGaze(InputFrame input, double dt)
        {
            var g = cfg.gaze;
            var gaze = input.gaze;
            float cone = gaze != null && gaze.source == GazeSources.Eye ? g.directConeDeg : g.headRayConeDeg;
            Toy best = null;
            float bestScore = float.PositiveInfinity;
            Candy bestCandy = null;
            float bestCandyScore = float.PositiveInfinity;
            bool prevOk = false; float prevScore = 0;
            if (gaze != null && gaze.valid)
            {
                foreach (var t in toys)
                {
                    if (!GAZEABLE.Contains(t.state) || !t.lit) continue;
                    var k = t.Kind;
                    var to = Center(t) - gaze.origin;
                    float d = to.magnitude;
                    // 减去敌人自身的角半径：离得近的更好看中
                    // 10-31 审查：加成封顶 3°——缝合熊在 1 米处角半径约 14°，直视旁边的小鬼也会被熊抢走注视
                    float angR = Mathf.Min(3f, Mathf.Atan2(Mathf.Max(k.height, k.radius * 2) * 0.6f, d) * 180f / Mathf.PI);
                    float eff = Vec.AngleDeg(gaze.dir, to) - angR;
                    if (t == gazeTarget) { prevOk = eff <= cone + g.snapDeg; prevScore = eff; }
                    if (eff <= cone + g.snapDeg && eff < bestScore) { bestScore = eff; best = t; }
                }
                // 粘滞：上一帧盯着的目标还在锥内时，新目标要领先 2° 以上才换（两个目标挨着时不来回跳）
                if (prevOk && best != gazeTarget && bestScore > prevScore - 2f) { best = gazeTarget; bestScore = prevScore; }
                foreach (var c in candies)
                {
                    if (c.state != CandyStates.Rest && c.state != CandyStates.Falling) continue;
                    var to = c.pos - gaze.origin;
                    float eff = Vec.AngleDeg(gaze.dir, to) - Mathf.Atan2(0.04f, to.magnitude) * 180f / Mathf.PI;
                    if (eff <= cone + g.snapDeg && eff < bestCandyScore) { bestCandyScore = eff; bestCandy = c; }
                }
            }
            // 10-31 审查：锥里有任何敌人时糖就选不中——改成谁离视线更近选谁（糖要领先 1° 才压过敌人）
            gazeCandy = bestCandy != null && (best == null || bestCandyScore < bestScore - 1f) ? bestCandy : null;
            if (gazeCandy != null) best = null;
            gazeTarget = best;
            if (best != null) best.lastDirect = time;

            var head = input.head;
            var f = cfg.freeze;
            foreach (var t in toys)
            {
                var k = t.Kind;
                bool wasGazed = t.gazed;
                t.gazed = time - t.lastDirect <= g.graceSec && GAZEABLE.Contains(t.state) && t.lit;
                if (t.gazed && !wasGazed) { var e = Emit(GameEventTypes.GazeOn); e.id = t.id; e.kind = t.kind; }
                var c = Center(t);
                if (t.gazed) t.zone = Zones.Direct;
                else if (head != null && Vec.AngleDeg(head.fwd, c - head.pos) <= g.peripheralHalfDeg) t.zone = Zones.Peripheral;
                else t.zone = Zones.Outside;

                if (k.freeze == FreezeRules.Inverse)
                {
                    // 南瓜头：余光里僵住（要在光里）；被盯着超过 0.4 秒才受惊逃跑——扫一眼不算。
                    // 手伸到它跟前（12 厘米内）时吓得不敢动，可以抓（10-29 实机：戴眼动头显伸手时眼睛本能看向它，原规则几乎抓不到）
                    t.residual = 0;
                    t.gazeAccum = t.gazed ? t.gazeAccum + dt : 0;
                    bool handNear = HandNear(input, c, 0.12f);
                    bool wasScared = t.scared;
                    t.scared = t.gazed && t.gazeAccum >= 0.4 && !handNear;
                    if (t.scared && !wasScared && ACTIVE.Contains(t.state))
                    {
                        var e = Emit(GameEventTypes.PumpkinRun).At(t.pos); e.id = t.id;
                        // 10-31 审查：玩家本能会盯着它看，第一次被吓跑时说清楚怎么对付
                        HintOnce("pumpkinRun", "南瓜头被盯久了会逃。扫一眼就移开，用余光照着它；手对准它一捏，也能把它隔空拉过来。", 5f);
                    }
                    t.frozen = (t.lit && (t.zone == Zones.Peripheral || (t.gazed && !t.scared) || handNear)) || freezeAllT > 0;
                    continue;
                }

                // 残留定身
                double before = t.residual;
                double maxR = k.residualMax > 0 ? k.residualMax : f.residualMax;
                if (t.gazed)
                {
                    t.gazeAccum += dt;
                    double need = k.freeze == FreezeRules.Stare ? k.stareSec : 0;
                    double eff = MathD.Max(0, t.gazeAccum - need);
                    if (t.gazeAccum >= need && residualEnabled) t.residual = MathD.Max(t.residual, MathD.Min(MathD.Max(eff, 0.3) * f.residualPerGazeSec, maxR));
                    t.warned = false;
                }
                else
                {
                    t.gazeAccum = MathD.Max(0, t.gazeAccum - dt * 2);   // 10-31：移开目光（眨眼）后慢慢衰减，不再一下清零——盯熊要 0.8 秒，以前每眨一次眼就得重来
                    t.residual = MathD.Max(0, t.residual - dt);
                    if (ACTIVE.Contains(t.state) && t.residual > 0 && t.residual <= f.warnSec && !t.warned)
                    {
                        t.warned = true;
                        var e = Emit(GameEventTypes.ThawWarn).At(t.pos); e.id = t.id;
                    }
                    if (before > 0 && t.residual == 0 && ACTIVE.Contains(t.state)) { var e = Emit(GameEventTypes.Thaw); e.id = t.id; }
                }
                bool stareOk = k.freeze != FreezeRules.Stare || t.gazeAccum >= k.stareSec;
                t.frozen = (t.gazed && stareOk) || t.residual > 0 || freezeAllT > 0;
                if (k.freeze == FreezeRules.Stare && t.gazed) t.stareK = (float)MathD.Min(1, t.gazeAccum / k.stareSec);
                else t.stareK = (float)MathD.Max(0, t.stareK - dt * 2);
            }
        }

        static bool HandNear(InputFrame input, Vector3 p, float r)
        {
            if (input == null || input.hands == null) return false;
            foreach (var h in input.hands) if (h.tracked && h.hasPoint && (h.point - p).sqrMagnitude <= r * r) return true;
            return false;
        }
    }
}
