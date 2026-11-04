using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>技能表决定冷却。每个技能独立计时，只在施放成功后进入冷却。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(SpiritEntity))]
    public sealed class SpiritSkillReleaser : MonoBehaviour
    {
        private sealed class Slot
        {
            public cfg.entity_skill Config;
            public ISpiritSkill Skill;
            public double Progress;
        }
        private readonly List<Slot> _skills = new();
        private SpiritEntity _entity;
        private bool _ticking;
        public int SkillCount => _skills.Count;

        public void Initialize(SpiritEntity entity, cfg.Tables tables)
        {
            Clear();
            if (entity == null || entity.gameObject != gameObject || tables == null) return;
            _entity = entity;
            var attribute = tables.EntityTbAttribute.GetOrDefault(entity.SpiritId);
            if (attribute == null) return;
            var seen = new HashSet<int>();
            foreach (int id in attribute.OwnedSkill)
            {
                if (!seen.Add(id)) continue;
                var config = tables.EntityTbSkill.GetOrDefault(id);
                if (config == null || config.SkillCD <= 0 || float.IsNaN(config.SkillCD) || float.IsInfinity(config.SkillCD))
                { Debug.LogWarning($"[SpiritSkillReleaser] 技能 {id} 缺少有效冷却配置。", this); continue; }
                var skill = SpiritSkillFactory.Create(id, tables);
                if (skill == null) { Debug.LogWarning($"[SpiritSkillReleaser] 技能 {id} 的效果尚未配置或实现。", this); continue; }
                _skills.Add(new Slot { Config = config, Skill = skill });
            }
        }

        public float RemainingCooldown(int skillId)
        {
            foreach (var slot in _skills)
                if (slot.Config.Id == skillId)
                    return (float)((1d - slot.Progress) * _entity.GetSkillCooldown(slot.Config.SkillCD));
            return 0;
        }

        private void Update() => Tick(Time.deltaTime);
        private void Tick(float deltaTime)
        {
            if (_ticking || !isActiveAndEnabled || _entity == null || !_entity.CanAct || Time.timeScale <= 0 ||
                deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            _ticking = true;
            try
            {
                // 超长帧最多施放一次；无目标或满血时保持就绪，不积攒多次施放。
                for (int i = 0; i < _skills.Count; i++)
                {
                    var slot = _skills[i];
                    slot.Progress = System.Math.Min(1d, slot.Progress + deltaTime / _entity.GetSkillCooldown(slot.Config.SkillCD));
                    if (slot.Progress < 1d - 1e-6) continue;
                    slot.Progress = 1d;
                    if (slot.Skill.TryCast(_entity)) slot.Progress = 0;
                    if (_entity == null || !_entity.CanAct) break;
                }
            }
            finally { _ticking = false; }
        }

        public void Clear() { _skills.Clear(); _entity = null; }
        private void OnDisable() => Clear();
    }
}
