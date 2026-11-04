using System;
using System.Collections.Generic;
using Sproutshot.Utils;
using UnityEngine;
using UnityEngine.Serialization;

namespace Sproutshot.MapGen
{
    public readonly struct MapGeneratedEvent : IGameEvent
    {
        public readonly GridMapData Map;
        public MapGeneratedEvent(GridMapData map) { Map = map; }
    }

    public enum MapTerrain : byte { Grass, Forest, Water, Sand }
    public enum MapTheme : byte { Forest, Grassland, Wetland, Dryland, Frontier }
    public enum MapObjectCategory : byte { Decal, Obstacle, Decoration, Landmark, Core }
    public enum MapCoreState : byte { Alive, Broken, Restored }

    public readonly struct MapPoint
    {
        public readonly float X, Y;
        public MapPoint(float x, float y) { X = x; Y = y; }
        public static float DistanceSquared(MapPoint a, MapPoint b)
        {
            float x = a.X - b.X, y = a.Y - b.Y;
            return x * x + y * y;
        }
    }

    [Serializable]
    public sealed class GridMapRules
    {
        [Range(7, 31)] public int Width = 10;
        [Range(9, 41)] public int Height = 18;
        public int Seed = 20260922;
        public bool RandomizeSeed;
        [Range(.25f, 3f)] public float CellSize = 1.35f;
        [FormerlySerializedAs("TileRadius"), Range(.35f, 1.2f)] public float ArtScale = .78f;
        public MapTheme Theme;
        [HideInInspector] public int CoreCount;
        [Range(1.5f, 4f)] public float SpawnClearRadius = 2.1f;
        [Range(.8f, 2f)] public float CorridorHalfWidth = 1.15f;
        [Range(0f, 2f)] public float DensityScale = 1f;
        [Range(0f, 2f)] public float ObstacleDensityScale = 1f;
        [Range(0f, 2f)] public float DecorationDensityScale = 1f;
        [Range(0f, 3f)] public float EdgeInset = .75f;
        [Range(.5f, .9f)] public float WalkableGroundCoverage = .70f;

        public void Clamp()
        {
            Width = Math.Max(7, Math.Min(31, Width));
            Height = Math.Max(9, Math.Min(41, Height));
            CellSize = Limit(CellSize, .25f, 3f, 1.35f);
            ArtScale = Limit(ArtScale, .35f, 1.2f, .78f);
            if ((int)Theme > 4) Theme = MapTheme.Forest;
            CoreCount = 0;
            SpawnClearRadius = Limit(SpawnClearRadius, 1.5f, 4f, 2.1f);
            CorridorHalfWidth = Limit(CorridorHalfWidth, .8f, 2f, 1.15f);
            DensityScale = Limit(DensityScale, 0, 2, 1);
            ObstacleDensityScale = Limit(ObstacleDensityScale, 0, 2, 1);
            DecorationDensityScale = Limit(DecorationDensityScale, 0, 2, 1);
            EdgeInset = Limit(EdgeInset, 0, 3, .75f);
            WalkableGroundCoverage = Limit(WalkableGroundCoverage, .5f, .9f, .70f);
        }
        private static float Limit(float v, float lo, float hi, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) ? fallback : Math.Max(lo, Math.Min(hi, v));
    }

    [Serializable]
    public sealed class GridCell
    {
        public MapTerrain Terrain;
        public byte VisualVariant;
        [Range(0f, 1f)] public float SecondaryGroundBlend;
    }

    public sealed class MapPlacement
    {
        public int Id;
        public MapObjectCategory Category;
        public int VariantIndex;
        public MapPoint Position;
        public GridCoord Anchor;
        public float Width, Height, RotationDegrees, FootprintRadius;
        public bool FlipX, BlocksMovement;
        public MapCoreState CoreState;
    }

    /// <summary>Continuous map with equal-sized square cells for terrain samples.</summary>
    public sealed class GridMapData
    {
        public readonly int Width, Height, Seed;
        public readonly GridCoord Center;
        public readonly GridCell[] Cells;
        public readonly MapTheme Theme;
        public readonly float ArtScale, CellSize;
        public readonly MapPoint SpawnPosition;
        public float SpawnClearRadius, CorridorHalfWidth;
        public float BoundaryInset;
        public readonly List<MapPlacement> Placements = new List<MapPlacement>();
        public readonly List<MapPlacement> Cores = new List<MapPlacement>();
        private readonly Dictionary<float, MapNavigationGrid> navigationGrids = new Dictionary<float, MapNavigationGrid>();
        public float MinX => -CellSize * .5f;
        public float MaxX => (Width - .5f) * CellSize;
        public float MinY => -CellSize * .5f;
        public float MaxY => (Height - .5f) * CellSize;
        public float WorldWidth => Width * CellSize;
        public float WorldHeight => Height * CellSize;
        public float Area => WorldWidth * WorldHeight;
        public float WalkableMinX => MinX + BoundaryInset;
        public float WalkableMaxX => MaxX - BoundaryInset;
        public float WalkableMinY => MinY + BoundaryInset;
        public float WalkableMaxY => MaxY - BoundaryInset;

        public GridMapData(int width, int height, int seed) : this(width, height, seed, MapTheme.Forest, 1.35f) { }
        public GridMapData(int width, int height, int seed, MapTheme theme, float cellSize, float artScale = .78f)
        {
            if (width < 1 || height < 1 || width > 1024 || height > 1024) throw new ArgumentOutOfRangeException();
            if (float.IsNaN(cellSize) || float.IsInfinity(cellSize) || cellSize <= 0 ||
                float.IsNaN(artScale) || float.IsInfinity(artScale) || artScale <= 0)
                throw new ArgumentOutOfRangeException("Grid cell size and art scale must be finite and positive.");
            Width = width; Height = height; Seed = seed; Theme = theme;
            ArtScale = artScale; CellSize = cellSize;
            Center = new GridCoord(width / 2, height / 2);
            SpawnPosition = CellToLocal(Center);
            Cells = new GridCell[width * height];
            for (int i = 0; i < Cells.Length; i++) Cells[i] = new GridCell();
        }

