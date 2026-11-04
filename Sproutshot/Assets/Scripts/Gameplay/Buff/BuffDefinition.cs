using System;
using cfg;

namespace Sproutshot.Gameplay
{
    /// <summary>
    /// 保存配置表中的 Buff 规则，供多份活动 Buff 共享。
    /// </summary>
    public class BuffDefinition
    {
        public readonly int Id;
        public readonly string Name;
        /// <summary>每层改变的数值。</summary>
        public readonly float Bonus;
        /// <summary>累计增幅上限，0 表示不限制；由具体行为策略使用。</summary>
        public readonly float MaxBonus;
        /// <summary>持续时间，-1 表示永久。</summary>
        public readonly float Duration;
        /// <summary>最大层数，-1 表示无上限。</summary>
        public readonly int MaxStack;
        /// <summary>每次施加时增加的层数。</summary>
        public readonly int AddStack;
        public readonly BuffType BuffType;
        public readonly ValueBonusType ValueBonusType;
        /// <summary>周期数值来源：0 固定值，1 目标最大生命比例，2 施加时传入的伤害。</summary>
        public readonly int PeriodicValueMode;

        public BuffDefinition(buff_attribute attribute)
        {
            if (attribute is null) throw new ArgumentNullException(nameof(attribute));

            Id = attribute.Id;
            Name = attribute.Name;
            Bonus = attribute.Bonus;
            MaxBonus = attribute.MaxBonus;
            Duration = attribute.Duration;
            MaxStack = attribute.MaxStack;
            AddStack = attribute.AddStack;
            BuffType = attribute.BuffType;
            ValueBonusType = attribute.ValueBonusType;
            PeriodicValueMode = attribute.PeriodicValueMode;
        }
    }
}
