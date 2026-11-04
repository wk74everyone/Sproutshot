using System;
using System.Collections;
using System.Collections.Generic;
using Sproutshot.Utils;
using UnityEngine;
using YooAsset;

namespace Sproutshot.UI
{
    /// <summary>养成与抽卡共用的立绘解析、加载和释放。地址只来自客户端 IconPath。</summary>
    public sealed class SpiritPortraitAssets : IDisposable
    {
        public const string FallbackAddress = "Sprout_hero_sprout";
        private readonly cfg.Tables _tables;
        private readonly Dictionary<string, AssetHandle> _handles = new Dictionary<string, AssetHandle>();
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>();
        private bool _disposed;

        public SpiritPortraitAssets(cfg.Tables tables) => _tables = tables ?? throw new ArgumentNullException(nameof(tables));

        public static string GetAddress(cfg.Tables tables, int spiritId)
        {
            string address = tables?.EntityTbAttribute.GetOrDefault(spiritId)?.IconPath?.Trim();
            if (string.IsNullOrEmpty(address)) return FallbackAddress;
            if (YooAssetManager.HasInstance && YooAssetManager.Instance.IsInitialized &&
                YooAssets.TryGetPackage("DefaultPackage", out var package) && !package.IsLocationValid(address))
                return FallbackAddress;
            return address;
        }

        public Sprite Get(int spiritId)
        {
            if (_disposed) return null;
            if (_sprites.TryGetValue(GetAddress(_tables, spiritId), out var sprite)) return sprite;
            return _sprites.TryGetValue(FallbackAddress, out sprite) ? sprite : null;
        }

        public bool IsLoaded(int spiritId) => !_disposed && _handles.ContainsKey(GetAddress(_tables, spiritId)) && _handles.ContainsKey(FallbackAddress);

        public IEnumerator Load(IEnumerable<int> spiritIds)
        {
            if (_disposed || !YooAssetManager.Instance.IsInitialized) yield break;
            var addresses = new HashSet<string> { FallbackAddress };
            if (spiritIds != null) foreach (int id in spiritIds) addresses.Add(GetAddress(_tables, id));
            foreach (string address in addresses)
            {
                if (_disposed) yield break;
                if (!_handles.TryGetValue(address, out var handle))
                {
                    handle = YooAssetManager.Instance.LoadAssetHandleAsync<Sprite>(address);
                    if (handle == null) continue;
                    _handles.Add(address, handle);
                }
                yield return handle;
                if (_disposed) yield break;
                if (handle.IsValid && handle.Status == EOperationStatus.Succeeded && handle.AssetObject is Sprite sprite)
                    _sprites[address] = sprite;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var handle in _handles.Values) if (handle != null && handle.IsValid) handle.Release();
            _handles.Clear(); _sprites.Clear();
        }
    }
}
