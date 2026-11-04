using System;
using System.Collections;
using DG.Tweening;
using Sproutshot.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace Sproutshot.UI
{
    /// <summary>通用小花场景过渡，不创建对局、不等待旧敌人或战斗 HUD。</summary>
    public sealed class FlowerSceneTransition : SingletonMono<FlowerSceneTransition>
    {
        private const float AnimationDuration = .45f;
        private static readonly string[] FlowerAddresses =
            { "transition_flower_cream", "transition_flower_pink" };
        private readonly Sprite[] _sprites = new Sprite[2];
        private readonly AssetHandle[] _handles = new AssetHandle[2];
        private FlowerTransitionView _view;
        private bool _loadingSprites;
        public bool IsTransitioning { get; private set; }
        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            _view = gameObject.AddComponent<FlowerTransitionView>();
            _view.Initialize();
        }
        public void Prewarm()
        {
            if (!_loadingSprites && (_sprites[0] == null || _sprites[1] == null))
                StartCoroutine(LoadSprites());
        }
        public bool EnterScene(string sceneName, bool reloadCurrent = false, Action onLoaded = null)
        {
            if (IsTransitioning || string.IsNullOrWhiteSpace(sceneName) ||
                (!reloadCurrent && SceneManager.GetActiveScene().name == sceneName)) return false;
            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[FlowerSceneTransition] 场景未加入构建列表：{sceneName}");
                return false;
            }
            IsTransitioning = true;
            StartCoroutine(EnterSceneRoutine(sceneName, onLoaded));
            return true;
        }
        private IEnumerator EnterSceneRoutine(string sceneName, Action onLoaded)
        {
            try
            {
                _view.BlockInput();
                yield return LoadSprites();
                if (_sprites[0] == null || _sprites[1] == null)
                {
                    Debug.LogError("[FlowerSceneTransition] 小花素材未加载，保留当前场景。");
                    yield break;
                }
                yield return _view.Cover(_sprites, AnimationDuration).WaitForCompletion();
                yield return null;
                AsyncOperation loading = LoadScene(sceneName);
                if (loading != null)
                {
                    yield return loading;
                    yield return null; // 新场景完成 Start 和布局；新对局框架自行管理其初始化。
                    onLoaded?.Invoke();
                    Canvas.ForceUpdateCanvases();
                }
                yield return _view.Reveal(AnimationDuration).WaitForCompletion();
            }
            finally
            {
                _view.Hide();
                IsTransitioning = false;
            }
        }
        private static AsyncOperation LoadScene(string sceneName)
        {
            try { return SceneManager.LoadSceneAsync(sceneName); }
            catch (Exception error)
            {
                Debug.LogError($"[FlowerSceneTransition] 加载 {sceneName} 失败：{error.Message}");
                return null;
            }
        }
        private IEnumerator LoadSprites()
        {
            if (_loadingSprites)
            {
                while (_loadingSprites) yield return null;
                yield break;
            }

            _loadingSprites = true;
            try
            {
                if (!YooAssetManager.Instance.IsInitialized)
                    yield return YooAssetManager.Instance.InitDefaultPackage();
                if (!YooAssetManager.Instance.IsInitialized) yield break;

                for (int i = 0; i < _sprites.Length; i++)
                {
                    if (_sprites[i] != null) continue;
                    _handles[i] = YooAssetManager.Instance.LoadAssetHandleAsync<Sprite>(FlowerAddresses[i]);
                    if (_handles[i] == null) continue;
                    yield return _handles[i];
                    if (_handles[i].Status == EOperationStatus.Succeeded)
                        _sprites[i] = _handles[i].AssetObject as Sprite;
                    if (_sprites[i] == null)
                    {
                        Debug.LogError($"[FlowerSceneTransition] 无法加载 Sprite：{FlowerAddresses[i]}");
                        YooAssetManager.Instance.Release(_handles[i]);
                        _handles[i] = null;
                    }
                }
            }
            finally { _loadingSprites = false; }
        }
        protected override void OnDestroy()
        {
            for (int i = 0; i < _handles.Length; i++)
                YooAssetManager.Instance.Release(_handles[i]);
            base.OnDestroy();
        }
    }
}
