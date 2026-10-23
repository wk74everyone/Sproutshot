using System.Collections.Generic;
using cfg;
using System;

namespace Sproutshot.Gameplay
{
    /// <summary>
    /// 通过身上的buff，计算出应该得到的数值
    /// </summary>
    public class AttributeSet
    {
        private readonly HashSet<ActiveBuff> _bonuses = new HashSet<ActiveBuff>();

        public T GetValue<T>(T baseValue, ValueBonusType bonusType, Func<T, float, T> applyBonus) where T : struct
        {
            float totalBonus = 0f;

            foreach (var bonus in _bonuses)
            {
                ActiveBuff activeBuff = bonus;
                BuffDefinition definition = activeBuff.BuffDefinition;

                if (definition.ValueBonusType == bonusType)
                {
                    // TotalBonus = 每层加成 × 当前层数
                    totalBonus += activeBuff.TotalBonus;
                }
            }

            return applyBonus(baseValue, totalBonus);
        }

        public void Add(ActiveBuff buff)
        {
            if (buff == null)
                throw new ArgumentNullException(nameof(buff));

            _bonuses.Add(buff);
        }

        public void Remove(ActiveBuff buff)
        {
            _bonuses.Remove(buff);
        }
    }
}