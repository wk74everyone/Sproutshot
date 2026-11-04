using Sproutshot.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class PlayerBullet : BaseBullet, IPoolable
    {
        [SerializeField, Min(.01f)] private float speed = 14f;
        [SerializeField, Min(.01f)] private float maxDistance = 12f;
        [SerializeField, Min(.01f)] private float lifeTime = 2f;

        [SerializeField] private float _radius = 0.2f;
        [SerializeField, Min(0)] private float _seekRadius = 8f;
        private Vector3 flightDirection;
        private float travelled;
        private float elapsed;
        private bool flying;
        private EnemyEntity _target;
        private uint _targetVersion;
        private RunContext _context;
        private readonly BulletContext _effectContext = new();
        private readonly BulletEffectPipeline _pipeline = new();
        private ElementProjectileVisual _flightVisual;
        private SpiritLeafVisual _leafVisual;
        private readonly List<BulletBuffSnapshot> _snapshots = new();
        private readonly HashSet<(EnemyEntity enemy, uint life)> _hitLives = new();
        private readonly List<(EnemyEntity enemy, uint life, float fraction)> _sweptHits = new();
        private readonly List<Vector3> _outboundPath = new();
        private int _returnIndex;
        private EnemyEntity _returnDeparture, _lastHitTarget;
        private uint _returnDepartureLife, _lastHitLife;
        public Entity Owner => _effectContext.Owner;
        public float CurrentDamage => _effectContext.Damage;
        public IReadOnlyList<BulletBuffSnapshot> BuffSnapshots => _snapshots;
        public IReadOnlyList<BulletEffect> Effects => _pipeline.Effects;
        // 调试/兼容入口；具体计数与消耗归策略所有，子弹主体不判断 Buff ID。
        public int RemainingPierces => _pipeline.Find<PierceEffect>()?.RemainingCount ?? 0;

        public float SeekRadius => _seekRadius;
        public bool IsReturning => _effectContext.IsReturning;

        private readonly List<EnemyEntity> _enemyList = new();

        /// <summary>兼容方向发射入口：仅在发射时尝试锁定敌人，未找到时沿给定方向飞行。</summary>
        public override void Launch(Vector3 direction)
        {
            BeginFlight(RunContext.Current?.PlayerEntity, RunContext.Current?.PlayerContext?.CurrentDamage ?? 0);
            flightDirection = new Vector3(direction.x, direction.y, 0).normalized;
            TryAcquireTarget();
            if (_target == null && flightDirection == Vector3.zero) Recycle();
            else StartFlight();
        }

        /// <summary>发射器已经选好目标时使用，避免重复索敌；有效目标不会被更近敌人替换。</summary>
        public void Launch(EnemyEntity target)
        {
            Launch(RunContext.Current?.PlayerEntity, target);
        }

        public void Launch(Entity owner, EnemyEntity target) =>
            Launch(owner, target, RunContext.Current?.PlayerContext?.CurrentDamage ?? 0);

        /// <summary>伙伴使用自己的伤害，复用玩家子弹的追踪、命中与对象池。</summary>
        public void Launch(EnemyEntity target, float damage)
        {
            Launch(null, target, damage);
        }

        public void Launch(Entity owner, EnemyEntity target, float damage)
        {
            TryLaunch(owner, target, damage);
        }

        /// <summary>发射器传入本轮共享状态；只有真正开始飞行才返回成功。</summary>
        public bool TryLaunch(Entity owner, EnemyEntity target, float damage, BulletSalvo salvo = null)
        {
            BeginFlight(owner, damage, salvo);
            if (damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage) || target == null ||
                !target.isActiveAndEnabled || target.IsDead) { Recycle(); return false; }
            SetTarget(target);
            StartFlight();
            return true;
        }

        /// <summary>技能定向弹不索敌、不追踪、不继承选卡效果，复用现有命中管线和对象池。</summary>
        public bool TryLaunchDirected(Entity owner, Vector3 direction, float damage, int extraPierces = 0)
        {
            BeginFlight(owner, damage, copyOwnerBuffs: false);
            direction.z = 0;
            if (_context == null || owner == null || !owner.isActiveAndEnabled || damage <= 0 ||
                float.IsNaN(damage) || float.IsInfinity(damage) || float.IsNaN(direction.sqrMagnitude) ||
                float.IsInfinity(direction.sqrMagnitude) || direction.sqrMagnitude < .000001f)
            { Recycle(); return false; }
            flightDirection = direction.normalized;
            _effectContext.HomingEnabled = false;
            if (extraPierces > 0) _pipeline.Add(new PierceEffect(extraPierces));
            StartFlight();
            return true;
        }

        private void BeginFlight(Entity owner, float damage, BulletSalvo salvo = null, bool copyOwnerBuffs = true)
        {
            ResetAll();
            _context = RunContext.Current;
            _effectContext.Initialize(owner, _context, damage, salvo);
            AddBuffs(copyOwnerBuffs && owner != null ? owner.ASC : null);
        }

        private void StartFlight()
        {
            _pipeline.OnLaunch(_effectContext);
            if (_flightVisual == null && _effectContext.Elements != BulletElement.None)
                _flightVisual = gameObject.GetComponent<ElementProjectileVisual>() ?? gameObject.AddComponent<ElementProjectileVisual>();
            if (_flightVisual != null) _flightVisual.Play(_effectContext.Elements, flightDirection);
            // 只为携带回叶归途的子弹记录轨迹，普通子弹不产生逐帧路径数据。
            if (_pipeline.Find<ReturningLeafEffect>() != null) _outboundPath.Add(transform.position);
            flying = true;
        }

        private bool IsTargetValid() => _target != null && _target.isActiveAndEnabled &&
            !_target.IsDead && _target.LifeVersion == _targetVersion;

        private void SetTarget(EnemyEntity target)
        {
            _target = target;
            _targetVersion = target.LifeVersion;
            Vector3 delta = target.transform.position - transform.position;
            delta.z = 0;
            flightDirection = delta.sqrMagnitude > 0 ? delta.normalized : Vector3.up;
        }

        private void TryAcquireTarget()
        {
            SpatialGrid grid = _context?.SpatialGrid;
            if (grid != null && grid.isActiveAndEnabled && grid.IsInitialized &&
                grid.TryQueryNearest(transform.position, _seekRadius, out EnemyEntity target))
                SetTarget(target);
        }

        private void UpdateTarget()
        {
            if (_effectContext.HomingEnabled && IsTargetValid()) return;
            // 目标死亡、失活或被对象池复用后，只解除追踪，保留上一帧方向。
            // 飞行途中不再寻找新目标，避免已经发射的子弹突然拐弯。
            _target = null;
            _targetVersion = 0;
        }

        private void Update()
        {
            if (flying && (_context == null || _context != RunContext.Current || _context.LevelContext.IsStopped)) { Recycle(); return; }
            if (Time.timeScale <= 0f) return;
            Fly(Time.deltaTime);
        }

        private void LateUpdate()
        {
            if (flying && _flightVisual != null) _flightVisual.Tick(Time.deltaTime, flightDirection);
        }

        public override void Query()
        {
            // 只检查当前帧移动后的圆形范围；单体直接选最近目标，AOE 复用结果列表。
            if (!flying || !isActiveAndEnabled || _context != RunContext.Current) return;
            SpatialGrid spatialGrid = _context?.SpatialGrid;
            if (spatialGrid == null || !spatialGrid.isActiveAndEnabled || !spatialGrid.IsInitialized) return;

            if (!_isAOE)
            {
                QuerySegment(transform.position, transform.position);
                return;
            }

            spatialGrid.QueryCircle(transform.position, _radius, _enemyList);
            bool hit = false;
            for (int i = 0; i < _enemyList.Count; i++)
            {
                EnemyEntity enemy = _enemyList[i];
                if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead || !_hitLives.Add((enemy,enemy.LifeVersion))) continue;
                if (IsReturning && ReferenceEquals(enemy, _returnDeparture) && enemy.LifeVersion == _returnDepartureLife) continue;
                var areaHit = new BulletHitContext(enemy, enemy.LifeVersion, transform.position, _effectContext.Damage);
                if (_pipeline.ProcessHit(_effectContext, ref areaHit))
                {
                    hit = true; _lastHitTarget = enemy; _lastHitLife = areaHit.TargetLife;
                }
            }
            // 回池会清空结果列表，必须等全部目标处理完再调用。
            if (hit) FinishFlight(BulletFlightEndReason.HitLimit);
        }

        private void QuerySegment(Vector3 from, Vector3 to)
        {
            var grid = _context?.SpatialGrid;
            if (!flying || !isActiveAndEnabled || _context != RunContext.Current || grid == null || !grid.IsInitialized) return;
            Vector3 delta = to - from;
            float lengthSquared = delta.x * delta.x + delta.y * delta.y;
            grid.QueryCircle((from + to) * .5f, Mathf.Sqrt(lengthSquared) * .5f + _radius, _enemyList);
            _sweptHits.Clear();
            foreach (var enemy in _enemyList)
            {
                if (enemy == null || enemy.IsDead || _hitLives.Contains((enemy, enemy.LifeVersion))) continue;
                // 折返点仍重叠的目标先离开再允许命中，避免刚折返便在原地耗尽回程次数。
                if (IsReturning && ReferenceEquals(enemy, _returnDeparture) && enemy.LifeVersion == _returnDepartureLife) continue;
                Vector3 relative = enemy.transform.position - from;
                float t = lengthSquared > 0 ? Mathf.Clamp01((relative.x * delta.x + relative.y * delta.y) / lengthSquared) : 0;
                Vector3 offset = relative - delta * t;
                if (offset.x * offset.x + offset.y * offset.y <= _radius * _radius)
                    _sweptHits.Add((enemy, enemy.LifeVersion, t));
            }
            _sweptHits.Sort((a, b) => a.fraction.CompareTo(b.fraction));
            for (int i = 0; i < _sweptHits.Count; i++)
            {
                var hit = _sweptHits[i];
                if (hit.enemy == null || !hit.enemy.isActiveAndEnabled || hit.enemy.IsDead || hit.enemy.LifeVersion != hit.life ||
                    !_hitLives.Add((hit.enemy, hit.life))) continue;
                var hitContext = new BulletHitContext(hit.enemy, hit.life, from + delta * hit.fraction, _effectContext.Damage);
                if (!_pipeline.ProcessHit(_effectContext, ref hitContext)) continue;
                _lastHitTarget = hit.enemy; _lastHitLife = hit.life;
                if (!hitContext.ContinueFlight)
                {
                    transform.position = from + delta * hit.fraction;
                    FinishFlight(BulletFlightEndReason.HitLimit);
                    return; // 回池或切换返程会清空命中记录，立即停止本段遍历。
                }
                if (!_effectContext.HomingEnabled) { _target = null; _targetVersion = 0; }
            }
        }

        private void Fly(float deltaTime)
        {
            if (!flying || deltaTime <= 0) return;
            if (_context == null || _context != RunContext.Current || _context.LevelContext.IsStopped) { Recycle(); return; }
            if (IsReturning) { FlyReturn(deltaTime); return; }
            float stepTime = Mathf.Min(deltaTime, Mathf.Max(0, lifeTime - elapsed));
            float distance = Mathf.Min(speed * stepTime, Mathf.Max(0, maxDistance - travelled));
            UpdateTarget();
            if (_target != null)
            {
                Vector3 delta = _target.transform.position - transform.position;
                delta.z = 0;
                float targetDistance = delta.magnitude;
                if (targetDistance > 0) flightDirection = delta / targetDistance;
                // 单步不越过目标中心，避免高速追踪弹在目标两侧来回摆动并漏判。
                distance = Mathf.Min(distance, targetDistance);
            }
            Vector3 previous = transform.position;
            transform.position += flightDirection * distance;
            travelled += distance;
            elapsed += deltaTime;
            // 射程/寿命结束的最后一个落点也先检测，命中回池后不再重复归还。
            if (_isAOE) Query(); else QuerySegment(previous, transform.position);
            if (!flying || IsReturning) return;
            RecordPath();
            if (travelled >= maxDistance) FinishFlight(BulletFlightEndReason.Range);
            else if (elapsed >= lifeTime) FinishFlight(BulletFlightEndReason.Lifetime);
        }

        private void RecordPath()
        {
            if (_outboundPath.Count == 0) return;
            if ((transform.position - _outboundPath[_outboundPath.Count - 1]).sqrMagnitude > .00000001f)
                _outboundPath.Add(transform.position);
        }

        private void FinishFlight(BulletFlightEndReason reason)
        {
            if (!IsReturning)
            {
                RecordPath();
                if (_outboundPath.Count >= 2 && _pipeline.TryBeginReturn(_effectContext, reason))
                {
                    _returnIndex = _outboundPath.Count - 2;
                    _hitLives.Clear();
                    _target = null; _targetVersion = 0;
                    _returnDeparture = _lastHitTarget; _returnDepartureLife = _lastHitLife;
                    ClearDepartureIfSeparated();
                    elapsed = travelled = 0;
                    return;
                }
            }
            Recycle();
        }

        private void ClearDepartureIfSeparated()
        {
            if (_returnDeparture == null || !_returnDeparture.isActiveAndEnabled ||
                _returnDeparture.LifeVersion != _returnDepartureLife ||
                (transform.position - _returnDeparture.transform.position).sqrMagnitude > (_radius + .01f) * (_radius + .01f))
            {
                _returnDeparture = null; _returnDepartureLife = 0;
            }
        }

        private void FlyReturn(float deltaTime)
        {
            float stepTime = Mathf.Min(deltaTime, Mathf.Max(0, lifeTime - elapsed));
            float remaining = speed * stepTime;
            elapsed += deltaTime;
            while (flying && _returnIndex >= 0 && remaining > 0)
            {
                Vector3 previous = transform.position;
                Vector3 goal = _outboundPath[_returnIndex];
                float distance = Vector3.Distance(previous, goal);
                if (distance <= .00001f) { _returnIndex--; continue; }
                float move = Mathf.Min(distance, remaining);
                flightDirection = (goal - previous) / distance;
                transform.position = previous + flightDirection * move;
                travelled += move; remaining -= move;
                if (_isAOE) Query(); else QuerySegment(previous, transform.position);
                if (!flying) return;
                ClearDepartureIfSeparated();
                if (move >= distance) _returnIndex--;
            }
            if (flying && (_returnIndex < 0 || elapsed >= lifeTime)) Recycle();
        }

        /// <summary>后续命中逻辑也可调用此方法归还子弹。</summary>
        public void Recycle()
        {
            flying = false;
            PoolManager.Instance.Put(this);
        }

        public void ResetAll()
        {
            if (_flightVisual != null) _flightVisual.Clear();
            if (_leafVisual == null) _leafVisual = GetComponent<SpiritLeafVisual>();
            if (_leafVisual != null) _leafVisual.Clear();
            _enemyList.Clear();
            _sweptHits.Clear(); _hitLives.Clear();
            _outboundPath.Clear(); _returnIndex = -1;
            _returnDeparture = _lastHitTarget = null;
            _returnDepartureLife = _lastHitLife = 0;
            _pipeline.Clear(_effectContext);
            _snapshots.Clear();
            if (_asc != null) _asc.Clear();
            _effectContext.Reset();
            flightDirection = Vector3.zero;
            travelled = elapsed = 0;
            flying = false;
            _target = null;
            _targetVersion = 0;
            _context = null;
        }

        private void OnDisable() => ResetAll();

        /// <summary>发射前复制主人已叠好的子弹 Buff，ActiveBuff 独立，保留原有层数。</summary>
        public override void AddBuffs(SimpleASC ownerASC)
        {
            if (flying || _asc == null || ownerASC == _asc) return;
            _pipeline.Clear(_effectContext);
            _snapshots.Clear();
            _asc.Clear();
            // 快照是独立的值数据；ASC 副本便于查看本次携带的 Buff 和层数。
            if (ownerASC != null)
                foreach (var buff in ownerASC.ActiveBuffs)
                    if (buff.BuffDefinition.BuffType == cfg.BuffType.Bullet && BulletEffectFactory.Supports(buff.BuffDefinition.Id) &&
                        buff.StackCount > 0 && (buff.IsPermanent || buff.RemainingTime > 0))
                    {
                        _asc.AddSnapshot(buff);
                        _snapshots.Add(new BulletBuffSnapshot(buff));
                    }
            _pipeline.Build(_snapshots);
        }
    }
}
