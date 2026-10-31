// 本地化完整性（附加用例，不在原型 40 项内）：规则层用到的每个键在 zh.json / en.json 里都存在，且中文与规则层默认文本一致。
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TrickOrFreeze.Core;
using UnityEngine;

namespace TrickOrFreeze.Tests
{
    public class LocalizationTests
    {
        static Dictionary<string, string> Load(string name)
        {
            var text = File.ReadAllText(Path.Combine(Application.dataPath, "_Game/Localization", name));
            var d = new Dictionary<string, string>();
            foreach (Match m in Regex.Matches(text, "\"((?:[^\"\\\\]|\\\\.)*)\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""))
                d[Regex.Unescape(m.Groups[1].Value)] = Regex.Unescape(m.Groups[2].Value);
            return d;
        }

        [Test]
        public void Extra_本地化键齐全且中文一致()
        {
            var zh = Load("zh.json");
            var en = Load("en.json");
            CollectionAssert.AreEquivalent(zh.Keys, en.Keys);
            var expect = new Dictionary<string, string>();
            foreach (var c in NightDefs.CHAPTERS) expect[c.nameKey] = c.name;
            foreach (var n in NightDefs.NIGHTS)
            {
                expect[n.nameKey] = n.name;
                if (n.intro != null) expect[n.introKey] = n.intro;
                if (n.lesson != null) foreach (var s in n.lesson) expect[s.sayKey] = s.say;
                if (n.brief != null) foreach (var s in n.brief) expect[s.sayKey] = s.say;
            }
            foreach (var k in EnemyKinds.All.Values)
            {
                expect[k.nameKey] = k.label;
                if (k.HasIntro) expect[k.introKey] = k.intro;
            }
            foreach (var kv in expect)
            {
                Assert.That(zh.ContainsKey(kv.Key), "zh 缺键 " + kv.Key);
                Assert.AreEqual(kv.Value, zh[kv.Key], kv.Key);
                Assert.That(!string.IsNullOrEmpty(en[kv.Key]), "en 缺文本 " + kv.Key);
            }
        }
    }
}
