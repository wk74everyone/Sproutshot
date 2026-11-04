using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Scripting;

namespace Sproutshot.Utils
{
    [Serializable]
    public sealed class CloudBaseSettings
    {
        public bool enabled;
        public string envId;
        public string wechatAppId;
    }

    [Serializable]
    public sealed class CloudBaseData
    {
        public string playerId;
        public int highestClearedStage;
        public int revealedMapMask;
        public string objectKey;
        public string url;
        public int expiresIn;
        public string tempFilePath;
        public string text;
        public EconomyProfile profile;
        public EconomyCatalog catalog;
        public EconomyReceipt receipt;
        public EconomyReceipt[] history;
        public RewardedAdStatus rewardedAds;
        public bool completed;
        public StageProgressData Progress => new StageProgressData {
            HighestClearedStage = highestClearedStage, RevealedMapMask = revealedMapMask
        };
    }

    public sealed class CloudBaseException : Exception
    {
        public string Code { get; }
        public CloudBaseException(string code, string message) : base(message) { Code = code; }
    }

    [Preserve]
    public sealed class CloudBaseClient : MonoBehaviour
    {
        [Serializable]
        private sealed class Request
        {
            public int requestId;
            public string action;
            public string envId;
            public int highestClearedStage;
            public int revealedMapMask;
            public string localFilePath;
            public string fileName;
            public string contentType;
            public string objectKey;
            public string text;
            public string adUnitId;
            public string economyAction;
            public string operationId;
            public string expectedPlayerId;
            public EconomyPayload payload;
        }

        [Serializable]
        private sealed class Response
        {
            public int requestId;
            public bool success;
            public string code;
            public string message;
            public CloudBaseData data;
        }

        private sealed class Pending
        {
            public TaskCompletionSource<CloudBaseData> Completion;
            public float Deadline;
        }

        private static CloudBaseClient _instance;
        private readonly Dictionary<int, Pending> _pending = new Dictionary<int, Pending>();
        private readonly List<int> _expired = new List<int>();
        private CloudBaseSettings _settings;
        private int _nextRequest;
        private bool _initialized;
        private bool _verifiedPlayer;
        private float _nextSync;
        private float _retrySeconds = 5f;
        private Task _sync;

        public string LastError { get; private set; }
        public bool IsReady => _initialized && _verifiedPlayer;
#if UNITY_EDITOR
        // Assigned by Assets/Editor. Neither transport nor local credentials exist in player builds.
        public static Func<bool> EditorBackendEnabled;
        public static Func<string, Task<string>> EditorRequest;
#endif
        public static bool IsEditorTestMode
        {
            get
            {
#if UNITY_EDITOR
                return EditorBackendEnabled?.Invoke() == true && EditorRequest != null;
#else
                return false;
#endif
            }
        }
        public static bool IsSupportedPlatform
        {
            get
            {
#if (UNITY_WEBGL || UNITY_WEIXINMINIGAME) && !UNITY_EDITOR
                return true;
#elif UNITY_EDITOR
                return IsEditorTestMode;
#else
                return false;
#endif
            }
        }

