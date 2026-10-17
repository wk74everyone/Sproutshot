using System;

namespace Sproutshot.Utils
{
    [Serializable]
    public sealed class StageProgressData
    {
        public const int StagesPerMap = 10;
        public const int MapCount = 5;
        public const int TotalStageCount = StagesPerMap * MapCount;

        // 全局关卡编号从 1 开始；0 表示尚未通关任何关卡。
        public int HighestClearedStage;

        // 每一位对应一张地图，仅在散云动画完成后记录。旧存档默认均未播放。
        public int RevealedMapMask;

        public bool IsMapUnlocked(int mapNumber) =>
            mapNumber >= 1 && mapNumber <= MapCount &&
            HighestClearedStage >= (mapNumber - 1) * StagesPerMap;

        public bool HasRevealedMap(int mapNumber) =>
            IsMapUnlocked(mapNumber) && (RevealedMapMask & (1 << (mapNumber - 1))) != 0;
    }
}
