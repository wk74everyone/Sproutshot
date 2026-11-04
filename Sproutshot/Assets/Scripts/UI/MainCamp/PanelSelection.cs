using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.UI
{
    public class PanelSelection : MonoBehaviour
    {
        [SerializeField, Range(0.12f, 0.6f)] private float _duration = 0.28f;
        [SerializeField, Range(0f, 100f)] private float _slideDistance = 44f;
        [SerializeField] private TrainingPanel _trainingPanel;
        [SerializeField] private SproutRafflePanel _rafflePanel;
        [SerializeField] private MapPanel _mapPanel;
        [SerializeField] private CultivatePanelView _cultivatePanel;

        // 与底部导航的位置一致；营地使用各页面后面的共享背景。
        private enum Tab { Camp = 0, Cultivate = 1, Map = 2, Spirit = 3, Training = 4 }
        private Tab _current;

        private void Awake()
        {
            _current = Tab.Camp;
            if (IsVisible(_trainingPanel)) _current = Tab.Training;
            if (IsVisible(_rafflePanel)) _current = Tab.Spirit;
            if (IsVisible(_mapPanel)) _current = Tab.Map;
            if (IsVisible(_cultivatePanel)) _current = Tab.Cultivate;
            InitializePanel(_trainingPanel, _current == Tab.Training);
            InitializePanel(_rafflePanel, _current == Tab.Spirit);
            InitializePanel(_mapPanel, _current == Tab.Map);
            InitializePanel(_cultivatePanel, _current == Tab.Cultivate);
            if (_current == Tab.Cultivate) _cultivatePanel.OnOpened(0f);
        }

        private static bool IsVisible(Component panel) => panel != null && panel.gameObject.activeSelf &&
            (!panel.TryGetComponent<CanvasGroup>(out var group) || group.alpha > .001f);

        private static void InitializePanel(Component panel, bool visible)
        {
            if (panel != null) ProceduralAnimation.DoPanelVisibility(panel.transform, visible, 0f);
        }

        public void ShowTrainingPanel()
        {
            SwitchTo(Tab.Training);
        }

        public void ShowMapPanel()
        {
            SwitchTo(Tab.Map);
        }

        public void ShowRafflePanel()
        {
            SwitchTo(Tab.Spirit);
        }

        public void ShowCamp()
        {
            SwitchTo(Tab.Camp);
        }

        public void ShowCultivatePanel()
        {
            SwitchTo(Tab.Cultivate);
        }

        private void SwitchTo(Tab target)
        {
            if (_current == target) return;
            float direction = (int)target > (int)_current ? 1f : -1f;
            Vector2 offset = Vector2.right * (_slideDistance * direction);
            // 只关闭实际离开的页面，避免重复点当前 tab 时先关再开。
            switch (_current)
            {
                case Tab.Training: _trainingPanel?.OnClosed(_duration, offset); break;
                case Tab.Spirit: _rafflePanel?.OnClosed(_duration, offset); break;
                case Tab.Map: _mapPanel?.OnClosed(_duration, offset); break;
                case Tab.Cultivate: _cultivatePanel?.OnClosed(_duration, offset); break;
            }
            _current = target;
            switch (target)
            {
                case Tab.Training: _trainingPanel?.OnOpened(_duration, offset); break;
                case Tab.Spirit: _rafflePanel?.OnOpened(_duration, offset); break;
                case Tab.Map: _mapPanel?.OnOpened(_duration, offset); break;
                case Tab.Cultivate: _cultivatePanel?.OnOpened(_duration, offset); break;
            }
        }
    }
}
