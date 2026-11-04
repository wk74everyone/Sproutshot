using System.Collections.Generic;
using Sproutshot.Utils;
using UnityEngine;
using YooAsset;

namespace Sproutshot.MapGen
{
    /// <summary>Stable file-name addresses for the five continuous-terrain art sets.</summary>
    public static class GridMapArtLibrary
    {
        public const float PixelsPerUnit = 200f;
        public const int GroundCount = 2;
        public const int DecalCount = 4;
        public const int ObstacleCount = 4;
        public const int DecorationCount = 6;
        public const int LandmarkCount = 2;
        private const string SpriteMaterialAddress = "map_sprite";
        private const string GroundMaterialAddress = "natural_map_ground";
        public const string PollutionCorePrefabAddress = "PollutionCore";
        public const string PollutionCorePrefabPath = "Assets/Arts/Prefabs/Enemy/PollutionCore.prefab";

        // Indices are a placement contract: trees/roots precede rocks, and wetland
        // water decals are indices 2 (shallow) and 3 (deep).
        private static readonly ThemeArt[] Themes =
        {
            new ThemeArt("forest", "root",
                new[] { "grass", "moss_grass" },
                new[] { "dirt_path", "moss_patch", "leaf_litter", "pond_shore" },
                new[] { "round_tree", "tall_tree", "moss_rock", "stump" },
                new[] { "short_grass", "fern", "mushroom", "fallen_leaves", "white_flower", "yellow_flower" },
                new[] { "arched_roots", "fallen_log" }),
            new ThemeArt("grassland", "flower",
                new[] { "light_grass", "exposed_soil" },
                new[] { "narrow_path", "grass_transition", "dry_grass", "sparse_flowers" },
                new[] { "low_tree", "large_bush", "round_rock", "remnant_wood" },
                new[] { "short_grass", "seed_heads", "white_flower", "yellow_flower", "pink_flower", "purple_flower" },
                new[] { "flower_ring_mound", "weathered_rocks" }),
            new ThemeArt("wetland", "lotus",
                new[] { "wet_mud", "grass_islet" },
                new[] { "silt_patch", "sand_shore", "shallow_water", "deep_water" },
                new[] { "reed_roots", "dead_root", "wet_rock", "bank_log" },
                new[] { "water_grass", "reed_seedling", "lotus_leaf", "lotus_flower", "floating_leaf", "water_ripple" },
                new[] { "lotus_pond", "islet_deadwood" }),
            new ThemeArt("dryland", "crystal",
                new[] { "golden_sand", "gravel_soil" },
                new[] { "sand_strokes", "gravel_patch", "cracked_earth", "dry_grass" },
                new[] { "large_cactus", "dead_tree", "eroded_rock", "sandstone_pillar" },
                new[] { "dry_grass", "dry_twigs", "small_stones", "low_bush", "small_cactus", "drought_flower" },
                new[] { "rock_arch", "pillar_cluster" }),
            new ThemeArt("frontier", "tumor",
                new[] { "barren_earth", "remnant_grass" },
                new[] { "exposed_earth", "cracks", "dead_leaves", "root_veins" },
                new[] { "twisted_tree", "broken_stump", "broken_rock", "thorn_roots" },
                new[] { "sparse_grass", "gray_moss", "dead_leaves", "broken_twigs", "fungi", "seedlings" },
                new[] { "root_remains", "stone_pile" })
        };

        private static readonly string[] SharedAddresses =
        {
            "shared_mask_transition_straight_01_512",
            "shared_mask_transition_curve_01_512",
            "shared_mask_transition_corner_01_512",
            "shared_mask_transition_irregular_01_512",
            "shared_shadow_circle_soft_01_256",
            "shared_shadow_ellipse_soft_01_256",
            "shared_particle_leaf_long_01_128",
            "shared_particle_leaf_round_01_128",
            "shared_particle_light_dot_01_128",
            "shared_particle_star_short_01_128",
            "shared_ripple_soft_ring_01_256",
            "shared_noise_water_color_01_512",
            "shared_noise_water_ripples_01_512"
        };

        private static readonly HashSet<string> KnownShared = new HashSet<string>(SharedAddresses);
        private static readonly Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
        private static readonly Dictionary<string, AssetHandle> Handles = new Dictionary<string, AssetHandle>();
        private static bool isLoaded;
        private static Sprite fallbackGround;
        private static Material spriteMaterial;
        private static Material groundMaterial;
        private static GameObject pollutionCorePrefab;
#if UNITY_EDITOR
        private static bool editorPreviewLoaded;
#endif

        public static MapTheme ActiveTheme { get; private set; } = MapTheme.Forest;

        /// <summary>Compatibility entry point; loads the current theme only.</summary>
        public static bool LoadMapSprites() => LoadMapSprites(ActiveTheme);

