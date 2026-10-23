using Sproutshot.MapGen;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GridMapGenerator), typeof(GridMapView))]
    public class SpatialGrid : MonoBehaviour
    {
        private GridMapGenerator _generator;
        private GridMapView _mapView;
        private GridMapData _map;
        private RunContext _context;

        // 每个地图格子里有哪些敌人
        private List<EnemyEntity>[] _buckets;

        // 每个敌人当前在哪个格子，便于跨格更新和移除
        private readonly Dictionary<EnemyEntity, int> _entityCells = new();

        public bool IsInitialized => _mapView != null && _map != null && _buckets != null && _mapView.Map == _map;
        public int RegisteredCount => _entityCells.Count;

        private void OnEnable()
        {
            _generator = GetComponent<GridMapGenerator>();
            _mapView = GetComponent<GridMapView>();
            if (_generator == null || _mapView == null)
            {
                Debug.LogError("[SpatialGrid] 需要与 GridMapGenerator、GridMapView 挂在同一个物体上。", this);
                return;
            }

            _generator.MapGenerated += OnMapGenerated;

            // 兼容地图生成后才启用组件；同一张地图重新启用时保留已有登记。
            if (_mapView.Map != null)
                OnMapGenerated(_mapView.Map);
        }

        private void OnDisable()
        {
            if (_generator != null)
                _generator.MapGenerated -= OnMapGenerated;
            if (_context != null && _context.SpatialGrid == this)
                _context.SpatialGrid = null;
            _context = null;
        }

        private void OnMapGenerated(GridMapData map)
        {
            // 只接收已经显示到当前 MapView 的地图，避免重复通知清空敌人登记。
            if (map == null || _mapView.Map != map) return;
            if (!IsInitialized)
                Initialize(_mapView);

            // 即使网格已经初始化，重新启用时也恢复对局引用；独立地图预览允许没有对局。
            RunContext context = RunContext.Current;
            if (_context != null && _context != context && _context.SpatialGrid == this)
                _context.SpatialGrid = null;
            _context = context;
            if (_context != null)
                _context.SpatialGrid = this;
        }

        public void Initialize(GridMapView mapView)
        {
            if (mapView == null)
                throw new System.ArgumentNullException(nameof(mapView));

            if (mapView.Map == null)
                throw new System.InvalidOperationException("请在地图生成完成后初始化。");

            _mapView = mapView;
            _map = mapView.Map;

            // 重新初始化时，旧桶和字典需要一起重置
            _entityCells.Clear();
            _buckets = new List<EnemyEntity>[_map.Cells.Length];

            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = new List<EnemyEntity>();
        }

        // 将敌人加入对应桶，记录索引；防止重复登记
        public void Register(EnemyEntity enemy)
        {
            if (ReferenceEquals(enemy, null)) return;
            if (!IsInitialized || enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead ||
                !TryGetCellIndex(enemy.transform.position, out int index))
            {
                Unregister(enemy);
                return;
            }

            if (_entityCells.TryGetValue(enemy, out int previous))
            {
                if (previous == index) return;
                _buckets[previous].Remove(enemy);
                _entityCells[enemy] = index;
            }
            else
            {
                _entityCells.Add(enemy, index);
            }
            _buckets[index].Add(enemy);
        }

        // 从桶和字典中移除；重复调用也安全
        public void Unregister(EnemyEntity enemy)
        {
            // Unity 对已销毁对象的 == null 会为 true；仍需用其原引用移除索引。
            if (ReferenceEquals(enemy, null) || !_entityCells.TryGetValue(enemy, out int index)) return;
            _entityCells.Remove(enemy);
            if (_buckets != null && index >= 0 && index < _buckets.Length)
                _buckets[index].Remove(enemy);
        }

        // 格子改变时，从旧桶移到新桶
        public void UpdatePosition(EnemyEntity enemy)
        {
            // 复用登记逻辑：同格不变，跨格迁移；死亡、失活或越界则移除。
            // 未登记的有效敌人也可以通过此入口加入。
            Register(enemy);
        }

        /// <summary>
        /// 查询世界 XY 平面圆内的存活敌人中心，radius 使用世界单位，不包含敌人身体半径。
        /// 清空并复用调用方的结果列表；移动后需要先 UpdatePosition，保证桶索引是最新的。
        /// 圆心可以在地图外，只要查询范围与地图相交。
        /// </summary>
        public void QueryCircle(Vector3 world, float radius, List<EnemyEntity> results)
        {
            if (results == null) throw new System.ArgumentNullException(nameof(results));
            results.Clear();
            QueryCircleInternal(world, radius, results, out _);
        }

        /// <summary>查询世界 XY 圆内最近的存活敌人中心，不生成列表、不排序；等距时保留先找到的敌人。</summary>
        public bool TryQueryNearest(Vector3 world, float radius, out EnemyEntity nearest)
        {
            QueryCircleInternal(world, radius, null, out nearest);
            return nearest != null;
        }

        private void QueryCircleInternal(Vector3 world, float radius, List<EnemyEntity> results, out EnemyEntity nearest)
        {
            nearest = null;
            if (!IsInitialized || !Finite(world) || !Finite(radius) || radius < 0) return;

            // 将世界圆的包围盒四角转为地图局部坐标；支持地图平移、XY 缩放和 Z 轴旋转。
            Transform mapTransform = _mapView.transform;
            Vector3 a = mapTransform.InverseTransformPoint(new Vector3(world.x - radius, world.y - radius, world.z));
            Vector3 b = mapTransform.InverseTransformPoint(new Vector3(world.x - radius, world.y + radius, world.z));
            Vector3 c = mapTransform.InverseTransformPoint(new Vector3(world.x + radius, world.y - radius, world.z));
            Vector3 d = mapTransform.InverseTransformPoint(new Vector3(world.x + radius, world.y + radius, world.z));
            int left = 0, right = _map.Width - 1, bottom = 0, top = _map.Height - 1;
            if (Finite(a) && Finite(b) && Finite(c) && Finite(d))
            {
                float minX = System.Math.Min(System.Math.Min(a.x, b.x), System.Math.Min(c.x, d.x));
                float maxX = System.Math.Max(System.Math.Max(a.x, b.x), System.Math.Max(c.x, d.x));
                float minY = System.Math.Min(System.Math.Min(a.y, b.y), System.Math.Min(c.y, d.y));
                float maxY = System.Math.Max(System.Math.Max(a.y, b.y), System.Math.Max(c.y, d.y));
                if (maxX < _map.MinX || minX > _map.MaxX || maxY < _map.MinY || minY > _map.MaxY) return;
                // 先裁剪到地图，避免极大查询范围转换为整数坐标时溢出。
                GridCoord first = _map.NearestCoordinate(new MapPoint(
                    System.Math.Max(minX, _map.MinX), System.Math.Max(minY, _map.MinY)));
                GridCoord last = _map.NearestCoordinate(new MapPoint(
                    System.Math.Min(maxX, _map.MaxX), System.Math.Min(maxY, _map.MaxY)));
                left = first.Col; right = last.Col; bottom = first.Row; top = last.Row;
            }

            // 极大但有限的半径可能使包围盒溢出，此时扫描全部桶，仍用 double 做精确筛选。
            double radiusSquared = (double)radius * radius;
            double nearestDistanceSquared = radiusSquared;
            for (int row = bottom; row <= top; row++)
                for (int col = left; col <= right; col++)
                {
                    List<EnemyEntity> bucket = _buckets[row * _map.Width + col];
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        EnemyEntity enemy = bucket[i];
                        if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) continue;
                        Vector3 position = enemy.transform.position;
                        if (!Finite(position)) continue;
                        double dx = (double)position.x - world.x, dy = (double)position.y - world.y;
                        double distanceSquared = dx * dx + dy * dy;
                        if (distanceSquared > radiusSquared) continue;
                        if (results != null) results.Add(enemy);
                        else if (nearest == null || distanceSquared < nearestDistanceSquared)
                        {
                            nearest = enemy;
                            nearestDistanceSquared = distanceSquared;
                        }
                    }
                }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 point) => Finite(point.x) && Finite(point.y) && Finite(point.z);

        private bool TryGetCellIndex(Vector3 world, out int index)
        {
            index = -1;

            if (!IsInitialized || !Finite(world))
                return false;

            Vector3 local = _mapView.transform.InverseTransformPoint(world);
            if (!Finite(local)) return false;
            var point = new MapPoint(local.x, local.y);

            if (!_map.ContainsPoint(point))
                return false;

            index = _map.IndexOf(_map.NearestCoordinate(point));
            return index >= 0;
        }
    }
}
