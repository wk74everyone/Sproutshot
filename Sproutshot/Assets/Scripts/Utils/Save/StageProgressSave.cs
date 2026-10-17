using System;
using UnityEngine;

namespace Sproutshot.Utils
{
    // 将存档入口集中在这里，之后换云端存储时不用在各个 UI 中操作 PlayerPrefs。
    public static class StageProgressSave
    {
        private const string SaveKey = "Sproutshot.StageProgress.v1";

        public static StageProgressData Load()
        {
            string json = PlayerPrefs.GetString(SaveKey, string.Empty);
            if (string.IsNullOrEmpty(json))
                return new StageProgressData();

            try
            {
                var data = JsonUtility.FromJson<StageProgressData>(json) ?? new StageProgressData();
                data.HighestClearedStage = Mathf.Clamp(data.HighestClearedStage, 0, StageProgressData.TotalStageCount);
                int unlockedMapCount = Mathf.Min(StageProgressData.MapCount,
                    data.HighestClearedStage / StageProgressData.StagesPerMap + 1);
                data.RevealedMapMask &= (1 << unlockedMapCount) - 1;
                return data;
            }
            catch (ArgumentException)
            {
                Debug.LogWarning("[StageProgressSave] 关卡存档无法解析，本次使用初始进度。");
                return new StageProgressData();
            }
        }

        public static bool IsStageUnlocked(int stageNumber) =>
            stageNumber >= 1 && stageNumber <= StageProgressData.TotalStageCount &&
            stageNumber - 1 <= Load().HighestClearedStage;

        // 只在闯关模式成功通关后调用；重玩旧关卡不会让进度倒退。
        public static void RecordStageCleared(int stageNumber)
        {
            if (stageNumber <= 0 || stageNumber > StageProgressData.TotalStageCount)
                return;

            var data = Load();
            if (stageNumber <= data.HighestClearedStage)
                return;

            data.HighestClearedStage = stageNumber;
            Save(data);
        }

        public static void RecordMapRevealed(int mapNumber)
        {
            // 重新读取最新存档，不能用动画开始时的快照覆盖期间更新的关卡进度。
            var data = Load();
            if (mapNumber <= 1 || !data.IsMapUnlocked(mapNumber) || data.HasRevealedMap(mapNumber))
                return;

            data.RevealedMapMask |= 1 << (mapNumber - 1);
            Save(data);
        }

        private static void Save(StageProgressData data)
        {
            PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
    }
}
