// 小蜘蛛：沿蛛网中心垂下一根丝，慢慢放下再爬回去（丝长随之伸缩）。偶尔停顿抖腿。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class SpiderClimb : MonoBehaviour
    {
        public float drop = 0.55f;
        public float period = 9f;
        Vector3 basePos; float seed; Transform silk;

        void Start()
        {
            basePos = transform.localPosition; seed = Random.value * 10;
            // 丝：细长圆柱（Unity 内置），放在蜘蛛与挂点之间
            var s = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(s.GetComponent<Collider>());
            s.name = "Silk"; silk = s.transform; silk.SetParent(transform.parent, false);
            var r = s.GetComponent<Renderer>();
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mat.color = new Color(0.85f, 0.88f, 1f);
            r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void Update()
        {
            float t = Time.time / period + seed;
            float k = Mathf.SmoothStep(0, 1, Mathf.PingPong(t * 2, 1));
            float pause = Mathf.Sin(Time.time * 23 + seed) * 0.006f * k;
            var p = basePos + Vector3.down * (drop * k) + Vector3.right * pause;
            transform.localPosition = p;
            transform.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(Time.time * 1.7f + seed) * 6);
            if (silk)
            {
                float len = Mathf.Max(0.001f, (basePos - p).magnitude);
                silk.localPosition = (basePos + p) / 2;
                silk.localScale = new Vector3(0.004f, len / 2, 0.004f);
            }
        }
    }
}
