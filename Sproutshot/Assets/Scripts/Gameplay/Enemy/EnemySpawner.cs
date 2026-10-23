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
        private float _spawnInterval;
        private float _timer;
        private bool _warnedClearance;

        private void Awake()
        {
            //_spawnInterval = RunContext.Current.LevelContext.SpawnInterval;
            _spawnInterval = 1f;
            _timer = _spawnInterval;
            ResolveNavigation();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            if (_timer <= 0f)
            {
                SpawnEntity();
                _timer = _spawnInterval;
            }
            else
            {
                _timer -= Time.deltaTime;
            }
        }

        private void SpawnEntity()
        {
            if (_entityList.Count == 0 || _entityList[0] == null) return;
            // 空间网格未就绪时等待下次刷怪，避免生成未登记的敌人。
            SpatialGrid spatialGrid = RunContext.Current?.SpatialGrid;
            if (spatialGrid == null || !spatialGrid.isActiveAndEnabled || !spatialGrid.IsInitialized) return;
            EnemyEntity prefab = _entityList[0];
            if (!TryGetSpawnPosition(prefab, out Vector3 position)) return;

            var entity = PoolManager.Instance.Get<EnemyEntity>(prefab, position, Quaternion.identity);
            // 所有生成的敌人使用本地图的共享流场，不依赖每个敌人自行搜索场景。
            entity.InitialAttribute(11);
            EnemyChase chase = entity.GetComponentInChildren<EnemyChase>();
            if (chase != null)
                chase.BindFlowField(_flowField);
            entity.BindSpatialGrid(spatialGrid);
        }

        private void ResolveNavigation()
        {
            if (_mapView == null) _mapView = GetComponentInParent<GridMapView>();
            if (_mapView != null && _flowField == null)
                _flowField = _mapView.GetComponentInChildren<FlowFieldGenerator>();
        }

        private bool TryGetSpawnPosition(EnemyEntity prefab, out Vector3 position)
        {
            position = transform.position;
            ResolveNavigation();
            // 流场尚未生成时等待下次刷怪，不在障碍中心先生成一个无法移动的敌人。
            if (_mapView == null || _mapView.Map == null || _flowField == null ||
                !_flowField.isActiveAndEnabled || !_flowField.HasFlowField) return false;

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

            if (CanSpawn(position, radius)) return true;

            // 优先朝出生区方向找点，沿核心周围逐圈搜索；物件、边界和连通性均需满足。
            Vector3 toward = _mapView.WorldSpawnPosition;
            float angle = Mathf.Atan2(toward.y - position.y, toward.x - position.x);
            CircleCollider2D core = GetComponent<CircleCollider2D>();
            Vector3 coreScale = transform.lossyScale;
            float coreRadius = core != null ? core.radius * Mathf.Max(Mathf.Abs(coreScale.x), Mathf.Abs(coreScale.y)) : 0;
            Vector3 origin = position;
            for (int ring = 0; ring < 8; ring++)
            {
                float distance = coreRadius + radius + .08f + ring * .35f;
                for (int sample = 0; sample < 16; sample++)
                {
                    float a = angle + sample * Mathf.PI * 2 / 16;
                    var candidate = new Vector3(origin.x + Mathf.Cos(a) * distance,
                        origin.y + Mathf.Sin(a) * distance, origin.z);
                    if (!CanSpawn(candidate, radius)) continue;
                    position = candidate;
                    return true;
                }
            }
            return false;
        }

        private bool CanSpawn(Vector3 position, float radius)
        {
            return _mapView.IsWalkableWorldPosition(position, radius) &&
                _flowField.TryGetMoveDirection(position, out _);
        }
    }
}
