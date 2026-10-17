using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sproutshot.MapGen
{
    /// <summary>Prefab-owned touch/mouse input and collision-aware movement on a generated map.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class PlayerClickToMove : MonoBehaviour
    {
        [SerializeField, Min(.1f)] private float moveSpeed = 3.5f;
        [SerializeField, Min(.001f)] private float arrivalDistance = .025f;
        [SerializeField, Min(0)] private float navigationPadding = .04f;
        [SerializeField] private Rigidbody2D body;
        [SerializeField] private CircleCollider2D footprint;
        [SerializeField] private SpriteRenderer visual;

        private readonly List<MapPoint> path = new List<MapPoint>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private GridMapView mapView;
        private Camera inputCamera;
        private int waypoint;
        private float worldZ;

        public float MoveSpeed => moveSpeed;
        public bool IsMoving => waypoint < path.Count;
        public GridMapView MapView => mapView;
        public Vector3 Destination { get; private set; }

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (footprint == null) footprint = GetComponent<CircleCollider2D>();
            if (visual == null) visual = GetComponentInChildren<SpriteRenderer>();
        }

        /// <summary>The scene spawner supplies context; all movement components already belong to the prefab.</summary>
        public void BindToMap(GridMapView map, Camera camera)
        {
            mapView = map;
            inputCamera = camera;
            worldZ = map != null ? map.transform.position.z : transform.position.z;
            StopMoving();
            UpdateSorting();
        }

        public void WarpToSpawn()
        {
            if (mapView == null || mapView.Map == null || body == null) return;
            StopMoving();
            Vector3 position = mapView.WorldSpawnPosition;
            body.position = position;
            transform.position = new Vector3(position.x, position.y, worldZ);
            Destination = transform.position;
            UpdateSorting();
        }

        private void Update()
        {
            if (mapView == null || mapView.Map == null || Time.timeScale <= 0) return;
            // Suppress the emulated mouse while touch input is present to avoid duplicate commands.
            if (Input.touchCount > 0)
            {
                if (Input.touchCount == 1)
                {
                    Touch touch = Input.GetTouch(0);
                    if (touch.phase == TouchPhase.Began) TryMoveFromScreenPosition(touch.position);
                }
                return;
            }
            if (Input.GetMouseButtonDown(0)) TryMoveFromScreenPosition(Input.mousePosition);
        }

        public bool TryMoveFromScreenPosition(Vector2 screenPosition)
        {
            if (mapView == null || mapView.Map == null || IsPointerOverUI(screenPosition)) return false;
            if (inputCamera == null) inputCamera = Camera.main;
            if (inputCamera == null || !inputCamera.pixelRect.Contains(screenPosition)) return false;
            var plane = new Plane(mapView.transform.forward, mapView.transform.position);
            Ray ray = inputCamera.ScreenPointToRay(screenPosition);
            return plane.Raycast(ray, out float distance) && MoveToWorldPosition(ray.GetPoint(distance));
        }

        private bool IsPointerOverUI(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            uiHits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPosition }, uiHits);
            foreach (RaycastResult hit in uiHits)
                if (hit.module is GraphicRaycaster) return true;
            return false;
        }

        /// <summary>Returns false when the requested area cannot be reached; a new command replaces the old one.</summary>
        public bool MoveToWorldPosition(Vector3 destination)
        {
            if (mapView == null || mapView.Map == null || body == null || footprint == null) return false;
            StopMoving();
            Vector3 start = mapView.transform.InverseTransformPoint(new Vector3(body.position.x, body.position.y, worldZ));
            Vector3 target = mapView.transform.InverseTransformPoint(destination);
            Vector3 scale = mapView.transform.lossyScale;
            float mapScale = Mathf.Max(.001f, Mathf.Min(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            Vector3 playerScale = footprint.transform.lossyScale;
            float radius = footprint.radius * Mathf.Max(Mathf.Abs(playerScale.x), Mathf.Abs(playerScale.y));
            float clearance = (radius + navigationPadding) / mapScale;
            if (!MapPathfinder.TryFindPath(mapView.Map, new MapPoint(start.x, start.y),
                new MapPoint(target.x, target.y), clearance, path)) return false;
            if (path.Count == 0) return true;
            MapPoint last = path[path.Count - 1];
            Destination = mapView.transform.TransformPoint(new Vector3(last.X, last.Y, 0));
            return true;
        }

        private void FixedUpdate()
        {
            if (!IsMoving || mapView == null || body == null) return;
            MapPoint point = path[waypoint];
            Vector2 target = mapView.transform.TransformPoint(new Vector3(point.X, point.Y, 0));
            Vector2 current = body.position;
            Vector2 next = Vector2.MoveTowards(current, target, moveSpeed * Time.fixedDeltaTime);
            body.MovePosition(next);
            if ((target - current).sqrMagnitude <= arrivalDistance * arrivalDistance)
            {
                waypoint++;
                if (!IsMoving) body.velocity = Vector2.zero;
            }
        }

        public void StopMoving()
        {
            path.Clear(); waypoint = 0;
            if (body != null) { body.velocity = Vector2.zero; body.angularVelocity = 0; }
            Destination = transform.position;
        }

        private void LateUpdate() => UpdateSorting();

        private void UpdateSorting()
        {
            if (visual != null && mapView != null && mapView.Map != null)
                visual.sortingOrder = mapView.SortingOrderForWorldPosition(transform.position);
        }

        private void OnDisable() => StopMoving();
    }
}
