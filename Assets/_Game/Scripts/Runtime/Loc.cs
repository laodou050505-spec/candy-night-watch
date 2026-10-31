// 本地化：运行时读 Resources 下的 zh/en（构建时由编辑器同步自 Assets/_Game/Localization），支持 {参数} 代入与语言切换事件。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TrickOrFreeze
{
    public static class Loc
    {
        public static string Lang { get; private set; } = "zh";
        public static event Action Changed;
        static Dictionary<string, string> table;

        static void Ensure()
        {
            if (table != null) return;
            Lang = PlayerPrefs.GetString("lang", Application.systemLanguage == SystemLanguage.Chinese || Application.systemLanguage == SystemLanguage.ChineseSimplified ? "zh" : "en");
            Load();
        }

        static void Load()
        {
            var ta = Resources.Load<TextAsset>("Localization/" + Lang);
            table = new Dictionary<string, string>();
            if (ta == null) { Debug.LogError("[Loc] 缺少语言表 " + Lang); return; }
            foreach (Match m in Regex.Matches(ta.text, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                table[Regex.Unescape(m.Groups[1].Value)] = Regex.Unescape(m.Groups[2].Value);
        }

        public static void SetLang(string lang)
        {
            Ensure();
            if (lang == Lang) return;
            Lang = lang;
            PlayerPrefs.SetString("lang", lang);
            Load();
            Changed?.Invoke();
        }

        public static bool IsZh { get { Ensure(); return Lang == "zh"; } }

        /// <summary>取文本；缺键时用 fallback（规则层给的中文默认文本），再缺就返回键本身。</summary>
        public static string T(string key, string fallback = null, Dictionary<string, string> args = null)
        {
            Ensure();
            string s = key != null && table.TryGetValue(key, out var v) ? v : (fallback ?? key ?? "");
            if (args != null)
                foreach (var kv in args)
                {
                    string val = kv.Value;
                    // 规则层约定：参数名 kind 的值是敌人种类 id，需要再查名字
                    if (kv.Key == "kind") val = T("kind." + kv.Value + ".name", kv.Value);
                    s = s.Replace("{" + kv.Key + "}", val);
                }
            return s;
        }
    }
}
