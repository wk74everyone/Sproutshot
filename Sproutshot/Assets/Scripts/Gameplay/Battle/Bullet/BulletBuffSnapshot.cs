namespace Sproutshot.Gameplay
{
    /// <summary>发射时复制的值数据，不引用主人可变的 ActiveBuff。</summary>
    public readonly struct BulletBuffSnapshot
    {
        public readonly int BuffId;
        public readonly int StackCount;
        public readonly float BonusPerStack;
        public readonly float MaxBonus;
        public readonly float RemainingTime;
        public float TotalBonus => BonusPerStack * StackCount;

        public BulletBuffSnapshot(ActiveBuff buff)
        {
            BuffId = buff.BuffDefinition.Id;
            StackCount = buff.StackCount;
            BonusPerStack = buff.BuffDefinition.Bonus;
            MaxBonus = buff.BuffDefinition.MaxBonus;
            RemainingTime = buff.RemainingTime;
        }
    }
}
