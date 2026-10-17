using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.MapGen
{
    /// <summary>Continuous themed terrain with bilinear sampling of a hidden square grid.</summary>
    [DisallowMultipleComponent]
    public sealed class GridMapView : MonoBehaviour
    {
        private sealed class CoreVisual
        {
            public SpriteRenderer Renderer;
            public Sprite CoreAliveSprite;
            public float CoreVisualWidth;
        }

        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int SecondaryTextureId = Shader.PropertyToID("_SecondaryTex");
        private static readonly int FieldTextureId = Shader.PropertyToID("_FieldTex");
        private static readonly int MapSizeId = Shader.PropertyToID("_MapSize");
        private static readonly int GroundSizeId = Shader.PropertyToID("_GroundSize");
        private static readonly int TileSizeId = Shader.PropertyToID("_TileSize");
        private static readonly int PrimaryRectId = Shader.PropertyToID("_PrimaryRect");
        private static readonly int SecondaryRectId = Shader.PropertyToID("_SecondaryRect");
        private readonly Dictionary<int, CoreVisual> coreVisuals = new Dictionary<int, CoreVisual>();
        private Transform generatedRoot;
        private Texture2D fieldTexture;
        private Material groundMaterial;
        private GridMapData map;
        private Bounds bounds;

        public GridMapData Map => map;
        public float CellSize => map == null ? 1.35f : map.CellSize;
        public float ArtWidth => (map == null ? .78f : map.ArtScale) * 2f;
        public Vector3 WorldSpawnPosition => map == null ? transform.position :
            transform.TransformPoint(new Vector3(map.SpawnPosition.X, map.SpawnPosition.Y, 0));

        public void Build(GridMapData value, GridMapRules mapRules)
        {
            if (value == null) throw new System.ArgumentNullException(nameof(value));
            if (!GridMapArtLibrary.LoadMapSprites(value.Theme))
                throw new System.InvalidOperationException("Map theme assets are not ready: " + value.Theme);
            Transform prefabVisual = GridMapArtLibrary.PollutionCorePrefab().transform.Find("Visual");
            if (prefabVisual == null || prefabVisual.GetComponent<SpriteRenderer>()?.sprite == null)
                throw new System.InvalidOperationException("PollutionCore 预制体需要 Visual 子节点及其 SpriteRenderer 图片。");
            Clear();
            map = value;
            bounds = CalculateBounds();
            generatedRoot = new GameObject("Natural Map " + map.Theme).transform;
            generatedRoot.SetParent(transform, false);
            BuildGround();
            Transform decals = CreateLayerRoot("Ground Decals"), objects = CreateLayerRoot("Scene Objects");
            foreach (MapPlacement placement in map.Placements)
                BuildPlacement(placement, placement.Category == MapObjectCategory.Decal ? decals : objects);
            BuildBoundary();
            UploadTerrainBlend();
            GridMapArtLibrary.RetainOnlyTheme(map.Theme);
        }

        private void BuildGround()
        {
            Sprite primary = GridMapArtLibrary.Ground(map.Theme, 0), secondary = GridMapArtLibrary.Ground(map.Theme, 1);
            if (primary == null || secondary == null) return;
            var item = new GameObject("Continuous Ground");
            item.transform.SetParent(generatedRoot, false);
            item.transform.localPosition = bounds.center;
            var ground = item.AddComponent<SpriteRenderer>();
            ground.sprite = primary;
            // Continue the painted ground past the playable edge while a following camera approaches it.
            // The following player can reach any edge, including on very tall phone screens.
            float margin = ArtWidth * 16f;
            Vector2 groundSize = new Vector2(bounds.size.x + margin * 2, bounds.size.y + margin * 2);
            Vector2 tileSize = primary.bounds.size;
            // One quad keeps the map field continuous; texture repetition is handled by the shader.
            item.transform.localScale = new Vector3(groundSize.x / tileSize.x, groundSize.y / tileSize.y, 1);
            ground.sortingOrder = -2000;
            groundMaterial = new Material(GridMapArtLibrary.GroundMaterial()) { name = "Map Ground " + map.Theme };
            ground.sharedMaterial = groundMaterial;
            int width = Mathf.Clamp(map.Width * 8, 64, 256), height = Mathf.Clamp(map.Height * 8, 64, 384);
            fieldTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "Map Terrain Blend Field",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            groundMaterial.SetTexture(MainTextureId, primary.texture);
            groundMaterial.SetTexture(SecondaryTextureId, secondary.texture);
            groundMaterial.SetTexture(FieldTextureId, fieldTexture);
            groundMaterial.SetVector(MapSizeId, new Vector4(bounds.size.x, bounds.size.y, 0, 0));
            groundMaterial.SetVector(GroundSizeId, new Vector4(groundSize.x, groundSize.y, 0, 0));
            groundMaterial.SetVector(TileSizeId, new Vector4(tileSize.x, tileSize.y, 0, 0));
            groundMaterial.SetVector(PrimaryRectId, TextureRect(primary));
            groundMaterial.SetVector(SecondaryRectId, TextureRect(secondary));
        }

        private static Vector4 TextureRect(Sprite sprite)
        {
            Rect rect = sprite.textureRect;
            return new Vector4(rect.x / sprite.texture.width, rect.y / sprite.texture.height,
                rect.width / sprite.texture.width, rect.height / sprite.texture.height);
        }

        private void BuildPlacement(MapPlacement placement, Transform parent)
        {
            if (placement.Category == MapObjectCategory.Core)
            {
                BuildCore(placement, parent);
                return;
            }
            Sprite sprite = ResolveSprite(placement);
            if (sprite == null) return;
            var item = new GameObject(placement.Category + " " + placement.Id);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = new Vector3(placement.Position.X, placement.Position.Y, 0);
            bool decal = placement.Category == MapObjectCategory.Decal;
            var renderer = CreateRenderer(item.transform, "Visual", decal ? -1900 : SortingOrderAtLocalY(placement.Position.Y));
            renderer.sprite = sprite;
            FitWidth(renderer, placement.Width);
            renderer.flipX = placement.FlipX;
            renderer.transform.localRotation = Quaternion.Euler(0, 0, placement.RotationDegrees);
            // Sprite child scaling does not change the footprint collider's world radius.
            if (!decal) renderer.transform.localPosition = Vector3.up *
                (sprite.bounds.size.y * renderer.transform.localScale.y * .2f);
            if (placement.BlocksMovement)
                item.AddComponent<CircleCollider2D>().radius = placement.FootprintRadius;
            if (!decal)
            {
                Sprite shadow = GridMapArtLibrary.Shared("shared_shadow_ellipse_soft_01_256");
                if (shadow != null)
                {
                    var shadowRenderer = CreateRenderer(item.transform, "Foot Shadow", -1800);
                    shadowRenderer.sprite = shadow;
                    FitWidth(shadowRenderer, placement.FootprintRadius * 2.5f);
                }
            }
        }

        private void BuildCore(MapPlacement placement, Transform parent)
        {
            GameObject prefab = GridMapArtLibrary.PollutionCorePrefab();
            Vector3 localPosition = new Vector3(placement.Position.X, placement.Position.Y, 0);
            GameObject item = Instantiate(prefab, parent.TransformPoint(localPosition),
                parent.rotation * prefab.transform.localRotation, parent);
            item.name = "PollutionCore " + placement.Id;
            item.transform.localPosition = localPosition;
            var renderer = item.transform.Find("Visual").GetComponent<SpriteRenderer>();
            renderer.sortingOrder = SortingOrderAtLocalY(placement.Position.Y);
            // Reuse the authored collider, keeping it consistent with the navigation footprint.
            var collider = item.GetComponent<CircleCollider2D>();
            if (collider != null)
            {
                float scale = Mathf.Max(.001f, Mathf.Max(Mathf.Abs(item.transform.localScale.x), Mathf.Abs(item.transform.localScale.y)));
                collider.radius = placement.FootprintRadius / scale;
            }
            var visual = new CoreVisual { Renderer = renderer,
                CoreAliveSprite = renderer.sprite,
                CoreVisualWidth = renderer.sprite.bounds.size.x * Mathf.Abs(renderer.transform.localScale.x) };
            coreVisuals[placement.Id] = visual;
            if (placement.CoreState != MapCoreState.Alive)
            {
                renderer.sprite = GridMapArtLibrary.Core(map.Theme, placement.CoreState);
                FitWidth(renderer, visual.CoreVisualWidth);
            }
        }

        private Sprite ResolveSprite(MapPlacement placement)
        {
            switch (placement.Category)
            {
                case MapObjectCategory.Decal: return GridMapArtLibrary.Decal(map.Theme, placement.VariantIndex);
                case MapObjectCategory.Obstacle: return GridMapArtLibrary.Obstacle(map.Theme, placement.VariantIndex);
                case MapObjectCategory.Decoration: return GridMapArtLibrary.Decoration(map.Theme, placement.VariantIndex);
                case MapObjectCategory.Landmark: return GridMapArtLibrary.Landmark(map.Theme, placement.VariantIndex);
                default: return null;
            }
        }

        private void BuildBoundary()
        {
            Transform parent = CreateLayerRoot("Movement Boundary");
            float t = Mathf.Max(.5f, ArtWidth);
            AddBoundary(parent, new Vector2(bounds.min.x - t * .5f, bounds.center.y), new Vector2(t, bounds.size.y + t * 2));
            AddBoundary(parent, new Vector2(bounds.max.x + t * .5f, bounds.center.y), new Vector2(t, bounds.size.y + t * 2));
            AddBoundary(parent, new Vector2(bounds.center.x, bounds.min.y - t * .5f), new Vector2(bounds.size.x, t));
            AddBoundary(parent, new Vector2(bounds.center.x, bounds.max.y + t * .5f), new Vector2(bounds.size.x, t));
        }

        private static void AddBoundary(Transform parent, Vector2 position, Vector2 size)
        {
            var edge = new GameObject("Map Edge");
            edge.transform.SetParent(parent, false);
            edge.transform.localPosition = position;
            edge.AddComponent<BoxCollider2D>().size = size;
        }

        /// <summary>Visual state only. The future combat framework owns damage, spawning and victory.</summary>
        public bool SetCoreState(int coreIndex, MapCoreState state)
        {
            if (map == null || coreIndex < 0 || coreIndex >= map.Cores.Count) return false;
            MapPlacement core = map.Cores[coreIndex];
            core.CoreState = state;
            if (coreVisuals.TryGetValue(core.Id, out CoreVisual visual))
            {
                visual.Renderer.sprite = state == MapCoreState.Alive ? visual.CoreAliveSprite : GridMapArtLibrary.Core(map.Theme, state);
                FitWidth(visual.Renderer, visual.CoreVisualWidth);
            }
            return true;
        }

        public Vector3 CellToLocal(GridCoord coord)
        {
            if (map == null) return Vector3.zero;
            MapPoint point = map.CellToLocal(coord);
            return new Vector3(point.X, point.Y, 0);
        }

        public bool TryGetCellAtWorldPosition(Vector3 worldPosition, out GridCoord coord)
        {
            coord = default;
            if (map == null) return false;
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            if (local.x < bounds.min.x || local.x > bounds.max.x || local.y < bounds.min.y || local.y > bounds.max.y) return false;
            coord = map.NearestCoordinate(new MapPoint(local.x, local.y));
            return map.Contains(coord);
        }

        public bool IsWalkableWorldPosition(Vector3 worldPosition, float clearance = 0)
        {
            if (map == null) return false;
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            float scale = Mathf.Max(.001f, Mathf.Min(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)));
            return map.IsWalkable(new MapPoint(local.x, local.y), Mathf.Max(0, clearance) / scale);
        }

        public int SortingOrderForWorldPosition(Vector3 worldPosition) => SortingOrderAtLocalY(transform.InverseTransformPoint(worldPosition).y);
        private int SortingOrderAtLocalY(float y) => 1000 - Mathf.RoundToInt(y / ArtWidth * 18f);
        public Bounds LocalBounds() => map == null ? new Bounds(Vector3.zero, Vector3.one) : bounds;

        private Bounds CalculateBounds()
        {
            Vector3 min = new Vector3(map.MinX, map.MinY, 0);
            Vector3 max = new Vector3(map.MaxX, map.MaxY, 0);
            var result = new Bounds(); result.SetMinMax(min, max); return result;
        }

        private void UploadTerrainBlend()
        {
            if (map == null || fieldTexture == null) return;
            int width = fieldTexture.width, height = fieldTexture.height;
            var fieldPixels = new Color32[width * height];
            for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
            {
                float x = bounds.min.x + (col + .5f) / width * bounds.size.x;
                float y = bounds.min.y + (row + .5f) / height * bounds.size.y;
                float blend = map.SampleGroundBlend(new MapPoint(x, y));
                fieldPixels[row * width + col] = new Color32((byte)Mathf.RoundToInt(blend * 255), 0, 0, 255);
            }
            fieldTexture.SetPixels32(fieldPixels);
            fieldTexture.Apply(false, true);
        }

        private Transform CreateLayerRoot(string label)
        {
            var layer = new GameObject(label).transform; layer.SetParent(generatedRoot, false); return layer;
        }

        private static SpriteRenderer CreateRenderer(Transform parent, string label, int order)
        {
            var child = new GameObject(label); child.transform.SetParent(parent, false);
            var renderer = child.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = order;
            renderer.sharedMaterial = GridMapArtLibrary.SpriteMaterial();
            return renderer;
        }

        private static void FitWidth(SpriteRenderer renderer, float width)
        {
            if (renderer.sprite != null) renderer.transform.localScale = Vector3.one *
                (Mathf.Max(.01f, width) / Mathf.Max(.001f, renderer.sprite.bounds.size.x));
        }

        public void Clear()
        {
            coreVisuals.Clear();
            if (generatedRoot != null)
            {
                generatedRoot.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(generatedRoot.gameObject); else DestroyImmediate(generatedRoot.gameObject);
            }
            if (fieldTexture != null)
            {
                if (Application.isPlaying) Destroy(fieldTexture); else DestroyImmediate(fieldTexture);
            }
            if (groundMaterial != null)
            {
                if (Application.isPlaying) Destroy(groundMaterial); else DestroyImmediate(groundMaterial);
            }
            generatedRoot = null; fieldTexture = null; groundMaterial = null; map = null;
        }

        private void OnDestroy() => Clear();
    }
}
