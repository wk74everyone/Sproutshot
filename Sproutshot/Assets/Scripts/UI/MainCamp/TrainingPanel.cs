using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.UI
{
    public class TrainingPanel : MonoBehaviour
    {
        private void Awake()
        {
            // 场景中可能使用 alpha=0 初始隐藏，透明并不会自动禁用点击。
            if (TryGetComponent<CanvasGroup>(out var group) && group.alpha <= 0f)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }
        }

        private void Start()
        {
            FlowerSceneTransition.Instance.Prewarm();
        }

        public void OnEndlessClicked()
        {
            // 临时只进入地图预览，新的对局入口由新框架接管。
            FlowerSceneTransition.Instance.EnterScene("GameScene");
        }

        public void OnOpened(float duration)
        {
            ProceduralAnimation.DoPanelVisibility(transform, true, duration);
        }

        public void OnClosed(float duration)
        {
            ProceduralAnimation.DoPanelVisibility(transform, false, duration);
        }
    }
}
