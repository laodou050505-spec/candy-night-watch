// 游戏主循环：输入 → 规则层 Game.Update → 同步敌人/糖/灯视图 → 事件分派给表现层（特效、音效、字幕、UI）。
// 场景里只放一个，引用由 SceneBuilder 在编辑器里自动连好。
using System;
using System.Collections.Generic;
using TrickOrFreeze.Core;
using TrickOrFreeze.Input;
using TrickOrFreeze.View;
using UnityEngine;

namespace TrickOrFreeze
{
    public class GameLoop : MonoBehaviour
    {
        [Header("坐标")]
        public Transform worldRoot;          // 规则层坐标系（玩家坐在原点，面朝 -Z）
        [Header("输入")]
        public DesktopInputProvider desktopInput;
        public MonoBehaviour picoInput;      // PicoInputProvider（仅在 PICO 设备上启用）
        [Header("预制体（按种类 id）")]
        public List<KindPrefab> enemyPrefabs = new List<KindPrefab>();
        public GameObject candyPrefab;
        public Sprite candyIcon;             // 抱着糖的小鬼头顶的糖果图标
        public GameObject lampPrefab;
        public Material dizzyMat, trailMat;
        [Header("表现")]
        public SceneFx fx;
        public Sfx sfx;
        public HudPresenter hud;
        public SanctumView sanctum;

        [Serializable] public class KindPrefab { public string kind; public GameObject prefab; public float bellyFront = -1f; /* 10-31 编辑器量好的肚子前缘（模型本地单位），抱糖位置用 */ }

        public Game game { get; private set; }
        public bool paused;                  // 暂停菜单打开时规则层不走
        public event Action<GameEvent> OnEvent;
        IInputProvider input;
        readonly Dictionary<int, EnemyView> enemies = new Dictionary<int, EnemyView>();
        readonly Dictionary<int, Transform> candies = new Dictionary<int, Transform>();
        readonly Dictionary<int, Transform> lamps = new Dictionary<int, Transform>();
        // 10-31 SyncViews 复用集合，避免每帧 GC 分配
        readonly HashSet<int> _aliveSet = new HashSet<int>();
        readonly List<int> _deadList = new List<int>();
        // 10-31 LampView 缓存，避免每帧 GetComponent
        readonly Dictionary<int, LampView> _lampViews = new Dictionary<int, LampView>();
        Transform actorsRoot;

        void Awake()
        {
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 GameLoop.Awake 开始");
            BindSpawnsToScene();
            game = new Game(new GameOptions());
            // 10-31 防穿模：加载编辑器烘焙的场景占用网格（Rebuild Gameplay / 菜单 Bake Solid Grid 生成）
            var sg = Resources.Load<TextAsset>("solid_grid");
            game.solid = sg ? SolidGrid.FromBytes(sg.bytes) : null;
            if (fx && fx.skyStars) fx.skyStars.grid = game.solid;   // 星星飞上天时也避开藤架
            if (game.solid == null) Debug.LogWarning("[GameLoop] 没有场景占用网格（solid_grid.bytes），路线不做防穿模检查");
            actorsRoot = new GameObject("Actors").transform;
            actorsRoot.SetParent(worldRoot, false);
            bool xr = UnityEngine.XR.XRSettings.isDeviceActive;
            if (xr && picoInput is IInputProvider p) { input = p; if (desktopInput) desktopInput.enabled = false; }
            else { input = desktopInput; if (picoInput) picoInput.enabled = false; }
            TrickOrFreeze.Diag.DeviceDiag.Stage("6 GameLoop.Awake 完成");
        }

        // 地面出生点跟着场景里的摆件走：在摆件包围盒朝向其桌腿梯脚的那一侧边缘外 8cm 处冒出来（摆件挪了也不用改代码）
        static readonly (string id, string label, string obj, float leg)[] SpawnProps =
        {
            ("coffin", "小棺材", "S7_Coffin", -100), ("wardrobe", "獠牙衣柜", "S6_FangWardrobe", 100),
            ("grave", "墓碑", "S12_Gravestone", -40), ("pumpkins", "南瓜堆", "S8_PumpkinPile", 40),
        };

