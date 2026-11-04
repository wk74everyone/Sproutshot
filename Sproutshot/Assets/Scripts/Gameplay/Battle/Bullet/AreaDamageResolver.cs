using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>可复用的范围伤害查询。仅提交伤害，不再次运行子弹效果链。</summary>
    public sealed class AreaDamageResolver
    {
        private readonly List<EnemyEntity> _nearby = new();
        private readonly List<(EnemyEntity enemy, uint life)> _targets = new();

        public bool TryApply(BulletContext context, Vector3 center, float radius, float damage,
            EnemyEntity excluded = null, uint excludedLife = 0)
        {
            if (context.Run == null || context.Run != RunContext.Current ||
                radius <= 0 || float.IsNaN(radius) || float.IsInfinity(radius) ||
                damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage) ||
                !Finite(center.x) || !Finite(center.y) || !Finite(center.z)) return false;
            var grid = context.Run.SpatialGrid;
            if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized) return false;
            try
            {
                grid.QueryCircle(center, radius, _nearby);
                foreach (var enemy in _nearby)
                {
                    if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead ||
                        (ReferenceEquals(enemy, excluded) && enemy.LifeVersion == excludedLife)) continue;
                    _targets.Add((enemy, enemy.LifeVersion));
                }
                // 伤害回调若结束战斗并回收子弹，Clear 会清空列表；索引遍历可立即安全停止。
                for (int i = 0; i < _targets.Count; i++)
                {
                    var target = _targets[i];
                    var hit = new BulletHitContext(target.enemy, target.life, center, damage);
                    BulletDamageResolver.TryApply(context, ref hit);
                }
                return true;
            }
            finally { Clear(); }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public void Clear() { _nearby.Clear(); _targets.Clear(); }
    }
}
