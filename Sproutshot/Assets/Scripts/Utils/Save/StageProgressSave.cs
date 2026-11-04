using System;
using UnityEngine;

namespace Sproutshot.Utils
{
    /// <summary>地图读接口：数据库是永久进度来源，本地仅保留显示缓存与旧档迁移来源。</summary>
    public static class StageProgressSave
    {
        private const string SaveKey = "Sproutshot.StageProgress.v1";
        private const string ActivePlayerKey = SaveKey + ".ActivePlayer";
        private const string LegacyOwnerKey = SaveKey + ".LegacyImportedBy";
        private static StageProgressData _server;
        public static event Action Changed;
        public static int Revision { get; private set; }
        public static string PlayerId => PlayerPrefs.GetString(ActivePlayerKey, string.Empty);
        private static string CacheKey => SaveKey + ".ServerCache." + PlayerId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _server = null; Revision = 0; Changed = null; }

        public static StageProgressData Load() => _server != null ? Normalize(_server) :
            Read(string.IsNullOrEmpty(PlayerId) ? SaveKey : CacheKey);

        private static StageProgressData Read(string key)
        {
            string json = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return new StageProgressData();
            try { return Normalize(JsonUtility.FromJson<StageProgressData>(json) ?? new StageProgressData()); }
            catch (ArgumentException) { Debug.LogWarning("[StageProgressSave] 进度缓存损坏，等待服务器恢复。"); return new StageProgressData(); }
        }

        public static StageProgressData Normalize(StageProgressData data)
        {
            int highest = Mathf.Clamp(data.HighestClearedStage, 0, StageProgressData.TotalStageCount);
            int count = Mathf.Min(StageProgressData.MapCount, highest / StageProgressData.StagesPerMap + 1);
            return new StageProgressData { HighestClearedStage = highest, RevealedMapMask = data.RevealedMapMask & ((1 << count) - 1) };
        }

        private static void ValidatePlayer(string playerId)
        {
            if (playerId == null || playerId.Length != 64) throw new ArgumentException("Invalid player ID.");
            foreach (char c in playerId)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) throw new ArgumentException("Invalid player ID.");
        }

        public static void ApplyServer(string playerId, StageProgressData data)
        {
            ValidatePlayer(playerId);
            var next = Normalize(data);
            bool changed = PlayerId != playerId || _server == null ||
                _server.HighestClearedStage != next.HighestClearedStage || _server.RevealedMapMask != next.RevealedMapMask;
            PlayerPrefs.SetString(ActivePlayerKey, playerId);
            _server = next;
            PlayerPrefs.SetString(CacheKey, JsonUtility.ToJson(next));
            PlayerPrefs.Save();
            if (changed) { Revision++; Changed?.Invoke(); }
        }

        public static StageProgressData GetLegacyForMigration(string playerId)
        {
            ValidatePlayer(playerId);
            string owner = PlayerPrefs.GetString(LegacyOwnerKey, string.Empty);
            var legacy = string.IsNullOrEmpty(owner) || owner == playerId ? Read(SaveKey) : new StageProgressData();
            var scoped = Read(SaveKey + "." + playerId);
            return Normalize(new StageProgressData { HighestClearedStage = Math.Max(legacy.HighestClearedStage, scoped.HighestClearedStage),
                RevealedMapMask = legacy.RevealedMapMask | scoped.RevealedMapMask });
        }

        public static void MarkLegacyMigrated(string playerId)
        {
            ValidatePlayer(playerId);
            if (!PlayerPrefs.HasKey(LegacyOwnerKey)) { PlayerPrefs.SetString(LegacyOwnerKey, playerId); PlayerPrefs.Save(); }
        }

        public static bool IsStageUnlocked(int stageNumber) => stageNumber >= 1 &&
            stageNumber <= StageProgressData.TotalStageCount && stageNumber - 1 <= Load().HighestClearedStage;

        public static void RecordStageCleared(int stageNumber) =>
            Debug.LogWarning("[StageProgressSave] 通关进度需要服务器结算，未写入本地永久存档。");

        public static async void RecordMapRevealed(int mapNumber)
        {
            if (CloudEconomy.Profile == null || mapNumber < 2 || !Load().IsMapUnlocked(mapNumber) || Load().HasRevealedMap(mapNumber)) return;
            try { await CloudEconomy.ExecuteAsync("reveal", new EconomyPayload { map = mapNumber }); }
            catch (Exception error) { Debug.LogWarning("[StageProgressSave] 章节显示状态保存失败：" + error.Message); }
        }
    }
}