        public void BindSpawnsToScene()
        {
            var garden = worldRoot ? worldRoot.Find("Garden") : null;
            if (!garden) return;
            var list = new List<SpawnPoint>();
            foreach (var (id, label, obj, leg) in SpawnProps)
            {
                var t = garden.Find(obj);
                if (!t) continue;
                var rs = t.GetComponentsInChildren<Renderer>();
                if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                var foot = worldRoot.TransformPoint(Game.LegFloor(leg));
                var c = b.center; c.y = 0; foot.y = 0;
                var dir = (foot - c).normalized;
                // 包围盒（XZ）沿 dir 方向的外缘
                float ext = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
                var p = worldRoot.InverseTransformPoint(c + dir * (ext + 0.08f)); p.y = 0;
                list.Add(new SpawnPoint(id, label, p, leg));
            }
            if (list.Count == SpawnProps.Length) Game.SetGroundSpawns(list.ToArray());
            else Debug.LogWarning($"[GameLoop] 场景里只找到 {list.Count}/{SpawnProps.Length} 个出生摆件，沿用默认出生点");
        }

        public InputFrame CurrentInput => input?.Frame;
        public bool IsXR => input != null && input.IsXR;

        /// <summary>开始第 n 夜（0 起）。菜单/结算面板调用。</summary>
        public void StartNight(int index)
        {
            ClearViews();
            game.tutorial = !UI.GameSettings.TutorialDone;     // 新手教学只在第一次玩时出现
            ApplyUpgrades();
            game.StartNight(index);
            Dispatch(game.Update(0.0001, new InputFrame()));
        }

        /// <summary>封面“玩法”练习</summary>
        public void StartPractice()
        {
            ClearViews();
            ApplyUpgrades();
            game.StartPractice(Progress.Unlocked + 1);   // 10-31：按已解锁的夜数练对应的本事
            Dispatch(game.Update(0.0001, new InputFrame()));
        }

        // 糖果铺买的升级（UI.Shop）交给规则层
        void ApplyUpgrades()
        {
            game.capUpgrade = TrickOrFreeze.Shop.Level(TrickOrFreeze.Shop.Cauldron);
            game.magnetBonus = TrickOrFreeze.Shop.Level(TrickOrFreeze.Shop.Magnet) * 0.07f;
            game.digestMul = 1f - 0.15f * TrickOrFreeze.Shop.Level(TrickOrFreeze.Shop.Digest);
        }

        public void SkipLesson()
        {
            if (game.lesson != null && !game.lesson.done) game.FinishLesson();
            if (game.brief != null && !game.brief.done) game.FinishBrief();
        }

        public void BackToTitle()
        {
            ClearViews();
            game.phase = Phases.Title;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            input?.Tick(dt);
            var frame = input?.Frame ?? new InputFrame();
            GateSkills(frame);
            // 菜单阶段：规则层不跑（标题页的“捏合开始”由 UI 处理）
            if (game.phase == Phases.Playing && !paused)
            {
                var evs = game.Update(dt, frame);
                Dispatch(evs);
            }
            SyncViews(dt);
        }

        /// <summary>
        /// 这一关没有的手势技能（10-30 用户：前面关卡里还没学的、回头重玩前面的关也一样）：手势事件直接丢掉，
        /// 手套也不亮南瓜灯的蓄力光——看起来就像没这回事，不会让人以为触发了什么。菜单和封面里一律不认手势技能。
        /// </summary>
        void GateSkills(InputFrame frame)
        {
            bool playing = game.phase == Phases.Playing;
            // 10-31 用局部变量捕获避免 lambda 闭包每帧分配
            bool playingLocal = playing;
            var skills = game.skills;
            if (frame.events != null) frame.events.RemoveAll(e => e.type == InputEventTypes.Skill && (!playingLocal || e.id == null || !skills.ContainsKey(e.id)));
            if (frame.hands != null && !(playing && skills.ContainsKey(SkillIds.Lamp)))
                foreach (var h in frame.hands) if (h != null) h.lampK = 0;
        }

        // 10-31 应用焦点/后台状态：游戏中自动打开暂停菜单；AudioListener 跟着暂停
        void OnApplicationPause(bool pause)
        {
            if (pause && game.phase == Phases.Playing && !paused)
            {
                paused = true;
                AudioListener.pause = true;
                hud?.OpenPause();
            }
            else if (!pause)
            {
                AudioListener.pause = paused; // 10-31 恢复焦点时按暂停菜单状态决定是否继续暂停
            }
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus && game.phase == Phases.Playing && !paused)
            {
                paused = true;
                AudioListener.pause = true;
                hud?.OpenPause();
            }
        }

