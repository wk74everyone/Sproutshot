namespace Sproutshot.Gameplay
{
    public static class BulletDamageResolver
    {
        /// <summary>统一伤害提交入口。敌人自己的 TakeDamage 继续负责防御、死亡和掉落链。</summary>
        public static bool TryApply(BulletContext context, ref BulletHitContext hit)
        {
            var target = hit.Target;
            if (context.Run == null || context.Run != RunContext.Current || target == null ||
                !target.isActiveAndEnabled || target.IsDead || target.LifeVersion != hit.TargetLife ||
                hit.Damage <= 0 || float.IsNaN(hit.Damage) || float.IsInfinity(hit.Damage)) return false;
            target.TakeDamage(hit.Damage);
            return true;
        }
    }
}
