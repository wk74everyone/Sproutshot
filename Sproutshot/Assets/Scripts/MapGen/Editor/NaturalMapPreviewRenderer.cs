using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using YooAsset.Editor;

namespace Sproutshot.MapGen.Editor
{
    /// <summary>Renders the real generator in a disposable preview scene without changing the user's scene.</summary>
    public static class NaturalMapPreviewRenderer
    {
        [MenuItem("Tools/Sproutshot/Validate And Render Five Maps")]
        public static void RenderAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run visual map validation outside Play mode.");
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../MapGenerationTests/GeneratedPreviews"));
            Directory.CreateDirectory(output);
            var results = new List<string>();
            results.Add(ValidateAssetAddresses());
            foreach (MapTheme theme in Enum.GetValues(typeof(MapTheme)))
            {
                var rules = new GridMapRules { Theme = theme, Width = 12, Height = 21,
                    CellSize = 1.35f, ArtScale = .78f, Seed = 20261014, RandomizeSeed = false };
                if (!GridMapArtLibrary.LoadEditorPreview(theme)) throw new InvalidOperationException("Missing art: " + theme);
                GridMapData map = GridMapGenerator.CreateMapData(rules, rules.Seed);
                Scene scene = EditorSceneManager.NewPreviewScene();
                RenderTexture target = null;
                Texture2D image = null;
                RenderTexture previous = RenderTexture.active;
                try
                {
                    var root = new GameObject("Map Validation " + theme);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    var view = root.AddComponent<GridMapView>();
                    view.Build(map, rules);
                    Bounds bounds = view.LocalBounds();
                    var cameraObject = new GameObject("Validation Camera");
                    SceneManager.MoveGameObjectToScene(cameraObject, scene);
                    Camera camera = cameraObject.AddComponent<Camera>();
                    camera.scene = scene;
                    camera.enabled = false;
                    camera.orthographic = true;
                    camera.aspect = 800f / 1280f;
                    camera.orthographicSize = Mathf.Max(bounds.size.y * .5f, bounds.size.x / camera.aspect * .5f) * 1.035f;
                    camera.transform.position = bounds.center + Vector3.back * 20f;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.12f, .20f, .18f, 1);
                    camera.nearClipPlane = .1f;
                    camera.farClipPlane = 100f;
                    var data = cameraObject.AddComponent<UniversalAdditionalCameraData>();
                    data.renderPostProcessing = false;
                    target = new RenderTexture(800, 1280, 24, RenderTextureFormat.ARGB32);
                    target.Create();
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                    if (!RenderPipeline.SupportsRenderRequest(camera, request))
                        throw new InvalidOperationException("Current renderer does not support camera render requests.");
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
                    image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    image.Apply();
                    string path = Path.Combine(output, theme.ToString().ToLowerInvariant() + "_generated.png");
                    File.WriteAllBytes(path, image.EncodeToPNG());
                    results.Add(theme + ": " + map.Placements.Count + " placements, " + map.Cores.Count + " cores; " + path);
                    ValidateView(view, map);
                }
                finally
                {
                    RenderTexture.active = previous;
                    if (image != null) UnityEngine.Object.DestroyImmediate(image);
                    if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            results.Add(PlayerMovementValidation.Run(output));
            File.WriteAllLines(Path.Combine(output, "validation.txt"), results);
            Debug.Log("Natural map visual validation passed\n" + string.Join("\n", results));
        }

        private static string ValidateAssetAddresses()
        {
            var settings = AssetDatabase.LoadAssetAtPath<BundleCollectorSetting>("Assets/Resources/BundleCollectorSetting.asset");
            if (settings == null) throw new InvalidOperationException("Missing YooAsset collector settings.");
            var expected = new HashSet<string>(GridMapArtLibrary.GetSharedAddresses());
            foreach (MapTheme theme in Enum.GetValues(typeof(MapTheme)))
                expected.UnionWith(GridMapArtLibrary.GetThemeAddresses(theme));
            expected.Add("natural_map_ground"); expected.Add("map_sprite");
            expected.Add("Player");
            expected.Add(GridMapArtLibrary.PollutionCorePrefabAddress);
            var addresses = new HashSet<string>();
            var command = new CollectCommand("DefaultPackage", new NormalIgnoreRule())
                { EnableAddressable = true, SupportExtensionless = true };
            command.SetSimulateBuild(true);
            foreach (var package in settings.Packages)
            {
                if (package.PackageName != "DefaultPackage") continue;
                foreach (var group in package.Groups)
                foreach (var collector in group.Collectors)
                {
                    if (collector.CollectPath != "Assets/Arts/Sprites/Maps" &&
                        !collector.CollectPath.StartsWith("Assets/Arts/Sprites/Maps/", StringComparison.Ordinal) &&
                        collector.CollectPath != "Assets/Arts/Prefabs/Characters" &&
                        collector.CollectPath != GridMapArtLibrary.PollutionCorePrefabPath &&
                        collector.CollectPath != "Assets/Arts/Shaders/Tile/natural_map_ground.mat" &&
                        collector.CollectPath != "Assets/Arts/Shaders/Tile/map_sprite.mat") continue;
                    collector.CheckConfigError();
                    foreach (var asset in collector.GetAllCollectAssets(command, group))
                    {
                        if (collector.CollectPath == "Assets/Arts/Prefabs/Characters" && asset.Address != "Player") continue;
                        if (!addresses.Add(asset.Address)) throw new InvalidOperationException("Duplicate map address: " + asset.Address);
                    }
                }
            }
            if (!addresses.SetEquals(expected))
            {
                var missing = new HashSet<string>(expected); missing.ExceptWith(addresses);
                var extra = new HashSet<string>(addresses); extra.ExceptWith(expected);
                throw new InvalidOperationException("YooAsset map address mismatch. Missing: " + string.Join(", ", missing) + "; extra: " + string.Join(", ", extra));
            }
            return "PASS: " + addresses.Count + " YooAsset map/player/core addresses; composition previews excluded.";
        }

