using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>最靠近 Viewport 中心的列表项放大，其余项缩小。不改变滚动位置。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    public sealed class ScrollCenterScale : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float _selectedScale = 1f;
        [SerializeField, Min(0.01f)] private float _unselectedScale = 0.8f;
        [SerializeField, Min(0f)] private float _transitionSpeed = 12f;

        private ChapterContainer _chapterContainer;
        private ScrollRect _scrollRect;
        private RectTransform _content;
        private RectTransform[] _items;
        private bool _applyImmediately;

        public int SelectedIndex { get; private set; } = -1;

        private void OnEnable()
        {
            _scrollRect = GetComponent<ScrollRect>();
            _chapterContainer = GetComponent<ChapterContainer>();
            RefreshItems();
        }

        // 动态替换或重排列表项后也可以调用；直接增删子物体会自动刷新。
        public void RefreshItems()
        {
            _content = _scrollRect != null ? _scrollRect.content : null;
            SelectedIndex = -1;
            _applyImmediately = true;
            if (_content == null)
            {
                _items = null;
                return;
            }

            _items = new RectTransform[_content.childCount];
            for (int i = 0; i < _items.Length; i++)
                _items[i] = _content.GetChild(i) as RectTransform;
        }

        private void Start()
        {
            // 初次显示时先完成布局，避免用布局前的位置选中错误的项。
            Canvas.ForceUpdateCanvases();
        }

        private void LateUpdate()
        {
            if (_scrollRect.content != _content ||
                (_content != null && (_items == null || _content.childCount != _items.Length)))
                RefreshItems();
            if (_content == null || _items == null)
                return;

            RectTransform viewport = _scrollRect.viewport != null
                ? _scrollRect.viewport
                : (RectTransform)_scrollRect.transform;
            Vector2 center = viewport.rect.center;
            float nearestDistance = float.PositiveInfinity;
            int nearestIndex = -1;

            for (int i = 0; i < _items.Length; i++)
            {
                RectTransform item = _items[i];
                if (item == null || !item.gameObject.activeInHierarchy)
                    continue;

                Vector3 itemCenter = viewport.InverseTransformPoint(item.TransformPoint(item.rect.center));
                float distance = _scrollRect.horizontal
                    ? Mathf.Abs(itemCenter.x - center.x)
                    : Mathf.Abs(itemCenter.y - center.y);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestIndex = i;
                }
            }

            if (SelectedIndex != nearestIndex)
            {
                SelectedIndex = nearestIndex;
                if (SelectedIndex >= 0 && _chapterContainer != null)
                    _chapterContainer.OnRefreshItem(SelectedIndex);
            }

            float blend = _applyImmediately || _transitionSpeed <= 0f
                ? 1f
                : 1f - Mathf.Exp(-_transitionSpeed * Time.unscaledDeltaTime);

            for (int i = 0; i < _items.Length; i++)
            {
                RectTransform item = _items[i];
                if (item == null)
                    continue;

                Vector3 target = Vector3.one * (i == SelectedIndex ? _selectedScale : _unselectedScale);
                if (item.localScale.Equals(target))
                    continue;

                Vector3 scale = Vector3.Lerp(item.localScale, target, blend);
                item.localScale = (scale - target).sqrMagnitude < 0.000001f ? target : scale;
            }

            _applyImmediately = false;
        }
    }
}
