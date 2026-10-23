using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    // Nine slices with independently sized vertical/horizontal borders, matching the design.
    [AddComponentMenu("UI/Card Raffle Frame")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class CardRaffleFrame : MaskableGraphic
    {
        public Texture2D frameTexture;
        public Vector2 sourceBorder = new Vector2(72, 56);
        public Vector2 displayBorder = new Vector2(54, 60.75f);
        public override Texture mainTexture => frameTexture != null ? frameTexture : Texture2D.whiteTexture;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (frameTexture == null) return;
            Rect r = GetPixelAdjustedRect();
            float bx = Mathf.Min(displayBorder.x, r.width * .5f);
            float by = Mathf.Min(displayBorder.y, r.height * .5f);
            float[] xs = { r.xMin, r.xMin + bx, r.xMax - bx, r.xMax };
            float[] ys = { r.yMin, r.yMin + by, r.yMax - by, r.yMax };
            float[] us = { 0, sourceBorder.x / frameTexture.width, 1 - sourceBorder.x / frameTexture.width, 1 };
            float[] vs = { 0, sourceBorder.y / frameTexture.height, 1 - sourceBorder.y / frameTexture.height, 1 };
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++) vh.AddVert(new Vector2(xs[x], ys[y]), color, new Vector2(us[x], vs[y]));
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 3; x++)
                {
                    int i = y * 4 + x;
                    vh.AddTriangle(i, i + 4, i + 1);
                    vh.AddTriangle(i + 1, i + 4, i + 5);
                }
        }
    }
}
