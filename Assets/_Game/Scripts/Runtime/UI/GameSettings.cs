// 玩家设置（存在 PlayerPrefs）：音乐音量、音效音量、视线光点开关、新手教学是否已完成。界面改了立刻生效。
// 10-31 内存缓存 Music/Sfx，避免高频帧里反复读 PlayerPrefs；改值时立即 Save
using UnityEngine;

namespace TrickOrFreeze.UI
{
    public static class GameSettings
    {
        public static event System.Action Changed;

        // 内存缓存：NaN = 尚未初始化，首次访问从 PlayerPrefs 读取
        static float _music = float.NaN, _sfx = float.NaN;

        public static float Music
        {
            get
            {
                if (float.IsNaN(_music)) _music = PlayerPrefs.GetFloat("set.music", 1f);
                return _music;
            }
            set
            {
                _music = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("set.music", _music);
                PlayerPrefs.Save(); // 10-31 改值即持久化
                Changed?.Invoke();
            }
        }

        public static float Sfx
        {
            get
            {
                if (float.IsNaN(_sfx)) _sfx = PlayerPrefs.GetFloat("set.sfx", 1f);
                return _sfx;
            }
            set
            {
                _sfx = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("set.sfx", _sfx);
                PlayerPrefs.Save(); // 10-31 改值即持久化
                Changed?.Invoke();
            }
        }

        public static bool TutorialDone { get => PlayerPrefs.GetInt("tut.done", 0) == 1; set { PlayerPrefs.SetInt("tut.done", value ? 1 : 0); PlayerPrefs.Save(); Changed?.Invoke(); } }
        public static bool GazeDot { get => PlayerPrefs.GetInt("set.gazeDot", 1) == 1; set { PlayerPrefs.SetInt("set.gazeDot", value ? 1 : 0); Changed?.Invoke(); } }

        // 10-31 外部（如 HudPresenter 换肤）需要通知设置变更时调用此方法；event 只能在类内部 Invoke
        public static void NotifyChanged() => Changed?.Invoke();
    }
}
