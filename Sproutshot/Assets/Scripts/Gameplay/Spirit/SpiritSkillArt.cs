using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>复用已有叶片、露滴和星光；霜圈以细描边绘制，只在首次使用时生成。</summary>
    internal static class SpiritSkillArt
    {
        public static Sprite Leaf { get; private set; }
        public static Sprite Spark { get; private set; }
        public static Sprite Drop { get; private set; }
        public static Sprite Ring { get; private set; }
        public static Material Material { get; private set; }
        public static bool Load()
        {
            if (Leaf != null && Spark != null && Drop != null && Ring != null && Material != null) return true;
            var leaves = Resources.Load<Texture2D>("VFX/ThunderAtlas");
            var drops = Resources.Load<Texture2D>("VFX/ElementBurstAtlas");
            Material = Resources.Load<Material>("VFX/ThunderVfx");
            if (leaves == null || drops == null || Material == null) { Debug.LogError("[SpiritSkillArt] 缺少现有特效图集或材质。"); return false; }
            Sprite Slice(Texture2D atlas, string name, float x, float top, float width, float height)
            {
                var sprite = Sprite.Create(atlas, new Rect(x / 1254f * atlas.width, (1254 - top - height) / 1254f * atlas.height,
                    width / 1254f * atlas.width, height / 1254f * atlas.height), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                sprite.name = name; return sprite;
            }
            Leaf = Slice(leaves, "SpiritLeaf", 170, 805, 365, 340);
            Spark = Slice(leaves, "SpiritSpark", 770, 750, 345, 430);
            Drop = Slice(drops, "SpiritDew", 225, 735, 305, 410);
            const int size = 256;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SpiritFrostRing", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float px = (x + .5f) / size * 2 - 1, py = (y + .5f) / size * 2 - 1;
                float angle = Mathf.Atan2(py, px);
                float distance = Mathf.Abs(Mathf.Sqrt(px * px + py * py) - (.89f + .009f * Mathf.Sin(angle * 7) + .006f * Mathf.Sin(angle * 13)));
                float alpha = 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.025f, .043f, distance));
                var color = distance < .023f ? new Color(.68f, .95f, 1) : new Color(.12f, .34f, .39f);
                color.a = alpha * .9f; pixels[y * size + x] = color;
            }
            texture.SetPixels(pixels); texture.Apply(false, true);
            Ring = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            Ring.name = "SpiritFrostRing";
            return true;
        }

        public static void Size(SpriteRenderer renderer, float width, float height)
        {
            var bounds = renderer.sprite.bounds.size; var scale = renderer.transform.parent.lossyScale;
            renderer.transform.localScale = new Vector3(width / bounds.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                height / bounds.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1);
        }
    }
}