        void Dispatch(List<GameEvent> evs)
        {
            foreach (var e in evs)
            {
                try
                {
                    fx?.Handle(e, this);
                    sfx?.Handle(e, this);
                    hud?.Handle(e, this);
                    // 新手教学做完（或跳过）一次就记下来，以后第一夜也不再出现
                    if (e.type == GameEventTypes.LessonDone) UI.GameSettings.TutorialDone = true;
                    sanctum?.Handle(e, this);
                    OnEvent?.Invoke(e);
                }
                catch (Exception ex) { Debug.LogException(ex); }
            }
        }

        public Vector3 W(Vector3 local) => worldRoot.TransformPoint(local);

        void SyncViews(float dt)
        {
            float t = Time.time;
            // 10-31 复用集合，避免每帧 GC 分配
            _aliveSet.Clear();
            // 敌人
            foreach (var toy in game.toys)
            {
                _aliveSet.Add(toy.id);
                if (!enemies.TryGetValue(toy.id, out var v) || v.kind != toy.kind && !(v.kind == KindIds.Ghost && toy.kind == KindIds.Ghostling))
                {
                    if (v) Destroy(v.gameObject);
                    var go = new GameObject($"Enemy_{toy.kind}_{toy.id}");
                    go.transform.SetParent(actorsRoot, false);
                    v = go.AddComponent<EnemyView>();
                    v.bellyOverride = BellyFor(toy.kind);
                    v.Init(toy, PrefabFor(toy.kind), dizzyMat, trailMat, candyPrefab, candyIcon);
                    enemies[toy.id] = v;
                }
                v.Sync(toy, dt, t, game);
            }
            RemoveMissing(enemies, _aliveSet);
            // 散落的糖
            _aliveSet.Clear();
            foreach (var c in game.candies)
            {
                _aliveSet.Add(c.id);
                if (!candies.TryGetValue(c.id, out var tr))
                {
                    tr = Instantiate(candyPrefab, actorsRoot).transform;
                    tr.localRotation = Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0);
                    candies[c.id] = tr;
                }
                tr.localPosition = c.pos;
                if (c.state == CandyStates.Falling || c.state == CandyStates.Pulled) tr.Rotate(200 * dt, 300 * dt, 0, Space.Self);
                // 被注视时放大一点，提示“可以隔空取”
                float s = game.gazeCandy == c ? 1.25f : 1f;
                tr.localScale = Vector3.Lerp(tr.localScale, Vector3.one * s, dt * 10);
            }
            RemoveMissing(candies, _aliveSet);
            // 灯
            _aliveSet.Clear();
            foreach (var l in game.lamps)
            {
                _aliveSet.Add(l.id);
                if (!lamps.TryGetValue(l.id, out var tr)) { tr = Instantiate(lampPrefab, actorsRoot).transform; tr.localPosition = l.from; lamps[l.id] = tr; }
                // 10-31 LampView 缓存，避免每帧 GetComponent
                if (!_lampViews.TryGetValue(l.id, out var lv) || !lv) _lampViews[l.id] = lv = tr.GetComponent<LampView>();
                if (lv) lv.Sync(l, dt, Camera.main);
                else tr.localPosition = l.pos;
            }
            RemoveMissing(lamps, _aliveSet);
        }

        float BellyFor(string kind)
        {
            string k = kind == KindIds.Ghostling ? KindIds.Ghost : kind;   // 小幽灵用大幽灵模型（模型本地单位相同）
            foreach (var p in enemyPrefabs) if (p.kind == k) return p.bellyFront;
            return -1f;
        }

        GameObject PrefabFor(string kind)
        {
            string k = kind == KindIds.Ghostling ? KindIds.Ghost : kind;
            foreach (var p in enemyPrefabs) if (p.kind == k) return p.prefab;
            return enemyPrefabs[0].prefab;
        }

        void RemoveMissing<T>(Dictionary<int, T> d, HashSet<int> alive) where T : Component
        {
            // 10-31 复用 _deadList，避免每次 new List<int>()
            _deadList.Clear();
            foreach (var kv in d) if (!alive.Contains(kv.Key)) _deadList.Add(kv.Key);
            foreach (var id in _deadList) { if (d[id]) Destroy(d[id].gameObject); d.Remove(id); _lampViews.Remove(id); }
        }

        void ClearViews()
        {
            foreach (var v in enemies.Values) if (v) Destroy(v.gameObject);
            foreach (var v in candies.Values) if (v) Destroy(v.gameObject);
            foreach (var v in lamps.Values) if (v) Destroy(v.gameObject);
            enemies.Clear(); candies.Clear(); lamps.Clear();
        }
    }
}
