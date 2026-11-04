namespace Sproutshot.Gameplay
{
    public class FirstStrikeBulletEffect : BulletEffect
    {
        private readonly float _baseAddition = 0.3f;
        private float _finalAddition;
        private bool _hasHit;

        public FirstStrikeBulletEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId) { }

        public override void OnLaunch(BulletContext context)
        {
            _hasHit = false;
            // 10 是卡牌 ID；本次发射固定加成，不受后续选卡变化影响。
            int count = context.Run?.Cards.GetAcquiredCount(10) ?? 0;
            _finalAddition = _baseAddition * count;
        }

        public override void BeforeHit(BulletContext context, ref BulletHitContext hit)
        {
            // 只修改本次命中的伤害，由统一入口结算一次防御；不污染后续目标的伤害。
            if (!_hasHit) hit.Damage += _finalAddition * context.BaseDamage;
        }

        public override void AfterHit(BulletContext context, ref BulletHitContext hit)
        {
            // 失效目标或未成功提交的伤害，不消耗这枚子弹的首击机会。
            if (hit.DamageApplied) _hasHit = true;
        }

        public override void OnRecycle(BulletContext context)
        {
            _hasHit = false;
            _finalAddition = 0;
        }
    }
}
