using System;

namespace Sproutshot.UI
{
    /// <summary>
    /// 为 UIView 声明 YooAsset 地址，使调用方可以使用 Open&lt;T&gt;()。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIWindowAttribute : Attribute
    {
        public string Address { get; }

        public UIWindowAttribute(string address)
        {
            if (string.IsNullOrWhiteSpace(address))
                throw new ArgumentException("UI 资源地址不能为空。", nameof(address));

            Address = address;
        }
    }
}
