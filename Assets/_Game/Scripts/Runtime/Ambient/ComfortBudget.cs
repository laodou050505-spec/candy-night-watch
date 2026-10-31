// 舒适度与性能预算：场景加载后统一收敛氛围效果（不改场景摆放，只在运行时压参数）。
// 10-28 实机反馈：余光里一直在闪、在晃、在飞的东西太多，人会不舒服；头显上实时点光太多也拖帧率。
//   · 灯光闪烁 Flicker：幅度 ≤ 12%、频率 ≤ 4（原来 35% / 9，余光里像在闪屏）
//   · 摇摆装饰 Swayer：摆角 ≤ 3°、周期 ≥ 4.5 秒、阵风 ≤ 0.2
//   · 呼吸发光 GlowPulse：幅度 ≤ 3%
//   · 蝙蝠群：近群最多 3 只、远群最多 8 只，飞行速度 ×0.6
//   · 实时点光（仅头显）：只保留离座位 7 米内最近的 8 盏，其余关掉（灯罩 / 灯芯的自发光还在，远看照样是亮着的灯）
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public static class ComfortBudget
    {
        public const float FlickerMax = 0.12f, FlickerSpeedMax = 4f;
        public const float SwayAngleMax = 3f, SwayPeriodMin = 4.5f, SwayGustMax = 0.2f;
        public const float GlowPulseMax = 0.03f;
        public const int NearBatsMax = 3, FarBatsMax = 8;
        public const float LightRadius = 7f; public const int LightMax = 8;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Apply()
        {
            foreach (var f in Object.FindObjectsByType<Flicker>(FindObjectsInactive.Include)) { f.amount = Mathf.Min(f.amount, FlickerMax); f.speed = Mathf.Min(f.speed, FlickerSpeedMax); }
            foreach (var s in Object.FindObjectsByType<Swayer>(FindObjectsInactive.Include)) { s.angle = Mathf.Min(s.angle, SwayAngleMax); s.period = Mathf.Max(s.period, SwayPeriodMin); s.gust = Mathf.Min(s.gust, SwayGustMax); }
            foreach (var g in Object.FindObjectsByType<GlowPulse>(FindObjectsInactive.Include)) g.amount = Mathf.Min(g.amount, GlowPulseMax);
            foreach (var fl in Object.FindObjectsByType<FlockOrbit>(FindObjectsInactive.Include))
            {
                if (!fl.wingFlap) continue;
                bool near = fl.height < 10f && fl.name != "CoverBats";
                if (fl.name == "CoverBats") continue;                    // 封面小布景里的 4 只离得近、飞得慢，保留
                fl.count = Mathf.Min(fl.count, near ? NearBatsMax : FarBatsMax);
                fl.speed *= 0.6f;
            }
            if (Application.platform == RuntimePlatform.Android) BudgetLights();
        }

        static void BudgetLights()
        {
            var lights = new List<Light>();
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                if (l.type != LightType.Directional && !IsCover(l.transform)) lights.Add(l);
            var seat = new Vector3(0, 1.15f, 0);
            lights.Sort((a, b) => Vector3.Distance(a.transform.position, seat).CompareTo(Vector3.Distance(b.transform.position, seat)));
            int kept = 0;
            foreach (var l in lights)
            {
                bool keep = kept < LightMax && Vector3.Distance(l.transform.position, seat) <= LightRadius;
                if (keep) kept++;
                else
                {
                    l.enabled = false;
                    var fl = l.GetComponent<Flicker>(); if (fl) fl.enabled = false;
                }
            }
            Debug.Log($"[ComfortBudget] 实时点光保留 {kept}/{lights.Count}");
        }

        // 封面小布景的灯只在封面时亮，不占游戏里的名额
        static bool IsCover(Transform t) { for (; t; t = t.parent) if (t.name == "CoverStage") return true; return false; }
    }
}
