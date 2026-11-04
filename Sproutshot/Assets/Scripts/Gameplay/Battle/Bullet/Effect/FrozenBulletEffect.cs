using Sproutshot.Utils;

namespace Sproutshot.Gameplay
{
    public class FrozenBulletEffect : BulletEffect
    {
        private BuffDefinition _frozenDefinition;

        public FrozenBulletEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId)
        {
            // 减速buff的id固定为4
            _frozenDefinition = new BuffDefinition(LubanConfigManager.Instance.Tables.BuffTbAttribute.Get(4));
        }

        public override void AfterHit(BulletContext context, ref BulletHitContext hit)
        {
            var target = hit.Target;
            if (!hit.DamageApplied || context.Run == null || context.Run != RunContext.Current || target == null ||
                !target.isActiveAndEnabled || target.IsDead || target.LifeVersion != hit.TargetLife) return;
            if (target.ASC == null)
                target.ASC = target.GetComponent<SimpleASC>() ?? target.gameObject.AddComponent<SimpleASC>();
            target.ASC.Add(_frozenDefinition);
        }
    }
}