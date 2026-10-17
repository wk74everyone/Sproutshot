using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Sproutshot.Utils;
using UnityEngine;
using UnityEngine.UI;
using YooAsset;

namespace Sproutshot.UI
{
    /// <summary>
    /// UI 的创建、层级和销毁入口。
    /// 仅创建低频与高频两个 Canvas；Popup/Overlay 与动态 UI 共享高频 Canvas。
    /// </summary>
    public sealed class UIManager : SingletonMono<UIManager>
    {
        private sealed class UIEntry
        {
            public UIView View;
            public AssetHandle Handle;
        }

        private sealed class PendingLoad
        {
            public bool Cancelled;
            public AssetHandle Handle;
        }

        [Header("Canvas Scaling")]
        [SerializeField] private Vector2 _referenceResolution = new Vector2(1080f, 1920f);
        [SerializeField, Range(0f, 1f)] private float _matchWidthOrHeight = 0.5f;
        [SerializeField] private int _lowFrequencySortingOrder = 100;
        [SerializeField] private int _highFrequencySortingOrder = 200;

        private readonly Dictionary<Type, UIEntry> _entries = new Dictionary<Type, UIEntry>();
        private readonly Dictionary<Type, PendingLoad> _pendingLoads = new Dictionary<Type, PendingLoad>();
        private readonly Dictionary<UILayer, RectTransform> _layerRoots = new Dictionary<UILayer, RectTransform>();
        private readonly Dictionary<Type, string> _addressCache = new Dictionary<Type, string>();

        private bool _initialized;
        private int _uiLayer;

        public int OpenCount => _entries.Count;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            EnsureInitialized();
        }

        /// <summary>预先创建 Canvas 根节点；不调用也会在首次 Open 时自动执行。</summary>
        public void EnsureInitialized()
        {
            if (_initialized) return;

            _uiLayer = LayerMask.NameToLayer("UI");
            if (_uiLayer < 0)
                _uiLayer = 5;

            RectTransform lowRoot = CreateCanvasRoot("LowFrequencyCanvas", _lowFrequencySortingOrder);
            RectTransform highRoot = CreateCanvasRoot("HighFrequencyCanvas", _highFrequencySortingOrder);

            _layerRoots.Add(UILayer.Static, CreateLayerRoot("Static", lowRoot));
            _layerRoots.Add(UILayer.Dynamic, CreateLayerRoot("Dynamic", highRoot));
            _layerRoots.Add(UILayer.Popup, CreateLayerRoot("Popup", highRoot));
            _layerRoots.Add(UILayer.Overlay, CreateLayerRoot("Overlay", highRoot));

            _initialized = true;
        }

        /// <summary>使用 UIView 上的 UIWindowAttribute 同步打开 UI。</summary>
        public T Open<T>(object args = null) where T : UIView
        {
            return Open<T>(GetAddress(typeof(T)), args);
        }

        /// <summary>从指定 YooAsset 地址同步加载并打开 UI。</summary>
        public T Open<T>(string address, object args = null) where T : UIView
        {
            EnsureInitialized();
            Type type = typeof(T);

            if (TryGet(type, out T opened))
            {
                BringToFront(opened);
                OpenExisting(opened, args);
                return opened;
            }

            if (_pendingLoads.TryGetValue(type, out PendingLoad pending))
                pending.Cancelled = true;

            if (!ValidateAddress(address, type))
                return null;

            AssetHandle handle = YooAssetManager.Instance.LoadAssetHandleSync<GameObject>(address);
            if (handle == null)
                return null;

            return CreateLoadedView<T>(address, handle, args);
        }

        /// <summary>使用 UIView 上的 UIWindowAttribute 异步打开 UI。</summary>
        public Coroutine OpenAsync<T>(Action<T> onOpened = null, object args = null, uint priority = 0)
            where T : UIView
        {
            return OpenAsync(GetAddress(typeof(T)), onOpened, args, priority);
        }

        /// <summary>
        /// 异步加载并打开 UI。同类型加载中再次调用会等待首个请求并复用其结果。
        /// </summary>
        public Coroutine OpenAsync<T>(string address, Action<T> onOpened = null, object args = null, uint priority = 0)
            where T : UIView
        {
            EnsureInitialized();
            return StartCoroutine(OpenAsyncRoutine(address, onOpened, args, priority));
        }

        public bool TryGet<T>(out T view) where T : UIView
        {
            return TryGet(typeof(T), out view);
        }