        public static CloudBaseClient Instance
        {
            get
            {
                if (_instance != null) return _instance;
                if (!Application.isPlaying) throw new InvalidOperationException("CloudBase requires Play Mode.");
                var host = new GameObject("SproutshotCloudBase");
                _instance = host.AddComponent<CloudBaseClient>();
                DontDestroyOnLoad(host);
                return _instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() { _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (!IsSupportedPlatform) return;
            if (IsEditorTestMode) { var editorClient = Instance; return; }
            var asset = Resources.Load<TextAsset>("CloudBaseSettings");
            if (asset == null) return;
            var settings = JsonUtility.FromJson<CloudBaseSettings>(asset.text);
            if (settings != null && settings.enabled) { var client = Instance; }
        }

        private void Awake()
        {
            _instance = this;
            var asset = Resources.Load<TextAsset>("CloudBaseSettings");
            _settings = asset == null ? null : JsonUtility.FromJson<CloudBaseSettings>(asset.text);
        }

        private void Update()
        {
            _expired.Clear();
            foreach (var item in _pending)
                if (Time.realtimeSinceStartup >= item.Value.Deadline) _expired.Add(item.Key);
            foreach (int id in _expired)
            {
                var pending = _pending[id];
                _pending.Remove(id);
                pending.Completion.TrySetException(new CloudBaseException("TIMEOUT", "云服务响应超时，请检查网络后重试。"));
            }
            if (IsSupportedPlatform && (IsEditorTestMode || (_settings != null && _settings.enabled)) &&
                (_sync == null || _sync.IsCompleted) && Time.realtimeSinceStartup >= _nextSync)
                _sync = SynchronizeAsync();
        }

        private async Task SynchronizeAsync()
        {
            try
            {
                await CloudEconomy.RefreshAsync();
                _verifiedPlayer = true;
                _nextSync = Time.realtimeSinceStartup + 60f;
                _retrySeconds = 5f;
                LastError = null;
            }
            catch (Exception error)
            {
                string code = error is CloudBaseException cloud ? cloud.Code : "CLOUD_SYNC_FAILED";
                if (LastError != code) Debug.LogWarning("[CloudBase] " + code + ": " + error.Message);
                LastError = code;
                _nextSync = Time.realtimeSinceStartup + _retrySeconds;
                _retrySeconds = Mathf.Min(60f, _retrySeconds * 2f);
            }
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;
            if (IsEditorTestMode)
            {
                await SendAsync(new Request { action = "initialize" });
                _initialized = true;
                return;
            }
            if (!IsSupportedPlatform) throw new CloudBaseException("UNSUPPORTED_PLATFORM", "请在微信小游戏中验证云开发调用。");
            if (_settings == null || !_settings.enabled || string.IsNullOrWhiteSpace(_settings.envId) ||
                string.IsNullOrWhiteSpace(_settings.wechatAppId))
                throw new CloudBaseException("WECHAT_APP_ID_REQUIRED", "请先在 CloudBaseSettings 配置正式 AppID 并启用云开发。");
            await SendAsync(new Request { action = "initialize", envId = _settings.envId });
            _initialized = true;
        }

        public async Task<CloudBaseData> EconomyAsync(string action, string operationId, EconomyPayload payload)
        {
            await InitializeAsync();
            return await SendAsync(new Request { action = "economy", economyAction = action,
                operationId = operationId, payload = payload,
                expectedPlayerId = action == "load" ? null : CloudEconomy.Profile?.playerId });
        }

        public async Task<bool> PlayRewardedAdAsync(string adUnitId)
        {
            await InitializeAsync();
            return (await SendAsync(new Request { action = "playRewardedAd", adUnitId = adUnitId })).completed;
        }

        public async Task<CloudBaseData> UploadFileAsync(string localFilePath, string fileName, string contentType)
        {
            await InitializeAsync();
            return await SendAsync(new Request { action = "uploadFile", localFilePath = localFilePath, fileName = fileName, contentType = contentType });
        }

        public async Task<CloudBaseData> UploadTextAsync(string text, string fileName, string contentType = "application/json")
        {
            await InitializeAsync();
            return await SendAsync(new Request { action = "uploadText", text = text, fileName = fileName, contentType = contentType });
        }

        public async Task<CloudBaseData> DownloadFileAsync(string objectKey)
        {
            await InitializeAsync();
            return await SendAsync(new Request { action = "downloadFile", objectKey = objectKey });
        }

        public async Task<CloudBaseData> DownloadTextAsync(string objectKey)
        {
            await InitializeAsync();
            return await SendAsync(new Request { action = "downloadText", objectKey = objectKey });
        }

        public async Task<CloudBaseData> GetFileUrlAsync(string objectKey)
        {
            await InitializeAsync();
            return await SendAsync(new Request { action = "getFileUrl", objectKey = objectKey });
        }

        public async Task DeleteFileAsync(string objectKey)
        {
            await InitializeAsync();
            await SendAsync(new Request { action = "deleteFile", objectKey = objectKey });
        }

        // A real-device diagnostic: upload, download and compare, then remove only this probe file.
        public async Task VerifyStorageAsync()
        {
            string text = "{\"probe\":\"" + Guid.NewGuid().ToString("N") + "\"}";
            var file = await UploadTextAsync(text, "probe-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var downloaded = await DownloadTextAsync(file.objectKey);
                if (downloaded.text != text) throw new CloudBaseException("FILE_MISMATCH", "云存储回读内容不一致。");
            }
            finally { await DeleteFileAsync(file.objectKey); }
        }

        private Task<CloudBaseData> SendAsync(Request request)
        {
            var completion = new TaskCompletionSource<CloudBaseData>();
            request.requestId = ++_nextRequest;
            _pending.Add(request.requestId, new Pending { Completion = completion,
                Deadline = Time.realtimeSinceStartup + (request.action == "playRewardedAd" ? 200f : 45f) });
            try
            {
#if UNITY_EDITOR
                if (!IsEditorTestMode) throw new CloudBaseException("EDITOR_TEST_DISABLED", "请在本地测试窗口启用测试身份。");
                SendEditorRequestAsync(request);
#elif UNITY_WEBGL || UNITY_WEIXINMINIGAME
                SproutshotCloudBase_Request(JsonUtility.ToJson(request), gameObject.name);
#else
                throw new CloudBaseException("UNSUPPORTED_PLATFORM", "此运行环境不支持微信原生接口。");
#endif
            }
            catch (Exception error) { _pending.Remove(request.requestId); completion.TrySetException(error); }
            return completion.Task;
        }

#if UNITY_EDITOR
        private async void SendEditorRequestAsync(Request request)
        {
            try
            {
                string response = await EditorRequest(JsonUtility.ToJson(request));
                if (this != null) OnCloudBaseResponse(response);
            }
            catch (Exception error)
            {
                if (_pending.TryGetValue(request.requestId, out var pending))
                {
                    _pending.Remove(request.requestId);
                    pending.Completion.TrySetException(error is CloudBaseException ? error :
                        new CloudBaseException("EDITOR_BACKEND_UNAVAILABLE", "本地测试服务连接失败：" + error.Message));
                }
            }
        }
#endif

        [Preserve]
        public void OnCloudBaseResponse(string json)
        {
            Response response;
            try { response = JsonUtility.FromJson<Response>(json); }
            catch (ArgumentException) { Debug.LogWarning("[CloudBase] Invalid bridge response."); return; }
            if (response == null || !_pending.TryGetValue(response.requestId, out var pending)) return;
            _pending.Remove(response.requestId);
            if (response.success && response.data != null) pending.Completion.TrySetResult(response.data);
            else pending.Completion.TrySetException(new CloudBaseException(response.code ?? "INVALID_RESPONSE", response.message ?? "云服务返回无效响应。"));
        }

        private void OnDestroy()
        {
            var pendingRequests = new List<Pending>(_pending.Values);
            _pending.Clear();
            foreach (var pending in pendingRequests)
                pending.Completion.TrySetException(new CloudBaseException("CLIENT_DESTROYED", "云服务客户端已退出。"));
            if (_instance == this) _instance = null;
        }

#if (UNITY_WEBGL || UNITY_WEIXINMINIGAME) && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SproutshotCloudBase_Request(string request, string receiver);
#endif
    }
}
