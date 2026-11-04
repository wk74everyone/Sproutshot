using Sproutshot.Utils;
using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>复用少量 Image 完成召唤，不使用逐帧贴图、粒子系统或运行时纹理。</summary>
    public sealed class SproutSummonView : MonoBehaviour
    {
        private const int MaxResults = 10;
        private const int ResultColumns = 5;
        private const float CardColumnStep = 202f;
        private const float CardRowStep = 280f;
        private static readonly Vector2 TenCardSize = new Vector2(180, 250);
        private const float ContactTime = .38f;
        private const float PopTime = .84f;
        private const float RevealTime = 1.64f;
        private static readonly Color Ink = new Color(.10f, .25f, .23f);
        private static readonly Color Paper = new Color(1f, .98f, .90f);
        private readonly Image[] _ripples = new Image[2];
        private readonly Image[] _sparkles = new Image[5];
        private readonly RectTransform[] _cards = new RectTransform[MaxResults];
        private readonly Image[] _portraits = new Image[MaxResults];
        private readonly Image[] _cardFrames = new Image[MaxResults];
        private readonly TMP_Text[] _names = new TMP_Text[MaxResults];
        private RectTransform _root, _stage;
        private Image _backdrop, _dew, _hero;
        private TMP_Text _title, _hint;
        private Button _skip, _continue;
        private AudioHandle _bubbles, _pop;
        private float _soundVolume;
        private Sequence _sequence;
        private Image _illustration;
        private Color _illustrationColor;
        private SproutSummonResult[] _results;
        private bool _preview;
        private string _businessSummary;
        public bool IsOpen => gameObject.activeSelf;

        public void Initialize(Canvas canvas, TMP_FontAsset font, Action close)
        {
            _root = (RectTransform)transform;
            _root.SetParent(canvas.rootCanvas.transform, false);
            Stretch(_root);
            gameObject.layer = 5;
            // 独立排序，同时保持在场景小花过渡（32000）之下。
            var overlay = gameObject.AddComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = 25000;
            gameObject.AddComponent<GraphicRaycaster>();
            _backdrop = CreateImage("InputBlocker", _root);
            Stretch(_backdrop.rectTransform);
            _backdrop.raycastTarget = true;
            _stage = CreateRect("DesignSpace", _root, new Vector2(1080, 1920));

            // 用不透明纸色区域遮住原页面的标题、余额、价格和导航，避免两套文案叠在一起。
            CreatePaperPanel("Header", new Vector2(1080, 270), new Vector2(0, 825));
            CreatePaperPanel("Footer", new Vector2(1080, 700), new Vector2(0, -610));
            _title = CreateText("Title", _stage, font, 56, new Vector2(700, 120), new Vector2(-100, 805));
            _hint = CreateText("Hint", _stage, font, 40, new Vector2(900, 150), new Vector2(0, -435));
            _title.fontStyle = FontStyles.Bold;
            _dew = CreateImage("FallingStarDew", _stage);
            _dew.rectTransform.sizeDelta = new Vector2(108, 108);
            for (int i = 0; i < _ripples.Length; i++)
            {
                _ripples[i] = CreateImage("Ripple " + i, _stage);
                _ripples[i].rectTransform.sizeDelta = new Vector2(260, 130);
            }
            _hero = CreateImage("EmergingSpirit", _stage);
            _hero.rectTransform.sizeDelta = new Vector2(360, 400);
            for (int i = 0; i < _sparkles.Length; i++)
            {
                _sparkles[i] = CreateImage("Sparkle " + i, _stage);
                _sparkles[i].rectTransform.sizeDelta = new Vector2(56, 56);
            }
            for (int i = 0; i < MaxResults; i++)
            {
                _cardFrames[i] = CreateImage("Result " + (i + 1), _stage);
                _cards[i] = _cardFrames[i].rectTransform;
                _portraits[i] = CreateImage("Portrait", _cards[i]);
                _names[i] = CreateText("Name", _cards[i], font, 30, new Vector2(210, 60), new Vector2(0, -100));
            }
            _skip = CreateButton("Skip", font, new Vector2(190, 92), new Vector2(385, 805), "跳过", Skip);
            _continue = CreateButton("Continue", font, new Vector2(420, 116), new Vector2(0, -650), "继续", close);
            Hide();
        }

        public void ShowLoading()
        {
            ResetPresentation();
            gameObject.SetActive(true);
            Canvas.ForceUpdateCanvases();
            // 在窄屏/平板上都保留安全留白，不依赖物理屏幕像素。
            _stage.localScale = Vector3.one * Mathf.Min(_root.rect.width / 1080f, _root.rect.height / 1920f);
            _backdrop.color = new Color(.08f, .20f, .18f, .40f);
            _title.rectTransform.anchoredPosition = new Vector2(-100, 805);
            _title.text = "召唤准备中";
            _hint.text = "";
        }

        public void Play(SproutSummonResult[] results, SproutSummonAssets assets,
            Image illustration, Vector2 poolPosition, float volume, bool preview, string businessSummary = null)
        {
            _results = results;
            _preview = preview;
            _businessSummary = businessSummary;
            _illustration = illustration;
            if (_illustration != null) _illustrationColor = _illustration.color;
            _dew.sprite = assets.StarDew;
            _hero.sprite = results[0].Sprite;
            foreach (var ripple in _ripples) ripple.sprite = assets.Ripple;
            foreach (var sparkle in _sparkles) sparkle.sprite = assets.Sparkle;
            _skip.image.sprite = _continue.image.sprite = assets.ButtonFrame;
            _soundVolume = Mathf.Clamp01(volume);
            _title.text = preview ? "召唤演出预览" : "星露的礼物";
            _hint.text = "星露落入叶碗中…";
            _skip.gameObject.SetActive(true);
            Vector2 pool = GetPoolPosition(illustration, poolPosition);
            PrepareCards(assets.CardFrame);

            _dew.rectTransform.anchoredPosition = pool + new Vector2(0, 340);
            _dew.rectTransform.localScale = Vector3.one;
            _sequence = DOTween.Sequence().SetUpdate(true);
            _sequence.Insert(0, Move(_dew.rectTransform, pool, ContactTime).SetEase(Ease.InQuad));
            _sequence.Insert(0, _dew.rectTransform.DOScale(new Vector3(.82f, 1.18f, 1), ContactTime));
            _sequence.Insert(ContactTime, _dew.rectTransform.DOScale(new Vector3(1.35f, .55f, 1), .10f));
            _sequence.Insert(ContactTime + .08f, Fade(_dew, 0, .12f));
            _sequence.InsertCallback(ContactTime, () =>
            {
                _hint.text = "水面泛起了泡泡…";
                _bubbles = AudioManager.Instance.PlaySfx(AudioId.SummonBubbles, _soundVolume);
            });
            if (_illustration != null)
                _sequence.Insert(0, Fade(_illustration, _illustrationColor.a * .75f, .22f));

            for (int i = 0; i < _ripples.Length; i++)
            {
                Image ripple = _ripples[i];
                ripple.rectTransform.anchoredPosition = pool;
                ripple.rectTransform.localScale = Vector3.one * .35f;
                ripple.color = new Color(results[0].Accent.r, results[0].Accent.g, results[0].Accent.b, 0);
                float start = ContactTime + i * .14f;
                _sequence.Insert(start, Fade(ripple, .85f, .05f));
                _sequence.Insert(start + .05f, Fade(ripple, 0, .60f));
                _sequence.Insert(start, ripple.rectTransform.DOScale(1.8f, .65f).SetEase(Ease.OutQuad));
            }

            _hero.rectTransform.anchoredPosition = pool;
            _hero.rectTransform.localScale = Vector3.one * .18f;
            _hero.rectTransform.localEulerAngles = new Vector3(0, 0, -5);
            _sequence.InsertCallback(PopTime, () =>
            {
                _hint.text = "精灵来啦！";
                _pop = AudioManager.Instance.PlaySfx(AudioId.SummonPop, _soundVolume);
            });
            _sequence.Insert(PopTime, Fade(_hero, 1, .08f));
            _sequence.Insert(PopTime, Move(_hero.rectTransform, new Vector2(0, 180), .62f).SetEase(Ease.OutCubic));
            _sequence.Insert(PopTime, _hero.rectTransform.DOScale(new Vector3(.96f, 1.15f, 1), .24f).SetEase(Ease.OutBack));
            _sequence.Insert(PopTime + .24f, _hero.rectTransform.DOScale(new Vector3(1.08f, .94f, 1), .16f));
            _sequence.Insert(PopTime + .40f, _hero.rectTransform.DOScale(1, .22f).SetEase(Ease.OutSine));
            _sequence.Insert(PopTime, _hero.rectTransform.DOLocalRotate(new Vector3(0, 0, 3), .30f));
            _sequence.Insert(PopTime + .30f, _hero.rectTransform.DOLocalRotate(Vector3.zero, .30f));
            AddSparkles(results[0].Accent);

            _sequence.Insert(RevealTime, Fade(_hero, 0, .12f));
            _sequence.Insert(RevealTime, Fade(_backdrop, .90f, .18f));
            for (int i = 0; i < results.Length; i++)
                _sequence.Insert(RevealTime + i * .04f, _cards[i].DOScale(1, .22f).SetEase(Ease.OutBack));
            _sequence.OnComplete(ShowFinalResults);
        }

        public void Skip()
        {
            if (_results == null) return;
            _sequence?.Kill();
            _sequence = null;
            StopSounds(); // 不补播被跳过的音效，也不重新抽取结果。
            ShowFinalResults();
        }

        public void ShowError()
        {
            _title.rectTransform.anchoredPosition = new Vector2(0, 805);
            _title.text = "素材加载失败";
            _hint.text = "请检查 YooAsset 资源配置";
            _skip.gameObject.SetActive(false);
            _continue.gameObject.SetActive(true);
        }

        public void Hide()
        {
            ResetPresentation();
            gameObject.SetActive(false);
        }

        private void PrepareCards(Sprite frame)
        {
            bool single = _results.Length == 1;
            for (int i = 0; i < _results.Length; i++)
            {
                _cardFrames[i].sprite = frame;
                _cardFrames[i].preserveAspect = false;
                _cardFrames[i].color = Color.white;
                _cards[i].sizeDelta = single ? new Vector2(600, 650) : TenCardSize;
                // 十连横向五列、共两行；两行整体居中，先上排再下排。
                _cards[i].anchoredPosition = single ? new Vector2(0, 110) :
                    new Vector2((i % ResultColumns - (ResultColumns - 1) * .5f) * CardColumnStep,
                        110f + (.5f - i / ResultColumns) * CardRowStep);
                _portraits[i].sprite = _results[i].Sprite;
                _portraits[i].rectTransform.anchoredPosition = single ? new Vector2(0, 70) : new Vector2(0, 36);
                _portraits[i].rectTransform.sizeDelta = single ? new Vector2(360, 400) : new Vector2(112, 116);
                _names[i].rectTransform.anchoredPosition = single ? new Vector2(0, -200) : new Vector2(0, -65);
                _names[i].rectTransform.sizeDelta = single ? new Vector2(510, 130) : new Vector2(166, 90);
                _names[i].fontSize = single ? 42 : 32;
                _names[i].enableWordWrapping = false;
                _names[i].overflowMode = TextOverflowModes.Ellipsis;
                _names[i].richText = false;
                _names[i].text = string.IsNullOrEmpty(_results[i].Name) ? "小精灵" : _results[i].Name;
                _names[i].color = Ink;
                _cards[i].localScale = Vector3.zero;
            }
        }

        private void AddSparkles(Color accent)
        {
            for (int i = 0; i < _sparkles.Length; i++)
            {
                Image sparkle = _sparkles[i];
                float angle = (30 + i * 72) * Mathf.Deg2Rad;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                sparkle.rectTransform.anchoredPosition = new Vector2(0, 180) + direction * 120;
                sparkle.rectTransform.localScale = Vector3.zero;
                sparkle.rectTransform.localEulerAngles = new Vector3(0, 0, i * 17);
                sparkle.color = Color.Lerp(new Color(1, .89f, .38f), accent, .20f);
                float start = 1.06f + i * .035f;
                _sequence.Insert(start, sparkle.rectTransform.DOScale(1, .16f).SetEase(Ease.OutBack));
                _sequence.Insert(start, Move(sparkle.rectTransform, new Vector2(0, 180) + direction * 245, .55f).SetEase(Ease.OutQuad));
                _sequence.Insert(start + .18f, Fade(sparkle, 0, .36f));
            }
        }

        private void ShowFinalResults()
        {
            _sequence = null;
            _dew.rectTransform.localScale = _hero.rectTransform.localScale = Vector3.zero;
            foreach (var ripple in _ripples) ripple.rectTransform.localScale = Vector3.zero;
            foreach (var sparkle in _sparkles) sparkle.rectTransform.localScale = Vector3.zero;
            for (int i = 0; i < _results.Length; i++) _cards[i].localScale = Vector3.one;
            _backdrop.color = new Color(.08f, .20f, .18f, .90f);
            _skip.gameObject.SetActive(false);
            _continue.gameObject.SetActive(true);
            _title.rectTransform.anchoredPosition = new Vector2(0, 805);
            _title.text = _preview ? "召唤演出预览" : "召唤完成";
            _hint.text = _preview ? "仅预览动画，不消耗星露" :
                (string.IsNullOrEmpty(_businessSummary) ? "精灵来啦！" : _businessSummary);
        }

        private void ResetPresentation()
        {
            _sequence?.Kill();
            _sequence = null;
            StopSounds();
            if (_illustration != null) _illustration.color = _illustrationColor;
            _illustration = null;
            _results = null;
            _businessSummary = null;
            if (_dew == null) return;
            _dew.rectTransform.localScale = _hero.rectTransform.localScale = Vector3.zero;
            _dew.color = Color.white;
            _hero.color = new Color(1, 1, 1, 0);
            foreach (var ripple in _ripples) ripple.rectTransform.localScale = Vector3.zero;
            foreach (var sparkle in _sparkles) sparkle.rectTransform.localScale = Vector3.zero;
            foreach (var card in _cards) card.localScale = Vector3.zero;
            _skip.gameObject.SetActive(false);
            _continue.gameObject.SetActive(false);
        }

        private Vector2 GetPoolPosition(Image illustration, Vector2 normalized)
        {
            if (illustration == null) return new Vector2(0, -180);
            RectTransform rect = illustration.rectTransform;
            // Image preserveAspect 可能产生上下/左右留白，必须按实际绘制区域找碗口。
            Rect drawing = rect.rect;
            if (illustration.preserveAspect && illustration.sprite != null)
            {
                float aspect = illustration.sprite.rect.width / illustration.sprite.rect.height;
                if (drawing.width / drawing.height > aspect)
                {
                    float width = drawing.height * aspect;
                    drawing.x += (drawing.width - width) * rect.pivot.x;
                    drawing.width = width;
                }
                else
                {
                    float height = drawing.width / aspect;
                    drawing.y += (drawing.height - height) * rect.pivot.y;
                    drawing.height = height;
                }
            }
            Vector2 point = drawing.min + Vector2.Scale(normalized, drawing.size);
            return _stage.InverseTransformPoint(rect.TransformPoint(point));
        }

        private void StopSounds()
        {
            if (!AudioManager.HasInstance) return;
            AudioManager.Instance.Stop(_bubbles);
            AudioManager.Instance.Stop(_pop);
            _bubbles = _pop = default;
        }
        private void OnDisable()
        {
            _sequence?.Kill();
            _sequence = null;
            StopSounds();
        }

        private static Tween Fade(Graphic image, float alpha, float duration) =>
            DOTween.To(() => image.color.a, value =>
            {
                Color color = image.color;
                color.a = value;
                image.color = color;
            }, alpha, duration);

        private static Tween Move(RectTransform rect, Vector2 position, float duration) =>
            DOTween.To(() => rect.anchoredPosition, value => rect.anchoredPosition = value, position, duration);

        private static RectTransform CreateRect(string name, Transform parent, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.gameObject.layer = 5;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = size;
            return rect;
        }

        private static Image CreateImage(string name, Transform parent)
        {
            var image = CreateRect(name, parent, Vector2.zero).gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            image.preserveAspect = true;
            return image;
        }

        private void CreatePaperPanel(string name, Vector2 size, Vector2 position)
        {
            Image panel = CreateImage(name, _stage);
            panel.preserveAspect = false;
            panel.rectTransform.sizeDelta = size;
            panel.rectTransform.anchoredPosition = position;
            panel.color = Paper;
        }

        private static TMP_Text CreateText(string name, Transform parent, TMP_FontAsset font,
            int fontSize, Vector2 size, Vector2 position)
        {
            var label = CreateRect(name, parent, size).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = font;
            label.fontSize = fontSize;
            label.color = Ink;
            label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false;
            label.rectTransform.anchoredPosition = position;
            return label;
        }

        private Button CreateButton(string name, TMP_FontAsset font, Vector2 size, Vector2 position,
            string text, Action action)
        {
            Image image = CreateImage(name, _stage);
            image.rectTransform.sizeDelta = size;
            image.rectTransform.anchoredPosition = position;
            image.raycastTarget = true;
            var button = image.gameObject.AddComponent<Button>();
            image.gameObject.AddComponent<AudioButtonFeedback>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(() => action());
            TMP_Text label = CreateText("Label", image.transform, font, 44, size, Vector2.zero);
            label.text = text;
            label.color = Ink;
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy() { _sequence?.Kill(); StopSounds(); }
    }
}