        /// <summary>关闭并销毁指定类型的 UI；若尚在异步加载则取消创建。</summary>
        public bool Close<T>() where T : UIView
        {
            return Close(typeof(T));
        }

        public bool Close(UIView view)
        {
            if (view == null) return false;

            Type type = view.GetType();
            return _entries.TryGetValue(type, out UIEntry entry) && entry.View == view && Close(type);
        }

        /// <summary>关闭并销毁某个逻辑层级的全部 UI。</summary>
        public int CloseLayer(UILayer layer)
        {
            var types = new List<Type>();
            foreach (KeyValuePair<Type, UIEntry> pair in _entries)
            {
                if (pair.Value.View != null && pair.Value.View.Layer == layer)
                    types.Add(pair.Key);
            }

            for (int i = 0; i < types.Count; i++)
                Close(types[i]);

            return types.Count;
        }

        /// <summary>取消未完成的加载，并关闭、销毁全部 UI。</summary>
        public void CloseAll()
        {
            foreach (PendingLoad pending in _pendingLoads.Values)
                pending.Cancelled = true;

            var types = new List<Type>(_entries.Keys);
            for (int i = 0; i < types.Count; i++)
                Close(types[i]);
        }

        private IEnumerator OpenAsyncRoutine<T>(string address, Action<T> onOpened, object args, uint priority)
            where T : UIView
        {
            Type type = typeof(T);

            if (TryGet(type, out T opened))
            {
                BringToFront(opened);
                OpenExisting(opened, args);
                onOpened?.Invoke(opened);
                yield break;
            }

            if (_pendingLoads.TryGetValue(type, out PendingLoad activePending) && !activePending.Cancelled)
            {
                yield return WaitForPendingLoad(type, activePending);
                TryGet(type, out T pendingResult);
                onOpened?.Invoke(pendingResult);
                yield break;
            }

            if (!ValidateAddress(address, type))
            {
                onOpened?.Invoke(null);
                yield break;
            }

            var pending = new PendingLoad();
            _pendingLoads[type] = pending;

            AssetHandle handle = YooAssetManager.Instance.LoadAssetHandleAsync<GameObject>(address, priority);
            pending.Handle = handle;
            if (handle == null)
            {
                RemovePending(type, pending);
                onOpened?.Invoke(null);
                yield break;
            }

            yield return handle;

            if (pending.Cancelled || !IsCurrentPending(type, pending))
            {
                YooAssetManager.Instance.Release(handle);
                RemovePending(type, pending);
                onOpened?.Invoke(null);
                yield break;
            }

            RemovePending(type, pending);

            if (handle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[UIManager] 异步加载 {type.Name} 失败：{address}，错误：{handle.Error}", this);
                YooAssetManager.Instance.Release(handle);
                onOpened?.Invoke(null);
                yield break;
            }

            T result = CreateLoadedView<T>(address, handle, args);
            onOpened?.Invoke(result);
        }

        private IEnumerator WaitForPendingLoad(Type type, PendingLoad pending)
        {
            while (_pendingLoads.TryGetValue(type, out PendingLoad current) && ReferenceEquals(current, pending))
                yield return null;
        }

        private T CreateLoadedView<T>(string address, AssetHandle handle, object args) where T : UIView
        {
            Type type = typeof(T);
            GameObject instance = null;
            T view = null;

            try
            {
                T prefabView = handle.AssetObject is GameObject prefab ? prefab.GetComponent<T>() : null;
                if (prefabView == null)
                {
                    Debug.LogError($"[UIManager] 预制体 {address} 的根节点缺少 {type.Name} 组件。", this);
                    YooAssetManager.Instance.Release(handle);
                    return null;
                }

                RectTransform parent = _layerRoots[prefabView.Layer];
                instance = handle.InstantiateSync(new InstantiateOptions(false, parent, false));
                if (instance == null)
                    throw new InvalidOperationException($"无法实例化 UI 预制体：{address}");

                view = instance.GetComponent<T>();
                if (view == null)
                    throw new InvalidOperationException($"实例根节点缺少 {type.Name} 组件：{address}");

                SetLayerRecursively(instance.transform, _uiLayer);
                WarnAboutNestedCanvases(view, address);

                var entry = new UIEntry
                {
                    View = view,
                    Handle = handle
                };
                _entries.Add(type, entry);

                view.InitializeView();
                BringToFront(view);
                view.OpenView(args);
                return view;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                _entries.Remove(type);

                InvokeCloseAndRelease(view);
                if (instance != null)
                {
                    instance.SetActive(false);
                    Destroy(instance);
                }

                YooAssetManager.Instance.Release(handle);
                return null;
            }
        }

