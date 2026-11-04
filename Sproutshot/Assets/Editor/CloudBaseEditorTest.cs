#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Sproutshot.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

[InitializeOnLoad]
internal static class CloudBaseEditorTest
{
    [Serializable] internal sealed class Settings { public bool enabled = true; public string account = "player-a"; }
    [Serializable] private sealed class Session { public int protocol, pid; public string endpoint, token, projectKey; }
    [Serializable] private sealed class Health { public bool ok; public string projectKey; public int protocol; }
    [Serializable] internal sealed class Reply { public bool success; public string code, message; public CloudBaseData data; }
    private static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd(Path.DirectorySeparatorChar);
    private static readonly string SettingsPath = Path.Combine(Root, "UserSettings/SproutshotEditorTest.json");
    private static readonly string StateDirectory = Path.Combine(Root, "Library/SproutshotEditorBackend");
    private static readonly object LogLock = new object();
    private static Session _session;
    private static Task<Session> _starting;
    internal static Settings Configuration { get; private set; }
    internal static string DatabasePath => Path.Combine(StateDirectory, "database");

    static CloudBaseEditorTest()
    {
        try
        {
            Configuration = File.Exists(SettingsPath) ? JsonUtility.FromJson<Settings>(File.ReadAllText(SettingsPath)) : new Settings();
            if (Configuration == null) Configuration = new Settings();
        }
        catch (Exception error) { Configuration = new Settings { enabled = false }; Debug.LogWarning("[本地测试] 配置读取失败：" + error.Message); }
        CloudBaseClient.EditorBackendEnabled = () => Configuration.enabled;
        CloudBaseClient.EditorRequest = SendAsync;
    }

    internal static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
        File.WriteAllText(SettingsPath, JsonUtility.ToJson(Configuration, true), new UTF8Encoding(false));
    }

    internal static async Task StartAsync() { await EnsureServerAsync(); }
    internal static void ForgetSession() { _session = null; }

    private static bool IsRunning(int pid)
    {
        try { using (var process = Process.GetProcessById(pid)) return !process.HasExited && process.ProcessName.Equals("node", StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string ProjectKey()
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Root.ToLowerInvariant()))).Replace("-", "").ToLowerInvariant();
    }

    private static Session ReadSession()
    {
        string path = Path.Combine(StateDirectory, "session.json");
        if (!File.Exists(path)) return null;
        Session session;
        try { session = JsonUtility.FromJson<Session>(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (ArgumentException) { return null; }
        if (session == null || session.protocol != 1 || session.projectKey != ProjectKey() ||
            string.IsNullOrEmpty(session.token) || session.token.Length != 64 ||
            !Uri.TryCreate(session.endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1") return null;
        return session;
    }

    private static async Task<Session> EnsureServerAsync()
    {
        if (_session != null && IsRunning(_session.pid)) return _session;
        if (_starting == null) _starting = StartCoreAsync();
        try { _session = await _starting; return _session; }
        finally { _starting = null; }
    }

    private static async Task<Session> StartCoreAsync()
    {
        var previous = ReadSession();
        if (previous != null && IsRunning(previous.pid) && await Healthy(previous)) return previous;
        string dependency = Path.Combine(Root, "cloudbase/tests/node_modules/@electric-sql/pglite");
        if (!Directory.Exists(dependency)) throw new InvalidOperationException("缺少本地测试依赖，请在项目目录运行 npm.cmd ci --prefix cloudbase/tests。");
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs/node.exe");
        if (!File.Exists(executable))
        {
            executable = null;
            foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
            {
                string candidate = Path.Combine(directory.Trim('"'), "node.exe");
                if (File.Exists(candidate)) { executable = candidate; break; }
            }
        }
        if (executable == null || !File.Exists(executable)) throw new InvalidOperationException("未找到 Node.js，请安装 Node.js 后启动本地测试。");
        Directory.CreateDirectory(StateDirectory);
        string logPath = Path.Combine(StateDirectory, "backend.log");
        var process = new Process { StartInfo = new ProcessStartInfo {
            FileName = executable, Arguments = "\"" + Path.Combine(Root, "cloudbase/tests/editor-backend.cjs") + "\"",
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        } };
        DataReceivedEventHandler log = (_, args) => {
            if (string.IsNullOrEmpty(args.Data)) return;
            lock (LogLock) File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + args.Data + Environment.NewLine);
        };
        process.OutputDataReceived += log; process.ErrorDataReceived += log;
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        DateTime deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited) throw new InvalidOperationException("本地测试服务启动失败，请查看 Library/SproutshotEditorBackend/backend.log。");
            var candidate = ReadSession();
            if (candidate != null && candidate.pid == process.Id && await Healthy(candidate))
            {
                Debug.Log("[本地测试] 已连接独立 PostgreSQL 测试库，当前账号：" + Configuration.account);
                return candidate;
            }
            await Task.Delay(150);
        }
        throw new TimeoutException("本地测试服务启动超时，请查看 Library/SproutshotEditorBackend/backend.log。");
    }

    private static async Task<bool> Healthy(Session session)
    {
        using (var request = UnityWebRequest.Get(session.endpoint + "/health"))
        {
            request.timeout = 2; request.SetRequestHeader("Authorization", "Bearer " + session.token);
            await Complete(request.SendWebRequest());
            if (request.result != UnityWebRequest.Result.Success) return false;
            var health = JsonUtility.FromJson<Health>(request.downloadHandler.text);
            return health != null && health.ok && health.protocol == 1 && health.projectKey == ProjectKey();
        }
    }

    private static Task Complete(UnityWebRequestAsyncOperation operation)
    {
        var completion = new TaskCompletionSource<bool>();
        if (operation.isDone) completion.SetResult(true);
        else operation.completed += _ => completion.TrySetResult(true);
        return completion.Task;
    }

    private static async Task<string> SendAsync(string json)
    {
        if (!Configuration.enabled) throw new CloudBaseException("EDITOR_TEST_DISABLED", "本地测试身份未启用。");
        var session = await EnsureServerAsync();
        using (var request = new UnityWebRequest(session.endpoint + "/request", "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 35;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + session.token);
            request.SetRequestHeader("X-Sproutshot-Editor-Player", Configuration.account);
            await Complete(request.SendWebRequest());
            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError)
            { _session = null; throw new CloudBaseException("EDITOR_CONNECTION_FAILED", "本地测试服务连接失败，请重新连接后重试。"); }
            if (request.responseCode != 200)
            {
                var failure = JsonUtility.FromJson<Reply>(request.downloadHandler.text);
                throw new CloudBaseException(failure?.code ?? "EDITOR_HTTP_ERROR", failure?.message ?? "本地测试请求失败。");
            }
            return request.downloadHandler.text;
        }
    }

    internal static async Task<CloudBaseData> LoadAsync()
    {
        var reply = JsonUtility.FromJson<Reply>(await SendAsync("{\"requestId\":1,\"action\":\"economy\",\"economyAction\":\"load\"}"));
        if (!reply.success) throw new CloudBaseException(reply.code, reply.message);
        return reply.data;
    }

    internal static async Task<CloudBaseData> GrantAsync()
    {
        var json = "{\"requestId\":1,\"action\":\"editorGrant\",\"operationId\":\"" + Guid.NewGuid() + "\"}";
        var reply = JsonUtility.FromJson<Reply>(await SendAsync(json));
        if (!reply.success) throw new CloudBaseException(reply.code, reply.message);
        if (Application.isPlaying) await CloudEconomy.RefreshAsync();
        return reply.data;
    }
}

