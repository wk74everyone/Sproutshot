using Sproutshot.Utils;

namespace Sproutshot.Gameplay
{
    public class FireBulletEffect : BulletEffect
    {
        private readonly BuffDefinition _burnDefinition;
        private readonly float _damageRatio;

        public FireBulletEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId)
        {
            // 燃烧弹的效果是点燃，所以施加buff的ID固定为1
            _burnDefinition = new BuffDefinition(LubanConfigManager.Instance.Tables.BuffTbAttribute.Get(1));
            _damageRatio = snapshot.TotalBonus;
        }

        public override void AfterHit(BulletContext context, ref BulletHitContext hit)
        {
            var target = hit.Target;
            if (!hit.DamageApplied || context.Run == null || context.Run != RunContext.Current || target == null ||
                !target.isActiveAndEnabled || target.IsDead || target.LifeVersion != hit.TargetLife) return;
            // 使用发射时基础伤害和这枚子弹自己的比例快照，不受穿透增伤或敌人防御影响。
            float damage = context.BaseDamage * _damageRatio;
            if (context.BaseDamage <= 0 || _damageRatio <= 0 || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (target.ASC == null)
                target.ASC = target.GetComponent<SimpleASC>() ?? target.gameObject.AddComponent<SimpleASC>();
            target.ASC.Add(_burnDefinition, damage);
        }
    }
}