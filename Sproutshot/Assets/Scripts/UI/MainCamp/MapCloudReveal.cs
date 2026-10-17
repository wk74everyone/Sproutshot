using System.Collections;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.UI
{
    // 挂在 CloudCover 上；外层用于拼接地图边缘的装饰云不受影响。
    [RequireComponent(typeof(Animator))]
    public sealed class MapCloudReveal : MonoBehaviour
    {
        [SerializeField, Range(2, StageProgressData.MapCount)] private int _mapNumber = 2;
        [SerializeField] private Animator _animator;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private CanvasGroup _mapVisibility;
        [SerializeField] private string _revealState = "Base Layer.CloudCover";
        [SerializeField] private string _idleState = "Base Layer.Empty";

        private RectTransform[] _clouds;
        private Vector2[] _initialPositions;
        private readonly Vector3[] _corners = new Vector3[4];
        private readonly WaitForSecondsRealtime _visibilityPoll = new WaitForSecondsRealtime(0.1f);
        private Coroutine _revealRoutine;

        private void Awake()
        {
            if (_animator == null)
                _animator = GetComponent<Animator>();

            // 缓存动画会移动的子云，关闭途中恢复原位，下次打开才能完整重播。
            _clouds = new RectTransform[transform.childCount];
            _initialPositions = new Vector2[_clouds.Length];
            for (int i = 0; i < _clouds.Length; i++)
            {
                _clouds[i] = transform.GetChild(i) as RectTransform;
                if (_clouds[i] != null)
                    _initialPositions[i] = _clouds[i].anchoredPosition;
            }
        }

        private void OnEnable()
        {
            var progress = StageProgressSave.Load();
            if (progress.HasRevealedMap(_mapNumber))
            {
                gameObject.SetActive(false);
                return;
            }

            RestorePositions();
            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                _animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                int idleHash = Animator.StringToHash(_idleState);
                if (_animator.HasState(0, idleHash))
                {
                    _animator.Play(idleHash, 0, 0f);
                    _animator.Update(0f);
                }
            }

            if (progress.IsMapUnlocked(_mapNumber))
                _revealRoutine = StartCoroutine(Reveal());
        }

        private void OnDisable()
        {
            if (_revealRoutine != null)
            {
                StopCoroutine(_revealRoutine);
                _revealRoutine = null;
            }
            RestorePositions();
        }

        private IEnumerator Reveal()
        {
            // 等面板淡入完成、云层进入视口，避免玩家还没看到就已播完。
            yield return null;
            while (!IsVisible())
                yield return _visibilityPoll;

            int stateHash = Animator.StringToHash(_revealState);
            if (_animator == null || !_animator.isActiveAndEnabled ||
                _animator.runtimeAnimatorController == null || !_animator.HasState(0, stateHash))
            {
                Debug.LogWarning($"[MapCloudReveal] 地图 {_mapNumber} 的散云动画不可用，直接显示已解锁地图。", this);
                _revealRoutine = null;
                gameObject.SetActive(false);
                yield break;
            }

            _animator.Play(stateHash, 0, 0f);
            _animator.Update(0f);
            while (true)
            {
                var state = _animator.GetCurrentAnimatorStateInfo(0);
                if (!_animator.isActiveAndEnabled || state.fullPathHash != stateHash || state.loop ||
                    _animator.speed <= 0f || state.speed <= 0f || state.speedMultiplier <= 0f)
                {
                    // 不把异常/循环动画记作已完成；修复后重新进入仍可播放。
                    Debug.LogWarning($"[MapCloudReveal] 地图 {_mapNumber} 的散云状态必须为正常播放的非循环动画。", this);
                    break;
                }

                if (state.normalizedTime >= 1f && !_animator.IsInTransition(0))
                {
                    StageProgressSave.RecordMapRevealed(_mapNumber);
                    break;
                }
                yield return null;
            }

            _revealRoutine = null;
            gameObject.SetActive(false);
        }

        private bool IsVisible()
        {
            if (_mapVisibility != null && (_mapVisibility.alpha < 0.99f || !_mapVisibility.interactable))
                return false;
            if (_viewport == null)
                return true;

            // 云层根节点位于地图接缝处，真正遮挡地图的是向上延伸的子云。
            // 不临时获取组件/分配数组，只检测缓存的子云与视口是否相交。
            Rect viewportRect = _viewport.rect;
            foreach (var cloud in _clouds)
            {
                if (cloud == null || !cloud.gameObject.activeInHierarchy)
                    continue;
                cloud.GetWorldCorners(_corners);
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                foreach (var corner in _corners)
                {
                    Vector2 point = _viewport.InverseTransformPoint(corner);
                    min = Vector2.Min(min, point);
                    max = Vector2.Max(max, point);
                }
                if (Rect.MinMaxRect(min.x, min.y, max.x, max.y).Overlaps(viewportRect))
                    return true;
            }
            return false;
        }

        private void RestorePositions()
        {
            if (_clouds == null)
                return;
            for (int i = 0; i < _clouds.Length; i++)
                if (_clouds[i] != null)
                    _clouds[i].anchoredPosition = _initialPositions[i];
        }
    }
}
