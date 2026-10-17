using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>独立的顶层过渡 Canvas。花朵只创建一次，之后重复使用。</summary>
    public sealed class FlowerTransitionView : MonoBehaviour
    {
        private const int FlowerCount = 32;
        private readonly RectTransform[] _flowers = new RectTransform[FlowerCount];
        private readonly float[] _scales = new float[FlowerCount];
        private readonly float[] _delays = new float[FlowerCount];
        private RectTransform _root;
        private CanvasGroup _group;
        private Image _backdrop;
        private Sequence _animation;

        public void Initialize()
        {
            var canvasObject = new GameObject("FlowerTransitionCanvas", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup));
            _root = canvasObject.GetComponent<RectTransform>();
            _root.SetParent(transform, false);
            canvasObject.layer = 5;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            _group = canvasObject.GetComponent<CanvasGroup>();

            _backdrop = CreateImage("MintBackdrop", _root);
            var backdropRect = _backdrop.rectTransform;
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = backdropRect.offsetMax = Vector2.zero;
            _backdrop.color = new Color(0.86f, 0.94f, 0.78f, 0f);
            _backdrop.raycastTarget = true;

            for (int i = 0; i < FlowerCount; i++)
            {
                Image flower = CreateImage("Flower " + i, _root);
                flower.raycastTarget = false;
                flower.preserveAspect = true;
                _flowers[i] = flower.rectTransform;
            }
            Hide();
        }

        public void BlockInput()
        {
            _root.gameObject.SetActive(true);
            _group.alpha = 1f;
            _group.blocksRaycasts = true;
            _group.interactable = false;
        }

        public Tween Cover(Sprite[] sprites, float duration)
        {
            _animation?.Kill();
            BlockInput();
            Canvas.ForceUpdateCanvases();
            int columns = _root.rect.width > _root.rect.height ? 8 : 4;
            int rows = FlowerCount / columns;
            float size = Mathf.Min(_root.rect.width / columns, _root.rect.height / rows) * 1.65f;
            _backdrop.color = new Color(0.86f, 0.94f, 0.78f, 0f);
            _animation = DOTween.Sequence().SetUpdate(true);
            _animation.Insert(duration * 0.2f, FadeBackdrop(1f, duration * 0.65f));

            for (int i = 0; i < FlowerCount; i++)
            {
                var flower = _flowers[i];
                int column = i % columns;
                int row = i / columns;
                Vector2 anchor = new Vector2((column + 0.5f) / columns, (row + 0.5f) / rows);
                flower.anchorMin = flower.anchorMax = anchor;
                flower.anchoredPosition = Vector2.zero;
                flower.sizeDelta = Vector2.one * size;
                flower.localScale = Vector3.zero;
                flower.localEulerAngles = new Vector3(0f, 0f, (i * 137) % 360);
                flower.GetComponent<Image>().sprite = sprites[i % sprites.Length];
                _scales[i] = 0.87f + (i % 5) * 0.055f;
                _delays[i] = anchor.y * duration * 0.35f;
                _animation.Insert(_delays[i], flower.DOScale(_scales[i], duration * 0.65f)
                    .SetEase(Ease.OutBack));
            }
            return _animation;
        }

        public Tween Reveal(float duration)
        {
            _animation?.Kill();
            _animation = DOTween.Sequence().SetUpdate(true);
            _animation.Insert(0f, FadeBackdrop(0f, duration * 0.85f));
            for (int i = 0; i < FlowerCount; i++)
            {
                _animation.Insert(_delays[i] * 0.7f,
                    _flowers[i].DOScale(0f, duration * 0.7f).SetEase(Ease.InBack));
            }
            return _animation;
        }

        public void Hide()
        {
            _animation?.Kill();
            _animation = null;
            if (_root == null) return;
            _group.blocksRaycasts = false;
            _backdrop.color = new Color(0.86f, 0.94f, 0.78f, 0f);
            for (int i = 0; i < FlowerCount; i++)
                _flowers[i].localScale = Vector3.zero;
            _root.gameObject.SetActive(false);
        }

        private static Image CreateImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return go.GetComponent<Image>();
        }

        private Tween FadeBackdrop(float alpha, float duration)
        {
            // 不额外依赖 DOTween 的 Image 扩展模块。
            return DOTween.To(() => _backdrop.color.a, value =>
            {
                Color color = _backdrop.color;
                color.a = value;
                _backdrop.color = color;
            }, alpha, duration);
        }

        private void OnDestroy() => _animation?.Kill();
    }
}
