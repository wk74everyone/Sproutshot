using UnityEngine;

namespace Sproutshot.UI
{
    /// <summary>
    /// 所有受 UIManager 管理的 UI 预制体基类。
    /// 基类不提供 Update；数据变化时由业务事件主动刷新，避免大量空 Update。
    /// </summary>
    [DisallowMultipleComponent]
    public abstract class UIView : MonoBehaviour
    {
        [SerializeField] private UILayer _layer = UILayer.Dynamic;

        public UILayer Layer => _layer;
        public bool IsOpen { get; private set; }
        public bool IsCreated { get; private set; }

        /// <summary>关闭并销毁当前 UI，同时释放资源句柄。</summary>
        public void CloseSelf()
        {
            if (UIManager.HasInstance)
                UIManager.Instance.Close(this);
        }

        internal void InitializeView()
        {
            if (IsCreated) return;

            IsCreated = true;
            OnCreated();
        }

        internal void OpenView(object args)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            IsOpen = true;
            OnOpen(args);
        }

        internal void CloseView()
        {
            if (!IsOpen) return;

            try
            {
                OnClose();
            }
            finally
            {
                IsOpen = false;
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
            }
        }

        internal void ReleaseView()
        {
            if (!IsCreated) return;

            IsCreated = false;
            OnRelease();
        }

        /// <summary>实例创建完成且仍为 inactive 时调用，只调用一次。</summary>
        protected virtual void OnCreated() { }

        /// <summary>UI 激活后调用；重复 Open 已打开的单例 UI 时也会调用。</summary>
        protected virtual void OnOpen(object args) { }

        /// <summary>UI 失活前调用。</summary>
        protected virtual void OnClose() { }

        /// <summary>GameObject 销毁、资源句柄释放前调用，只调用一次。</summary>
        protected virtual void OnRelease() { }
    }
}