internal sealed class CloudBaseEditorTestWindow : EditorWindow
{
    private bool _busy;
    private string _status = "开启后，编辑器抽卡使用独立测试账号，正式微信构建不包含此入口。";
    private CloudBaseData _snapshot;

    [MenuItem("Tools/Sproutshot/本地测试")]
    private static void Open() { GetWindow<CloudBaseEditorTestWindow>("本地测试"); }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("抽卡与存档测试身份", EditorStyles.boldLabel);
        var settings = CloudBaseEditorTest.Configuration;
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || _busy || CloudGrowthConfigUpdater.IsBusy))
        {
            bool enabled = EditorGUILayout.Toggle("启用测试身份", settings.enabled);
            string account = EditorGUILayout.TextField("测试账号", settings.account);
            if (enabled != settings.enabled || account != settings.account)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(account, "^[a-zA-Z0-9_-]{1,40}$"))
                { settings.enabled = enabled; settings.account = account; CloudBaseEditorTest.Save(); _snapshot = null; }
            }
        }
        EditorGUILayout.HelpBox("账号首次获得 10000 种子和 10000 星露，仅发一次。再次进入播放会保留余额、精灵和保底。切换账号前请停止播放。", MessageType.Info);
        using (new EditorGUI.DisabledScope(_busy || CloudGrowthConfigUpdater.IsBusy || !settings.enabled))
        {
            if (GUILayout.Button("启动并连接测试服务")) Run(async () => { await CloudBaseEditorTest.StartAsync(); _snapshot = await CloudBaseEditorTest.LoadAsync(); });
            if (GUILayout.Button("刷新账号数据")) Run(async () => { _snapshot = await CloudBaseEditorTest.LoadAsync(); if (Application.isPlaying) await CloudEconomy.RefreshAsync(); });
            if (GUILayout.Button("补充 1000 种子和 1000 星露")) Run(async () => { _snapshot = await CloudBaseEditorTest.GrantAsync(); });
        }
        using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode || _busy || CloudGrowthConfigUpdater.IsBusy))
            if (GUILayout.Button("更新成长表配置（本地）")) CloudGrowthConfigUpdater.UpdateFromMenu();
        if (!string.IsNullOrEmpty(CloudGrowthConfigUpdater.LastMessage))
            EditorGUILayout.HelpBox(CloudGrowthConfigUpdater.LastMessage, MessageType.Info);
        if (_snapshot?.profile != null)
        {
            var profile = _snapshot.profile;
            EditorGUILayout.LabelField("种子 / 星露", profile.sproutSeed + " / " + profile.starDew);
            EditorGUILayout.LabelField("精灵数量 / 保底", (profile.ownedSpirits?.Length ?? 0) + " / " + profile.pityCount);
        }
        EditorGUILayout.HelpBox(_status, MessageType.None);
        EditorGUILayout.LabelField("测试数据库目录");
        EditorGUILayout.SelectableLabel(CloudBaseEditorTest.DatabasePath, GUILayout.Height(40));
    }

    private async void Run(Func<Task> work)
    {
        _busy = true; _status = "处理中…";
        try { await work(); _status = "已连接测试账号：" + CloudBaseEditorTest.Configuration.account; }
        catch (Exception error) { _status = error.Message; Debug.LogWarning("[本地测试] " + error.Message); }
        finally { _busy = false; if (this != null) Repaint(); }
    }
}
#endif
