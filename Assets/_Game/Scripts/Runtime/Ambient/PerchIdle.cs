// 栖息待机（乌鸦、猫头鹰、黑猫这类单网格小动物）：大部分时间静止只有轻微呼吸起伏，
// 每隔几秒快速转一下头（整体绕竖轴转 ±20~45°，停一会儿再转回来），偶尔轻跳一下。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class PerchIdle : MonoBehaviour
    {
        public float turnMax = 40f;
        public float hop = 0.04f;           // 轻跳高度（米），0 = 不跳
        public float breathe = 0.02f;
        Quaternion baseRot; Vector3 basePos, baseScale;
        float t, next, targetYaw, yaw, hopT = -1;

        void Start()
        {
            baseRot = transform.localRotation; basePos = transform.localPosition; baseScale = transform.localScale;
            next = Random.Range(1f, 4f); t = Random.Range(0f, 10f);
        }

        void Update()
        {
            float dt = Time.deltaTime; t += dt; next -= dt;
            if (next <= 0)
            {
                // 转头：要么转向一侧，要么回正；偶尔顺带轻跳
                targetYaw = Mathf.Abs(targetYaw) > 1f && Random.value < 0.6f ? 0 : Random.Range(-turnMax, turnMax);
                if (hop > 0 && Random.value < 0.25f) hopT = 0;
                next = Random.Range(1.5f, 5f);
            }
            yaw = Mathf.MoveTowards(yaw, targetYaw, dt * 260f);            // 动物转头很快
            float y = 0;
            if (hopT >= 0) { hopT += dt * 3.5f; y = Mathf.Sin(Mathf.Min(hopT, 1f) * Mathf.PI) * hop; if (hopT >= 1) hopT = -1; }
            transform.localRotation = baseRot * Quaternion.Euler(0, yaw, 0);
            transform.localPosition = basePos + Vector3.up * y;
            float b = 1 + Mathf.Sin(t * 2.2f) * breathe;
            transform.localScale = new Vector3(baseScale.x * b, baseScale.y * (2 - b), baseScale.z * b);
        }
    }
}
