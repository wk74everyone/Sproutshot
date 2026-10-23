using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    // Small vector decorations keep the reference's clean outlines at every resolution.
    [AddComponentMenu("UI/Card Raffle Shape")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CardRaffleShape : MaskableGraphic
    {
        public float radius = 16f;
        public float strokeWidth = 2f;
        public Color strokeColor = new Color(.043f, .188f, .2f, 1f);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            const int segments = 12;
            const int count = (segments + 1) * 4;
            float rad = Mathf.Min(radius, Mathf.Min(r.width, r.height) * .5f);
            float inset = Mathf.Clamp(strokeWidth, 0, rad);
            for (int i = 0; i < count; i++)
            {
                int corner = i / (segments + 1);
                float angle = (corner * 90f + (i % (segments + 1)) * 90f / segments) * Mathf.Deg2Rad;
                Vector2 center = new Vector2(corner == 0 || corner == 3 ? r.xMax - rad : r.xMin + rad,
                    corner < 2 ? r.yMax - rad : r.yMin + rad);
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                vh.AddVert(center + direction * rad, strokeWidth > 0 ? strokeColor : color, Vector2.zero);
                vh.AddVert(center + direction * (rad - inset), strokeWidth > 0 ? strokeColor : color, Vector2.zero);
                vh.AddVert(center + direction * (rad - inset), color, Vector2.zero);
            }
            vh.AddVert(r.center, color, Vector2.zero);
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                vh.AddTriangle(i * 3, next * 3, i * 3 + 1);
                vh.AddTriangle(next * 3, next * 3 + 1, i * 3 + 1);
                vh.AddTriangle(count * 3, i * 3 + 2, next * 3 + 2);
            }
        }
    }
}
