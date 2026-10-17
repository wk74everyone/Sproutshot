using System;

namespace Sproutshot.MapGen
{
    /// <summary>Deterministic five-theme layouts built around clear combat corridors, not visible tiles.</summary>
    internal static class GridMapBuilder
    {
        public static GridMapData Generate(GridMapRules rules, int seed)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            rules.Clamp();
            var map = new GridMapData(rules.Width, rules.Height, seed, rules.Theme, rules.CellSize, rules.ArtScale)
            {
                SpawnClearRadius = rules.SpawnClearRadius * rules.ArtScale,
                CorridorHalfWidth = rules.CorridorHalfWidth * rules.ArtScale
            };
            var random = new MapRandom(seed);
            var noise = new SeededNoise(seed);
            BuildCores(map, rules, random);
            BuildTerrain(map, noise);
            BuildPathDecals(map, random);
            // Keep object counts independent of the terrain sampling resolution.
            // Reference area matches the authored art scale and existing combat density.
            float densityUnits = map.Area / (2.75f * map.ArtScale * map.ArtScale);
            PlaceScattered(map, random, MapObjectCategory.Landmark, rules.DensityScale <= 0 ? 0 : 2, true);
            PlaceScattered(map, random, MapObjectCategory.Obstacle, (int)(densityUnits * .045f * rules.DensityScale), true);
            PlaceScattered(map, random, MapObjectCategory.Decal, (int)(densityUnits * .035f * rules.DensityScale), false);
            PlaceScattered(map, random, MapObjectCategory.Decoration, (int)(densityUnits * .15f * rules.DensityScale), false);
            return map;
        }

        private static void BuildCores(GridMapData map, GridMapRules rules, MapRandom random)
        {
            // Opposite quadrants create distinct approach routes from the central clearing.
            float[,] positions = { { .22f, .23f }, { .78f, .77f }, { .78f, .23f }, { .22f, .77f } };
            for (int i = 0; i < rules.CoreCount; i++)
            {
                float x = positions[i, 0] + (float)(random.Unit() - .5) * .045f;
                float y = positions[i, 1] + (float)(random.Unit() - .5) * .045f;
                var point = new MapPoint(map.MinX + (map.MaxX - map.MinX) * x, map.MinY + (map.MaxY - map.MinY) * y);
                var core = NewPlacement(map, MapObjectCategory.Core, 0, point, 2.8f * map.ArtScale,
                    .65f * map.ArtScale, true);
                core.CoreState = MapCoreState.Alive;
                map.Cores.Add(core);
                map.Placements.Add(core);
            }
        }

        private static void BuildTerrain(GridMapData map, SeededNoise noise)
        {
            foreach (GridCoord coord in map.Coordinates())
            {
                GridCell cell = map.Get(coord);
                MapPoint point = map.CellToLocal(coord);
                double field = noise.Fractal(point.X / map.ArtScale * .08 + 13.7,
                    point.Y / map.ArtScale * .08 + 57.1, 3);
                cell.SecondaryGroundBlend = (float)Math.Max(0, Math.Min(1, (field - .26) / .54));
                if (map.Theme == MapTheme.Wetland)
                    cell.SecondaryGroundBlend = .18f + cell.SecondaryGroundBlend * .82f;
                cell.VisualVariant = (byte)(cell.SecondaryGroundBlend >= .5f ? 1 : 0);
                cell.Terrain = TerrainFor(map.Theme, cell.VisualVariant);
            }
        }

        private static MapTerrain TerrainFor(MapTheme theme, int variant)
        {
            switch (theme)
            {
                case MapTheme.Forest: return variant == 0 ? MapTerrain.Grass : MapTerrain.Forest;
                case MapTheme.Wetland: return variant == 0 ? MapTerrain.Water : MapTerrain.Grass;
                case MapTheme.Dryland: return MapTerrain.Sand;
                case MapTheme.Frontier: return MapTerrain.Forest;
                default: return MapTerrain.Grass;
            }
        }

        private static void BuildPathDecals(GridMapData map, MapRandom random)
        {
            if (map.Theme != MapTheme.Forest && map.Theme != MapTheme.Grassland) return;
            foreach (MapPlacement core in map.Cores)
            {
                float distance = (float)Math.Sqrt(MapPoint.DistanceSquared(map.SpawnPosition, core.Position));
                // These sprites are curved path fragments, not seamless strips. Leave breaks between them.
                int segments = Math.Max(1, (int)(distance / (6f * map.ArtScale)));
                for (int i = 0; i < segments; i++)
                {
                    float t = .22f + (i + .5f) / segments * .48f;
                    var point = new MapPoint(map.SpawnPosition.X + (core.Position.X - map.SpawnPosition.X) * t,
                        map.SpawnPosition.Y + (core.Position.Y - map.SpawnPosition.Y) * t);
                    var p = NewPlacement(map, MapObjectCategory.Decal, 0, point, 3.1f * map.ArtScale, .1f * map.ArtScale, false);
                    p.RotationDegrees = (float)(Math.Atan2(core.Position.Y - map.SpawnPosition.Y,
                        core.Position.X - map.SpawnPosition.X) * 180 / Math.PI) + 90f;
                    map.Placements.Add(p);
                }
            }
        }

