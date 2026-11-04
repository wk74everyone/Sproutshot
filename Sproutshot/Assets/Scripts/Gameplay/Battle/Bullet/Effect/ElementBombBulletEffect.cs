using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class ElementBombBulletEffect : BulletEffect
    {
        private readonly float _damageRatio;
        private const float BaseRadius = 2f;
        private float _radius;
        private bool _enableBuff;
        private bool _triggered;
        private BulletElement _elements;

        private readonly AreaDamageResolver _areaDamage = new();

        public ElementBombBulletEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId)
        {
            _damageRatio = snapshot.BonusPerStack;
        }

        public override void OnLaunch(BulletContext context)
        {
            // 从本枚子弹的快照判定元素；保留按拥有元素种类增长半径的规则。
            _triggered = false;
            _elements = context.Elements;
            _enableBuff = _elements != BulletElement.None;
            int multiplier = 1;
            if ((_elements & BulletElement.Fire) != 0) multiplier++;
            if ((_elements & BulletElement.Frost) != 0) multiplier++;
            if ((_elements & BulletElement.Lightning) != 0) multiplier++;
            _radius = _enableBuff ? BaseRadius * multiplier : 0;
        }

        public override void AfterHit(BulletContext context, ref BulletHitContext hit)
        {
            if (_triggered || !_enableBuff || !hit.DamageApplied || context.Run == null || context.Run != RunContext.Current || _radius <= 0 ||
                _damageRatio <= 0 || float.IsNaN(_damageRatio) || float.IsInfinity(_damageRatio)) return;
            var grid = context.Run.SpatialGrid;
            if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized) return;
            // 每枚子弹独立消耗一次机会。先标记，再结算；穿透命中后续敌人不会重复炸裂。
            _triggered = true;
            if (_areaDamage.TryApply(context, hit.Position, _radius, context.BaseDamage * _damageRatio, hit.Target, hit.TargetLife))
                CueManager.Instance.ExecuteAt(CueType.ElementBurst, context.Run, hit.Position, _radius, _elements);
        }

        public override void OnRecycle(BulletContext context)
        {
            _areaDamage.Clear();
            _radius = 0;
            _enableBuff = _triggered = false;
            _elements = BulletElement.None;
        }
    }
}
