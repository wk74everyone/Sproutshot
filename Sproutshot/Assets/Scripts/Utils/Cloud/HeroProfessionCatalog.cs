using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Utils
{
    [Serializable]
    public sealed class HeroProfession
    {
        public int id, attributeId;
        public string name, specialty, description, playerPrefabAddress;
        public bool available;
    }

    /// <summary>客户端职业映射。开放状态也必须由后端校验，不能靠修改本地配置解锁。</summary>
    public static class HeroProfessionCatalog
    {
        public const int DefaultProfessionId = 1;
        [Serializable] private sealed class Configuration { public HeroProfession[] professions; }
        private static IReadOnlyList<HeroProfession> _all;
        public static IReadOnlyList<HeroProfession> All
        {
            get
            {
                if (_all != null) return _all;
                var asset = Resources.Load<TextAsset>("HeroProfessions");
                var entries = asset == null ? null : JsonUtility.FromJson<Configuration>(asset.text)?.professions;
                if (entries == null || entries.Length == 0) throw new InvalidOperationException("职业配置缺失。");
                var ids = new HashSet<int>();
                foreach (var entry in entries)
                    if (entry == null || entry.id <= 0 || !ids.Add(entry.id) || string.IsNullOrWhiteSpace(entry.name) ||
                        (entry.available && (entry.attributeId <= 0 || string.IsNullOrWhiteSpace(entry.playerPrefabAddress))))
                        throw new InvalidOperationException("职业配置无效。");
                _all = Array.AsReadOnly(entries);
                return _all;
            }
        }

        public static HeroProfession Find(int id)
        {
            foreach (var profession in All) if (profession.id == id) return profession;
            return null;
        }

        public static int EquippedId(EconomyProfile profile) =>
            profile == null || profile.equippedProfessionId == 0 ? DefaultProfessionId : profile.equippedProfessionId;

        public static HeroProfession Equipped(EconomyProfile profile)
        {
            var profession = Find(EquippedId(profile));
            if (profession == null) throw new InvalidOperationException("出战职业配置缺失，请更新游戏。");
            if (!profession.available) throw new InvalidOperationException("该职业尚未开放，请回营地选择可用职业。");
            return profession;
        }
    }
}
