using Sproutshot.Utils;
using Sproutshot.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>复用已有货币预制体的数字和 GetMore 入口；金额始终来自服务端。</summary>
    [DisallowMultipleComponent]
    public sealed class CloudWalletView : MonoBehaviour
    {
        private enum Currency { SproutSeed, StarDew }

        [SerializeField] private Currency _currency;
        [SerializeField] private TMP_Text _count;
        [SerializeField] private Button _getMore;
        private TMP_FontAsset Font => _count != null ? _count.font : null;

        private void OnEnable()
        {
            if (_getMore == null)
            {
                var more = transform.Find("GetMore");
                if (more != null) _getMore = more.GetComponent<Button>() ?? more.gameObject.AddComponent<Button>();
            }
            if (_getMore != null) _getMore.onClick.AddListener(RequestAdvertisement);
            CloudEconomy.Changed += Refresh;
            RewardedAdManager.BusyChanged += RefreshButton;
            Refresh();
        }

        private void RefreshButton()
        {
            if (_getMore != null) _getMore.interactable = !RewardedAdManager.IsBusy;
        }

        private async void RequestAdvertisement()
        {
            if (RewardedAdManager.IsBusy) return;
            if (CloudEconomy.PendingAction == "claimRewardedAd") { WatchAdvertisement(); return; }
            var ads = CloudEconomy.RewardedAds;
            if (ads == null)
            {
                try { await CloudEconomy.RefreshAsync(); }
                catch (System.Exception error) { if (this != null && isActiveAndEnabled) CloudFeedback.Show(error.Message, font: Font); return; }
                if (this == null || !isActiveAndEnabled) return;
                ads = CloudEconomy.RewardedAds;
                if (ads == null) { CloudFeedback.Show("服务端尚未配置广告奖励。", font: Font); return; }
            }
            if (!ads.enabled) { CloudFeedback.Show("广告奖励尚未开放。", font: Font); return; }
            bool seeds = _currency == Currency.SproutSeed;
            int remaining = seeds ? ads.seedRemaining : ads.dewRemaining;
            if (remaining <= 0) { CloudFeedback.Show("今天该奖励的领取次数已用完。", font: Font); return; }
            int amount = seeds ? ads.seedReward : ads.dewReward;
            CloudFeedback.Confirm($"完整观看广告可领取 {amount} {(seeds ? "种子" : "星露")}\n今日剩余 {remaining} 次", WatchAdvertisement, Font);
        }

        private async void WatchAdvertisement()
        {
            try
            {
                var data = await RewardedAdManager.Instance.WatchAsync(_currency == Currency.SproutSeed ? "seed" : "dew");
                if (this == null || !isActiveAndEnabled) return;
                if (data == null) { CloudFeedback.Show("广告未完整观看，未领取奖励。", font: Font); return; }
                var receipt = data.receipt;
                CloudFeedback.Show(receipt.rewardSeeds > 0 ? $"已领取 {receipt.rewardSeeds} 种子" : $"已领取 {receipt.rewardDew} 星露", font: Font);
            }
            catch (System.Exception error)
            {
                if (this == null || !isActiveAndEnabled) return;
                bool retry = CloudEconomy.PendingAction == "claimRewardedAd" || CloudEconomy.PendingAction == "beginRewardedAd";
                CloudFeedback.Show(error.Message, retry ? (System.Action)WatchAdvertisement : null, Font);
            }
        }

        private void Refresh()
        {
            RefreshButton();
            if (_count == null) return;
            var profile = CloudEconomy.Profile;
            if (profile == null)
            {
                _count.text = "--";
                return;
            }
            _count.text = (_currency == Currency.SproutSeed ? profile.sproutSeed : profile.starDew).ToString();
        }

        private void OnDisable()
        {
            CloudEconomy.Changed -= Refresh;
            RewardedAdManager.BusyChanged -= RefreshButton;
            if (_getMore != null) _getMore.onClick.RemoveListener(RequestAdvertisement);
        }
    }
}
