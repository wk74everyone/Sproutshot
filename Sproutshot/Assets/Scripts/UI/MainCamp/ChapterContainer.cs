using Sproutshot.Utils;
using TMPro;
using UnityEngine;

namespace Sproutshot.UI
{
    public class ChapterContainer : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Transform _mapPanel;

        private string[] _chapterDesc = new string[3]
        {
            "Chapter 1\n<size=96><color=#3CBC00>萌芽启程</color></size>",
            "暂未开放",
            "暂未开放"
        };

        public void OnRefreshItem(int idx)
        {
            if (_label == null || _chapterDesc == null || idx < 0 || idx >= _chapterDesc.Length)
                return;

            _label.text = _chapterDesc[idx];
        }

        public void OnConfirmClicked()
        {
            // 防止连续点击重复播放。
            if (_mapPanel == null || _mapPanel.gameObject.activeSelf)
                return;

            ProceduralAnimation.DoPanelVisibility(_mapPanel, true, 0.25f, rotate: false);
        }

        public void CloseMap(float duration)
        {
            ProceduralAnimation.DoPanelVisibility(_mapPanel, false, duration, rotate: false);
        }
    }
}
