using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class ThunderBombBulletEffect : BulletEffect
    {
        private readonly float _damageRatio;
        private const float BaseRadius = 2f;
        private float _radius;

        private readonly List<EnemyEntity> _nearbyEnemies = new();
        private readonly List<(EnemyEntity enemy, uint life)> _targets = new();

        public ThunderBombBulletEffect(BulletBuffSnapshot snapshot) : base(snapshot.BuffId)
        {
            _damageRatio = snapshot.BonusPerStack;
        }

        public override void OnLaunch(BulletContext context)
        {
            // 半径仍按选卡次数增长；发射后选卡不会改变已经飞出的子弹。
            _radius = Mathf.Max(1f, BaseRadius * (context.Run?.Cards.GetAcquiredCount(8) ?? 0) * 0.3f);
        }

        private void ApplyThunderDamage(BulletContext context, Vector3 center)
        {
            if (context.Run == null || context.Run != RunContext.Current)
                return;

            var grid = context.Run.SpatialGrid;
            if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized)
                return;

            // 基础子弹伤害的 50%，不是敌人生命的 50%。
            float damage = context.BaseDamage * _damageRatio;

            grid.QueryCircle(center, _radius, _nearbyEnemies);

            // 先冻结本次命中的生命编号，避免结算期间死亡、回池影响后续目标。
            _targets.Clear();
            foreach (var enemy in _nearbyEnemies)
            {
                if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead)
                    continue;
                _targets.Add((enemy, enemy.LifeVersion));
            }

            foreach (var target in _targets)
            {
                var thunderHit = new BulletHitContext(
                    target.enemy,
                    target.life,
                    center,
                    damage);

                BulletDamageResolver.TryApply(context, ref thunderHit);
            }

            _nearbyEnemies.Clear();
            _targets.Clear();
        }

        public override void AfterHit(BulletContext context, ref BulletHitContext hit)
        {
            var salvo = context.Salvo;
            if (!hit.DamageApplied || context.Run == null || context.Run != RunContext.Current ||
                salvo == null || salvo.Run != context.Run || salvo.Owner != context.Owner ||
                salvo.Number <= 0 || salvo.Number % 4 != 0 || _radius <= 0 ||
                _damageRatio <= 0 || float.IsNaN(_damageRatio) || float.IsInfinity(_damageRatio)) return;
            var grid = context.Run.SpatialGrid;
            if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized) return;
            // 使用命中位置，不依赖直击目标仍然存活；先消耗本轮机会，再进行范围伤害。
            if (salvo.TryConsumeEffect(BuffId))
            {
                ApplyThunderDamage(context, hit.Position);
                CueManager.Instance.ExecuteAt(CueType.ThunderStrike, context.Run, hit.Position, _radius);
            }
        }

        public override void OnRecycle(BulletContext context)
        {
            _nearbyEnemies.Clear();
            _targets.Clear();
            _radius = 0;
        }
    }
}