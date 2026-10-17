using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.UI
{
    public class MapPanel : MonoBehaviour
    {
        private const int StagesPerMap = StageProgressData.StagesPerMap;
        private ChapterContainer _chapterContainer;

        // 萌芽森林的10个关卡
        [SerializeField] private StagePlatform[] _map1Stages = new StagePlatform[10];
        // 花草原野的10个关卡
        [SerializeField] private StagePlatform[] _map2Stages = new StagePlatform[10];
        // 荷叶湿地的10个关卡
        [SerializeField] private StagePlatform[] _map3Stages = new StagePlatform[10];
        // 暖金沙地的10个关卡
        [SerializeField] private StagePlatform[] _map4Stages = new StagePlatform[10];
        // 污染边境的10个关卡
        [SerializeField] private StagePlatform[] _map5Stages = new StagePlatform[10];

        private void Awake()
        {
            // 沿用 ChapterContainer 中已经配置好的 MapHolder 引用，不增加场景绑定。
            _chapterContainer = GetComponentInChildren<ChapterContainer>(true);
        }

        private void OnEnable()
        {
            DoLoad();
        }

        private void OnDisable()
        {
            // 关闭整个地图页后，下次进入从章节选择开始，同时取消子面板动画。
            if (_chapterContainer != null)
                _chapterContainer.CloseMap(0f);
        }

        public void DoLoad()
        {
            int highestClearedStage = StageProgressSave.Load().HighestClearedStage;
            ApplyStates(_map1Stages, 1, highestClearedStage);
            ApplyStates(_map2Stages, 1 + StagesPerMap, highestClearedStage);
            ApplyStates(_map3Stages, 1 + StagesPerMap * 2, highestClearedStage);
            ApplyStates(_map4Stages, 1 + StagesPerMap * 3, highestClearedStage);
            ApplyStates(_map5Stages, 1 + StagesPerMap * 4, highestClearedStage);
        }

        private static void ApplyStates(StagePlatform[] stages, int firstStageNumber, int highestClearedStage)
        {
            if (stages == null)
                return;

            for (int i = 0; i < stages.Length; i++)
            {
                if (stages[i] == null)
                    continue;

                // 已通关的关卡和下一关开放。用减法比较，避免最大存档值 +1 溢出。
                bool isOpen = firstStageNumber + i - 1 <= highestClearedStage;
                stages[i].SetState(isOpen);
            }
        }

        public void OnOpened(float duration)
        {
            // 从非激活状态打开时 OnEnable 会刷新，不重复读取存档。
            if (gameObject.activeSelf)
                DoLoad();
            ProceduralAnimation.DoPanelVisibility(transform, true, duration);
        }

        public void OnClosed(float duration)
        {
            ProceduralAnimation.DoPanelVisibility(transform, false, duration);
        }

        public void OnReturnClicked()
        {
            // 只关闭 MapHolder，保留包含章节选择的 MapPanel。
            if (_chapterContainer != null)
                _chapterContainer.CloseMap(0.25f);
        }
    }
}
