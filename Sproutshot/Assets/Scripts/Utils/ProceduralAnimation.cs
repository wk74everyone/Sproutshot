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

        public static void DoPanelVisibility(Transform panel, bool visible, float duration, bool rotate = true)
        {
            if (panel == null)
                return;

            panel.DOKill();
            if (!panel.TryGetComponent<CanvasGroup>(out var group))
            {
                panel.gameObject.SetActive(visible);
                return;
            }

            group.DOKill(); // 取消旧的关闭回调，避免新面板打开后又被关闭。
            group.interactable = false;

            if (!visible && !panel.gameObject.activeSelf)
            {
                group.alpha = 0f;
                group.blocksRaycasts = false;
                return;
            }

            if (visible && !panel.gameObject.activeSelf)
                group.alpha = 0f; // 启用前隐藏，避免闪烁。

            group.blocksRaycasts = visible || group.alpha > 0f;
            if (visible)
                panel.gameObject.SetActive(true);

            Vector3 rotation = new Vector3(0f, 0f, visible ? 360f : 180f);
            if (duration <= 0f)
            {
                group.alpha = visible ? 1f : 0f;
                group.interactable = visible;
                group.blocksRaycasts = visible;
                if (rotate)
                    panel.localEulerAngles = rotation;
                panel.gameObject.SetActive(visible);
                return;
            }

            if (rotate)
            {
                panel.DORotate(rotation, duration)
                    .SetUpdate(true)
                    .SetLink(panel.gameObject, LinkBehaviour.KillOnDisable);
            }

            DoUIAlpha(panel, true, visible ? 1f : 0f, duration, () =>
            {
                group.interactable = visible;
                group.blocksRaycasts = visible;
                if (!visible)
                    panel.gameObject.SetActive(false);
            });
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
