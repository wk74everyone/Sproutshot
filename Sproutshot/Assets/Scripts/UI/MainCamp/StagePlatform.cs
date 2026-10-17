using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sproutshot.Utils;
using Sproutshot.MapGen;

namespace Sproutshot.UI
{
    public class StagePlatform : MonoBehaviour
    {
        [SerializeField] private int _level;
        [SerializeField] private GameObject _showContent;
        [SerializeField] private TMP_Text _seedCount;
        [SerializeField] private TMP_Text _starDewCount;

        private Button _button;
        private bool _initialized;

        public bool IsOpen { get; private set; }
        public int Level => _level;

        public void SetState(bool isOpen)
        {
            if (!_initialized)
            {
                _button = GetComponent<Button>();
                if (_button == null)
                {
                    Debug.LogError("[StagePlatform] 关卡台子缺少 Button。", this);
                    return;
                }
                _button.onClick.AddListener(OnClicked);
                _initialized = true;
            }

            IsOpen = isOpen;
            LoadDropData();
            if (_button != null)
                _button.interactable = isOpen;
        }

        private void LoadDropData()
        {
            if (!IsOpen) return;

            var data = LubanConfigManager.Instance.Tables.TbReward.Get(_level);
            int highestClearedStage = StageProgressSave.Load().HighestClearedStage + 1;
            if (highestClearedStage == _level)
            {
                _showContent.SetActive(true);
                _seedCount.text = $"{data.SproutSeed}";
                _starDewCount.text = $"{data.StarDew}";
            }
            else if (highestClearedStage > _level)
            {
                _showContent.SetActive(true);
                _seedCount.text = $"{data.SproutSeed / 2}";
                _starDewCount.text = $"{data.StarDew / 2}";
            }
        }

        private void OnClicked()
        {
            if (!IsOpen) return;
            if (!StageProgressSave.IsStageUnlocked(_level)) return;
            MapSceneSelection.SelectStage(_level);
            // 进入所选章节；地图就绪后由 MapPlayerSpawner 生成玩家。
            FlowerSceneTransition.Instance.EnterScene("GameScene");
        }
    }
}