        /// <summary>Loads one 21-sprite theme. Shared art is loaded only when requested.</summary>
        public static bool LoadMapSprites(MapTheme theme)
        {
            theme = NormalizeTheme(theme);
#if UNITY_EDITOR
            if (editorPreviewLoaded && !Application.isPlaying)
                return ActiveTheme == theme ? isLoaded : LoadEditorPreview(theme);
            if (editorPreviewLoaded)
            {
                Sprites.Clear();
                spriteMaterial = groundMaterial = null;
                pollutionCorePrefab = null;
                isLoaded = editorPreviewLoaded = false;
            }
#endif
            if (isLoaded && ActiveTheme == theme) return true;
            if (!YooAssetManager.Instance.IsInitialized)
            {
                Debug.LogError("[GridMapArtLibrary] YooAsset 尚未初始化，无法加载地图资源。");
                return false;
            }

            bool complete = true;
            foreach (string address in GetThemeAddresses(theme)) complete &= LoadSprite(address) != null;
            if (spriteMaterial == null) spriteMaterial = LoadMaterial(SpriteMaterialAddress);
            if (groundMaterial == null) groundMaterial = LoadMaterial(GroundMaterialAddress);
            complete &= spriteMaterial != null && groundMaterial != null;
            if (complete) { ActiveTheme = theme; isLoaded = true; }
            if (!complete)
                Debug.LogError($"[GridMapArtLibrary] {theme} 素材加载不完整，请检查 Maps 收集器及导入设置。");
            return complete;
        }

        /// <summary>Called only after a successful view rebuild; preparation keeps the previous map's art alive.</summary>
        public static void RetainOnlyTheme(MapTheme theme)
        {
#if UNITY_EDITOR
            if (editorPreviewLoaded) return;
#endif
            foreach (MapTheme other in System.Enum.GetValues(typeof(MapTheme)))
                if (other != theme)
                    foreach (string address in GetThemeAddresses(other)) Release(address);
        }

#if UNITY_EDITOR
        /// <summary>Explicit edit-mode preview path. Player builds and normal Play mode use YooAsset.</summary>
        public static bool LoadEditorPreview(MapTheme theme)
        {
            if (Application.isPlaying) return false;
            theme = NormalizeTheme(theme);
            Sprites.Clear();
            bool complete = true;
            foreach (string address in GetThemeAddresses(theme))
            {
                Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(EditorSpritePath(address));
                if (sprite != null) Sprites[address] = sprite;
                else { complete = false; Debug.LogError("Preview sprite missing: " + address); }
            }
            spriteMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Arts/Shaders/Tile/map_sprite.mat");
            groundMaterial = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Arts/Shaders/Tile/natural_map_ground.mat");
            ActiveTheme = theme;
            editorPreviewLoaded = true;
            isLoaded = complete && spriteMaterial != null && groundMaterial != null;
            return isLoaded;
        }

        private static string EditorSpritePath(string address)
        {
            string[] pieces = address.Split('_');
            string theme = pieces[0] == "shared" ? "Shared" : pieces[0];
            string category = char.ToUpperInvariant(pieces[1][0]) + pieces[1].Substring(1);
            return "Assets/Arts/Sprites/Maps/" + theme + "/" + category + "/" + address + ".png";
        }
#endif

        public static Sprite Ground(MapTheme theme, int index) =>
            GetThemeSprite(theme, Catalog(theme).Ground, index) ?? FallbackGround();
        public static Sprite Decal(MapTheme theme, int index) => GetThemeSprite(theme, Catalog(theme).Decal, index);
        public static Sprite Obstacle(MapTheme theme, int index) => GetThemeSprite(theme, Catalog(theme).Obstacle, index);
        public static Sprite Decoration(MapTheme theme, int index) => GetThemeSprite(theme, Catalog(theme).Decoration, index);
        public static Sprite Landmark(MapTheme theme, int index) => GetThemeSprite(theme, Catalog(theme).Landmark, index);
        public static Sprite Core(MapTheme theme, MapCoreState state) => GetThemeSprite(theme, Catalog(theme).Core, (int)state);

        /// <summary>Masks/noise are grayscale sprites; use texture red values, not alpha.</summary>
        public static Sprite Shared(string address)
        {
            if (string.IsNullOrEmpty(address) || !KnownShared.Contains(address))
            {
                Debug.LogError($"[GridMapArtLibrary] 未登记的共用地图素材：{address}");
                return null;
            }
#if UNITY_EDITOR
            if (editorPreviewLoaded && !Application.isPlaying)
            {
                if (Sprites.TryGetValue(address, out Sprite cached)) return cached;
                Sprite sprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(EditorSpritePath(address));
                if (sprite != null) Sprites[address] = sprite;
                return sprite;
            }
#endif
            if (!YooAssetManager.Instance.IsInitialized) return null;
            return LoadSprite(address);
        }

