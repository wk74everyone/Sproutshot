using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.MapGen
{
    public class FlowFieldGenerator : MonoBehaviour
    {
        [SerializeField] private GridMapView _mapView;
        [SerializeField] private MapPlayerSpawner _playerSpawner;
        [SerializeField] private Transform _target;
        [SerializeField] private bool _autoUpdate = true;
        // 包含安全边距的敌人半径，使用地图局部单位；不同体型应使用不同流场实例。
        [SerializeField, Min(0)] private float _clearance = .2f;
        [SerializeField, Min(.02f)] private float _rebuildInterval = .15f;
        // 到达实际目标的距离阈值，使用世界单位。
        [SerializeField, Min(0)] private float _arrivalDistance = .05f;
        private byte[][] _costField;
        private float[][] _integrationField;
        private Vector2[][] _flowField;
        private int[] _nextIndices;
        private byte[] _connections;
        private GridMapData _map;
        private MapNavigationGrid _grid;
        private MapPoint _targetLocal;
        private int _goalIndex = -1, _lastTargetIndex = -1;
        private float _nextRebuildTime;
        private bool _hasFlowField;
        private readonly List<FrontierEntry> _open = new List<FrontierEntry>(256);
        private static readonly int[] ColumnOffsets = { -1, 0, 1, -1, 1, -1, 0, 1 };
        private static readonly int[] RowOffsets = { -1, -1, -1, 0, 0, 1, 1, 1 };
        private const byte Blocked = byte.MaxValue;

        /// <summary>当前地图和通行半径的流场是否有效。</summary>
        public bool HasFlowField => _hasFlowField && IsNavigationCurrent();

        /// <summary>当前角色通行半径，使用地图局部单位。</summary>
        public float Clearance => _grid != null ? _grid.Clearance : _clearance;

        /// <summary>最近一次成功重建积分场和流向场的耗时，不包括导航连接初始化。</summary>
        public double LastRebuildMilliseconds { get; private set; }
        public int RebuildCount { get; private set; }

        /// <summary>关闭后可使用 Initialize / RebuildFlow 手动控制目标和计算时机。</summary>
        public bool AutoUpdate
        {
            get => _autoUpdate;
            set { _autoUpdate = value; _nextRebuildTime = Time.time; }
        }

        private void Awake() => ResolveReferences();

        private void OnEnable() => _nextRebuildTime = Time.time;

        private void OnDisable() => ClearResult();

        private void LateUpdate()
        {
            if (_autoUpdate) RefreshFlowField();
        }

        private void ResolveReferences()
        {
            if (_mapView == null) _mapView = GetComponentInParent<GridMapView>();
            if (_mapView != null && _playerSpawner == null)
                _playerSpawner = _mapView.GetComponent<MapPlayerSpawner>();
        }

        /// <summary>指定追踪对象；传 null 后恢复使用地图的 MapPlayerSpawner.Player。</summary>
        public void SetTarget(Transform target)
        {
            _target = target;
            ClearResult();
            _nextRebuildTime = 0;
        }

        /// <summary>
        /// 自动模式每帧调用，也可手动调用。相同目标格复用结果，换格后按时间间隔重算。
        /// force 为 true 时立即计算。目标缺失、地图未准备好或计算失败返回 false。
        /// </summary>
        public bool RefreshFlowField(bool force = false)
        {
            ResolveReferences();
            Transform target = _target != null ? _target :
                (_playerSpawner != null && _playerSpawner.Player != null ? _playerSpawner.Player.transform : null);
            if (_mapView == null || _mapView.Map == null || target == null)
            {
                ClearResult();
                return false;
            }

            if (!Finite(_clearance) || _clearance < 0 ||
                _clearance * 2 > Mathf.Min(_mapView.Map.WorldWidth, _mapView.Map.WorldHeight))
            {
                ClearResult();
                return false;
            }

            if (_map != _mapView.Map || _grid == null || _grid.Clearance != _clearance || !IsNavigationCurrent())
            {
                CostField(_mapView.Map, _clearance);
                force = true;
            }

            if (!force && Time.time < _nextRebuildTime) return HasFlowField;
            _nextRebuildTime = Time.time + (Finite(_rebuildInterval) ? Mathf.Max(.02f, _rebuildInterval) : .15f);

            Vector3 local = _mapView.transform.InverseTransformPoint(target.position);
            var point = new MapPoint(local.x, local.y);
            if (!Finite(local) || !_map.IsWalkable(point, _grid.Clearance))
            {
                ClearResult();
                return false;
            }

            int index = _grid.NearestIndex(point);
            if (!force && HasFlowField && index == _lastTargetIndex &&
                MapPathfinder.CanTraverse(_map, point, _grid.Point(_goalIndex), _grid.Clearance))
            {
                // 玩家仍在同一格时，仅更新最后一段的实际目标位置。
                _targetLocal = point;
                return true;
            }
            return IntegrationField(point);
        }

        /// <summary>地图 Placements 中的障碍变化后调用；下一次刷新会重建通行数据。</summary>
        public void InvalidateNavigation()
        {
            if (_map != null) _map.InvalidateNavigationGrids();
            ClearResult();
            _nextRebuildTime = 0;
        }

        private void ClearResult()
        {
            _hasFlowField = false;
            _goalIndex = _lastTargetIndex = -1;
        }

        private bool IsNavigationCurrent() => _map != null && _grid != null &&
            (_mapView == null || _mapView.Map == _map) &&
            ReferenceEquals(_map.GetNavigationGrid(_grid.Clearance), _grid);

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 point) => Finite(point.x) && Finite(point.y) && Finite(point.z);

        /// <summary>地图显示完成后绑定地图，并按敌人的局部半径生成代价场。</summary>
        public void Initialize(GridMapView mapView, float clearance)
        {
            if (mapView == null) throw new System.ArgumentNullException(nameof(mapView));
            if (mapView.Map == null)
                throw new System.InvalidOperationException("请在地图显示完成后初始化流场。");
            _mapView = mapView;
            _playerSpawner = mapView.GetComponent<MapPlayerSpawner>();
            CostField(mapView.Map, clearance);
        }

        private void CostField(GridMapData map, float clearance)
        {
            if (map == null) throw new System.ArgumentNullException(nameof(map));
            MapNavigationGrid navigation = map.GetNavigationGrid(clearance);
            bool rebuildConnections = !ReferenceEquals(navigation, _grid) || _connections == null;
            if (_mapView == null) _mapView = GetComponent<GridMapView>();
            _map = map;
            _grid = navigation;
            _clearance = clearance;
            // 代价场变化后，旧的积分和流向不再有效。
            ClearResult();
            _nextRebuildTime = 0;
            MapNavigationGrid grid = _grid;
            if (_costField == null || _costField.Length != grid.Columns || _costField[0].Length != grid.Rows)
            {
                _costField = new byte[grid.Columns][];
                for (int col = 0; col < grid.Columns; col++)
                    _costField[col] = new byte[grid.Rows];
            }

            for (int i = 0; i < grid.Count; i++)
            {
                int col = i % grid.Columns;
                int row = i / grid.Columns;

                _costField[col][row] = grid.Walkable[i] ? (byte)1 : Blocked;
            }
            if (rebuildConnections) BuildConnections();
        }

        private void BuildConnections()
        {
            // 缓存每个节点八条边的通行位。目标变化不会改变障碍，只在地图/半径变化时计算。
            _connections = new byte[_grid.Count];
            for (int index = 0; index < _grid.Count; index++)
            {
                if (!_grid.Walkable[index]) continue;
                int col = index % _grid.Columns, row = index / _grid.Columns;
                MapPoint point = _grid.Point(index);
                // 仅检查每条无向边一次，同时写入反向连接，避免重复扫掠。
                for (int d = 4; d < 8; d++)
                {
                    int x = col + ColumnOffsets[d], y = row + RowOffsets[d];
                    if (x < 0 || x >= _grid.Columns || y < 0 || y >= _grid.Rows) continue;
                    int next = y * _grid.Columns + x;
                    if (!_grid.Walkable[next] || !MapPathfinder.CanTraverse(_map, point, _grid.Point(next), _grid.Clearance)) continue;
                    _connections[index] |= (byte)(1 << d);
                    _connections[next] |= (byte)(1 << (7 - d));
                }
            }
        }

        private bool IntegrationField(MapPoint targetLocal)
        {
            ClearResult();
            _open.Clear();
            if (_map == null || _grid == null || _costField == null)
                return false;
            if (_mapView != null && _mapView.Map != _map) return false;

            if (!IsNavigationCurrent()) CostField(_map, _grid.Clearance);
            long started = System.Diagnostics.Stopwatch.GetTimestamp();

            // 分配积分场和流向场；尺寸相同时复用数组。
            if (_integrationField == null || _flowField == null || _integrationField.Length != _grid.Columns || _integrationField[0].Length != _grid.Rows)
            {
                _integrationField = new float[_grid.Columns][];
                _flowField = new Vector2[_grid.Columns][];
                _nextIndices = new int[_grid.Count];

                for (int col = 0; col < _grid.Columns; col++)
                {
                    _integrationField[col] = new float[_grid.Rows];
                    _flowField[col] = new Vector2[_grid.Rows];
                }
            }

            // 每次计算都清除上次结果
            for (int col = 0; col < _grid.Columns; col++)
            {
                for (int row = 0; row < _grid.Rows; row++)
                {
                    _integrationField[col][row] = float.PositiveInfinity;
                    // 目标格和不可达格保持零方向；失败时也不残留旧流向。
                    _flowField[col][row] = Vector2.zero;
                    _nextIndices[row * _grid.Columns + col] = -1;
                }
            }

            // 目标点的合理性判断
            if (float.IsNaN(targetLocal.X) || float.IsNaN(targetLocal.Y) || float.IsInfinity(targetLocal.X) || float.IsInfinity(targetLocal.Y) || !_map.IsWalkable(targetLocal, _grid.Clearance))
                return false;

            if (!TryFindGoal(targetLocal, out int target)) return false;
            int targetCol = target % _grid.Columns;
            int targetRow = target / _grid.Columns;

            // 开始计算积分场
            _integrationField[targetCol][targetRow] = 0f;
            Push(target, 0f);

            while (_open.Count > 0)
            {
                FrontierEntry entry = Pop();

                int current = entry.Index;
                int currentCol = current % _grid.Columns;
                int currentRow = current / _grid.Columns;

                // 该节点可能已经通过另一条路线获得更小的积分
                if (entry.Cost > _integrationField[currentCol][currentRow])
                    continue;

                byte connections = _connections[current];
                for (int d = 0; d < 8; d++)
                {
                    if ((connections & (1 << d)) == 0) continue;
                    int nextCol = currentCol + ColumnOffsets[d];
                    int nextRow = currentRow + RowOffsets[d];
                    int next = nextRow * _grid.Columns + nextCol;

                    if (_costField[nextCol][nextRow] == Blocked)
                        continue;

                    bool diagonal =
                        nextCol != currentCol && nextRow != currentRow;

                    float distance = _grid.Step *
                        (diagonal ? 1.41421356f : 1f);

                    // 约定：格子代价作为“离开该格”的移动倍率
                    float candidate = entry.Cost +
                        distance * _costField[nextCol][nextRow];

                    if (candidate >= _integrationField[nextCol][nextRow])
                        continue;

                    _integrationField[nextCol][nextRow] = candidate;
                    _nextIndices[next] = current;
                    // 反向扩展时，邻居的前进方向应指向当前格。
                    _flowField[nextCol][nextRow] = new Vector2(
                        -ColumnOffsets[d], -RowOffsets[d]).normalized;
                    Push(next, candidate);
                }
            }

            _targetLocal = targetLocal;
            _goalIndex = target;
            _lastTargetIndex = _grid.NearestIndex(targetLocal);
            _hasFlowField = true;
            RebuildCount++;
            LastRebuildMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - started) *
                (1000d / System.Diagnostics.Stopwatch.Frequency);
            return true;
        }

        private readonly struct FrontierEntry
        {
            public readonly int Index;
            public readonly float Cost;
            public FrontierEntry(int index, float cost) { Index = index; Cost = cost; }
        }

        private static bool Earlier(FrontierEntry a, FrontierEntry b) =>
            a.Cost < b.Cost || (a.Cost == b.Cost && a.Index < b.Index);

        private void Push(int index, float cost)
        {
            var entry = new FrontierEntry(index, cost);
            int child = _open.Count;
            _open.Add(entry);
            while (child > 0)
            {
                int parent = (child - 1) / 2;
                if (!Earlier(entry, _open[parent])) break;
                _open[child] = _open[parent];
                child = parent;
            }
            _open[child] = entry;
        }

        private FrontierEntry Pop()
        {
            FrontierEntry first = _open[0], last = _open[_open.Count - 1];
            _open.RemoveAt(_open.Count - 1);
            if (_open.Count == 0) return first;
            int parent = 0;
            while (parent * 2 + 1 < _open.Count)
            {
                int child = parent * 2 + 1;
                if (child + 1 < _open.Count && Earlier(_open[child + 1], _open[child])) child++;
                if (!Earlier(_open[child], last)) break;
                _open[parent] = _open[child];
                parent = child;
            }
            _open[parent] = last;
            return first;
        }

        private bool TryFindGoal(MapPoint targetLocal, out int goal)
        {
            goal = -1;
            int nearest = _grid.NearestIndex(targetLocal);
            int col = nearest % _grid.Columns, row = nearest / _grid.Columns;
            float best = float.PositiveInfinity;
            // 目标可站立但最近的采样点可能被障碍占据，尝试附近可直达的节点。
            for (int y = Mathf.Max(0, row - 3); y <= Mathf.Min(_grid.Rows - 1, row + 3); y++)
            for (int x = Mathf.Max(0, col - 3); x <= Mathf.Min(_grid.Columns - 1, col + 3); x++)
            {
                if (_costField[x][y] == Blocked) continue;
                int index = y * _grid.Columns + x;
                MapPoint point = _grid.Point(index);
                float distance = MapPoint.DistanceSquared(point, targetLocal);
                if (distance >= best || !MapPathfinder.CanTraverse(_map, point, targetLocal, _grid.Clearance)) continue;
                goal = index;
                best = distance;
            }
            return goal >= 0;
        }

        /// <summary>世界坐标版本的手动重算入口；需要先 Initialize 或绑定 Inspector 中的地图。</summary>
        public bool RebuildFlow(Vector3 targetWorldPosition)
        {
            ResolveReferences();
            if (_mapView == null || _mapView.Map == null || !Finite(targetWorldPosition))
            {
                ClearResult();
                return false;
            }
            if (_map != _mapView.Map || _grid == null || _grid.Clearance != _clearance || !IsNavigationCurrent())
                CostField(_mapView.Map, _clearance);
            Vector3 local = _mapView.transform.InverseTransformPoint(targetWorldPosition);
            return IntegrationField(new MapPoint(local.x, local.y));
        }

        /// <summary>
        /// 获取导航格的地图局部流向。未生成、越界、目标格或不可达格返回零向量。
        /// </summary>
        public Vector2 GetFlowDirection(int col, int row)
        {
            if (!HasFlowField || _flowField == null || col < 0 || col >= _flowField.Length ||
                row < 0 || row >= _flowField[col].Length)
                return Vector2.zero;
            return _flowField[col][row];
        }

        /// <summary>
        /// 供敌人移动状态查询：输入世界位置，输出归一化的世界 XY 平面方向。
        /// 未生成、地图已替换、越界、障碍或不可达时返回 false，方向为零。
        /// 目标格内继续靠近实际目标，到达后返回 true 且方向为零。
        /// 攻击距离由敌人状态负责；直接移动 Transform 可使用 TryGetMovePosition 检查单步。
        /// </summary>
        public bool TryGetMoveDirection(Vector3 worldPosition, out Vector2 worldDirection)
        {
            return TryGetMoveDirection(worldPosition, out worldDirection, out _);
        }

        /// <summary>同时返回距本次移动节点的世界距离，供移动状态限制单步距离。</summary>
        public bool TryGetMoveDirection(Vector3 worldPosition, out Vector2 worldDirection, out float distance)
        {
            worldDirection = Vector2.zero;
            distance = 0;
            if (_mapView == null || !HasFlowField || !Finite(worldPosition))
                return false;

            Vector3 local = _mapView.transform.InverseTransformPoint(worldPosition);
            if (!Finite(local) ||
                !_map.ContainsPoint(new MapPoint(local.x, local.y), _grid.Clearance))
                return false;

            // NearestIndex 会夹到边缘，必须先检查地图边界。
            var from = new MapPoint(local.x, local.y);
            if (!TryFindReachableNode(from, out int index)) return false;
            int next = _nextIndices[index];
            bool approachingTarget = next < 0;
            MapPoint destination = next >= 0 ? _grid.Point(next) : _targetLocal;
            if (!MapPathfinder.CanTraverse(_map, from, destination, _grid.Clearance))
            {
                // 实际位置偏离格心时，直接朝下一格可能切过障碍；先回到当前格心。
                destination = _grid.Point(index);
                approachingTarget = false;
                if (!MapPathfinder.CanTraverse(_map, from, destination, _grid.Clearance)) return false;
            }

            Vector3 world = _mapView.transform.TransformPoint(new Vector3(destination.X, destination.Y, 0));
            Vector2 delta = new Vector2(world.x - worldPosition.x, world.y - worldPosition.y);
            float arrival = Finite(_arrivalDistance) ? Mathf.Max(0, _arrivalDistance) : .05f;
            if (approachingTarget && index == _goalIndex && delta.sqrMagnitude <= arrival * arrival) return true;
            distance = delta.magnitude;
            worldDirection = delta.normalized;
            return worldDirection.sqrMagnitude > 0;
        }

        /// <summary>
        /// 直接移动 Transform 的接口。maxDistance 使用世界单位；返回受地图障碍约束的下一位置。
        /// 步长不会超过下一导航节点，到达时返回原位置。失败同样保持原位置并返回 false。
        /// 仅检测地图 Placements 中的阻挡物，不处理敌人之间的分离或任意物理碰撞体。
        /// </summary>
        public bool TryGetMovePosition(Vector3 worldPosition, float maxDistance, out Vector3 nextPosition)
        {
            nextPosition = worldPosition;
            if (!Finite(maxDistance) || maxDistance < 0 ||
                !TryGetMoveDirection(worldPosition, out Vector2 direction, out float distance))
                return false;

            float step = Mathf.Min(maxDistance, distance);
            var candidate = new Vector3(worldPosition.x + direction.x * step,
                worldPosition.y + direction.y * step, worldPosition.z);
            if (!Finite(candidate)) return false;
            Vector3 from = _mapView.transform.InverseTransformPoint(worldPosition);
            Vector3 to = _mapView.transform.InverseTransformPoint(candidate);
            if (!MapPathfinder.CanTraverse(_map, new MapPoint(from.x, from.y), new MapPoint(to.x, to.y), _grid.Clearance))
                return false;

            nextPosition = candidate;
            return true;
        }

        /// <summary>沿锁定的世界方向短距离移动，用于扑撞；触及地图障碍时截短，不沿流场转弯。</summary>
        public bool TryGetStraightMovePosition(Vector3 worldPosition, Vector2 direction, float distance, out Vector3 nextPosition)
        {
            nextPosition = worldPosition;
            if (_mapView == null || !IsNavigationCurrent() || !Finite(worldPosition) ||
                !Finite(direction.x) || !Finite(direction.y) || !Finite(distance) || distance < 0) return false;
            Vector3 from = _mapView.transform.InverseTransformPoint(worldPosition);
            var start = new MapPoint(from.x, from.y);
            if (!_map.IsWalkable(start, _grid.Clearance)) return false;
            Vector2 step = direction.normalized * distance;
            Vector3 candidate = worldPosition + new Vector3(step.x, step.y, 0);
            if (!Finite(candidate)) return false;
            Vector3 to = _mapView.transform.InverseTransformPoint(candidate);
            var end = new MapPoint(to.x, to.y);
            float fraction = 1;
            if (!MapPathfinder.CanTraverse(_map, start, end, _grid.Clearance))
            {
                // 整段扫掠具有单调性：二分找到碰撞前可走的部分，最多检查八次。
                float low = 0, high = 1;
                for (int i = 0; i < 8; i++)
                {
                    float middle = (low + high) * .5f;
                    var point = new MapPoint(from.x + (to.x - from.x) * middle, from.y + (to.y - from.y) * middle);
                    if (MapPathfinder.CanTraverse(_map, start, point, _grid.Clearance)) low = middle;
                    else high = middle;
                }
                fraction = low;
            }
            nextPosition = worldPosition + new Vector3(step.x, step.y, 0) * fraction;
            return true;
        }

        private bool TryFindReachableNode(MapPoint from, out int index)
        {
            index = _grid.NearestIndex(from);
            int col = index % _grid.Columns, row = index / _grid.Columns;
            if (_costField[col][row] != Blocked && !float.IsPositiveInfinity(_integrationField[col][row]) &&
                MapPathfinder.CanTraverse(_map, from, _grid.Point(index), _grid.Clearance)) return true;

            // 连续位置可通行时，最近的离散采样点仍可能被障碍占据或处于另一连通区。
            // 只在这种情况下搜索附近可见节点，避免合法移动在格子边界上突然失败。
            index = -1;
            float best = float.PositiveInfinity;
            for (int y = Mathf.Max(0, row - 3); y <= Mathf.Min(_grid.Rows - 1, row + 3); y++)
            for (int x = Mathf.Max(0, col - 3); x <= Mathf.Min(_grid.Columns - 1, col + 3); x++)
            {
                if (_costField[x][y] == Blocked || float.IsPositiveInfinity(_integrationField[x][y])) continue;
                int candidate = y * _grid.Columns + x;
                MapPoint point = _grid.Point(candidate);
                float score = _integrationField[x][y] + Mathf.Sqrt(MapPoint.DistanceSquared(from, point));
                if (score >= best || !MapPathfinder.CanTraverse(_map, from, point, _grid.Clearance)) continue;
                index = candidate;
                best = score;
            }
            return index >= 0;
        }
    }
}
