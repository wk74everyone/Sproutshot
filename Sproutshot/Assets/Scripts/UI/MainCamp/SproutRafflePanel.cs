using System;
using System.Collections;
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

        private void Request(int count)
        {
            if (!isActiveAndEnabled || IsBusy) return;
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
        }

        private bool Begin(SproutSummonResult[] snapshot, int previewCount, bool preview)
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
            _loading = StartCoroutine(LoadAndPlay(snapshot, previewCount, preview));
            return true;
        }

        private IEnumerator LoadAndPlay(SproutSummonResult[] snapshot, int previewCount, bool preview)
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
                if (preview) snapshot = CreatePreviewResults(previewCount);
                _view.Play(snapshot, _assets, _illustration, _poolNormalizedPosition, _soundVolume, preview);
            }
            finally
            {
                _isLoading = false;
                _loading = null;
            }
        }

        private SproutSummonResult[] CreatePreviewResults(int count)
        {
            var results = new SproutSummonResult[count];
            for (int i = 0; i < count; i++)
            {
                // 明确是演出预览，不用 Random 冒充尚未实现的抽卡概率。
                Sprite sprite = _assets.PreviewSpirits[i % _assets.PreviewSpirits.Length];
                results[i] = new SproutSummonResult(sprite, "小精灵", new Color(.70f, 1f, .78f));
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

        public void OnOpened(float duration)
        {
            ProceduralAnimation.DoPanelVisibility(transform, true, duration);
        }

        public void OnClosed(float duration)
        {
            ClosePresentation();
            ProceduralAnimation.DoPanelVisibility(transform, false, duration);
        }

        private void OnDisable() => ClosePresentation();

        private void OnDestroy()
        {
            ClosePresentation();
            if (_view != null) Destroy(_view.gameObject);
            _assets?.Dispose();
        }
    }
}
