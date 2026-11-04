using System;
using System.Threading.Tasks;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class RewardedAdManager : MonoBehaviour
    {
        [Serializable]
        private sealed class Settings { public string seedAdUnitId, dewAdUnitId; }
        private static RewardedAdManager _instance;
        public static RewardedAdManager Instance
        {
            get
            {
                if (_instance != null) return _instance;
                _instance = new GameObject("RewardedAdManager").AddComponent<RewardedAdManager>();
                DontDestroyOnLoad(_instance.gameObject);
                return _instance;
            }
        }
        public static bool IsBusy { get; private set; }
        public static event Action BusyChanged;
#if UNITY_EDITOR
        // Set only by the editor assembly. A player build cannot simulate completed advertisements.
        public static Func<Task<bool>> EditorPlay;
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _instance = null; IsBusy = false; BusyChanged = null;
        }
        private void Awake() { if (_instance != null && _instance != this) { Destroy(gameObject); return; } _instance = this; }
        public async Task<CloudBaseData> WatchAsync(string rewardType)
        {
            if (rewardType != "seed" && rewardType != "dew") throw new ArgumentException("Unknown advertisement reward.", nameof(rewardType));
            if (IsBusy) throw new CloudBaseException("AD_BUSY", "广告正在处理，请稍候。");
            IsBusy = true; BusyChanged?.Invoke();
            try
            {
                if (CloudEconomy.Profile == null) await CloudEconomy.RefreshAsync();
                // A lost claim response must be retried without making the player watch another advertisement.
                if (CloudEconomy.PendingAction == "claimRewardedAd") return await CloudEconomy.RetryPendingAsync();
                string adUnitId = null;
                if (!CloudBaseClient.IsEditorTestMode)
                {
                    var asset = Resources.Load<TextAsset>("RewardedAdSettings");
                    var settings = asset == null ? null : JsonUtility.FromJson<Settings>(asset.text);
                    adUnitId = rewardType == "seed" ? settings?.seedAdUnitId : settings?.dewAdUnitId;
                    if (string.IsNullOrWhiteSpace(adUnitId)) throw new CloudBaseException("AD_UNIT_REQUIRED", "请先配置激励视频广告位 ID。");
                }
                var begun = await CloudEconomy.BeginRewardedAdAsync(rewardType);
                var ticket = begun.receipt;
                if (ticket == null || string.IsNullOrEmpty(ticket.ticketId)) throw new CloudBaseException("INVALID_AD_TICKET", "服务器未返回有效广告凭证。");
                if (!DateTimeOffset.TryParse(ticket.expiresAt, out var expires) || expires <= DateTimeOffset.UtcNow)
                    throw new CloudBaseException("AD_TICKET_EXPIRED", "上次广告申请已过期，请重新点击。");
                bool completed;
#if UNITY_EDITOR
                if (CloudBaseClient.IsEditorTestMode)
                {
                    if (EditorPlay == null) throw new CloudBaseException("AD_SIMULATOR_UNAVAILABLE", "编辑器广告模拟器尚未初始化。");
                    completed = await EditorPlay();
                }
                else
#endif
                    completed = await CloudBaseClient.Instance.PlayRewardedAdAsync(adUnitId);
                if (!completed) return null;
                return await CloudEconomy.ClaimRewardedAdAsync(ticket.ticketId);
            }
            finally { IsBusy = false; BusyChanged?.Invoke(); }
        }

    }
}
