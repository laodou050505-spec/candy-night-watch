// 编辑器菜单：运行规则层 EditMode 测试，并把结果写到 Logs/core_tests.json。
// 用途：MCP 的 RunCommand 需要用户确认时，可通过 ManageMenuItem 执行这个菜单来跑测试。
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace TrickOrFreeze.Tests
{
    public static class CoreTestRunnerMenu
    {
        static string OutPath => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs", "core_tests.json");

        [MenuItem("Tools/TrickOrFreeze/Run Core Tests")]
        public static void Run()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            if (File.Exists(OutPath)) File.Delete(OutPath);
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Callbacks());
            var filter = new Filter { testMode = TestMode.EditMode, assemblyNames = new[] { "TrickOrFreeze.Tests.EditMode" } };
            api.Execute(new ExecutionSettings(filter) { runSynchronously = true });   // 同步跑完：编辑器在后台不刷新时异步测试会一直挂着
        }

        class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor t) { }
            public void TestStarted(ITestAdaptor t) { }
            public void TestFinished(ITestResultAdaptor r) { }

            public void RunFinished(ITestResultAdaptor r)
            {
                var sb = new StringBuilder();
                sb.Append("{\"pass\":").Append(r.PassCount).Append(",\"fail\":").Append(r.FailCount)
                  .Append(",\"skip\":").Append(r.SkipCount).Append(",\"tests\":[");
                bool first = true;
                Walk(r, sb, ref first);
                sb.Append("]}");
                File.WriteAllText(OutPath, sb.ToString());
                Debug.Log($"[TrickOrFreeze] 规则层测试：通过 {r.PassCount}，失败 {r.FailCount} → {OutPath}");
            }

            static string Esc(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");

            static void Walk(ITestResultAdaptor r, StringBuilder sb, ref bool first)
            {
                if (!r.HasChildren)
                {
                    if (!first) sb.Append(",");
                    first = false;
                    sb.Append("{\"name\":\"").Append(Esc(r.Test.FullName)).Append("\",\"status\":\"").Append(r.TestStatus)
                      .Append("\",\"dur\":").Append(r.Duration.ToString("F3", CultureInfo.InvariantCulture))
                      .Append(",\"msg\":\"").Append(Esc(r.Message)).Append("\"}");
                    return;
                }
                foreach (var c in r.Children) Walk(c, sb, ref first);
            }
        }
    }
}