        public static Texture2D SharedTexture(string address) => Shared(address)?.texture;

        // Terrain samples select one of the theme's two continuous ground textures.
        public static Sprite Ground(GridCell cell) => Ground(ActiveTheme, cell?.VisualVariant ?? 0);
        public static Sprite Background() => Ground(ActiveTheme, 0);

        public static Material SpriteMaterial()
        {
            if (!isLoaded) LoadMapSprites(ActiveTheme);
            return spriteMaterial;
        }

        public static Material GroundMaterial()
        {
            if (!isLoaded) LoadMapSprites(ActiveTheme);
            return groundMaterial;
        }

        public static GameObject PollutionCorePrefab()
        {
            if (!isLoaded) LoadMapSprites(ActiveTheme);
            return pollutionCorePrefab;
        }

        public static IEnumerable<string> GetThemeAddresses(MapTheme theme)
        {
            foreach (string[] group in Catalog(theme).Groups)
                foreach (string address in group) yield return address;
        }

        public static IEnumerable<string> GetSharedAddresses()
        {
            foreach (string address in SharedAddresses) yield return address;
        }

        private static Sprite GetThemeSprite(MapTheme theme, string[] addresses, int index)
        {
            if (!LoadMapSprites(theme)) return null;
            int wrapped = index % addresses.Length;
            if (wrapped < 0) wrapped += addresses.Length;
            return Sprites.TryGetValue(addresses[wrapped], out Sprite sprite) ? sprite : null;
        }

        private static Sprite LoadSprite(string address)
        {
            if (Sprites.TryGetValue(address, out Sprite cached) && cached != null) return cached;
            AssetHandle handle = YooAssetManager.Instance.LoadAssetHandleSync<Sprite>(address);
            Sprite sprite = handle?.AssetObject as Sprite;
            if (sprite == null)
            {
                if (handle != null) YooAssetManager.Instance.Release(handle);
                Debug.LogError($"[GridMapArtLibrary] YooAsset 地址不存在或不是 Sprite：{address}");
                return null;
            }
            Sprites[address] = sprite;
            Handles[address] = handle;
            return sprite;
        }

        private static Material LoadMaterial(string address)
        {
            AssetHandle handle = YooAssetManager.Instance.LoadAssetHandleSync<Material>(address);
            Material material = handle?.AssetObject as Material;
            if (material != null) Handles[address] = handle;
            else if (handle != null) YooAssetManager.Instance.Release(handle);
            return material;
        }

        private static void Release(string address)
        {
            Sprites.Remove(address);
            if (!Handles.TryGetValue(address, out AssetHandle handle)) return;
            YooAssetManager.Instance.Release(handle);
            Handles.Remove(address);
        }

        private static MapTheme NormalizeTheme(MapTheme theme) =>
            (int)theme >= 0 && (int)theme < Themes.Length ? theme : MapTheme.Forest;
        private static ThemeArt Catalog(MapTheme theme) => Themes[(int)NormalizeTheme(theme)];

        private static Sprite FallbackGround()
        {
            if (fallbackGround != null) return fallbackGround;
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Generated continuous ground",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color(.35f, .68f, .18f, 1f);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            fallbackGround = Sprite.Create(texture, new Rect(0, 0, size, size), Vector2.one * .5f,
                PixelsPerUnit, 0, SpriteMeshType.FullRect);
            fallbackGround.name = texture.name;
            return fallbackGround;
        }

        private sealed class ThemeArt
        {
            public readonly string[] Ground, Decal, Obstacle, Decoration, Landmark, Core;
            public readonly string[][] Groups;

            public ThemeArt(string prefix, string core, string[] ground, string[] decal,
                string[] obstacle, string[] decoration, string[] landmark)
            {
                Ground = Addresses(prefix, "ground", 512, ground);
                Decal = Addresses(prefix, "decal", 512, decal);
                Obstacle = Addresses(prefix, "obstacle", 512, obstacle);
                Decoration = Addresses(prefix, "decoration", 256, decoration);
                Landmark = Addresses(prefix, "landmark", 1024, landmark);
                Core = Addresses(prefix, "core", 512,
                    new[] { core + "_alive", core + "_broken", core + "_restored" });
                Groups = new[] { Ground, Decal, Obstacle, Decoration, Landmark };
            }

            private static string[] Addresses(string prefix, string category, int size, string[] names)
            {
                var result = new string[names.Length];
                for (int i = 0; i < names.Length; i++) result[i] = $"{prefix}_{category}_{names[i]}_01_{size}";
                return result;
            }
        }
    }
}
