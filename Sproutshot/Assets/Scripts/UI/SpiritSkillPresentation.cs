using System;
using System.Collections.Generic;

namespace Sproutshot.UI
{
    /// <summary>养成界面的技能介绍；仅用于展示，不代表战斗技能已经实现。</summary>
    public static class SpiritSkillPresentation
    {
        public readonly struct Skill
        {
            public readonly string Name, Role, Description;
            public readonly int Id;
            public readonly float CooldownSeconds;
            public readonly bool IsConfigured;
            public Skill(cfg.entity_skill row)
            {
                Id = row.Id; Name = row.Name; Role = row.TypeStr;
                CooldownSeconds = row.SkillCD; Description = row.Desc; IsConfigured = true;
            }
            public Skill(int missingId)
            {
                Id = missingId; Name = "技能待配置"; Role = string.Empty;
                CooldownSeconds = 0; Description = $"技能 {missingId} 的配置缺失。"; IsConfigured = false;
            }
        }

        /// <summary>实体属性表的技能 ID 列表关联技能表，保留配置顺序，不依赖精灵固定 ID。</summary>
        public static IReadOnlyList<Skill> Read(cfg.Tables tables, int spiritId)
        {
            var ids = tables?.EntityTbAttribute.GetOrDefault(spiritId)?.OwnedSkill;
            if (ids == null || ids.Count == 0) return Array.Empty<Skill>();
            var skills = new List<Skill>(ids.Count);
            var seen = new HashSet<int>();
            foreach (int id in ids)
            {
                if (!seen.Add(id)) continue;
                var row = tables.EntityTbSkill.GetOrDefault(id);
                skills.Add(row != null ? new Skill(row) : new Skill(id));
            }
            return skills;
        }
    }
}
