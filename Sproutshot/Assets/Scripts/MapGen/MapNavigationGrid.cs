using System;
using System.Collections.Generic;

namespace Sproutshot.MapGen
{
    /// <summary>
    /// Shared square navigation samples in local map space, for one actor clearance.
    /// Contains no per-search state. Obtain cached instances from GridMapData.GetNavigationGrid.
    /// </summary>
    public sealed class MapNavigationGrid
    {
        public int Columns { get; }
        public int Rows { get; }
        public int Count { get; }
        public float Step { get; }
        public float Clearance { get; }
        public IReadOnlyList<bool> Walkable { get; }
        private readonly float minX, minY;

        internal MapNavigationGrid(GridMapData map, float clearance)
        {
            if (float.IsNaN(clearance) || float.IsInfinity(clearance) || clearance < 0)
                throw new ArgumentOutOfRangeException(nameof(clearance));
            float x0 = map.MinX + clearance, x1 = map.MaxX - clearance;
            float y0 = map.MinY + clearance, y1 = map.MaxY - clearance;
            if (x0 > x1 || y0 > y1)
                throw new ArgumentOutOfRangeException(nameof(clearance), "Actor does not fit inside the map.");
            Clearance = clearance;
            float desiredStep = Math.Max(.12f, map.ArtScale * .35f);
            // Bound allocation even for maps supplied outside the generator.
            Step = Math.Max(desiredStep, Math.Max(x1 - x0, y1 - y0) / 255f);
            Columns = Math.Max(1, (int)Math.Floor((x1 - x0) / Step) + 1);
            Rows = Math.Max(1, (int)Math.Floor((y1 - y0) / Step) + 1);
            Count = Columns * Rows;
            minX = x0 + ((x1 - x0) - (Columns - 1) * Step) * .5f;
            minY = y0 + ((y1 - y0) - (Rows - 1) * Step) * .5f;
            var walkable = new bool[Count];
            for (int i = 0; i < Count; i++) walkable[i] = true;
            foreach (MapPlacement placement in map.Placements)
            {
                if (!placement.BlocksMovement) continue;
                float radius = Math.Max(0, placement.FootprintRadius) + clearance;
                int left = Column(placement.Position.X - radius), right = Column(placement.Position.X + radius);
                int bottom = Row(placement.Position.Y - radius), top = Row(placement.Position.Y + radius);
                for (int y = Math.Max(0, bottom - 1); y <= Math.Min(Rows - 1, top + 1); y++)
                for (int x = Math.Max(0, left - 1); x <= Math.Min(Columns - 1, right + 1); x++)
                {
                    int i = y * Columns + x;
                    if (MapPoint.DistanceSquared(Point(i), placement.Position) < radius * radius)
                        walkable[i] = false;
                }
            }
            Walkable = Array.AsReadOnly(walkable);
        }

        /// <summary>Node center in local map units; indices are row * Columns + column.</summary>
        public MapPoint Point(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            return new MapPoint(minX + index % Columns * Step, minY + index / Columns * Step);
        }

        /// <summary>Nearest column, clamped to the grid bounds.</summary>
        public int Column(float x) => Math.Max(0, Math.Min(Columns - 1, (int)Math.Round((x - minX) / Step)));
        /// <summary>Nearest row, clamped to the grid bounds.</summary>
        public int Row(float y) => Math.Max(0, Math.Min(Rows - 1, (int)Math.Round((y - minY) / Step)));
        public int NearestIndex(MapPoint point) => Row(point.Y) * Columns + Column(point.X);

        /// <summary>
        /// In-bounds eight-way neighbours, including blocked nodes. Before connecting nodes,
        /// check Walkable and MapPathfinder.CanTraverse(map, Point(from), Point(to), Clearance).
        /// Clear endpoints alone do not guarantee that the connecting segment is clear.
        /// </summary>
        public IEnumerable<int> Neighbours(int index)
        {
            if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
            int col = index % Columns, row = index / Columns;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = col + dx, y = row + dy;
                if (x >= 0 && x < Columns && y >= 0 && y < Rows)
                    yield return y * Columns + x;
            }
        }
    }
}
