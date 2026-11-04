using System;

namespace Sproutshot.Utils
{
    /// <summary>养成界面与战斗共用的玩家属性加成，只读取等级和出战状态。</summary>
    public static class CultivationStats
    {
        public static void GetPlayerBonuses(EconomyProfile profile, cfg.Tables tables,
            out int damage, out int hp, out int defense, int playerId = 1)
        {
            damage = hp = defense = 0;
            if (profile == null) return;
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            var growth = tables.EntityTbGrowth.GetOrDefault(playerId);
            if (growth == null) throw new InvalidOperationException("主角成长配置缺失。");

            int steps = Math.Max(0, profile.playerLevel - 1);
            damage = growth.Damage * steps;
            hp = growth.Hp * steps;
            defense = growth.Defense * steps;
            if (profile.equippedSpiritId <= 0 || profile.ownedSpirits == null) return;

            foreach (var owned in profile.ownedSpirits)
            {
                if (owned == null || owned.spiritId != profile.equippedSpiritId) continue;
                var spirit = tables.EntityTbGrowth.GetOrDefault(owned.spiritId);
                // 与界面一致：配置完整、实际拥有且已出战的伙伴才计入总属性。
                if (spirit == null || tables.EntityTbAttribute.GetOrDefault(owned.spiritId) == null) return;
                damage += spirit.Damage * owned.level;
                hp += spirit.Hp * owned.level;
                defense += spirit.Defense * owned.level;
                return;
            }
        }
    }
}
