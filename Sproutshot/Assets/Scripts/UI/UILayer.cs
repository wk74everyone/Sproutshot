namespace Sproutshot.UI
{
    /// <summary>
    /// UI 的逻辑层级。同一更新频率的层级共享 Canvas，避免无意义地拆分 Canvas。
    /// </summary>
    public enum UILayer
    {
        /// <summary>几乎不变化的背景、装饰和静态页面。</summary>
        Static = 0,

        /// <summary>频繁变化的 HUD、计数器和动态页面。</summary>
        Dynamic = 100,

        /// <summary>弹窗。与 Dynamic 同属高频 Canvas，只通过兄弟节点保证层级。</summary>
        Popup = 200,

        /// <summary>Toast、Loading 等始终位于最上方的内容。</summary>
        Overlay = 300
    }

    public enum UIRenderFrequency
    {
        Low,
        High
    }

    public static class UILayerExtensions
    {
        public static UIRenderFrequency GetRenderFrequency(this UILayer layer)
        {
            return layer == UILayer.Static ? UIRenderFrequency.Low : UIRenderFrequency.High;
        }
    }
}
