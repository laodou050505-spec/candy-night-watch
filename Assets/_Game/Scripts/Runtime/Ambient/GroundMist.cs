// 贴地雾带：在指定区域里缓慢漂移、时隐时现的大团柔雾（低透明度叠加），让墓园、草坪和小径笼在薄雾里。
// 每个区域一个粒子系统，运行时生成（不占资源文件）；桌面附近不放雾，免得挡住玩法。
using UnityEngine;

namespace TrickOrFreeze.Ambient
{
    public class GroundMist : MonoBehaviour
    {
        [System.Serializable]
        public struct Zone { public Vector3 center; public Vector3 size; public int count; public Color color; }
        public Zone[] zones;

        void Start() => Build();

        /// <summary>生成各区域的雾粒子（编辑器截图时也可手动调用后 Simulate 预览）</summary>
        public void Build()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    float u = (x + 0.5f) / 32 - 1, v = (y + 0.5f) / 32 - 1;
                    float a = Mathf.Clamp01(1 - Mathf.Sqrt(u * u + v * v)); a = a * a * (3 - 2 * a);
                    tex.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            tex.Apply();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = new Material(sh);
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_SOFTPARTICLES_ON");
            mat.renderQueue = 3000;
            foreach (var z in zones)
            {
                var go = new GameObject("Mist");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = z.center;
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = ps.main;
                main.loop = true; main.prewarm = true; main.duration = 20;
                main.startLifetime = new ParticleSystem.MinMaxCurve(14, 22);
                main.startSpeed = 0;
                main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startColor = z.color;
                main.maxParticles = z.count * 2;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var em = ps.emission; em.rateOverTime = z.count / 18f;
                var shp = ps.shape; shp.shapeType = ParticleSystemShapeType.Box; shp.scale = z.size;
                var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(0.05f, 0.15f); vel.y = new ParticleSystem.MinMaxCurve(-0.005f, 0.01f); vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
                var col = ps.colorOverLifetime; col.enabled = true;
                var gr = new Gradient();
                gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                           new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.3f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });
                col.color = gr;
                var r = go.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.HorizontalBillboard; r.sortingFudge = 10;   // 水平铺开的雾片：不会和地面相交出硬边
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                ps.Play();
            }
        }
    }
}
