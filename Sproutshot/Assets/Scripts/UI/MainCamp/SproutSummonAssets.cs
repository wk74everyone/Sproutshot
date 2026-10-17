using System;
using System.Collections;
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
            "generic_doodle_button_frame_512x128",
            "player_sprout_128",
            "NPC_Yellow",
            "NPC_Pink",
            "NPC_Blue"
        };

        private static readonly string[] AudioAddresses =
        {
            "summon_bubbles_gulu",
            "summon_spirit_pop"
        };

        private readonly AssetHandle[] _spriteHandles = new AssetHandle[SpriteAddresses.Length];
        private readonly AssetHandle[] _audioHandles = new AssetHandle[AudioAddresses.Length];
        private readonly Sprite[] _previewSpirits = new Sprite[4];
        private bool _loading;
        private bool _disposed;
        private int _loadVersion;

        public bool IsReady { get; private set; }
        public Sprite StarDew { get; private set; }
        public Sprite Ripple { get; private set; }
        public Sprite Sparkle { get; private set; }
        public Sprite CardFrame { get; private set; }
        public Sprite ButtonFrame { get; private set; }
        public Sprite[] PreviewSpirits => _previewSpirits;
        public AudioClip Bubbles { get; private set; }
        public AudioClip Pop { get; private set; }

        /// <summary>由调用者先初始化资源包。所有资源并行请求，图片缺失时整体失败，音效允许缺失。</summary>
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
            for (int i = 0; i < _spriteHandles.Length; i++)
                _spriteHandles[i] = Request<Sprite>(manager, SpriteAddresses[i]);
            for (int i = 0; i < _audioHandles.Length; i++)
                _audioHandles[i] = Request<AudioClip>(manager, AudioAddresses[i]);

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

            for (int i = 0; i < _audioHandles.Length; i++)
            {
                AssetHandle handle = _audioHandles[i];
                if (handle != null)
                    yield return handle;

                if (_disposed || version != _loadVersion)
                    yield break;

                if (!HasAsset<AudioClip>(handle))
                {
                    Debug.LogWarning($"[SproutSummonAssets] 抽取音效加载失败：{AudioAddresses[i]}，本次使用无声音表现。");
                    manager.Release(handle);
                    _audioHandles[i] = null;
                }
            }

            if (allSpritesLoaded)
            {
                StarDew = _spriteHandles[0].AssetObject as Sprite;
                Ripple = _spriteHandles[1].AssetObject as Sprite;
                Sparkle = _spriteHandles[2].AssetObject as Sprite;
                CardFrame = _spriteHandles[3].AssetObject as Sprite;
                ButtonFrame = _spriteHandles[4].AssetObject as Sprite;
                for (int i = 0; i < _previewSpirits.Length; i++)
                    _previewSpirits[i] = _spriteHandles[5 + i].AssetObject as Sprite;
                Bubbles = _audioHandles[0]?.AssetObject as AudioClip;
                Pop = _audioHandles[1]?.AssetObject as AudioClip;
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
            Bubbles = Pop = null;
            Array.Clear(_previewSpirits, 0, _previewSpirits.Length);
            ReleaseHandles(YooAssetManager.Instance);
        }

        private void ReleaseHandles(YooAssetManager manager)
        {
            for (int i = 0; i < _spriteHandles.Length; i++)
            {
                manager.Release(_spriteHandles[i]);
                _spriteHandles[i] = null;
            }
            for (int i = 0; i < _audioHandles.Length; i++)
            {
                manager.Release(_audioHandles[i]);
                _audioHandles[i] = null;
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
