using Sproutshot.Utils;
using Sproutshot.MapGen;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private List<EnemyEntity> _entityList = new();
        [SerializeField] private GridMapView _mapView;
        [SerializeField] private FlowFieldGenerator _flowField;
        [SerializeField, Min(0)] private float _spawnPadding = .04f;
        [SerializeField] private Camera _spawnCamera;
        [SerializeField, Min(0)] private float _offscreenPadding = .8f;
        [SerializeField, Min(0)] private float _minPlayerDistance = 4f;
        [SerializeField, Min(1)] private int _batchSize = 10;
        private readonly Plane[] _frustumPlanes = new Plane[6];
        private int _scanIndex;
        private float _timer;
        private bool _warnedClearance;
        private int _warnedEnemyId;

        private void Awake()
        {
            _timer = 1f;
            ResolveNavigation();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            LevelRuntime level = RunContext.Current?.LevelContext;
            if (level == null || !level.CanSpawn) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                SpawnBatch();
                _timer = level.SpawnInterval;
            }
        }

        private void SpawnBatch()
        {
            // 同一帧完成一批，每个敌人独立选点并登记，后续选点会避开本批已生成的敌人。
            var level = RunContext.Current?.LevelContext;
            int count = level != null && !level.IsEndless ? level.BatchCount : Mathf.Max(1, _batchSize);
            for (int i = 0; i < count; i++)
                if (!SpawnEntity()) break;
        }

        private bool SpawnEntity()
        {
            RunContext context = RunContext.Current;
            LevelRuntime level = context?.LevelContext;
            if (level == null || !level.CanSpawn || !context.PlayerContext.IsInitialized ||
                context.PlayerContext.CurrentHp <= 0) return false;
            // 空间网格未就绪时等待下次刷怪，避免生成未登记的敌人。
            SpatialGrid spatialGrid = context.SpatialGrid;
            if (spatialGrid == null || !spatialGrid.isActiveAndEnabled || !spatialGrid.IsInitialized) return false;
            // 死亡和失活都会立即取消网格登记，死亡动画中的敌人不再占存活名额。
            if (spatialGrid.RegisteredCount >= level.MaxAliveCount) return false;
            int enemyId = level.EnemyIds[Random.Range(0, level.EnemyIds.Count)];
            EnemyEntity prefab = FindPrefab(enemyId);
            if (prefab == null) return false;
            if (!TryGetSpawnPosition(prefab, out Vector3 position)) return false;
            if (!level.TryConsumeSpawn(spatialGrid.RegisteredCount)) return false;

            EnemyEntity entity = null;
            try
            {
                entity = PoolManager.Instance.Get<EnemyEntity>(prefab, position, Quaternion.identity);
                entity.InitialAttribute(enemyId);
                entity.ApplyStageMultipliers(level.EnemyHpMultiplier, level.EnemyDamageMultiplier);
                // 所有生成的敌人使用本地图的共享流场。
                EnemyChase chase = entity.GetComponentInChildren<EnemyChase>();
                if (chase != null) chase.BindFlowField(_flowField);
                entity.BindSpatialGrid(spatialGrid);
                entity.BindSpawnLevel(level);
                return true;
            }
            catch (System.Exception exception)
            {
                level.CancelSpawn();
                if (entity != null) entity.Recycle();
                Debug.LogException(exception, this);
                return false;
            }
        }

        private EnemyEntity FindPrefab(int enemyId)
        {
            foreach (EnemyEntity prefab in _entityList)
                if (prefab != null && prefab.AttributeId == enemyId)
                {
                    _warnedEnemyId = 0;
                    return prefab;
                }
            if (_warnedEnemyId != enemyId)
            {
                Debug.LogError($"[EnemySpawner] 未配置属性 ID {enemyId} 对应的敌人预制体，请检查 Entity List 与 Attribute Id。", this);
                _warnedEnemyId = enemyId;
            }
            return null;
        }

        private void ResolveNavigation()
        {
            if (_mapView == null) _mapView = GetComponentInParent<GridMapView>();
            if (_mapView == null) _mapView = FindObjectOfType<GridMapView>();
            if (_mapView != null && _flowField == null)
                _flowField = _mapView.GetComponentInChildren<FlowFieldGenerator>();
            if (_spawnCamera == null) _spawnCamera = Camera.main;
        }

        private bool TryGetSpawnPosition(EnemyEntity prefab, out Vector3 position)
        {
            position = transform.position;
            ResolveNavigation();
            // 流场尚未生成时等待下次刷怪，不在障碍中心先生成一个无法移动的敌人。
            if (_mapView == null || _mapView.Map == null || _flowField == null ||
                !_flowField.isActiveAndEnabled || !_flowField.HasFlowField || _spawnCamera == null ||
                !_spawnCamera.isActiveAndEnabled) return false;

            Vector3 mapScale = _mapView.transform.lossyScale;
            float minScale = Mathf.Min(Mathf.Abs(mapScale.x), Mathf.Abs(mapScale.y));
            if (minScale <= 0) return false;
            float radius = _flowField.Clearance * minScale;
            CircleCollider2D footprint = prefab.GetComponentInChildren<CircleCollider2D>();
            if (footprint != null)
            {
                Vector3 scale = footprint.transform.lossyScale;
                Vector3 center = footprint.transform.TransformPoint(new Vector3(footprint.offset.x, footprint.offset.y, 0));
                Vector3 root = prefab.transform.position;
                float offset = new Vector2(center.x - root.x, center.y - root.y).magnitude;
                radius = footprint.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) + offset + Mathf.Max(0, _spawnPadding);
            }

            if (radius > _flowField.Clearance * minScale + .0001f)
            {
                if (!_warnedClearance)
                {
                    Debug.LogWarning("[EnemySpawner] 流场 Clearance 小于敌人半径加出生安全边距，请调整本地图的流场或为该体型绑定另一份流场。", this);
                    _warnedClearance = true;
                }
                return false;
            }
            _warnedClearance = false;

            GeometryUtility.CalculateFrustumPlanes(_spawnCamera, _frustumPlanes);
            Bounds visualBounds = new Bounds(Vector3.zero, new Vector3(radius * 2, radius * 2, .2f));
            foreach (var renderer in prefab.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (!renderer.enabled || renderer.sprite == null) continue;
                var spriteBounds = renderer.bounds;
                spriteBounds.center -= prefab.transform.position;
                visualBounds.Encapsulate(spriteBounds);
            }
            visualBounds.Expand(Mathf.Max(0, _offscreenPadding) * 2);
            var map = _mapView.Map;
            float clearance = radius / minScale;
            float minX = map.WalkableMinX + clearance, maxX = map.WalkableMaxX - clearance;
            float minY = map.WalkableMinY + clearance, maxY = map.WalkableMaxY - clearance;
            if (minX > maxX || minY > maxY) return false;
            // 优先地图四角的外围区域；每次随机选择一个角，不会在可见核心附近刷怪。
            for (int attempt = 0; attempt < 96; attempt++)
            {
                int corner = Random.Range(0, 4);
                float x = Random.Range(0f, .25f), y = Random.Range(0f, .25f);
                if ((corner & 1) != 0) x = 1 - x;
                if ((corner & 2) != 0) y = 1 - y;
                Vector3 candidate = _mapView.transform.TransformPoint(new Vector3(Mathf.Lerp(minX, maxX, x), Mathf.Lerp(minY, maxY, y), 0));
                if (!CanSpawnOffscreen(candidate, radius, visualBounds)) continue;
                position = candidate; return true;
            }
            // 四角受地形或镜头限制时，分批扫描导航点；整个地图可见时宁可等待，不在画面内兜底。
            var grid = map.GetNavigationGrid(_flowField.Clearance);
            for (int i = 0; i < Mathf.Min(256, grid.Count); i++)
            {
                int index = _scanIndex++ % grid.Count;
                if (_scanIndex == int.MaxValue) _scanIndex = 0;
                if (!grid.Walkable[index]) continue;
                var point = grid.Point(index);
                Vector3 candidate = _mapView.transform.TransformPoint(new Vector3(point.X, point.Y, 0));
                if (!CanSpawnOffscreen(candidate, radius, visualBounds)) continue;
                position = candidate; return true;
            }
            return false;
        }

        private bool CanSpawn(Vector3 position, float radius)
        {
            return _mapView.IsWalkableWorldPosition(position, radius) &&
                _flowField.TryGetMoveDirection(position, out _);
        }

        private bool CanSpawnOffscreen(Vector3 position, float radius, Bounds relativeBounds)
        {
            relativeBounds.center += position;
            if (GeometryUtility.TestPlanesAABB(_frustumPlanes, relativeBounds)) return false;
            var context = RunContext.Current;
            if (context?.PlayerEntity == null || (context.PlayerEntity.transform.position - position).sqrMagnitude <
                _minPlayerDistance * _minPlayerDistance) return false;
            return CanSpawn(position, radius) &&
                !context.SpatialGrid.TryQueryNearest(position, radius * 2, out _);
        }
    }
}
