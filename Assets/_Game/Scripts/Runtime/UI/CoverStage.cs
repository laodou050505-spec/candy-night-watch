// 封面（标题界面）：
//   背景 = 封面专用的“月下纸剧场”（10-29 用户：要更高级、不要和游戏场景一样；原先用的是游戏里的庄园花园）：
//          夜空、满月、月下鬼堡、远山小镇、墓园、两侧框景大树、层间流雾（stage/Backdrop，CoverBackdrop 搭）；
//          一整圈 360°：身后也有布景（鬼屋、风车、南瓜田、稻草人），脚下是雾海（10-29 用户：身后也要正常显示，不要黑色过渡）。
//          原先身后渐黑的 CoverVignette 罩子不再打开。
//   前景 = 漂浮的南瓜桌小布景（糖桶、坩埚、烛台、小鬼们、漂浮蜡烛、小墓园浮岛）+ 右边的标题字与按钮
//          （由 HudPresenter 搭在 logoAnchor / buttonAnchor 下）。
// 进封面时把游戏场景（花园、万圣节布景、场景月亮、桌面那一套、小鬼）整个藏起来，只留灯光、后期、玩家和界面；离开时还原。
// 封面固定在场景正前方（座位由 XRFloorOrigin 启动时自动校准到正对 -Z），所以一定在玩家面前。
using System.Collections.Generic;
using UnityEngine;

namespace TrickOrFreeze.UI
{
    public class CoverStage : MonoBehaviour
    {
        public Transform stage;            // 封面内容根（本地坐标：玩家在原点、眼高 1.15、面朝 -Z）
        public Transform logoAnchor, buttonAnchor, panelAnchor;
        public Camera cam;
        public Renderer vignette;          // 身后渐黑罩子（CoverVignette 材质的球）
        public GameObject[] hideInCover;   // 额外要藏的
        /// <summary>进封面时藏起来的 World 子节点（游戏桌面那一套）。</summary>
        // 不序列化：改清单不用重建场景（10-29 曾因场景里存着旧清单，花园没被藏起来）
        static readonly string[] hideNames = { "Desk_Table", "Desk_Bucket", "Desk_Cauldron", "Desk_CandleCluster", "VineRamps", "Actors", "BucketLight", "Garden", "Halloween", "Moon", "SkyDome" };
        /// <summary>这些 World 子节点里，离座位 clearRadius 米内的小摆件也藏起来（墓碑、棺材、蘑菇灯……），
        /// 只留地面、围墙、藤架、拱门这些大结构——标题和按钮后面要干净，前方远处的庄园才是背景主体。</summary>
        public string[] declutterRoots = { "Garden", "Halloween" };
        public float clearRadius = 5f, keepIfLargerThan = 3f;
        // 10-31：是否在封面、以及封面藏了哪些物体都存进场景（以前是运行时字段：编辑器里预览封面后脚本一重编译、
        // 或 Rebuild Gameplay 重建 CoverStage，这份清单就丢了，被藏的桌子、花园再也恢复不了，存场景时还被一起存成关着的）
        [SerializeField, HideInInspector] bool active;
        [SerializeField, HideInInspector] List<GameObject> hiddenNow = new List<GameObject>();
        public bool Active { get => active; private set => active = value; }
        MaterialPropertyBlock mpb;
        float fade;
        CameraClearFlags savedClear = CameraClearFlags.Skybox; Color savedBg;
        static readonly int ID_Forward = Shader.PropertyToID("_Forward"), ID_Fade = Shader.PropertyToID("_Fade");

        public void Enter()
        {
            if (Active) { Place(); return; }
            Active = true;
            hiddenNow.Clear();
            var world = transform.parent;
            if (world)
                foreach (Transform c in world)
                {
                    if (System.Array.IndexOf(hideNames, c.name) >= 0) { Hide(c.gameObject); continue; }
                    if (System.Array.IndexOf(declutterRoots, c.name) < 0) continue;
                    foreach (Transform d in c)
                    {
                        var rs = d.GetComponentsInChildren<Renderer>();
                        if (rs.Length == 0) continue;
                        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                        var lc = world.InverseTransformPoint(b.center);
                        if (b.size.magnitude < keepIfLargerThan && new Vector2(lc.x, lc.z).magnitude < clearRadius) Hide(d.gameObject);
                    }
                }
            foreach (var g in gameObject.scene.GetRootGameObjects())
                if (!transform.IsChildOf(g.transform) && !(cam && cam.transform.IsChildOf(g.transform))) Hide(g);
            if (hideInCover != null) foreach (var g in hideInCover) if (g) Hide(g);
            stage.gameObject.SetActive(true);
            if (vignette) vignette.gameObject.SetActive(false);   // 不再用身后渐黑（背景已是一整圈）
            fade = 0;
            // 封面有自己的夜空大球，原来的天空盒不用画（头显上省一整屏）
            if (cam) { savedClear = cam.clearFlags; savedBg = cam.backgroundColor; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; }
            Place();
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
            foreach (var g in hiddenNow) if (g) g.SetActive(true);
            hiddenNow.Clear();
            stage.gameObject.SetActive(false);
            if (vignette) vignette.gameObject.SetActive(false);
            if (cam) { cam.clearFlags = savedClear; cam.backgroundColor = savedBg; }
        }

        /// <summary>编辑器工具用：不管当前状态，把封面藏起来的东西全部恢复（存场景、重建前调用）</summary>
        public void ForceExit()
        {
            Active = true;
            Exit();
        }

        void Hide(GameObject g)
        {
            if (!g.activeSelf || hiddenNow.Contains(g)) return;   // 本来就关着的不碰，离开封面时也不会被误开
            g.SetActive(false);
            hiddenNow.Add(g);
        }

        void LateUpdate()
        {
            if (!Active || !vignette) return;
            // 罩子跟着头走（只跟位置，方向永远是封面正前方）；进封面时 0.8 秒淡入
            if (cam) vignette.transform.position = cam.transform.position;
            fade = Mathf.MoveTowards(fade, 1, Time.unscaledDeltaTime / 0.8f);
            if (mpb == null) mpb = new MaterialPropertyBlock();
            vignette.GetPropertyBlock(mpb);
            var f = stage.TransformDirection(Vector3.back);
            mpb.SetVector(ID_Forward, new Vector4(f.x, 0, f.z, 0));
            mpb.SetFloat(ID_Fade, Mathf.SmoothStep(0, 1, fade));
            vignette.SetPropertyBlock(mpb);
        }

        void Place()
        {
            stage.localPosition = Vector3.zero;
            stage.localRotation = Quaternion.identity;
        }
    }
}
