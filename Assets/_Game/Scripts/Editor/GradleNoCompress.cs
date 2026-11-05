// 生成 Gradle 工程之后、打 APK 之前的处理：
//
// 1) 清掉残留的旧分片，并校验每个分片文件的总长度——10-29 实机“一直卡在加载、进不了封面”的真正原因：
//    Unity 把大于 1MB 的数据文件切成 xxx.split0、split1……；Gradle 工程放在 Library 里会被反复复用，
//    新数据变小、需要的分片变少时，Unity 不会删掉多出来的旧分片。10-28 把打包副本的 Library 拷回了原工程，
//    globalgamemanagers.assets 当时有 10 片（split0~9），之后每次只需要 9 片，旧的 split9 一直被打进 APK；
//    头显按“所有分片首尾相接”读文件，长度比文件头声明的多出 37 万字节，引擎在加载全局着色器时死循环。
//    处理：按每组分片 split0 里的序列化文件头读出声明的大小，删掉多余分片；删完总长度还对不上就让打包失败（不出坏包）。
//
// 2) （已停用）APK 资源全部不压缩：当时误判是解压导致卡死，实际是上面的残留分片；保留 Patch 方法备查，不再调用。
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace TrickOrFreeze.EditorTools
{
    public class GradleNoCompress : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 1000;
        const long SplitSize = 1048576;

        public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
        {
            FixSplits(Path.Combine(unityLibraryPath, "src", "main", "assets", "bin", "Data"));
            WriteLocalizedAppName(unityLibraryPath);
        }

        /// <summary>
        /// 11-04 用户：中文从头到尾是中文、英文从头到尾是英文——应用名跟着头显系统语言：
        /// 默认（中文）“万圣守糖夜”，系统语言是英文时 values-en 里的“Candy Night Watch”。
        /// </summary>
        static void WriteLocalizedAppName(string unityLibraryPath)
        {
            var res = Path.Combine(Path.GetDirectoryName(unityLibraryPath), "launcher", "src", "main", "res");
            if (!Directory.Exists(res)) { Debug.LogWarning("[AppName] 找不到 " + res); return; }
            var dir = Path.Combine(res, "values-en");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "strings.xml"),
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<resources>\n  <string name=\"app_name\">" + AndroidBuild.ProductNameEn + "</string>\n</resources>\n");
            Debug.Log("[AppName] 已写入英文应用名 " + AndroidBuild.ProductNameEn);
        }

        /// <summary>按文件头声明的大小删掉多余的旧分片；校验失败抛异常中止打包。</summary>
        public static void FixSplits(string dataDir)
        {
            if (!Directory.Exists(dataDir)) { Debug.LogWarning("[SplitCheck] 找不到 " + dataDir); return; }
            var groups = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>>();
            foreach (var f in Directory.GetFiles(dataDir))
            {
                var m = Regex.Match(Path.GetFileName(f), @"^(.+)\.split(\d+)$");
                if (!m.Success) continue;
                if (!groups.TryGetValue(m.Groups[1].Value, out var l)) groups[m.Groups[1].Value] = l = new System.Collections.Generic.List<int>();
                l.Add(int.Parse(m.Groups[2].Value));
            }
            foreach (var kv in groups)
            {
                string Part(int i) => Path.Combine(dataDir, $"{kv.Key}.split{i}");
                long declared = DeclaredSize(Part(0));
                if (declared <= 0) continue;                                   // 不是序列化文件（没有文件头），不处理
                int need = (int)((declared + SplitSize - 1) / SplitSize);
                foreach (var i in kv.Value)
                    if (i >= need) { File.Delete(Part(i)); Debug.LogWarning($"[SplitCheck] 删掉残留的旧分片 {kv.Key}.split{i}"); }
                long total = 0;
                for (int i = 0; i < need; i++)
                {
                    if (!File.Exists(Part(i))) throw new BuildFailedException($"[SplitCheck] {kv.Key} 缺分片 split{i}");
                    total += new FileInfo(Part(i)).Length;
                }
                if (total != declared) throw new BuildFailedException($"[SplitCheck] {kv.Key} 分片总长 {total} ≠ 文件头声明 {declared}");
                Debug.Log($"[SplitCheck] {kv.Key}：{need} 片，共 {total} 字节，与文件头一致");
            }
        }

        // Unity 序列化文件头（大端）：版本号在 8~11 字节；版本 ≥22 时文件大小是 24~31 字节的 64 位整数，否则是 4~7 字节
        static long DeclaredSize(string path)
        {
            if (!File.Exists(path)) return -1;
            var b = new byte[48];
            using (var fs = File.OpenRead(path)) if (fs.Read(b, 0, b.Length) < b.Length) return -1;
            uint ver = BE32(b, 8);
            if (ver < 9 || ver > 40) return -1;
            return ver >= 22 ? (long)(((ulong)BE32(b, 24) << 32) | BE32(b, 28)) : BE32(b, 4);
        }

        static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

        static void Patch(string file)
        {
            if (!File.Exists(file)) { Debug.LogWarning("[GradleNoCompress] 找不到 " + file); return; }
            var lines = File.ReadAllLines(file);
            int n = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].TrimStart();
                if (!t.StartsWith("noCompress")) continue;
                lines[i] = lines[i].Substring(0, lines[i].Length - t.Length) + "noCompress = ['']";
                n++;
            }
            File.WriteAllLines(file, lines);
            Debug.Log($"[GradleNoCompress] {file}：改了 {n} 处，APK 里的文件全部不压缩存放");
        }
    }
}
