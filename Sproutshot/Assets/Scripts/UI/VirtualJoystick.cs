using Sproutshot.Gameplay;
using Sproutshot.MapGen;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sproutshot.UI
{
    /// <summary>固定摇杆；只绑定开始拖动的手指，不允许第二根手指抢走方向。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform _base;
        [SerializeField] private RectTransform _handle;
        [SerializeField] private CanvasGroup _visibility;
        [SerializeField] private bool _showInEditor;
        [SerializeField, Range(0, .5f)] private float _deadZone = .12f;
        private int _pointerId = int.MinValue;
        private PlayerClickToMove _player;
        public Vector2 Direction { get; private set; }
        public bool ShowInEditor { get => _showInEditor; set => _showInEditor = value; }

        private void Awake()
        {
            if (_base == null) _base = (RectTransform)transform;
            if (_handle == null) _handle = transform.Find("Handle") as RectTransform;
            if (_visibility == null) _visibility = GetComponent<CanvasGroup>();
        }

        private PlayerClickToMove CurrentPlayer()
        {
            var context = RunContext.Current;
            if (context?.PlayerEntity == null || !context.PlayerContext.IsInitialized ||
                context.PlayerContext.CurrentHp <= 0 || context.LevelContext.IsStopped) return null;
            return context.PlayerEntity.GetComponent<PlayerClickToMove>();
        }

        private void Update()
        {
            bool visible = Application.isEditor ? _showInEditor : Application.isMobilePlatform || Input.touchSupported;
            var player = CurrentPlayer();
            bool ready = visible && player != null && player.CanAcceptInput;
            _visibility.alpha = visible ? (ready ? .9f : .3f) : 0;
            _visibility.blocksRaycasts = _visibility.interactable = ready;
            if (!ready || (_player != null && _player != player)) Release();
        }

        public void OnPointerDown(PointerEventData evt)
        {
            if (_pointerId != int.MinValue || evt.button != PointerEventData.InputButton.Left ||
                _visibility == null || !_visibility.interactable) return;
            var player = CurrentPlayer();
            if (player == null || !player.CanAcceptInput) return;
            _player = player; _pointerId = evt.pointerId;
            OnDrag(evt);
        }

        public void OnDrag(PointerEventData evt)
        {
            if (_pointerId != evt.pointerId || _player == null) return;
            if (!_player.CanAcceptInput || _player != CurrentPlayer() ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(_base, evt.position, evt.pressEventCamera, out var local))
            { Release(); return; }
            if (float.IsNaN(local.x) || float.IsNaN(local.y) || float.IsInfinity(local.x) || float.IsInfinity(local.y)) { Release(); return; }
            float radius = Mathf.Max(1, (Mathf.Min(_base.rect.width, _base.rect.height) -
                (_handle != null ? Mathf.Max(_handle.rect.width, _handle.rect.height) : 0)) * .5f);
            Vector2 raw = Vector2.ClampMagnitude(local / radius, 1);
            if (_handle != null) _handle.anchoredPosition = raw * radius;
            float magnitude = raw.magnitude;
            Direction = magnitude <= _deadZone ? Vector2.zero : raw.normalized * ((magnitude - _deadZone) / (1 - _deadZone));
            _player.SetJoystickInput(Direction);
        }

        public void OnPointerUp(PointerEventData evt) { if (_pointerId == evt.pointerId) Release(); }
        private void Release()
        {
            if (_player != null) _player.SetJoystickInput(Vector2.zero);
            _player = null; _pointerId = int.MinValue; Direction = Vector2.zero;
            if (_handle != null) _handle.anchoredPosition = Vector2.zero;
        }
        private void OnDisable() => Release();
        private void OnApplicationPause(bool paused) { if (paused) Release(); }
        private void OnApplicationFocus(bool focused) { if (!focused) Release(); }
    }
}
