using System;
using System.IO;
using System.Reflection;
using Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sproutshot.MapGen.Editor
{
    /// <summary>Exercises the authored prefab, spawner, screen input and real 2D physics in a disposable scene.</summary>
    public static class PlayerMovementValidation
    {
        private static int checks;
        private static readonly MethodInfo Tick = typeof(PlayerClickToMove).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        public static string Run(string output)
        {
            checks = 0;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Arts/Prefabs/Characters/Player.prefab");
            Check(prefab != null && prefab.GetComponent<PlayerClickToMove>() != null, "Movement script must be authored on the prefab.");
            Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab) == 0, "Player prefab must not contain missing scripts.");
            var visual = prefab.GetComponentInChildren<SpriteRenderer>();
            Check(visual != null && visual.sprite != null, "Player's authored sprite reference must resolve.");
            string spritePath = AssetDatabase.GetAssetPath(visual.sprite);
            Check(!string.IsNullOrEmpty(spritePath) && AssetDatabase.LoadAssetAtPath<Sprite>(spritePath) == visual.sprite,
                "Player sprite must resolve to its current project asset.");
            File.WriteAllText(Path.Combine(output, "player_asset_info.txt"),
                "Player sprite: " + spritePath + "\nGUID: " + AssetDatabase.AssetPathToGUID(spritePath));
            Check(prefab.GetComponent<Rigidbody2D>().gravityScale == 0 && prefab.GetComponent<CircleCollider2D>() != null,
                "Prefab owns its top-down physics components.");

            // Physics checks run only in the isolated batch validation checkout. The menu path
            // validates prefab authoring without advancing or replacing the user's edit scene.
            if (!Application.isBatchMode) return "PASS: player prefab components and art references.";
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SimulationMode2D previousSimulation = Physics2D.simulationMode;
            try
            {
                SceneManager.SetActiveScene(scene);
                Physics2D.simulationMode = SimulationMode2D.Script;
                var root = new GameObject("Player Movement Validation");
                var view = root.AddComponent<GridMapView>();
                var generator = root.AddComponent<GridMapGenerator>();
                var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
                camera.orthographic = true; camera.orthographicSize = 5.8f; camera.aspect = 800f / 1280f;
                camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                var virtualCamera = new GameObject("Player Follow Camera").AddComponent<CinemachineVirtualCamera>();
                SetObject(generator, "virtualCamera", virtualCamera);
                var rules = new GridMapRules { Width = 12, Height = 21, Seed = 20261014, Theme = MapTheme.Forest };
                generator.DisplayMap(GridMapGenerator.CreateMapData(rules, rules.Seed));
                var spawner = root.AddComponent<MapPlayerSpawner>();
                SetObject(spawner, "generator", generator); SetObject(spawner, "mapView", view);
                SetObject(spawner, "playerPrefab", prefab); SetObject(spawner, "inputCamera", camera);
                Check(spawner.SpawnOrRepositionPlayer(), "Player spawns after the map is built.");
                PlayerClickToMove player = spawner.Player;
                Check(Vector3.Distance(player.transform.position, view.WorldSpawnPosition) < .001f, "Spawn position uses the reserved clearing.");
                Check(virtualCamera.Follow == player.transform, "Cinemachine follows the spawned player.");
                Check(spawner.SpawnOrRepositionPlayer() && spawner.Player == player && root.GetComponentsInChildren<PlayerClickToMove>().Length == 1,
                    "Regeneration reuses a single player.");
                var body = player.GetComponent<Rigidbody2D>();
                body.interpolation = RigidbodyInterpolation2D.None;
                Physics2D.SyncTransforms();
                camera.transform.position = view.WorldSpawnPosition + Vector3.back * 20;
                Vector3 destination = view.WorldSpawnPosition + Vector3.right;
                Check(player.TryMoveFromScreenPosition(camera.WorldToScreenPoint(destination)), "Screen pointer resolves to the map plane.");
                for (int step = 0; step < 100 && player.IsMoving; step++) Step(player);
                Check(Vector2.Distance(body.position, destination) < .06f && !player.IsMoving, "Physics movement arrives and stops.");
                Check(body.velocity.sqrMagnitude < .001f, "Stopped player does not drift.");

                // A raycastable UI button must consume a tap, including on the first touch frame.
                var eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();
                var canvas = new GameObject("UI", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                var button = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button));
                button.transform.SetParent(canvas.transform, false);
                ((RectTransform)button.transform).sizeDelta = new Vector2(300, 300);
                // Normal Play mode invokes these automatically; this batch test runs in edit mode.
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(eventSystem, null);
                EventSystem.current = eventSystem;
                Check(EventSystem.current == eventSystem, "Test EventSystem is active.");
                typeof(GraphicRaycaster).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(canvas.GetComponent<GraphicRaycaster>(), null);
                typeof(Image).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(button.GetComponent<Image>(), null);
                Canvas.ForceUpdateCanvases();
                button.GetComponent<Image>().Rebuild(CanvasUpdate.PreRender);
                Render(camera, null);
                player.StopMoving();
                Vector2 buttonPosition = RectTransformUtility.WorldToScreenPoint(camera, button.transform.position);
                var uiResults = new System.Collections.Generic.List<RaycastResult>();
                eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = buttonPosition }, uiResults);
                Check(uiResults.Count > 0, "Test UI must participate in screen raycasts.");
                Check(!player.TryMoveFromScreenPosition(buttonPosition) && !player.IsMoving,
                    "UI tap never issues a movement command.");
                UnityEngine.Object.DestroyImmediate(canvas.gameObject); UnityEngine.Object.DestroyImmediate(eventSystem.gameObject);

                player.WarpToSpawn(); Physics2D.SyncTransforms();
                MapPlacement core = view.Map.Cores[0];
                Check(player.MoveToWorldPosition(view.transform.TransformPoint(new Vector3(core.Position.X, core.Position.Y, 0))),
                    "Tap on a blocked core selects an accessible nearby point.");
                for (int step = 0; step < 1600 && player.IsMoving; step++)
                {
                    Step(player);
                    Check(view.IsWalkableWorldPosition(new Vector3(body.position.x, body.position.y, 0), .215f),
                        "Player physics never enters map obstacles or leaves the boundary.");
                }
                Check(!player.IsMoving && Vector2.Distance(body.position, player.Destination) < .06f, "Core approach route completes.");
                player.WarpToSpawn(); Physics2D.SyncTransforms();
                Render(camera, Path.Combine(output, "player_in_forest.png"));
                return "PASS: " + checks + " player prefab, spawn, input, UI, camera and physics movement checks.";
            }
            finally
            {
                Physics2D.simulationMode = previousSimulation;
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        private static void Step(PlayerClickToMove player) { Tick.Invoke(player, null); Physics2D.Simulate(Time.fixedDeltaTime); }
        private static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        private static void Render(Camera camera, string path)
        {
            RenderTexture previous = RenderTexture.active;
            var target = new RenderTexture(800, 1280, 24);
            Texture2D image = null;
            try
            {
                target.Create();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                RenderTexture.active = target;
                image = new Texture2D(800, 1280, TextureFormat.RGBA32, false);
                image.ReadPixels(new Rect(0, 0, 800, 1280), 0, 0); image.Apply();
                if (!string.IsNullOrEmpty(path)) File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
