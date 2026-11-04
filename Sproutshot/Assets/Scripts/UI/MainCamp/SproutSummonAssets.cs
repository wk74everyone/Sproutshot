using System;
using System.Collections;
using System.Collections.Generic;
using Sproutshot.Utils;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace Sproutshot.UI
{
    /// <summary>抽取表现所用的资源及其句柄。角色仅用于动画预览，不负责抽取概率或奖励。</summary>
    public sealed class SproutSummonAssets : IDisposable
    {
        private static readonly string[] SpriteAddresses =
        {
            "star_dew_reused_128x128",
            "ripple_256x128",
            "sparkle_64x64",
            "roster_normal_frame",
            "generic_doodle_button_frame_512x128"
        };


        private readonly AssetHandle[] _spriteHandles = new AssetHandle[SpriteAddresses.Length];
        private readonly SpiritPortraitAssets _portraits;
        private bool _loading;
        private bool _disposed;
        private int _loadVersion;

        public bool IsReady { get; private set; }
        public Sprite StarDew { get; private set; }
        public Sprite Ripple { get; private set; }
        public Sprite Sparkle { get; private set; }
        public Sprite CardFrame { get; private set; }
        public Sprite ButtonFrame { get; private set; }

        public SproutSummonAssets(cfg.Tables tables = null) =>
            _portraits = new SpiritPortraitAssets(tables ?? LubanConfigManager.Instance.Tables);

        public Sprite GetPortrait(int spiritId) => _portraits.Get(spiritId);
        public IEnumerator LoadPortraits(IEnumerable<int> spiritIds) => _portraits.Load(spiritIds);

        /// <summary>由调用者先初始化资源包。图片缺失时整体失败，音效由 AudioManager 缓存。</summary>
        public IEnumerator Load()
        {
            if (_disposed || IsReady)
                yield break;

            if (_loading)
            {
                while (_loading && !_disposed)
                    yield return null;
                yield break;
            }

            var manager = YooAssetManager.Instance;
            if (!manager.IsInitialized)
            {
                Debug.LogError("[SproutSummonAssets] 请先初始化 YooAsset 默认资源包。");
                yield break;
            }

            _loading = true;
            int version = ++_loadVersion;
            yield return AudioManager.Instance.Initialize();
            yield return AudioManager.Instance.PreloadScope(AudioScope.Scene);
            if (_disposed || version != _loadVersion) yield break;
            for (int i = 0; i < _spriteHandles.Length; i++)
                _spriteHandles[i] = Request<Sprite>(manager, SpriteAddresses[i]);

            bool allSpritesLoaded = true;
            for (int i = 0; i < _spriteHandles.Length; i++)
            {
                AssetHandle handle = _spriteHandles[i];
                if (handle != null)
                    yield return handle;

                // Dispose 可能发生于等待资源期间，不再访问已经释放的句柄。
                if (_disposed || version != _loadVersion)
                    yield break;

                if (!HasAsset<Sprite>(handle))
                {
                    allSpritesLoaded = false;
                    Debug.LogError($"[SproutSummonAssets] 抽取图片加载失败：{SpriteAddresses[i]}。请检查 YooAsset 采集配置和 Sprite 导入设置。");
                }
            }


            if (allSpritesLoaded)
            {
                StarDew = _spriteHandles[0].AssetObject as Sprite;
                Ripple = _spriteHandles[1].AssetObject as Sprite;
                Sparkle = _spriteHandles[2].AssetObject as Sprite;
                CardFrame = _spriteHandles[3].AssetObject as Sprite;
                ButtonFrame = _spriteHandles[4].AssetObject as Sprite;
                IsReady = true;
            }
            else
            {
                ReleaseHandles(manager);
            }

            _loading = false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _loading = false;
            ++_loadVersion;
            IsReady = false;
            StarDew = Ripple = Sparkle = CardFrame = ButtonFrame = null;
            _portraits.Dispose();
            ReleaseHandles(YooAssetManager.Instance);
        }

        private void ReleaseHandles(YooAssetManager manager)
        {
            for (int i = 0; i < _spriteHandles.Length; i++)
            {
                manager.Release(_spriteHandles[i]);
                _spriteHandles[i] = null;
            }
        }

        private static bool HasAsset<T>(AssetHandle handle) where T : Object
        {
            return handle != null && handle.IsValid &&
                   handle.Status == EOperationStatus.Succeeded && handle.AssetObject is T;
        }

        private static AssetHandle Request<T>(YooAssetManager manager, string address) where T : Object
        {
            try
            {
                return manager.LoadAssetHandleAsync<T>(address);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SproutSummonAssets] 资源请求失败：{address}\n{exception.Message}");
                return null;
            }
        }
    }
}
