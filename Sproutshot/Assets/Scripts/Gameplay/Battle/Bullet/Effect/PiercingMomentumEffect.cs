using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>穿林余势：本次伤害结算并穿透后，按基础伤害累加后续命中的增幅。</summary>
    public sealed class PiercingMomentumEffect : BulletEffect
    {
        private readonly float _bonusPerPierce;
        private readonly float _maxBonus;
        public float AccumulatedBonus { get; private set; }

        public PiercingMomentumEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId)
        {
            _bonusPerPierce = Mathf.Max(0, snapshot.TotalBonus);
            _maxBonus = Mathf.Max(0, snapshot.MaxBonus);
        }

        public override void OnPierce(BulletContext context, ref BulletHitContext hit)
        {
            float nextBonus = AccumulatedBonus + _bonusPerPierce;
            if (_maxBonus > 0) nextBonus = Mathf.Min(nextBonus, _maxBonus);
            // 只加入本次新增部分，不复利，也不覆盖其他策略对伤害的修改。
            context.Damage += context.BaseDamage * (nextBonus - AccumulatedBonus);
            AccumulatedBonus = nextBonus;
        }

        public override void OnRecycle(BulletContext context) => AccumulatedBonus = 0;
        public override void OnReturn(BulletContext context) => AccumulatedBonus = 0;
    }
}
