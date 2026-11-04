using DG.Tweening;
using System;
using UnityEngine;

namespace Sproutshot.Utils
{
    public static class ProceduralAnimation
    {
        public static void DoPop(Transform panel, bool isEndCurrentTween)
        {
            if (isEndCurrentTween)
            {
                DOTween.Kill(panel);
            }
        }

        public static void DoPanelRotation(Transform panel, bool isEndCurrentTween, Vector3 rotateValue, float duration)
        {
            if (isEndCurrentTween)
            {
                DOTween.Kill(panel);
            }
            panel.DORotate(rotateValue, duration);
        }

        public static void DoUIAlpha(Transform panel, bool isEndCurrentTween, float targetValue, float duration, Action callback = null)
        {
            if (panel.TryGetComponent<CanvasGroup>(out var cg))
            {
                // DOFade 的 target 是 CanvasGroup，不是 Transform。
                if (isEndCurrentTween)
                    cg.DOKill();

                cg.DOFade(targetValue, duration)
                    .SetUpdate(true)
                    .SetLink(panel.gameObject, LinkBehaviour.KillOnDisable)
                    .OnComplete(() => callback?.Invoke());
            }
        }

        /// <summary>面板只做淡入淡出和短距离平移，不旋转；连续切换从当前画面衔接。</summary>
        public static void DoPanelVisibility(Transform panel, bool visible, float duration,
            Vector2? slideOffset = null)
        {
            if (panel == null) return;
            if (!panel.TryGetComponent<PanelVisibilityTransition>(out var transition))
                transition = panel.gameObject.AddComponent<PanelVisibilityTransition>();
            transition.SetVisible(visible, duration, slideOffset ?? new Vector2(0f, 28f));
        }

        public static void ShakeIt(Transform panel, bool isEndCurrentTween, float duration, Vector3 strenth)
        {
            if (isEndCurrentTween)
            {
                DOTween.Kill(panel);
            }
            panel.DOShakeRotation(duration, strenth);
        }

        public static void ShakePosition(Transform panel, bool isEndCurrentTween, float duration, Vector3 strenth)
        {
            if (isEndCurrentTween)
            {
                DOTween.Kill(panel);
            }
            panel.DOShakePosition(duration, strenth);
        }
    }
}
