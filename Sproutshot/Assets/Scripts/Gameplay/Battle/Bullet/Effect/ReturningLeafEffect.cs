using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>回叶归途：飞行自然结束或用完命中次数后，只允许沿去程路径折返一次。</summary>
    public sealed class ReturningLeafEffect : BulletEffect
    {
        private readonly float _damageRatio;
        private bool _used;

        public ReturningLeafEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId) =>
            _damageRatio = Mathf.Clamp01(snapshot.TotalBonus);

        public override void OnLaunch(BulletContext context) => _used = false;

        public override bool TryBeginReturn(BulletContext context, BulletFlightEndReason reason)
        {
            if (_used || context.Run == null || context.Run != RunContext.Current || !context.BeginReturn(_damageRatio)) return false;
            _used = true;
            return true;
        }

        public override void OnRecycle(BulletContext context) => _used = false;
    }
}