        private static void PlaceScattered(GridMapData map, MapRandom random, MapObjectCategory category, int count, bool biasEdges)
        {
            int variants = category == MapObjectCategory.Decoration ? 6 :
                category == MapObjectCategory.Landmark ? 2 : 4;
            int placed = 0, attempts = Math.Max(40, count * 60);
            for (int attempt = 0; attempt < attempts && placed < count; attempt++)
            {
                int variant = placed % variants;
                var point = RandomPoint(map, random, biasEdges);
                float width = WidthFor(map, category, variant) * (.88f + (float)random.Unit() * .24f);
                bool blocking = category == MapObjectCategory.Obstacle || category == MapObjectCategory.Landmark;
                if (category == MapObjectCategory.Decal &&
                    ((map.Theme == MapTheme.Forest && variant == 3) || (map.Theme == MapTheme.Wetland && variant == 3))) blocking = true;
                float radius = category == MapObjectCategory.Decoration ? width * .30f :
                    category == MapObjectCategory.Decal ? width * .34f : width * .25f;
                if (!map.ContainsPoint(point, radius + map.ArtScale * .35f)) continue;
                if (blocking && map.IsReservedForCombat(point, radius + .2f * map.ArtScale)) continue;
                if (category != MapObjectCategory.Decal && map.IsReservedForCombat(point, radius * .25f)) continue;
                if (Overlaps(map, point, radius, category)) continue;
                var p = NewPlacement(map, category, variant, point, width, radius, blocking);
                if (category == MapObjectCategory.Decal)
                {
                    p.RotationDegrees = (float)(random.Unit() - .5) * 25f;
                    p.FlipX = random.Unit() < .5;
                }
                map.Placements.Add(p);
                placed++;
            }
        }

        private static float WidthFor(GridMapData map, MapObjectCategory category, int variant)
        {
            float r = map.ArtScale;
            switch (category)
            {
                case MapObjectCategory.Decoration: return r * (variant < 2 ? .8f : .65f);
                case MapObjectCategory.Obstacle: return r * (variant < 2 ? 2.2f : 1.8f);
                case MapObjectCategory.Landmark: return r * 3.8f;
                case MapObjectCategory.Decal: return r * (variant == 3 || (map.Theme == MapTheme.Wetland && variant == 2) ? 4.2f : 3.1f);
                default: return r * 2.8f;
            }
        }

        private static MapPoint RandomPoint(GridMapData map, MapRandom random, bool edgeBias)
        {
            double x = random.Unit(), y = random.Unit();
            if (edgeBias && random.Unit() < .72)
            {
                double band = .10 + random.Unit() * .15;
                switch (random.Next() % 4)
                {
                    case 0: x = band; break;
                    case 1: x = 1 - band; break;
                    case 2: y = band; break;
                    default: y = 1 - band; break;
                }
            }
            return new MapPoint(map.MinX + (float)x * (map.MaxX - map.MinX),
                map.MinY + (float)y * (map.MaxY - map.MinY));
        }

        private static bool Overlaps(GridMapData map, MapPoint point, float radius, MapObjectCategory category)
        {
            foreach (MapPlacement p in map.Placements)
            {
                // Ground patches may blend with one another. Keep the core and other physical objects clear.
                if (category == MapObjectCategory.Decal && p.Category == MapObjectCategory.Decal) continue;
                if (p.Category == MapObjectCategory.Decal && !p.BlocksMovement) continue;
                float spacing = radius + p.FootprintRadius + .15f * map.ArtScale;
                if (MapPoint.DistanceSquared(point, p.Position) < spacing * spacing) return true;
            }
            return false;
        }

        private static MapPlacement NewPlacement(GridMapData map, MapObjectCategory category, int variant,
            MapPoint position, float width, float radius, bool blocking) => new MapPlacement
        {
            Id = map.Placements.Count, Category = category, VariantIndex = variant, Position = position,
            Anchor = map.NearestCoordinate(position), Width = width, Height = width,
            FootprintRadius = radius, BlocksMovement = blocking
        };
    }
}
