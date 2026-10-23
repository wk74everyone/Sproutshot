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
        public float RemainingTime;

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