        private bool Close(Type type)
        {
            bool cancelledPending = false;
            if (_pendingLoads.TryGetValue(type, out PendingLoad pending))
            {
                pending.Cancelled = true;
                cancelledPending = true;
            }

            if (!_entries.TryGetValue(type, out UIEntry entry))
                return cancelledPending;

            _entries.Remove(type);
            DestroyEntry(entry);
            return true;
        }

        private void DestroyEntry(UIEntry entry)
        {
            if (entry == null) return;

            UIView view = entry.View;
            if (view != null)
            {
                InvokeCloseAndRelease(view);
                view.gameObject.SetActive(false);
                Destroy(view.gameObject);
            }

            YooAssetManager.Instance.Release(entry.Handle);
        }

        private static void InvokeCloseAndRelease(UIView view)
        {
            if (view == null) return;

            try
            {
                view.CloseView();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, view);
            }

            try
            {
                view.ReleaseView();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, view);
            }
        }

        private void OpenExisting(UIView view, object args)
        {
            try
            {
                view.OpenView(args);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, view);
            }
        }

        private bool TryGet<T>(Type type, out T view) where T : UIView
        {
            if (_entries.TryGetValue(type, out UIEntry entry))
            {
                if (entry.View != null)
                {
                    view = entry.View as T;
                    return view != null;
                }

                _entries.Remove(type);
                YooAssetManager.Instance.Release(entry.Handle);
            }

            view = null;
            return false;
        }

        private void BringToFront(UIView view)
        {
            if (view != null)
                view.transform.SetAsLastSibling();
        }

        private RectTransform CreateCanvasRoot(string name, int sortingOrder)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.layer = _uiLayer;
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = false;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = _referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = _matchWidthOrHeight;

            return canvasObject.GetComponent<RectTransform>();
        }

        private RectTransform CreateLayerRoot(string name, Transform parent)
        {
            var layerObject = new GameObject(name, typeof(RectTransform));
            layerObject.layer = _uiLayer;

            RectTransform rect = layerObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursively(root.GetChild(i), layer);
        }

        private void WarnAboutNestedCanvases(UIView view, string address)
        {
            Canvas[] nestedCanvases = view.GetComponentsInChildren<Canvas>(true);
            if (nestedCanvases.Length == 0) return;

            Debug.LogWarning(
                $"[UIManager] {address} 内包含 {nestedCanvases.Length} 个额外 Canvas。" +
                "同更新频率的 UI 应共享框架 Canvas，仅在必须隔离重建或特殊排序时保留嵌套 Canvas。",
                view);
        }

        private string GetAddress(Type type)
        {
            if (_addressCache.TryGetValue(type, out string cached))
                return cached;

            var attribute = type.GetCustomAttribute<UIWindowAttribute>();
            string address = attribute?.Address;
            _addressCache[type] = address;
            return address;
        }

        private bool ValidateAddress(string address, Type type)
        {
            if (!string.IsNullOrWhiteSpace(address)) return true;

            Debug.LogError(
                $"[UIManager] {type.Name} 没有资源地址。请添加 [UIWindow(\"address\")] 或调用 Open<T>(address)。",
                this);
            return false;
        }

        private bool IsCurrentPending(Type type, PendingLoad pending)
        {
            return _pendingLoads.TryGetValue(type, out PendingLoad current) && ReferenceEquals(current, pending);
        }

        private void RemovePending(Type type, PendingLoad pending)
        {
            if (IsCurrentPending(type, pending))
                _pendingLoads.Remove(type);
        }

        protected override void OnDestroy()
        {
            foreach (PendingLoad pending in _pendingLoads.Values)
            {
                pending.Cancelled = true;
                if (pending.Handle != null && pending.Handle.IsValid)
                    pending.Handle.Release();
            }
            _pendingLoads.Clear();

            foreach (UIEntry entry in _entries.Values)
            {
                if (entry.View != null)
                    InvokeCloseAndRelease(entry.View);

                YooAssetManager.Instance.Release(entry.Handle);
            }
            _entries.Clear();

            base.OnDestroy();
        }
    }
}
