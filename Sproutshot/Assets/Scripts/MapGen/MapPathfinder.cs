using System;
using System.Collections.Generic;

namespace Sproutshot.MapGen
{
    /// <summary>Continuous click destinations with an eight-way search grid hidden from the player.</summary>
    public static class MapPathfinder
    {
        /// <summary>
        /// Fills result with local map-space waypoints, excluding start. Out-of-bounds clicks are
        /// clamped; blocked clicks stop at the nearest reachable side of the obstacle. A walkable
        /// destination in a disconnected area fails instead of moving to an unrelated location.
        /// </summary>
        public static bool TryFindPath(GridMapData map, MapPoint start, MapPoint target, float clearance,
            List<MapPoint> result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            result.Clear();
            if (map == null || !Finite(start) || !Finite(target) || !Finite(clearance) || clearance < 0 ||
                !map.IsWalkable(start, clearance)) return false;
            float minX = map.MinX + clearance, maxX = map.MaxX - clearance;
            float minY = map.MinY + clearance, maxY = map.MaxY - clearance;
            if (minX > maxX || minY > maxY) return false;
            target = new MapPoint(Clamp(target.X, minX, maxX), Clamp(target.Y, minY, maxY));
            if (CanTraverse(map, start, target, clearance)) { result.Add(target); return true; }

            MapNavigationGrid grid = map.GetNavigationGrid(clearance);
            // Search state belongs to this request, never to the shared navigation grid.
            var closed = new bool[grid.Count];
            var costs = new float[grid.Count];
            var parents = new int[grid.Count];
            for (int i = 0; i < grid.Count; i++) { costs[i] = float.MaxValue; parents[i] = -1; }
            bool exactTarget = map.IsWalkable(target, clearance);
            float nearestPossible = float.MaxValue;
            if (!exactTarget)
                for (int i = 0; i < grid.Count; i++)
                    if (grid.Walkable[i]) nearestPossible = Math.Min(nearestPossible,
                        MapPoint.DistanceSquared(grid.Point(i), target));
            float projectedGoalDistance = (float)Math.Sqrt(nearestPossible) + grid.Step * .65f;

            // Connect the arbitrary start position to nearby graph nodes with the same swept
            // collision test used for all edges. No snapping through a nearby tree or wall.
            var open = new MinHeap();
            int startCol = grid.Column(start.X), startRow = grid.Row(start.Y);
            for (int y = Math.Max(0, startRow - 3); y <= Math.Min(grid.Rows - 1, startRow + 3); y++)
            for (int x = Math.Max(0, startCol - 3); x <= Math.Min(grid.Columns - 1, startCol + 3); x++)
            {
                int i = y * grid.Columns + x;
                MapPoint point = grid.Point(i);
                if (!grid.Walkable[i] || !CanTraverse(map, start, point, clearance)) continue;
                costs[i] = Distance(start, point);
                open.Add(i, costs[i], costs[i] + Distance(point, target));
            }

            int best = -1;
            float bestDistance = float.MaxValue;
            while (open.Count > 0)
            {
                HeapEntry entry = open.Pop();
                int current = entry.Index;
                if (closed[current] || entry.Cost > costs[current]) continue;
                closed[current] = true;
                MapPoint point = grid.Point(current);
                float distance = MapPoint.DistanceSquared(point, target);
                if (distance < bestDistance) { best = current; bestDistance = distance; }
                if (exactTarget && CanTraverse(map, point, target, clearance))
                    return BuildResult(grid, parents, current, start, target, map, clearance, result);
                if (!exactTarget && distance <= projectedGoalDistance * projectedGoalDistance)
                    return BuildResult(grid, parents, current, start, ApproachTarget(map, point, target, clearance),
                        map, clearance, result);

                int col = current % grid.Columns, row = current / grid.Columns;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int x = col + dx, y = row + dy;
                    if (x < 0 || x >= grid.Columns || y < 0 || y >= grid.Rows) continue;
                    int next = y * grid.Columns + x;
                    if (!grid.Walkable[next] || closed[next]) continue;
                    MapPoint nextPoint = grid.Point(next);
                    float cost = costs[current] + Distance(point, nextPoint);
                    if (cost >= costs[next] || !CanTraverse(map, point, nextPoint, clearance)) continue;
                    parents[next] = current;
                    costs[next] = cost;
                    open.Add(next, cost, cost + Distance(nextPoint, target));
                }
            }
            // The closest side can itself be separated by a wall. If the click was on an
            // obstacle, use the closest point in the component actually reachable from start.
            return !exactTarget && best >= 0 && BuildResult(grid, parents, best, start,
                ApproachTarget(map, grid.Point(best), target, clearance), map, clearance, result);
        }

