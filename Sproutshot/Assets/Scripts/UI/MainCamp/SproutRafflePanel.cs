using System;
using System.Collections;
using System.Collections.Generic;
using Sproutshot.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    public class SproutRafflePanel : MonoBehaviour
    {
        [SerializeField] private Image _illustration;
        [SerializeField] private TMP_FontAsset _font;
        [SerializeField] private Vector2 _poolNormalizedPosition = new Vector2(.5f, .355f);
        [SerializeField, Range(0f, 1f)] private float _soundVolume = .8f;
        [Tooltip("尚未接入抽卡业务时，用已有精灵预览演出，不扣货币、不入库。")]
        [SerializeField] private bool _previewWhenNoProvider = true;
        private SproutSummonAssets _assets;
        private SproutSummonView _view;
        private Coroutine _loading;
        private bool _isLoading;
        private bool _requesting;
        private int _openVersion;
        private TMP_Text _singleCost;
        private TMP_Text _tenCost;
        public TMP_FontAsset Font => _font;

        private void OnEnable()
        {
            CloudEconomy.Changed += RefreshCosts;
            RefreshCosts();
        }

        private void RefreshCosts()
        {
            if (_singleCost == null || _tenCost == null)
                foreach (var image in GetComponentsInChildren<Image>(true))
                {
                    if (image.sprite == null || (image.sprite.name != "cost_once" && image.sprite.name != "cost_ten")) continue;
                    string textName = "CloudCost";
                    var existing = image.transform.Find(textName);
                    TMP_Text label;
                    if (existing != null) label = existing.GetComponent<TMP_Text>();
                    else
                    {
                        var go = new GameObject(textName,typeof(RectTransform),typeof(TextMeshProUGUI));
                        go.transform.SetParent(image.transform,false); label=go.GetComponent<TextMeshProUGUI>();
                        label.font=_font; label.fontSize=28; label.alignment=TextAlignmentOptions.Center;
                        label.color=new Color(.1f,.22f,.15f); label.raycastTarget=false;
                        label.rectTransform.anchorMin=Vector2.zero; label.rectTransform.anchorMax=Vector2.one;
                        label.rectTransform.sizeDelta=Vector2.zero;
                    }
                    image.enabled=false;
                    if(image.sprite.name=="cost_once") _singleCost=label; else _tenCost=label;
                }
#if UNITY_EDITOR
            if (!CloudBaseClient.IsEditorTestMode)
            {
                if (_singleCost != null) _singleCost.text="演出预览";
                if (_tenCost != null) _tenCost.text="演出预览";
                return;
            }
#endif
            if (_singleCost != null) _singleCost.text=CloudEconomy.Catalog==null ? "星露 --" : $"星露 {CloudEconomy.Catalog.singleCost}";
            if (_tenCost != null) _tenCost.text=CloudEconomy.Catalog==null ? "星露 --" : $"星露 {CloudEconomy.Catalog.tenCost}";
        }

        /// <summary>后续抽卡业务可提供已确定的结果；UI 不负责概率、扣费和入库。</summary>
        public Func<int, SproutSummonResult[]> ResultProvider { get; set; }
        public bool IsBusy => _requesting || _isLoading || (_view != null && _view.IsOpen);

        public void RaffleOnce() => Request(1);
        public void RaffleTen() => Request(10);

        /// <summary>异步抽卡业务拿到真实结果后，也可直接调用。只接受单抽或十连。</summary>
        public bool PlayResults(SproutSummonResult[] results)
        {
            if (!isActiveAndEnabled || IsBusy || !ValidateResults(results)) return false;
            // 演出期间业务可以复用原数组，跳过仍展示本次已确定的快照。
            var snapshot = (SproutSummonResult[])results.Clone();
            return Begin(snapshot, 0, false);
        }

        private async void Request(int count)
        {
            if (!isActiveAndEnabled || IsBusy) return;
#if UNITY_EDITOR
            // 编辑器的演出预览与正式云端抽卡分开，不产生任何永久资产。
            if (!CloudBaseClient.IsEditorTestMode)
            {
            if (ResultProvider != null)
            {
                SproutSummonResult[] results;
                _requesting = true;
                try { results = ResultProvider(count); }
                catch (Exception error)
                {
                    Debug.LogError($"[SproutRafflePanel] 获取抽卡结果失败：{error.Message}");
                    return;
                }
                finally { _requesting = false; }
                if (!ValidateResults(results) || results.Length != count)
                {
                    Debug.LogError("[SproutRafflePanel] 抽取结果数量与按钮不一致。");
                    return;
                }
                PlayResults(results);
            }
            else if (_previewWhenNoProvider) Begin(null, count, true);
            else Debug.LogWarning("[SproutRafflePanel] 尚未接入抽卡结果提供方。");
            return;
            }
#endif
            int version = _openVersion;
            _requesting = true;
            try
            {
                await CloudEconomy.RefreshAsync();
                if (!isActiveAndEnabled || version != _openVersion) return;
                if (CloudEconomy.HasPending)
                {
                    var recovered = await CloudEconomy.RetryPendingAsync();
                    if (!isActiveAndEnabled || version != _openVersion) return;
                    if (recovered?.receipt?.kind == "summon") Begin(null, 0, false, recovered.receipt);
                    else CloudFeedback.Show("上次操作已恢复，请继续。", font: _font);
                    return;
                }
                // 保存成功但演出中断的上一批结果优先展示，避免用户以为重试又买了一次。
                if (TryShowUnseenReceipt()) return;
                var data = await CloudEconomy.ExecuteAsync("summon", new EconomyPayload { pool = "standard", count = count });
                if (!isActiveAndEnabled || version != _openVersion) return;
                Begin(null, 0, false, data.receipt);
            }
            catch (Exception error)
            {
                if (isActiveAndEnabled && version == _openVersion)
                    CloudFeedback.Show(error.Message, () => Request(count), _font);
            }
            finally { _requesting = false; }
        }

        private bool TryShowUnseenReceipt()
        {
            var profile = CloudEconomy.Profile;
            var receipt = profile?.lastSummon;
            if (receipt?.results == null || receipt.results.Length == 0) return false;
            string key = "Sproutshot.Cloud.SeenSummon." + profile.playerId;
            if (PlayerPrefs.GetString(key, string.Empty) == receipt.operationId) return false;
            return Begin(null, 0, false, receipt);
        }

        private bool Begin(SproutSummonResult[] snapshot, int previewCount, bool preview, EconomyReceipt receipt = null)
        {
            if (_view == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas == null || _font == null)
                {
                    Debug.LogError("[SproutRafflePanel] 需要所属 Canvas 与中文 TMP 字体引用。");
                    return false;
                }
                _view = new GameObject("SproutSummonOverlay", typeof(RectTransform))
                    .AddComponent<SproutSummonView>();
                _view.Initialize(canvas, _font, ClosePresentation);
            }
            _isLoading = true; // 在启动协程前锁定，防止连点。
            _view.ShowLoading();
            _loading = StartCoroutine(LoadAndPlay(snapshot, previewCount, preview, receipt));
            return true;
        }

        private IEnumerator LoadAndPlay(SproutSummonResult[] snapshot, int previewCount, bool preview, EconomyReceipt receipt)
        {
            try
            {
                if (!YooAssetManager.Instance.IsInitialized)
                    yield return YooAssetManager.Instance.InitDefaultPackage();
                if (!YooAssetManager.Instance.IsInitialized)
                {
                    _view.ShowError();
                    yield break;
                }
                if (_assets == null) _assets = new SproutSummonAssets();
                yield return _assets.Load();
                if (!_assets.IsReady)
                {
                    _view.ShowError();
                    yield break;
                }
                if (preview)
                {
                    int[] ids = GetPreviewSpiritIds();
                    yield return _assets.LoadPortraits(ids);
                    snapshot = CreatePreviewResults(previewCount, ids);
                }
                else if (receipt != null)
                {
                    var ids = new List<int>();
                    if (receipt.results != null) foreach (var item in receipt.results) if (item != null) ids.Add(item.spiritId);
                    yield return _assets.LoadPortraits(ids);
                    snapshot = CreateReceiptResults(receipt);
                }
                if (!ValidateResults(snapshot)) { _view.ShowError(); yield break; }
                string summary = receipt == null ? null : receipt.rewardSeeds > 0 ?
                    $"重复精灵返还 {receipt.rewardSeeds} 萌芽种子" : "新精灵已加入背包";
                _view.Play(snapshot, _assets, _illustration, _poolNormalizedPosition, _soundVolume, preview, summary);
                if (receipt != null && CloudEconomy.Profile != null)
                {
                    PlayerPrefs.SetString("Sproutshot.Cloud.SeenSummon." + CloudEconomy.Profile.playerId, receipt.operationId);
                    PlayerPrefs.Save();
                }
            }
            finally
            {
                _isLoading = false;
                _loading = null;
            }
        }

        private static int[] GetPreviewSpiritIds()
        {
            var tables = LubanConfigManager.Instance.Tables;
            var ids = new List<int>();
            foreach (var spirit in tables.EntityTbGrowth.DataList)
                if (spirit.Id != 1 && tables.EntityTbAttribute.GetOrDefault(spirit.Id) != null) ids.Add(spirit.Id);
            if (ids.Count == 0) ids.Add(1);
            return ids.ToArray();
        }

        private SproutSummonResult[] CreateReceiptResults(EconomyReceipt receipt)
        {
            if (receipt?.results == null) return null;
            var results = new SproutSummonResult[receipt.results.Length];
            for (int i = 0; i < results.Length; i++)
            {
                var item = receipt.results[i];
                if (item == null) return null;
                var sprite = _assets.GetPortrait(item.spiritId);
                string label = item.isNew ? item.name + "\n（新）" : $"{item.name}\n+{item.duplicateSeedAmount}种子";
                Color accent = item.rarity == 2 ? new Color(1f,.8f,.25f) :
                    item.rarity == 1 ? new Color(.45f,.75f,1f) : new Color(.7f,1f,.78f);
                results[i] = new SproutSummonResult(sprite, label, accent);
            }
            return results;
        }

        private SproutSummonResult[] CreatePreviewResults(int count, int[] ids)
        {
            var results = new SproutSummonResult[count];
            for (int i = 0; i < count; i++)
            {
                // 明确是演出预览，不用 Random 冒充尚未实现的抽卡概率。
                int id = ids[i % ids.Length];
                Sprite sprite = _assets.GetPortrait(id);
                string name = LubanConfigManager.Instance.Tables.EntityTbAttribute.GetOrDefault(id)?.Name ?? "小精灵";
                results[i] = new SproutSummonResult(sprite, name, new Color(.70f, 1f, .78f));
            }
            return results;
        }

        private static bool ValidateResults(SproutSummonResult[] results)
        {
            if (results == null || (results.Length != 1 && results.Length != 10)) return false;
            foreach (var result in results) if (result.Sprite == null) return false;
            return true;
        }

        public void ClosePresentation()
        {
            if (_loading != null && _isLoading)
            {
                StopCoroutine(_loading);
                // 中途关闭时，释放还在加载的句柄，下次打开重新加载。
                _assets?.Dispose();
                _assets = null;
            }
            _loading = null;
            _isLoading = false;
            if (_view != null) _view.Hide();
        }

        public void OnOpened(float duration, Vector2? slideOffset = null)
        {
            _openVersion++;
            ProceduralAnimation.DoPanelVisibility(transform, true, duration, slideOffset);
        }

        public void OnClosed(float duration, Vector2? slideOffset = null)
        {
            _openVersion++;
            ClosePresentation();
            ProceduralAnimation.DoPanelVisibility(transform, false, duration, slideOffset);
        }

        private void OnDisable()
        {
            _openVersion++;
            CloudEconomy.Changed -= RefreshCosts;
            ClosePresentation();
        }

        private void OnDestroy()
        {
            ClosePresentation();
            if (_view != null) Destroy(_view.gameObject);
            _assets?.Dispose();
        }
    }
}
