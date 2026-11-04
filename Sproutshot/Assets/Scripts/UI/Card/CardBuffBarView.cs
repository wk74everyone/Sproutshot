using System.Collections.Generic;
using Sproutshot.Gameplay;
using Sproutshot.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;

namespace Sproutshot.UI
{
    /// <summary>读取本局已领取卡牌的横向列表；复用图标并持有资源句柄至 HUD 释放。</summary>
    public sealed class CardBuffBarView : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private CardBuffItemView _itemTemplate;
        [SerializeField] private TMP_Text _emptyHint;
        private readonly Dictionary<int, CardBuffItemView> _items = new();
        private readonly Dictionary<string, AssetHandle> _icons = new();
        private RunContext _context;

        public void Bind(RunContext context)
        {
            if (!ReferenceEquals(_context, context))
            {
                ClearItems();
                _context = context;
                _scroll.StopMovement();
                _scroll.horizontalNormalizedPosition = 0;
            }
            _itemTemplate.gameObject.SetActive(false);
            if (context != null)
            {
                foreach (var acquired in context.Cards.AcquiredCards)
                {
                    if (acquired.Value <= 0) continue;
                    if (!_items.TryGetValue(acquired.Key, out var item))
                    {
                        item = Instantiate(_itemTemplate, _scroll.content);
                        item.name = "CardBuff_" + acquired.Key;
                        item.gameObject.SetActive(true);
                        _items.Add(acquired.Key, item);
                    }
                    var display = LubanConfigManager.Instance.Tables.TbDisplay.GetOrDefault(acquired.Key);
                    item.Bind(LoadIcon(display?.IconPath), acquired.Value);
                }
            }
            _emptyHint.gameObject.SetActive(_items.Count == 0);
            // 同帧领取后即可正确拖动；更新次数时保持玩家当前浏览位置。
            LayoutRebuilder.ForceRebuildLayoutImmediate(_scroll.content);
        }

        private Sprite LoadIcon(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            if (!_icons.TryGetValue(address, out var handle))
            {
                handle = YooAssetManager.Instance.LoadAssetHandleSync<Sprite>(address);
                if (handle != null) _icons.Add(address, handle);
            }
            return handle?.AssetObject as Sprite;
        }

        public void Unbind()
        {
            _context = null;
            ClearItems();
            if (_scroll != null) { _scroll.StopMovement(); _scroll.horizontalNormalizedPosition = 0; }
            if (_emptyHint != null) _emptyHint.gameObject.SetActive(true);
        }

        private void ClearItems()
        {
            foreach (var item in _items.Values)
                if (item != null)
                {
                    item.gameObject.SetActive(false);
                    Destroy(item.gameObject);
                }
            _items.Clear();
        }

        public void Release()
        {
            Unbind();
            foreach (var handle in _icons.Values)
                if (handle.IsValid) handle.Release();
            _icons.Clear();
        }

        private void OnDestroy() => Release();
    }
}
