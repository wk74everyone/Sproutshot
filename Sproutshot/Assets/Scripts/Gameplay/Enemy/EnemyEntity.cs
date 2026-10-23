using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyEntity : Entity, IPoolable
    {
        // 基础属性
        private float _currentHp;
        private float _maxHp;
        private float _currentDefense;
        private float _maxDefense;
        public float CurrentSpeed { get; private set; }
        private float _maxSpeed;
        public float CurrentDamage { get; private set; }
        private float _maxDamage;
        public float AttackDistance { get; private set; }
        private float _bodyRadius;
        private EnemyBrain _brain;
        private EnemyAnimation _animation;
        private SpatialGrid _spatialGrid;

        protected override void Awake()
        {
            base.Awake();
        }
        public float CurrentHp => _currentHp;
        /// <summary>配置表中的身体半径，使用角色根物体缩放前的局部单位。</summary>
        public float BodyRadius => _bodyRadius;
        public bool IsDead { get; private set; }
        /// <summary>失活或重置后改变，使追踪者能识别对象池中的下一次生命。</summary>
        public uint LifeVersion { get; private set; }

        /// <summary>生成并初始化属性后绑定，保存所属网格，避免回收时依赖全局对局引用。</summary>
        public void BindSpatialGrid(SpatialGrid spatialGrid)
        {
            if (_spatialGrid != spatialGrid)
                UnregisterFromSpatialGrid();
            _spatialGrid = spatialGrid;
            UpdateSpatialPosition();
        }

        /// <summary>追赶、扑撞等改变根物体位置后调用。</summary>
        public void UpdateSpatialPosition()
        {
            if (_spatialGrid != null && _spatialGrid.isActiveAndEnabled && _spatialGrid.IsInitialized)
                _spatialGrid.UpdatePosition(this);
        }

        private void UnregisterFromSpatialGrid()
        {
            if (_spatialGrid != null)
                _spatialGrid.Unregister(this);
            _spatialGrid = null;
        }

        private void OnDisable()
        {
            unchecked { LifeVersion++; }
            UnregisterFromSpatialGrid();
        }

        public void ResetAll()
        {
            unchecked { LifeVersion++; }
            IsDead = false;
            _currentHp = _maxHp;
            _currentDefense = _maxDefense;
            CurrentDamage = _maxDamage;
            CurrentSpeed = _maxSpeed;
        }

        public override void InitialAttribute(int id)
        {
            var attribute = LubanConfigManager.Instance.Tables.EntityTbAttribute.Get(id);
            _maxHp = attribute.Hp;
            _maxDefense = attribute.Defense;
            _maxDamage = attribute.Damage;
            _maxSpeed = attribute.Speed;
            AttackDistance = attribute.AttackDistance;
            _bodyRadius = attribute.BodyRadius;

            ResetAll();
        }

        public override void TakeDamage(float damage)
        {
            if (IsDead || !isActiveAndEnabled || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            damage = Mathf.Max(0.1f, damage - _currentDefense);
            _currentHp = Mathf.Max(0, _currentHp - damage);
            Damaged();
            if (_currentHp > 0) return;

            // 先标记死亡，同一帧的多次命中也只会触发一次死亡流程。
            IsDead = true;
            UnregisterFromSpatialGrid();
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_brain == null || !_brain.TryEnterDeath()) Recycle();
        }

        /// <summary>池中生成的敌人归还池；直接放在场景中的敌人则失活。</summary>
        public void Recycle()
        {
            UnregisterFromSpatialGrid();
            if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        protected override void Damaged()
        {
            if (_animation == null) _animation = GetComponent<EnemyAnimation>();
            if (_animation != null) _animation.PlayDamage();
        }
    }
}
