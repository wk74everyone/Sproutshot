using UnityEngine;

namespace Sproutshot.UI
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(RectTransform))]
    public sealed class CardRaffleFit : MonoBehaviour
    {
        private void OnEnable() => Fit();
        private void OnRectTransformDimensionsChange() => Fit();
        private void OnTransformParentChanged() => Fit();
        private void Fit()
        {
            var rect = (RectTransform)transform;
            var parent = rect.parent as RectTransform;
            if (parent == null || parent.rect.width <= 0 || parent.rect.height <= 0) return;
            float scale = Mathf.Min(parent.rect.width / 1080f, parent.rect.height / 1920f);
            rect.localScale = new Vector3(scale, scale, 1);
        }
    }
}
