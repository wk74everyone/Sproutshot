using UnityEngine;

namespace Sproutshot.UI
{
    public class PanelSelection : MonoBehaviour
    {
        [SerializeField, Range(0.1f, 1f)] private float _duration = 0.5f;
        [SerializeField] private TrainingPanel _trainingPanel;
        [SerializeField] private SproutRafflePanel _rafflePanel;
        [SerializeField] private MapPanel _mapPanel;

        public void ShowTrainingPanel()
        {
            HideAllPanels();
            _trainingPanel.OnOpened(_duration);
        }

        public void ShowMapPanel()
        {
            HideAllPanels();
            _mapPanel.OnOpened(_duration);
        }

        public void ShowRafflePanel()
        {
            HideAllPanels();
            _rafflePanel.OnOpened(_duration);
        }

        public void ShowCamp()
        {
            HideAllPanels();
        }

        private void HideAllPanels()
        {
            _trainingPanel.OnClosed(_duration);
            _rafflePanel.OnClosed(_duration);
            _mapPanel.OnClosed(_duration);
        }
    }
}
