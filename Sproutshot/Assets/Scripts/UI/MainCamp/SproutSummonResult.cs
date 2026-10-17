using UnityEngine;

namespace Sproutshot.UI
{
    /// <summary>只描述演出内容。抽取概率、消耗与背包由抽卡业务负责。</summary>
    public readonly struct SproutSummonResult
    {
        public readonly Sprite Sprite;
        public readonly string Name;
        public readonly Color Accent;

        public SproutSummonResult(Sprite sprite, string name, Color accent)
        {
            Sprite = sprite;
            Name = name;
            Accent = accent;
        }
    }
}
