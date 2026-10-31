// 每夜挑战与失败小技巧（10-31 审查：糖果币只靠“剩糖超过最好成绩”，买齐商店后就没有重玩动力；结算页也没有统计和建议）
// 规则层只负责判定，“首次完成发币”和存档由表现层（Progress）处理。
using System.Collections.Generic;

namespace TrickOrFreeze.Core
{
    public static class Challenges
    {
        /// <summary>本夜的三个挑战。数值随夜数递增；有首领的夜把第三个换成“打倒首领”</summary>
        public static List<ChallengeResult> Evaluate(NightDef night, int nightIndex, NightResult r)
        {
            var list = new List<ChallengeResult>();
            var st = r.stats;
            int stars = 1 + nightIndex / 3;                       // 1,1,1,2,2,2,3,3,3,4,4,4
            int digest = 2 + nightIndex / 2;                      // 2..7
            list.Add(Make("noLoss", "一颗糖都没被偷走", r.win && r.lost == 0, 4));
            if (nightIndex >= 3)
                list.Add(Make("stars", "送 {n} 只小鬼上天当星星", st.stars >= stars, 3, stars));
            else
                list.Add(Make("pulls", "隔空把 {n} 只小鬼拉过来", st.pulls >= 2, 3, 2));
            if (night.boss != null)
                list.Add(Make("boss", "打倒缝合熊", st.bossDown > 0, 5));
            else
                list.Add(Make("digest", "坩埚消化 {n} 只小鬼", st.digested >= digest, 3, digest));
            return list;
        }

        static ChallengeResult Make(string id, string text, bool done, int coins, int n = 0)
        {
            var c = new ChallengeResult { id = id, key = "challenge." + id, text = text.Replace("{n}", n.ToString()), done = done, coins = coins };
            c.args["n"] = n.ToString();
            return c;
        }

        /// <summary>没守住时给一条最相关的小技巧：按本夜的统计找最明显的短板</summary>
        public static (string key, string text) Tip(NightDef night, GameStats st, int lost)
        {
            bool has(string s) => System.Array.IndexOf(night.skills, s) >= 0;
            if (st.boxFull >= 3) return ("tip.potFull", "坩埚满了就先把小鬼扔远，或者等它发金光后甩成星星，别捏着干等。");
            if (st.pulls == 0) return ("tip.pull", "不用等小鬼走近：盯住它，再把手伸到身前捏一下，就能隔空拉过来。");
            if (has(SkillIds.Pray) && st.SkillUse(SkillIds.Pray) == 0) return ("tip.pray", "小鬼一多就双手合十，全场定住 3 秒，趁机一只只抓。");
            if (has(SkillIds.Sweep) && st.SkillUse(SkillIds.Sweep) == 0) return ("tip.sweep", "双手从胸前向两边一挥，能把桌上的小鬼都吹出去。");
            if (night.dark) return ("tip.dark", "黑夜里先召唤南瓜灯，把它挪到糖桶旁边，偷糖的猫就藏不住了。");
            if (st.stars == 0 && st.boxed < 3) return ("tip.star", "捏住小鬼别动，等它发金光，再朝远处用力一甩。");
            return ("tip.residual", "盯住小鬼一会儿再移开目光，它还会僵一阵：盯得越久，僵得越久。");
        }
    }
}
