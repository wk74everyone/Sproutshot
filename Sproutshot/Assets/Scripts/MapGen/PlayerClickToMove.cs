using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sproutshot.MapGen
{
    /// <summary>WASD/虚拟摇杆方向移动；沿用地图的连续圆形通行检测。</summary>
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
        private Vector2 joystickInput;
        private Vector2 directionInput;
        private bool inputEnabled = true;
        private bool hasFocus = true;
        private bool suspended;

        public float MoveSpeed => moveSpeed;
        public void SetMoveSpeed(float value)
        {
            if (value > 0 && !float.IsNaN(value) && !float.IsInfinity(value)) moveSpeed = value;
        }
        public bool IsMoving => directionInput.sqrMagnitude > .0001f || waypoint < path.Count;
        /// <summary>最近一个物理步经过地图阻挡修正后的移动速度，供表现判断实际移动。</summary>
        public Vector2 ActualVelocity { get; private set; }
        public bool CanAcceptInput => inputEnabled && hasFocus && !suspended && isActiveAndEnabled &&
            Time.timeScale > 0 && mapView != null && mapView.Map != null;
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
            if (!CanAcceptInput) { StopMoving(); return; }
            Vector2 keyboard = new Vector2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0));
            directionInput = Vector2.ClampMagnitude(keyboard + joystickInput, 1);
            if (directionInput.sqrMagnitude > .0001f) { path.Clear(); waypoint = 0; }
        }

        public void SetJoystickInput(Vector2 direction)
        {
            if (!CanAcceptInput || float.IsNaN(direction.x) || float.IsNaN(direction.y) ||
                float.IsInfinity(direction.x) || float.IsInfinity(direction.y)) direction = Vector2.zero;
            joystickInput = Vector2.ClampMagnitude(direction, 1);
            directionInput = joystickInput;
            path.Clear(); waypoint = 0;
        }

        public void SetInputEnabled(bool enabled) { inputEnabled = enabled; if (!enabled) StopMoving(); }

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
            if (!CanAcceptInput || body == null) { StopMoving(); return; }
            if (directionInput.sqrMagnitude > .0001f)
            {
                Vector2 origin = body.position;
                Vector2 delta = directionInput * (moveSpeed * Time.fixedDeltaTime);
                Vector2 resolved = ResolveStep(origin, delta);
                ActualVelocity = (resolved - origin) / Time.fixedDeltaTime;
                body.MovePosition(resolved);
                Destination = new Vector3(resolved.x, resolved.y, worldZ);
                return;
            }
            if (waypoint >= path.Count) { ActualVelocity = Vector2.zero; body.velocity = Vector2.zero; return; }
            MapPoint point = path[waypoint];
            Vector2 target = mapView.transform.TransformPoint(new Vector3(point.X, point.Y, 0));
            Vector2 current = body.position;
            Vector2 next = Vector2.MoveTowards(current, target, moveSpeed * Time.fixedDeltaTime);
            ActualVelocity = (next - current) / Time.fixedDeltaTime;
            body.MovePosition(next);
            if ((target - current).sqrMagnitude <= arrivalDistance * arrivalDistance)
            {
                waypoint++;
                if (waypoint >= path.Count) body.velocity = Vector2.zero;
            }
        }

        public void StopMoving()
        {
            ActualVelocity = Vector2.zero;
            joystickInput = directionInput = Vector2.zero;
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
        private void OnApplicationFocus(bool focused) { hasFocus = focused; if (!focused) StopMoving(); }
        private void OnApplicationPause(bool paused) { suspended = paused; if (paused) StopMoving(); }

        private Vector2 ResolveStep(Vector2 from, Vector2 delta)
        {
            if (CanTraverse(from, from + delta)) return from + delta;
            // 被阻挡时只沿一个可通行轴滑动，避免对角线和贴墙加速。
            Vector2 first = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? new Vector2(delta.x, 0) : new Vector2(0, delta.y);
            Vector2 second = delta - first;
            if (first.sqrMagnitude > .000001f && CanTraverse(from, from + first)) return from + first;
            if (second.sqrMagnitude > .000001f && CanTraverse(from, from + second)) return from + second;
            float low = 0, high = 1;
            for (int i = 0; i < 12; i++)
            {
                float middle = (low + high) * .5f;
                if (CanTraverse(from, from + delta * middle)) low = middle; else high = middle;
            }
            return from + delta * low;
        }

        private bool CanTraverse(Vector2 from, Vector2 to)
        {
            if (footprint == null || mapView == null || mapView.Map == null) return false;
            Vector3 offset = footprint.transform.TransformPoint(footprint.offset) - transform.position;
            Vector3 start = mapView.transform.InverseTransformPoint(new Vector3(from.x, from.y, worldZ) + offset);
            Vector3 end = mapView.transform.InverseTransformPoint(new Vector3(to.x, to.y, worldZ) + offset);
            Vector3 actorScale = footprint.transform.lossyScale, mapScale = mapView.transform.lossyScale;
            float scale = Mathf.Min(Mathf.Abs(mapScale.x), Mathf.Abs(mapScale.y));
            if (scale <= .0001f) return false;
            float radius = (footprint.radius * Mathf.Max(Mathf.Abs(actorScale.x), Mathf.Abs(actorScale.y)) + navigationPadding) / scale;
            return MapPathfinder.CanTraverse(mapView.Map, new MapPoint(start.x, start.y), new MapPoint(end.x, end.y), radius);
        }
    }
}
