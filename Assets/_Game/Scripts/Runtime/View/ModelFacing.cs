// 模型正面方向表（10-29 用户：所有生物的行进方向必须和面朝方向一致）。
// Tripo 生成的生物模型大多正面朝模型 -X，代码里统一转 90° 让 -X 对准行进方向（Transform +Z）。
// 但有几只并不正好朝 -X——用 8 方向 + 7° 细分渲染逐只核对过：
//   E5_Mitten 黑猫：脸朝 -X 往 -Z 偏约 30°
//   E6_Boo   小幽灵：脸朝 -X 往 -Z 偏约 10°
//   H4_WitchOnBroom 女巫：脸和扫帚头朝 -Z（在 SkyFlyer.modelYaw 里处理）
// 返回：把模型正面对准 Transform +Z 需要的绕 Y 角度（度）。
namespace TrickOrFreeze.View
{
    public static class ModelFacing
    {
        public static float Yaw(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return 90f;
            if (prefabName.StartsWith("E5_Mitten")) return 120f;
            if (prefabName.StartsWith("E6_Boo")) return 100f;
            return 90f;
        }
    }
}
