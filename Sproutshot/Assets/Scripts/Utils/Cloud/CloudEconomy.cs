using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Sproutshot.Utils
{
    [Serializable]
    public sealed class EconomyPayload
    {
        public string pool, battleId, installId, rewardType, ticketId;
        public int count, stage, defeated, map, highest, revealed, spiritId, professionId;
        public bool victory;
        public bool completed;
    }
    [Serializable] public sealed class OwnedSpirit { public int spiritId, level; }
    [Serializable]
    public sealed class SummonItem
    {
        public int spiritId, rarity, duplicateSeedAmount;
        public string name;
        public bool isNew, isHardPity;
    }
    [Serializable]
    public sealed class EconomyReceipt
    {
        public string operationId, kind, battleId, ticketId, rewardType, expiresAt;
        public int stage, totalCost, rewardSeeds, rewardDew, pityAfter, level, professionId;
        public bool victory, firstClear;
        public int rewardAmount;
        public SummonItem[] results;
    }
    [Serializable]
    public sealed class EconomyProfile
    {
        public string playerId;
        public long revision;
        public int sproutSeed, starDew, playerLevel, equippedSpiritId, highestClearedStage, revealedMapMask, pityCount, equippedProfessionId;
        public bool legacyImported;
        public OwnedSpirit[] ownedSpirits;
        public EconomyReceipt lastSummon;
    }
    [Serializable] public sealed class EconomyGrowth { public int level, cost, damage, hp; public bool terminal; }
    [Serializable]
    public sealed class RewardedAdStatus
    {
        public bool enabled;
        public string timezone, verificationMode;
        public int seedReward, dewReward, seedDailyLimit, dewDailyLimit, seedRemaining, dewRemaining;
    }
    [Serializable]
    public sealed class EconomySpirit
    {
        public int id, rarity, damage, selfDamage, hp, defense;
        public string name;
    }
    [Serializable]
    public sealed class EconomyCatalog
    {
        public int version, singleCost, tenCost, hardPity, duplicateSeeds;
        public EconomyGrowth[] growth;
        public EconomySpirit[] spirits;
    }

    /// <summary>数据库是永久数据的唯一来源；本地只保存未完成请求，供超时/重启后使用同一编号重试。</summary>
    public static class CloudEconomy
    {
        [Serializable]
        private sealed class PendingOperation
        {
            public string playerId, action, operationId;
            public EconomyPayload payload;
        }
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static EconomyProfile _profile;
        private static EconomyCatalog _catalog;
        private static RewardedAdStatus _rewardedAds;
        public static RewardedAdStatus RewardedAds => _rewardedAds == null ? null : JsonUtility.FromJson<RewardedAdStatus>(JsonUtility.ToJson(_rewardedAds));
        public static EconomyProfile Profile => _profile == null ? null : JsonUtility.FromJson<EconomyProfile>(JsonUtility.ToJson(_profile));
        public static EconomyCatalog Catalog => _catalog == null ? null : JsonUtility.FromJson<EconomyCatalog>(JsonUtility.ToJson(_catalog));
        public static string BattleId { get; private set; }
        public static int BattleStage { get; private set; }
        public static event Action Changed;
        public static bool HasPending => _profile != null && PlayerPrefs.HasKey(PendingKey);
        public static string PendingAction => HasPending ? ReadPending().action : null;
        private static string PendingKey => "Sproutshot.Cloud.Pending.v1." + _profile.playerId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            _profile = null; _catalog = null; _rewardedAds = null; BattleId = null; BattleStage = 0; Changed = null;
        }

        public static async Task RefreshAsync()
        {
            await Gate.WaitAsync();
            try { await LoadCoreAsync(); }
            finally { Gate.Release(); }
        }

        public static Task<CloudBaseData> EquipProfessionAsync(int professionId) => ExecuteAsync("equipProfession", new EconomyPayload { professionId = professionId });

        private static async Task LoadCoreAsync()
        {
            var client = CloudBaseClient.Instance;
            await client.InitializeAsync();
            var loaded = await client.EconomyAsync("load", null, new EconomyPayload());
            Apply(loaded);
            if (!_profile.legacyImported)
            {
                if (HasPending)
                {
                    var pending = ReadPending();
                    if (pending.action != "migrate") return;
                    await SubmitCoreAsync(pending);
                }
                else
                {
                    const string installKey = "Sproutshot.Cloud.LegacyInstall.v1";
                    string install = PlayerPrefs.GetString(installKey, string.Empty);
                    if (string.IsNullOrEmpty(install)) { install = Guid.NewGuid().ToString(); PlayerPrefs.SetString(installKey, install); PlayerPrefs.Save(); }
                    var legacy = StageProgressSave.GetLegacyForMigration(_profile.playerId);
                    await SubmitCoreAsync(NewPending("migrate", new EconomyPayload
                    {
                        installId = install,
                        highest = legacy.HighestClearedStage,
                        revealed = legacy.RevealedMapMask
                    }));
                    StageProgressSave.MarkLegacyMigrated(_profile.playerId);
                }
            }
        }

        public static async Task<CloudBaseData> ExecuteAsync(string action, EconomyPayload payload)
        {
            await Gate.WaitAsync();
            try
            {
                if (_profile == null) await LoadCoreAsync();
                var pending = HasPending ? ReadPending() : NewPending(action, payload);
                if (pending.action != action || JsonUtility.ToJson(pending.payload) != JsonUtility.ToJson(payload))
                    throw new CloudBaseException("PENDING_OPERATION", "上次操作尚未确认，请先重试上次操作。");
                return await SubmitCoreAsync(pending);
            }
            finally { Gate.Release(); }
        }

        public static async Task<CloudBaseData> RetryPendingAsync()
        {
            await Gate.WaitAsync();
            try
            {
                if (_profile == null) await LoadCoreAsync();
                if (!HasPending) return null;
                return await SubmitCoreAsync(ReadPending());
            }
            finally { Gate.Release(); }
        }

        private static PendingOperation ReadPending()
        {
            var value = JsonUtility.FromJson<PendingOperation>(PlayerPrefs.GetString(PendingKey));
            if (value == null || value.playerId != _profile.playerId || value.payload == null)
                throw new CloudBaseException("INVALID_PENDING_OPERATION", "本地待确认操作损坏，请保留数据并联系开发者。");
            return value;
        }

        private static PendingOperation NewPending(string action, EconomyPayload payload)
        {
            var value = new PendingOperation
            {
                playerId = _profile.playerId,
                action = action,
                operationId = Guid.NewGuid().ToString(),
                payload = payload
            };
            PlayerPrefs.SetString(PendingKey, JsonUtility.ToJson(value));
            PlayerPrefs.Save();
            return value;
        }

        private static async Task<CloudBaseData> SubmitCoreAsync(PendingOperation pending)
        {
            try
            {
                var data = await CloudBaseClient.Instance.EconomyAsync(pending.action, pending.operationId, pending.payload);
                if (data?.profile?.playerId != pending.playerId)
                    throw new CloudBaseException("PLAYER_CHANGED", "玩家身份已变化，请重新进入游戏。");
                Apply(data);
                PlayerPrefs.DeleteKey(PendingKey); PlayerPrefs.Save();
                if (pending.action == "beginBattle")
                {
                    BattleId = data.receipt.battleId; BattleStage = data.receipt.stage;
                }
                if (pending.action == "settleBattle") { BattleId = null; BattleStage = 0; }
                return data;
            }
            catch (CloudBaseException error)
            {
                // Transport/unknown backend errors keep the request. Known business rejections did not commit.
                if (error.Code == "INSUFFICIENT_DEW" || error.Code == "INSUFFICIENT_SEEDS" ||
                    error.Code == "MAX_LEVEL" || error.Code == "SPIRIT_NOT_OWNED" || error.Code == "STAGE_LOCKED" ||
                    error.Code == "MAP_LOCKED" || error.Code == "INVALID_POOL" || error.Code == "INVALID_REQUEST" ||
                    error.Code == "BATTLE_NOT_FOUND" || error.Code == "BATTLE_CLOSED" || error.Code == "BATTLE_EXPIRED" ||
                    error.Code == "INVALID_BATTLE_RESULT" || error.Code == "ADS_DISABLED" || error.Code == "AD_DAILY_LIMIT" ||
                    error.Code == "AD_TICKET_NOT_FOUND" || error.Code == "AD_TICKET_EXPIRED" || error.Code == "AD_NOT_COMPLETED" ||
                    error.Code == "AD_VERIFICATION_REQUIRED")
                { PlayerPrefs.DeleteKey(PendingKey); PlayerPrefs.Save(); }
                throw;
            }
        }

        private static void Apply(CloudBaseData data)
        {
            if (data?.profile == null || string.IsNullOrEmpty(data.profile.playerId))
                throw new CloudBaseException("INVALID_PROFILE", "服务器返回的档案无效。");
            if (_profile != null && _profile.playerId == data.profile.playerId && data.profile.revision < _profile.revision) return;
            _profile = data.profile; _catalog = data.catalog; _rewardedAds = data.rewardedAds;
            StageProgressSave.ApplyServer(_profile.playerId, new StageProgressData
            {
                HighestClearedStage = _profile.highestClearedStage,
                RevealedMapMask = _profile.revealedMapMask
            });
            Changed?.Invoke();
        }

        public static async Task BeginBattleAsync(int stage)
        {
            await ExecuteAsync("beginBattle", new EconomyPayload { stage = stage });
        }

        public static Task<CloudBaseData> UpgradePlayerAsync() => ExecuteAsync("upgradePlayer", new EconomyPayload());
        public static Task<CloudBaseData> BeginRewardedAdAsync(string rewardType) => ExecuteAsync("beginRewardedAd", new EconomyPayload { rewardType = rewardType });
        public static Task<CloudBaseData> ClaimRewardedAdAsync(string ticketId) => ExecuteAsync("claimRewardedAd", new EconomyPayload { ticketId = ticketId, completed = true });
        public static Task<CloudBaseData> UpgradeSpiritAsync(int spiritId) => ExecuteAsync("upgradeSpirit", new EconomyPayload { spiritId = spiritId });
        /// <summary>设置出战伙伴，传 0 卸下；服务端确认后统一刷新档案。</summary>
        public static Task<CloudBaseData> EquipSpiritAsync(int spiritId) => ExecuteAsync("equipSpirit", new EconomyPayload { spiritId = spiritId });

        public static void GetGrowthBonuses(out int damage, out int hp) => GetGrowthBonuses(out damage, out hp, out _);

        public static void GetGrowthBonuses(out int damage, out int hp, out int defense, int playerId = 1)
        {
            damage = hp = defense = 0;
            if (_profile == null) return;
            CultivationStats.GetPlayerBonuses(_profile, LubanConfigManager.Instance.Tables,
                out damage, out hp, out defense, playerId);
        }
    }
}
