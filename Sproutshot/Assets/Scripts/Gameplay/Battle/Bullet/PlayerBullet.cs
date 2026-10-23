using Sproutshot.Utils;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class PlayerBullet : BulletBase, IPoolable
    {
        [SerializeField, Min(.01f)] private float speed = 14f;
        [SerializeField, Min(.01f)] private float maxDistance = 12f;
        [SerializeField, Min(.01f)] private float lifeTime = 2f;

        [SerializeField] private float _radius = 0.2f;
        [SerializeField, Min(0)] private float _seekRadius = 8f;
        [SerializeField, Min(.02f)] private float _retargetInterval = .15f;
        private Vector3 flightDirection;
        private float travelled;
        private float elapsed;
        private bool flying;
        private float _damage;
        private EnemyEntity _target;
        private uint _targetVersion;
        private float _retargetTimer;
        private RunContext _context;

        public float SeekRadius => _seekRadius;

        private readonly List<EnemyEntity> _enemyList = new();

        /// <summary>兼容方向发射入口：优先锁定最近敌人，未找到时沿给定方向飞行并定期索敌。</summary>
        public override void Launch(Vector3 direction)
        {
            BeginFlight();
            flightDirection = new Vector3(direction.x, direction.y, 0).normalized;
            TryAcquireTarget();
            if (_target == null && flightDirection == Vector3.zero) Recycle();
        }

        /// <summary>发射器已经选好目标时使用，避免重复索敌；有效目标不会被更近敌人替换。</summary>
        public void Launch(EnemyEntity target)
        {
            BeginFlight();
            if (target == null || !target.isActiveAndEnabled || target.IsDead) { Recycle(); return; }
            SetTarget(target);
        }

        private void BeginFlight()
        {
            ResetAll();
            _context = RunContext.Current;
            _damage = _context?.PlayerContext?.CurrentDamage ?? 0;
            flying = true;
        }

        private bool IsTargetValid() => _target != null && _target.isActiveAndEnabled &&
            !_target.IsDead && _target.LifeVersion == _targetVersion;

        private void SetTarget(EnemyEntity target)
        {
            _target = target;
            _targetVersion = target.LifeVersion;
            _retargetTimer = 0;
            Vector3 delta = target.transform.position - transform.position;
            delta.z = 0;
            flightDirection = delta.sqrMagnitude > 0 ? delta.normalized : Vector3.up;
        }

        private void TryAcquireTarget()
        {
            _retargetTimer = Mathf.Max(.02f, _retargetInterval);
            SpatialGrid grid = _context?.SpatialGrid;
            if (grid != null && grid.isActiveAndEnabled && grid.IsInitialized &&
                grid.TryQueryNearest(transform.position, _seekRadius, out EnemyEntity target))
                SetTarget(target);
        }

        private void UpdateTarget(float deltaTime)
        {
            if (IsTargetValid()) return;
            _target = null;
            _targetVersion = 0;
            _retargetTimer -= deltaTime;
            if (_retargetTimer <= 0) TryAcquireTarget();
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            Fly(Time.deltaTime);
        }

        public override void Query()
        {
            // 只检查当前帧移动后的圆形范围；单体直接选最近目标，AOE 复用结果列表。
            if (!flying || !isActiveAndEnabled || _context != RunContext.Current) return;
            SpatialGrid spatialGrid = _context?.SpatialGrid;
            if (spatialGrid == null || !spatialGrid.isActiveAndEnabled || !spatialGrid.IsInitialized) return;

            if (!_isAOE)
            {
                if (!spatialGrid.TryQueryNearest(transform.position, _radius, out EnemyEntity nearest)) return;
                nearest.TakeDamage(_damage);
                Recycle();
                return;
            }

            spatialGrid.QueryCircle(transform.position, _radius, _enemyList);
            bool hit = false;
            for (int i = 0; i < _enemyList.Count; i++)
            {
                EnemyEntity enemy = _enemyList[i];
                if (enemy == null || !enemy.isActiveAndEnabled || enemy.IsDead) continue;
                enemy.TakeDamage(_damage);
                hit = true;
            }
            // 回池会清空结果列表，必须等全部目标处理完再调用。
            if (hit) Recycle();
        }

        private void Fly(float deltaTime)
        {
            if (!flying || deltaTime <= 0) return;
            if (_context != RunContext.Current) { Recycle(); return; }
            float stepTime = Mathf.Min(deltaTime, Mathf.Max(0, lifeTime - elapsed));
            float distance = Mathf.Min(speed * stepTime, Mathf.Max(0, maxDistance - travelled));
            UpdateTarget(stepTime);
            if (_target != null)
            {
                Vector3 delta = _target.transform.position - transform.position;
                delta.z = 0;
                float targetDistance = delta.magnitude;
                if (targetDistance > 0) flightDirection = delta / targetDistance;
                // 单步不越过目标中心，避免高速追踪弹在目标两侧来回摆动并漏判。
                distance = Mathf.Min(distance, targetDistance);
            }
            transform.position += flightDirection * distance;
            travelled += distance;
            elapsed += deltaTime;
            // 射程/寿命结束的最后一个落点也先检测，命中回池后不再重复归还。
            Query();
            if (flying && (travelled >= maxDistance || elapsed >= lifeTime)) Recycle();
        }

        /// <summary>后续命中逻辑也可调用此方法归还子弹。</summary>
        public void Recycle()
        {
            flying = false;
            PoolManager.Instance.Put(this);
        }

        public void ResetAll()
        {
            _enemyList.Clear();
            flightDirection = Vector3.zero;
            travelled = elapsed = 0;
            flying = false;
            _damage = 0;
            _target = null;
            _targetVersion = 0;
            _retargetTimer = 0;
            _context = null;
        }

        private void OnDisable() => ResetAll();
    }
}