        private static void ValidateView(GridMapView view, GridMapData map)
        {
            foreach (GridCoord coord in map.Coordinates())
            {
                Vector3 world = view.transform.TransformPoint(view.CellToLocal(coord));
                if (!view.TryGetCellAtWorldPosition(world, out GridCoord actual) || actual != coord)
                    throw new InvalidOperationException("World coordinate round-trip failed at " + coord);
            }
            Bounds bounds = view.LocalBounds();
            if (view.TryGetCellAtWorldPosition(new Vector3(bounds.max.x + 1, bounds.center.y, 0), out _))
                throw new InvalidOperationException("Map exterior must not target an edge sample.");
            for (int index = 0; index < map.Cores.Count; index++)
            {
                MapPlacement core = map.Cores[index];
                Transform instance = view.transform.Find("Natural Map " + map.Theme + "/Scene Objects/PollutionCore " + core.Id);
                if (instance == null || Vector3.Distance(instance.localPosition, new Vector3(core.Position.X, core.Position.Y, 0)) > .0001f)
                    throw new InvalidOperationException("Core prefab must use the generated map position.");
                bool hasSpawner = false;
                foreach (MonoBehaviour component in instance.GetComponents<MonoBehaviour>())
                    if (component != null && component.GetType().FullName == "Sproutshot.Gameplay.EnemySpawner") hasSpawner = true;
                if (!hasSpawner || GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(instance.gameObject) != 0)
                    throw new InvalidOperationException("Core prefab must preserve its authored EnemySpawner.");
                if (instance.GetComponentsInChildren<CircleCollider2D>().Length != 1 ||
                    instance.Find("Foot Shadow") == null || instance.childCount != 2)
                    throw new InvalidOperationException("Core prefab hierarchy/collider duplicated or lost.");
                var collider = instance.GetComponent<CircleCollider2D>();
                float radius = collider.radius * Mathf.Max(Mathf.Abs(instance.localScale.x), Mathf.Abs(instance.localScale.y));
                if (Mathf.Abs(radius - core.FootprintRadius) > .0001f)
                    throw new InvalidOperationException("Core prefab and navigation must share a collision radius.");
                var renderer = instance.Find("Visual").GetComponent<SpriteRenderer>();
                Sprite alive = GridMapArtLibrary.PollutionCorePrefab().transform.Find("Visual").GetComponent<SpriteRenderer>().sprite;
                if (renderer.sprite != alive) throw new InvalidOperationException("Alive core must retain the prefab's authored appearance.");
                if (!view.SetCoreState(index, MapCoreState.Broken) || !view.SetCoreState(index, MapCoreState.Restored))
                    throw new InvalidOperationException("Core state rendering failed.");
                if (renderer.sprite != GridMapArtLibrary.Core(map.Theme, MapCoreState.Restored))
                    throw new InvalidOperationException("Restored core state was not applied to the prefab renderer.");
                view.SetCoreState(index, MapCoreState.Alive);
                if (renderer.sprite != alive) throw new InvalidOperationException("Returning to Alive must restore the prefab sprite.");
            }
            foreach (SpriteRenderer renderer in view.GetComponentsInChildren<SpriteRenderer>())
            {
                if (renderer.sprite == null || renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null)
                    throw new InvalidOperationException("Generated renderer has missing art or material.");
                if (ShaderUtil.ShaderHasError(renderer.sharedMaterial.shader))
                    throw new InvalidOperationException("Shader compilation failed: " + renderer.sharedMaterial.shader.name);
            }
            // Rebuilding a map must replace the old prefab instances, rather than accumulate spawners.
            Transform oldRoot = view.transform.Find("Natural Map " + map.Theme);
            view.Build(map, new GridMapRules());
            if (oldRoot != null) throw new InvalidOperationException("Previous map's core prefabs were not cleared.");
            int spawners = 0;
            foreach (MonoBehaviour component in view.GetComponentsInChildren<MonoBehaviour>())
                if (component != null && component.GetType().FullName == "Sproutshot.Gameplay.EnemySpawner") spawners++;
            if (spawners != map.Cores.Count) throw new InvalidOperationException("Rebuilding changed the number of core prefab spawners.");
        }
    }
}
