using System.Collections.Generic;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    internal interface ISpiritSkill { bool TryCast(SpiritEntity caster); }

    /// <summary>技能 ID 关联行为，实体只通过 OwnedSkill 决定使用哪些技能；不解析描述文字。</summary>
    internal static class SpiritSkillFactory
    {
        public static ISpiritSkill Create(int skillId, cfg.Tables tables)
        {
            switch (skillId)
            {
                case 1:
                    var slow = tables.BuffTbAttribute.GetOrDefault(19);
                    return slow != null && slow.BuffType == cfg.BuffType.Value && slow.ValueBonusType == cfg.ValueBonusType.Speed
                        && slow.Duration > 0 && slow.Bonus < 0 ? new FrostRingSkill(new BuffDefinition(slow)) : null;
                case 2: return new LeafScatterSkill();
                case 3: return new DewHealSkill();
                default: return null;
            }
        }
    }

    internal sealed class FrostRingSkill : ISpiritSkill
    {
        public const float Radius = 4f;
        private readonly BuffDefinition _slow;
        private readonly List<EnemyEntity> _targets = new();
        public FrostRingSkill(BuffDefinition slow) => _slow = slow;
        public bool TryCast(SpiritEntity caster)
        {
            var run = caster.Context; var grid = run.SpatialGrid;
            if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized) return false;
            Vector3 center = caster.Owner.transform.position;
            grid.QueryCircle(center, Radius, _targets);
            bool applied = false;
            foreach (var target in _targets)
            {
                if (target == null || !target.isActiveAndEnabled || target.IsDead) continue;
                if (target.ASC == null) target.ASC = target.GetComponent<SimpleASC>() ?? target.gameObject.AddComponent<SimpleASC>();
                target.ASC.Add(_slow); applied = true;
            }
            _targets.Clear();
            if (applied) CueManager.Instance.ExecuteAt(CueType.SpiritFrost, run, center, Radius);
            return applied;
        }
    }

    internal sealed class LeafScatterSkill : ISpiritSkill
    {
        public bool TryCast(SpiritEntity caster)
        {
            var run = caster.Context; var grid = run.SpatialGrid;
            if (caster.BulletPrefab == null || grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized ||
                !grid.TryQueryNearestVisible(caster.transform.position, 8f, Camera.main, out _)) return false;
            float damage = run.PlayerContext.CurrentDamage * .6f;
            if (damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return false;
            bool fired = false;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3f;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                var bullet = PoolManager.Instance.Get(caster.BulletPrefab, caster.transform.position, Quaternion.identity);
                if (!bullet.TryLaunchDirected(caster, direction, damage, int.MaxValue)) continue;
                var visual = bullet.GetComponent<SpiritLeafVisual>() ?? bullet.gameObject.AddComponent<SpiritLeafVisual>();
                visual.Play(direction); fired = true;
            }
            if (fired) CueManager.Instance.ExecuteAt(CueType.SpiritScatter, run, caster.transform.position, .8f);
            return fired;
        }
    }

    internal sealed class DewHealSkill : ISpiritSkill
    {
        public bool TryCast(SpiritEntity caster)
        {
            var player = caster.Context.PlayerContext;
            if (player.CurrentHp <= 0 || player.CurrentHp >= player.MaxHp) return false;
            // Heal 保留小数余量，例如最大生命为 10 时每次治疗 0.5。
            caster.Owner.Heal(player.MaxHp * .05f);
            CueManager.Instance.ExecuteAt(CueType.SpiritHeal, caster.Context, caster.Owner.transform.position, .7f);
            return true;
        }
    }
}
