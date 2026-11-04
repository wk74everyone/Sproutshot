using System;
using System.Collections.Generic;
using Sproutshot.Utils;

namespace Sproutshot.UI
{
    /// <summary>只投影界面数据，不修改档案。等级/费用取后端，属性预览取本地 Luban 表。</summary>
    public sealed class CultivatePanelData
    {
        public sealed class HeroData
        {
            public int professionId, level, attack, nextAttack, health, nextHealth, defense, nextDefense, cost;
            public bool available, maxLevel, canUpgrade;
            public string unavailableReason;
        }

        public HeroData Hero { get; private set; }
        public List<CultivatePanelView.SpiritData> Spirits { get; private set; }

        public static CultivatePanelData Create(EconomyProfile profile, EconomyCatalog catalog, cfg.Tables tables)
        {
            if (profile == null || catalog == null || tables == null)
                throw new ArgumentException("养成数据尚未准备好。");
            var result = new CultivatePanelData { Hero = CreateHero(profile, catalog, tables,
                HeroProfessionCatalog.EquippedId(profile)), Spirits = new List<CultivatePanelView.SpiritData>() };

            var seen = new HashSet<int>();
            if (profile.ownedSpirits != null) foreach (var owned in profile.ownedSpirits)
            {
                if (owned == null || !seen.Add(owned.spiritId)) continue;
                var row = tables.EntityTbGrowth.GetOrDefault(owned.spiritId);
                var baseRow = tables.EntityTbAttribute.GetOrDefault(owned.spiritId);
                var spirit = new CultivatePanelView.SpiritData {
                    id = owned.spiritId, level = owned.level,
                    name = row?.Name ?? baseRow?.Name ?? $"精灵 {owned.spiritId}",
                    skills = SpiritSkillPresentation.Read(tables, owned.spiritId),
                    hasUpgradePreview = row != null && baseRow != null
                };
                Price(profile, catalog, owned.level, out spirit.upgradeCost, out spirit.isMaxLevel,
                    out spirit.canUpgrade, out spirit.unavailableReason);
                if (row != null && baseRow != null)
                {
                    spirit.attackBonus = row.Damage * owned.level;
                    spirit.healthBonus = row.Hp * owned.level;
                    spirit.defenseBonus = row.Defense * owned.level;
                    spirit.selfAttack = baseRow.Damage + row.SelfDamage * owned.level;
                    int nextLevel = spirit.isMaxLevel ? owned.level : owned.level + 1;
                    spirit.nextAttackBonus = row.Damage * nextLevel;
                    spirit.nextHealthBonus = row.Hp * nextLevel;
                    spirit.nextDefenseBonus = row.Defense * nextLevel;
                    spirit.nextSelfAttack = baseRow.Damage + row.SelfDamage * nextLevel;
                }
                else { spirit.canUpgrade = false; spirit.unavailableReason = "配置缺失"; }
                spirit.hasUpgradePreview &= !spirit.isMaxLevel && spirit.unavailableReason != "配置缺失";
                result.Spirits.Add(spirit);
            }
            // 固定按 ID 排列，升级和出战后不跳行。
            result.Spirits.Sort((a, b) => a.id.CompareTo(b.id));
            return result;
        }

        public static HeroData CreateHero(EconomyProfile profile, EconomyCatalog catalog, cfg.Tables tables, int professionId)
        {
            if (profile == null || catalog == null || tables == null) throw new ArgumentException("养成数据尚未准备好。");
            var hero = new HeroData { professionId = professionId, level = profile.playerLevel };
            var profession = HeroProfessionCatalog.Find(professionId);
            if (profession == null) { hero.unavailableReason = "配置缺失"; return hero; }
            if (!profession.available) { hero.unavailableReason = "尚未开放"; return hero; }
            var attribute = tables.EntityTbAttribute.GetOrDefault(profession.attributeId);
            var growth = tables.EntityTbGrowth.GetOrDefault(profession.attributeId);
            if (attribute == null || growth == null) { hero.unavailableReason = "配置缺失"; return hero; }
            hero.available = true;
            CultivationStats.GetPlayerBonuses(profile, tables, out int damageBonus, out int hpBonus,
                out int defenseBonus, profession.attributeId);
            hero.attack = attribute.Damage + damageBonus;
            hero.health = attribute.Hp + hpBonus;
            hero.defense = attribute.Defense + defenseBonus;
            Price(profile, catalog, hero.level, out hero.cost, out hero.maxLevel, out hero.canUpgrade, out hero.unavailableReason);
            hero.nextAttack = hero.attack + (hero.maxLevel ? 0 : growth.Damage);
            hero.nextHealth = hero.health + (hero.maxLevel ? 0 : growth.Hp);
            hero.nextDefense = hero.defense + (hero.maxLevel ? 0 : growth.Defense);
            return hero;
        }

        private static void Price(EconomyProfile profile, EconomyCatalog catalog, int level,
            out int cost, out bool maxLevel, out bool canUpgrade, out string reason)
        {
            EconomyGrowth current = null;
            int lastUpgradeLevel = 0;
            if (catalog.growth != null) foreach (var row in catalog.growth)
            {
                if (row == null) continue;
                if (row.level == level) current = row;
                if (!row.terminal) lastUpgradeLevel = Math.Max(lastUpgradeLevel, row.level);
            }
            cost = current?.cost ?? 0;
            // 费用行 N 表示 N 升 N+1，最后一行费用仍然可用。
            maxLevel = lastUpgradeLevel > 0 && level > lastUpgradeLevel;
            reason = maxLevel ? "已满级" : current == null || current.terminal || cost < 0 ? "配置缺失" :
                profile.sproutSeed < cost ? "种子不足" : null;
            canUpgrade = reason == null;
        }
    }
}