        /// <summary>Tests the entire swept circle, including map edges and every blocking footprint.</summary>
        public static bool CanTraverse(GridMapData map, MapPoint from, MapPoint to, float clearance)
        {
            if (map == null || !Finite(from) || !Finite(to) || !Finite(clearance) || clearance < 0 ||
                !map.ContainsPoint(from, clearance) || !map.ContainsPoint(to, clearance)) return false;
            foreach (MapPlacement placement in map.Placements)
            {
                if (!placement.BlocksMovement) continue;
                float radius = Math.Max(0, placement.FootprintRadius) + clearance;
                // Cheap broad phase keeps long maps with many small props inexpensive.
                if (placement.Position.X < Math.Min(from.X, to.X) - radius ||
                    placement.Position.X > Math.Max(from.X, to.X) + radius ||
                    placement.Position.Y < Math.Min(from.Y, to.Y) - radius ||
                    placement.Position.Y > Math.Max(from.Y, to.Y) + radius) continue;
                // Use double precision and only collapse truly zero-length segments. Movement
                // finishes with tiny segments, so treating a short segment as its first endpoint
                // would allow the second endpoint to creep into the footprint.
                double dx = (double)to.X - from.X, dy = (double)to.Y - from.Y;
                double length = dx * dx + dy * dy;
                double t = length == 0 ? 0 : Math.Max(0, Math.Min(1,
                    (((double)placement.Position.X - from.X) * dx + ((double)placement.Position.Y - from.Y) * dy) / length));
                double ox = from.X + t * dx - placement.Position.X, oy = from.Y + t * dy - placement.Position.Y;
                if (ox * ox + oy * oy < (double)radius * radius)
                    return false;
            }
            return true;
        }

        private static bool BuildResult(MapNavigationGrid grid, int[] parents, int end, MapPoint start, MapPoint target,
            GridMapData map, float clearance, List<MapPoint> result)
        {
            var reverse = new List<MapPoint>();
            for (int i = end; i >= 0; i = parents[i]) reverse.Add(grid.Point(i));
            reverse.Reverse();
            reverse.Add(target);
            MapPoint anchor = start;
            int next = 0;
            while (next < reverse.Count)
            {
                int farthest = reverse.Count - 1;
                while (farthest > next && !CanTraverse(map, anchor, reverse[farthest], clearance)) farthest--;
                if (!CanTraverse(map, anchor, reverse[farthest], clearance)) { result.Clear(); return false; }
                anchor = reverse[farthest];
                if (result.Count == 0 || MapPoint.DistanceSquared(result[result.Count - 1], anchor) > .000001f)
                    result.Add(anchor);
                next = farthest + 1;
            }
            return result.Count > 0;
        }

        private static MapPoint ApproachTarget(GridMapData map, MapPoint from, MapPoint target, float clearance)
        {
            float low = 0, high = 1;
            for (int i = 0; i < 24; i++)
            {
                float amount = (low + high) * .5f;
                var point = new MapPoint(from.X + (target.X - from.X) * amount,
                    from.Y + (target.Y - from.Y) * amount);
                if (CanTraverse(map, from, point, clearance)) low = amount; else high = amount;
            }
            // A tiny separation prevents floating-point rounding from putting the endpoint
            // microscopically inside a physical collider at the end of the movement.
            low = Math.Max(0, low - .0001f);
            return new MapPoint(from.X + (target.X - from.X) * low, from.Y + (target.Y - from.Y) * low);
        }

        private static float Distance(MapPoint a, MapPoint b) => (float)Math.Sqrt(MapPoint.DistanceSquared(a, b));
        private static bool Finite(MapPoint p) => Finite(p.X) && Finite(p.Y);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));

        private readonly struct HeapEntry
        {
            public readonly int Index;
            public readonly float Cost, Priority;
            public HeapEntry(int index, float cost, float priority) { Index = index; Cost = cost; Priority = priority; }
        }
        private sealed class MinHeap
        {
            private readonly List<HeapEntry> entries = new List<HeapEntry>();
            public int Count => entries.Count;
            public void Add(int index, float cost, float priority)
            {
                var entry = new HeapEntry(index, cost, priority);
                int child = entries.Count; entries.Add(entry);
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (!Earlier(entry, entries[parent])) break;
                    entries[child] = entries[parent]; child = parent;
                }
                entries[child] = entry;
            }
            public HeapEntry Pop()
            {
                HeapEntry first = entries[0], last = entries[entries.Count - 1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0) return first;
                int parent = 0;
                while (parent * 2 + 1 < entries.Count)
                {
                    int child = parent * 2 + 1;
                    if (child + 1 < entries.Count && Earlier(entries[child + 1], entries[child])) child++;
                    if (!Earlier(entries[child], last)) break;
                    entries[parent] = entries[child]; parent = child;
                }
                entries[parent] = last;
                return first;
            }
            private static bool Earlier(HeapEntry a, HeapEntry b) =>
                a.Priority < b.Priority || (a.Priority == b.Priority && a.Index < b.Index);
        }
    }
}
