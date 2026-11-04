using UnityEngine;

namespace Sproutshot.Gameplay
{
    public sealed class PierceEffect : BulletEffect
    {
        public int RemainingCount { get; private set; }
        private readonly int _initialCount;
        public PierceEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId) =>
            RemainingCount = _initialCount = Mathf.Max(0, Mathf.RoundToInt(snapshot.TotalBonus));
        /// <summary>技能固有穿透，0 表示没有选卡配置编号。</summary>
        internal PierceEffect(int extraHits) : base(0) => RemainingCount = _initialCount = Mathf.Max(0, extraHits);

        public override void AfterHit(BulletContext context, ref BulletHitContext hit)
        {
            if (RemainingCount > 0) hit.ContinueFlight = true;
        }

        public override void OnPierce(BulletContext context, ref BulletHitContext hit)
        {
            if (RemainingCount <= 0) return;
            RemainingCount--;
            context.HomingEnabled = false;
        }

        public override void OnRecycle(BulletContext context) => RemainingCount = 0;
        public override void OnReturn(BulletContext context) => RemainingCount = _initialCount;
    }
}
