using DG.Tweening;
using UnityEngine;

namespace Sproutshot.Utils
{
    /// <summary>持有单个面板的静止位置和动画；关闭、反向切换都不会累积位移。</summary>
    [DisallowMultipleComponent]
    public sealed class PanelVisibilityTransition : MonoBehaviour
    {
        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector3 _restPosition;
        private Sequence _sequence;
        private bool _initialized;
        private bool _visible;

        private Vector3 Position
        {
            get => _rect != null ? _rect.anchoredPosition3D : transform.localPosition;
            set
            {
                if (_rect != null) _rect.anchoredPosition3D = value;
                else transform.localPosition = value;
            }
        }

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _rect = transform as RectTransform;
            _group = GetComponent<CanvasGroup>();
            _restPosition = Position;
            _visible = gameObject.activeSelf && (_group == null || _group.alpha > .001f);
        }

        public void SetVisible(bool visible, float duration, Vector2 slideOffset)
        {
            Initialize();
            // 同一目标不重播，也不重新触发已在执行的关闭回调。
            if (_sequence != null && _visible == visible) return;
            StopTransition();
            _visible = visible;

            if (_group == null)
            {
                gameObject.SetActive(visible);
                return;
            }

            bool wasHidden = !gameObject.activeSelf || _group.alpha <= .001f;
            if (duration <= 0f || (!visible && wasHidden))
            {
                Finish();
                return;
            }

            _group.interactable = false;
            // 退出页立刻让出输入；进入页在动画期间拦截内容点击，底部导航仍可切换。
            _group.blocksRaycasts = visible;
            if (visible)
            {
                if (wasHidden)
                {
                    _group.alpha = 0f;
                    Position = _restPosition + (Vector3)slideOffset;
                }
                gameObject.SetActive(true);
            }

            if (!gameObject.activeInHierarchy)
            {
                Finish();
                return;
            }

            float moveDuration = visible ? duration : duration * .6f;
            float fadeDuration = Mathf.Min(moveDuration, duration * .65f);
            Vector3 destination = visible ? _restPosition : _restPosition - (Vector3)slideOffset * .4f;
            _sequence = DOTween.Sequence().SetUpdate(true)
                .SetLink(gameObject, LinkBehaviour.KillOnDisable);
            _sequence.Join(_group.DOFade(visible ? 1f : 0f, fadeDuration).SetEase(Ease.OutQuad));
            _sequence.Join(DOTween.To(() => Position, value => Position = value, destination, moveDuration)
                .SetEase(visible ? Ease.OutCubic : Ease.InQuad));
            _sequence.OnComplete(() =>
            {
                _sequence = null;
                Finish();
            });
        }

        private void Finish()
        {
            Position = _restPosition;
            if (_group != null)
            {
                _group.alpha = _visible ? 1f : 0f;
                _group.interactable = _visible;
                _group.blocksRaycasts = _visible;
            }
            gameObject.SetActive(_visible);
        }

        private void StopTransition()
        {
            _sequence?.Kill();
            _sequence = null;
        }

        private void OnDisable()
        {
            if (!_initialized) return;
            StopTransition();
            Position = _restPosition;
            if (_group == null) return;
            _group.alpha = _visible ? 1f : 0f;
            _group.interactable = _visible;
            _group.blocksRaycasts = _visible;
        }

        private void OnDestroy() => StopTransition();
    }
}
