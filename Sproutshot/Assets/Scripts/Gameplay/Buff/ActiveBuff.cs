using System;

namespace Sproutshot.Gameplay
{
    /// <summary>
    /// 在entity身上激活的buff
    /// </summary>
    public class ActiveBuff
    {
        public readonly BuffDefinition BuffDefinition;
        /// <summary>这份 Buff 当前的层数。</summary>
        public int StackCount;
        /// <summary>剩余时间；永久 Buff 不应参与倒计时。</summary>
        public float RemainingTime { get => (float)RemainingDuration; set => RemainingDuration = value; }
        // 内部使用双精度，避免不同帧率下累计误差漏掉到期时的最后一次结算。
        internal double RemainingDuration { get; set; }
        /// <summary>距上次周期结算累计的有效时间；刷新持续时间时保留，防止连续命中阻止跳伤。</summary>
        public double PeriodicElapsed { get; internal set; }
        /// <summary>命中时传入的每次周期伤害；已计算为实际数值，不是比例，也不乘状态层数。</summary>
        public float PeriodicDamage { get; internal set; }

        public bool IsPermanent => BuffDefinition.Duration == -1f;
        public float TotalBonus => BuffDefinition.Bonus * StackCount;

        public ActiveBuff(BuffDefinition buffDefinition)
        {
            if (buffDefinition == null) throw new ArgumentNullException(nameof(buffDefinition));

            BuffDefinition = buffDefinition;
            RemainingTime = buffDefinition.Duration;
            StackCount = buffDefinition.MaxStack == -1 ? buffDefinition.AddStack : Math.Min(buffDefinition.AddStack, buffDefinition.MaxStack);
        }
    }
}
