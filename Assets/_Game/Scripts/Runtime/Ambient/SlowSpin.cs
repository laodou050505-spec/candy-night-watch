// 绕自身竖轴（Quad 平放时是本地 Z）慢慢转：封面脚下的雾海用，看起来雾在缓缓流动。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class SlowSpin : MonoBehaviour
    {
        public float degPerSec = 1f;
        void Update() { transform.Rotate(0, 0, degPerSec * Time.deltaTime, Space.Self); }
    }
}
