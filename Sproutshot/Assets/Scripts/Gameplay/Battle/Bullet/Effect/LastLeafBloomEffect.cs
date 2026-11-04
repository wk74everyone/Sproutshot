namespace Sproutshot.Gameplay
{
    /// <summary>每段飞行最后一次成功命中时绽放；射程/寿命结束但没有命中时不触发。</summary>
    public sealed class LastLeafBloomEffect : BulletEffect
    {
        private const float Radius = 2f;
        private readonly float _damageRatio;
        private readonly AreaDamageResolver _areaDamage = new();
        private bool _bloomed;

        public LastLeafBloomEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId) => _damageRatio = snapshot.TotalBonus;
        public override void OnLaunch(BulletContext context) => _bloomed = false;

        public override void OnTerminalHit(BulletContext context, ref BulletHitContext hit)
        {
            if (_bloomed || !hit.DamageApplied || hit.ContinueFlight || context.Run == null || context.Run != RunContext.Current) return;
            // 先占用机会再结算，直击目标死亡后仍可在命中点绽放。
            _bloomed = true;
            if (_areaDamage.TryApply(context, hit.Position, Radius, context.BaseDamage * _damageRatio, hit.Target, hit.TargetLife))
                CueManager.Instance.ExecuteAt(CueType.LeafBloom, context.Run, hit.Position, Radius);
        }

        public override void OnReturn(BulletContext context) => _bloomed = false;
        public override void OnRecycle(BulletContext context) { _bloomed = false; _areaDamage.Clear(); }
    }
}
