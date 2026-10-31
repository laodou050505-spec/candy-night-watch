// 南瓜灯技能的表现（10-30，用户：南瓜灯不够亮、看不出“光”）：
//   出现：从掌心前飞出，弧线落到停留点，同时由小变大、灯光由暗到亮
//   常亮：南瓜灯自发光 + 实时暖光（火光一样轻轻跳动）+ 朝向镜头的一团暖色光晕
//         + 桌面上一圈暖光（正好是照亮范围：灯的照亮半径在桌面那一层的截面），一眼看出“这一片被照亮了”
//   快灭：最后 2 秒火光一闪一闪提示；熄灭时缩小、光晕和光圈淡出
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.View
{
    public class LampView : MonoBehaviour
    {
        public Transform lantern;
        public Light glowLight;
        public Transform halo;
        public Transform pool;
        public float lightIntensity = 0.7f;

        Renderer[] lanternRs; Renderer haloR, poolR;
        Vector3 lanternScale;
        float age = -1;
        MaterialPropertyBlock mpb;
        static readonly int ID_Em = Shader.PropertyToID("_EmissionColor"), ID_Base = Shader.PropertyToID("_BaseColor");

        void Init()
        {
            mpb = new MaterialPropertyBlock();
            lanternRs = lantern ? lantern.GetComponentsInChildren<Renderer>() : new Renderer[0];
            lanternScale = lantern ? lantern.localScale : Vector3.one;
            haloR = halo ? halo.GetComponent<Renderer>() : null;
            poolR = pool ? pool.GetComponent<Renderer>() : null;
            age = 0;
        }

        /// <summary>每帧由 GameLoop 调用。本物体挂在规则层坐标根下（localPosition = 规则层坐标）</summary>
        public void Sync(Lamp l, float dt, Camera cam)
        {
            if (age < 0) Init();
            age += dt;
            float fly = Mathf.Clamp01(age / 0.45f), e = 1 - (1 - fly) * (1 - fly) * (1 - fly);
            float t = Time.time;
            var p = Vector3.Lerp(l.from, l.pos, e) + Vector3.up * (Mathf.Sin(Mathf.PI * fly) * 0.06f + Mathf.Sin(t * 2f + l.id) * 0.01f * fly);
            transform.localPosition = p;
            // 亮度：出现时 0.3 秒亮起；最后 1.2 秒淡出；最后 2 秒一闪一闪
            float life = (float)l.t;
            float fade = Mathf.Clamp01(age / 0.3f) * Mathf.Clamp01(life / 1.2f);
            float flick = 0.88f + 0.08f * Mathf.Sin(t * 13f + l.id * 3f) + 0.05f * Mathf.Sin(t * 29f + l.id);
            if (life < 2f) flick *= 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(t * 9f));
            float k = fade * flick;
            if (lantern)
            {
                float s = Mathf.Lerp(0.25f, 1f, e) * Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(life / 1.2f));
                lantern.localScale = lanternScale * s;
                lantern.localRotation = Quaternion.Euler(0, Mathf.Sin(t * 0.9f + l.id) * 10f, Mathf.Sin(t * 1.7f + l.id) * 3f);
                foreach (var r in lanternRs)
                {
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(ID_Em, new Color(1f, 0.5f, 0.12f) * (0.35f * k));
                    r.SetPropertyBlock(mpb);
                }
            }
            if (glowLight) { glowLight.intensity = lightIntensity * k; glowLight.range = l.radius * 1.3f; }
            if (halo && cam)
            {
                halo.rotation = Quaternion.LookRotation(cam.transform.position - halo.position, cam.transform.up);   // 面片正面（+Z）朝向镜头，单面材质才看得见
                halo.localScale = Vector3.one * (0.34f * Mathf.Lerp(0.3f, 1f, e) * (0.94f + 0.12f * flick));
                if (haloR) { haloR.GetPropertyBlock(mpb); mpb.SetColor(ID_Base, new Color(1f, 0.6f, 0.22f, 0.55f * k)); haloR.SetPropertyBlock(mpb); }
            }
            if (pool)
            {
                // 桌面光圈：灯在桌面上空时显示在桌面那一层，半径 = 照亮球在桌面高度的截面半径
                float h = p.y - Layout.TABLE.y;
                bool show = Layout.OnTable(p) && h < l.radius && h > -0.02f;
                pool.gameObject.SetActive(show);
                if (show)
                {
                    float rg = Mathf.Sqrt(Mathf.Max(0.0001f, l.radius * l.radius - h * h));
                    pool.position = transform.parent ? transform.parent.TransformPoint(new Vector3(p.x, Layout.TABLE.y + 0.004f, p.z)) : new Vector3(p.x, Layout.TABLE.y + 0.004f, p.z);
                    pool.rotation = (transform.parent ? transform.parent.rotation : Quaternion.identity) * Quaternion.Euler(-90, 0, 0);   // 正面朝上（从桌子上方看）
                    pool.localScale = Vector3.one * (rg * 2f * Mathf.Lerp(0.2f, 1f, e));
                    if (poolR) { poolR.GetPropertyBlock(mpb); mpb.SetColor(ID_Base, new Color(1f, 0.6f, 0.2f, 0.4f * k)); poolR.SetPropertyBlock(mpb); }
                }
            }
        }
    }
}
