#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>直接导出成长 Excel，并更新独立测试数据库；不会调用云端管理接口。</summary>
internal static class CloudGrowthConfigUpdater
{
    [Serializable] private sealed class Report
    {
        public bool success, changed, profilePreserved, spiritConfigPreserved;
        public int version, levels;
        public string migration;
    }
    internal static bool IsBusy { get; private set; }
    internal static string LastMessage { get; private set; }

    static CloudGrowthConfigUpdater()
    {
        EditorApplication.playModeStateChanged += state => {
            if (IsBusy && state == PlayModeStateChange.ExitingEditMode)
            { EditorApplication.isPlaying = false; Debug.LogWarning("请等待成长配置更新完成后再进入播放。"); }
        };
    }

    [MenuItem("Tools/Sproutshot/更新成长表配置")]
    internal static async void UpdateFromMenu() { await RunAsync(false); }

    [MenuItem("Tools/Sproutshot/更新成长表配置", true)]
    private static bool CanUpdate() => !IsBusy && !EditorApplication.isPlayingOrWillChangePlaymode;

    [MenuItem("Tools/Sproutshot/重启本地测试后端")]
    private static async void RestartFromMenu() { await RunAsync(true); }

    [MenuItem("Tools/Sproutshot/重启本地测试后端", true)]
    private static bool CanRestart() => CanUpdate();

    private static async Task RunAsync(bool restartOnly)
    {
        if (!CanUpdate()) return;
        IsBusy = true;
        LastMessage = restartOnly ? "正在重启本地测试后端…" : "正在读取成长表并更新本地测试库…";
        EditorApplication.LockReloadAssemblies();
        try
        {
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string node = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs/node.exe");
            if (!File.Exists(node)) throw new InvalidOperationException("未找到 Node.js，请先安装 Node.js。");
            var info = new ProcessStartInfo {
                FileName = node,
                Arguments = "\"" + Path.Combine(root, "cloudbase/tools/update-growth-config.cjs") + "\"" + (restartOnly ? " --restart-only" : ""),
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(info))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                while (!process.HasExited) await Task.Delay(100);
                string output = await stdout, error = await stderr;
                if (process.ExitCode != 0) throw new InvalidOperationException(error.Trim());
                var report = JsonUtility.FromJson<Report>(output.Trim());
                if (report == null || !report.success || !report.profilePreserved || !report.spiritConfigPreserved)
                    throw new InvalidOperationException("成长配置更新结果无效，请查看本地日志。");
                CloudBaseEditorTest.ForgetSession();
                LastMessage = restartOnly ? "本地测试后端已重启，原存档保留。" : report.changed
                    ? $"已更新 {report.levels} 个等级的费用，配置版本 {report.version}。精灵属性和玩家存档保留。"
                    : $"已核对 {report.levels} 个等级，费用与成长表一致，无需新增迁移。精灵属性和玩家存档保留。";
                Debug.Log("[本地测试] " + LastMessage + (string.IsNullOrEmpty(report.migration) ? "" : "\n迁移文件：" + report.migration));
            }
        }
        catch (Exception error) { LastMessage = error.Message; Debug.LogError("[本地测试] " + LastMessage); }
        finally { EditorApplication.UnlockReloadAssemblies(); IsBusy = false; }
    }
}
#endif