        /// <summary>
        /// Returns the shared navigation grid for this clearance, in local map units.
        /// Each exact clearance is built once per map. Rejects invalid radii or actors larger than the map.
        /// </summary>
        public MapNavigationGrid GetNavigationGrid(float clearance = 0)
        {
            if (float.IsNaN(clearance) || float.IsInfinity(clearance) || clearance < 0)
                throw new ArgumentOutOfRangeException(nameof(clearance));
            if (!navigationGrids.TryGetValue(clearance, out MapNavigationGrid grid))
            {
                grid = new MapNavigationGrid(this, clearance);
                navigationGrids.Add(clearance, grid);
            }
            return grid;
        }

        /// <summary>
        /// Call after adding/removing/moving blocking placements or changing their footprints.
        /// Discard previously held grids and flow fields; the next request rebuilds navigation.
        /// Terrain blend changes and actor/target movement do not require invalidation.
        /// </summary>
        public void InvalidateNavigationGrids() => navigationGrids.Clear();

        public bool Contains(GridCoord c) => c.Col >= 0 && c.Row >= 0 && c.Col < Width && c.Row < Height;
        public int IndexOf(GridCoord c) => Contains(c) ? c.Row * Width + c.Col : -1;
        public GridCell Get(GridCoord c) { int i = IndexOf(c); return i < 0 ? null : Cells[i]; }
        public MapPoint CellToLocal(GridCoord c) => new MapPoint(c.Col * CellSize, c.Row * CellSize);

        public GridCoord NearestCoordinate(MapPoint point)
        {
            int col = Math.Max(0, Math.Min(Width - 1, (int)Math.Floor(point.X / CellSize + .5f)));
            int row = Math.Max(0, Math.Min(Height - 1, (int)Math.Floor(point.Y / CellSize + .5f)));
            return new GridCoord(col, row);
        }

        /// <summary>Bilinear field lookup, clamped to the outermost cell centers.</summary>
        public float SampleGroundBlend(MapPoint point)
        {
            float x = Math.Max(0, Math.Min(Width - 1, point.X / CellSize));
            float y = Math.Max(0, Math.Min(Height - 1, point.Y / CellSize));
            int left = (int)Math.Floor(x), bottom = (int)Math.Floor(y);
            int right = Math.Min(left + 1, Width - 1), top = Math.Min(bottom + 1, Height - 1);
            GridCell a = Cells[bottom * Width + left], b = Cells[bottom * Width + right];
            GridCell c = Cells[top * Width + left], d = Cells[top * Width + right];
            float tx = x - left, ty = y - bottom;
            return Bilinear(a.SecondaryGroundBlend, b.SecondaryGroundBlend,
                c.SecondaryGroundBlend, d.SecondaryGroundBlend, tx, ty);
        }

        private static float Bilinear(float a, float b, float c, float d, float x, float y)
        {
            float lower = a + (b - a) * x, upper = c + (d - c) * x;
            return lower + (upper - lower) * y;
        }

        public bool ContainsPoint(MapPoint point, float inset = 0) =>
            point.X >= MinX + inset && point.X <= MaxX - inset && point.Y >= MinY + inset && point.Y <= MaxY - inset;

        public bool IsWalkable(MapPoint point, float clearance = 0)
        {
            if (!ContainsPoint(point, BoundaryInset + clearance)) return false;
            foreach (MapPlacement p in Placements)
                if (p.BlocksMovement && MapPoint.DistanceSquared(point, p.Position) <
                    (p.FootprintRadius + clearance) * (p.FootprintRadius + clearance)) return false;
            return true;
        }

        public bool IsReservedForCombat(MapPoint point, float radius = 0)
        {
            if (MapPoint.DistanceSquared(point, SpawnPosition) < (SpawnClearRadius + radius) * (SpawnClearRadius + radius)) return true;
            // 中央横纵通路不依赖已移除的核心，保证整张地图都有宽阔的穿行方向。
            if (Math.Abs(point.X - SpawnPosition.X) < CorridorHalfWidth + radius ||
                Math.Abs(point.Y - SpawnPosition.Y) < CorridorHalfWidth + radius) return true;
            foreach (MapPlacement core in Cores)
            {
                float combatRadius = core.Width * .72f + radius;
                if (MapPoint.DistanceSquared(point, core.Position) < combatRadius * combatRadius) return true;
                if (DistanceToSegmentSquared(point, SpawnPosition, core.Position) <
                    (CorridorHalfWidth + radius) * (CorridorHalfWidth + radius)) return true;
            }
            return false;
        }

        internal static float DistanceToSegmentSquared(MapPoint p, MapPoint a, MapPoint b)
        {
            float x = b.X - a.X, y = b.Y - a.Y, length = x * x + y * y;
            float t = length <= .00001f ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * x + (p.Y - a.Y) * y) / length));
            return MapPoint.DistanceSquared(p, new MapPoint(a.X + x * t, a.Y + y * t));
        }

        public IEnumerable<GridCoord> Coordinates()
        {
            for (int row = 0; row < Height; row++)
                for (int col = 0; col < Width; col++) yield return new GridCoord(col, row);
        }
        public IEnumerable<GridCoord> Neighbours(GridCoord c)
        {
            for (int i = 0; i < 8; i++) { GridCoord next = c.Neighbour(i); if (Contains(next)) yield return next; }
        }
    }
}
