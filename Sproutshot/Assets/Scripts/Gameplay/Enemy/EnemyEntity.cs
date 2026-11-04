using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyEntity : Entity, IPoolable
    {
        [SerializeField, Min(1)] private int _attributeId = 11;
        /// <summary>预制体对应的属性表 ID，供刷怪点匹配关卡中的小怪类型。</summary>
        public int AttributeId => _attributeId;

        // 生命需要独立保存当前值；速度、攻击只保存配置中的基础值。
        private float _currentHp;
        private float _maxHp;
        private int _baseDefense;
        private float _baseSpeed;
        private float _baseDamage;
        /// <summary>读取时计算速度加成；状态到期或回池清空后自动恢复，最低为 0。</summary>
        public float CurrentSpeed => Mathf.Max(0f, ASC == null ? _baseSpeed :
            ASC.Attributes.GetValue(_baseSpeed, cfg.ValueBonusType.Speed, (value, bonus) => value * (1f + bonus)));
        public float CurrentDamage => _baseDamage;
        public float AttackDistance { get; private set; }
        private float _bodyRadius;
        private int _dropExp;
        private EnemyBrain _brain;
        private EnemyAnimation _animation;
        private SpatialGrid _spatialGrid;
        private LevelRuntime _spawnLevel;

        /// <summary>生成器绑定本次生命所属关卡，防止旧敌人的死亡计入新对局。</summary>
        internal void BindSpawnLevel(LevelRuntime level) => _spawnLevel = level;

        protected override void Awake()
        {
            base.Awake();
            InitializeBuffVisuals();
        }
        public float CurrentHp => _currentHp;
        public override float MaxHp => _maxHp;
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
            _spawnLevel = null;
            unchecked { LifeVersion++; }
            ClearBuffs();
            UnregisterFromSpatialGrid();
        }

        public void ResetAll()
        {
            _spawnLevel = null;
            unchecked { LifeVersion++; }
            ClearBuffs();
            IsDead = false;
            _currentHp = _maxHp;
        }

        public override void InitialAttribute(int id)
        {
            var attribute = LubanConfigManager.Instance.Tables.EntityTbAttribute.Get(id);
            _attributeId = id;
            _maxHp = attribute.Hp;
            _baseDefense = attribute.Defense;
            _baseDamage = attribute.Damage;
            _baseSpeed = attribute.Speed;
            AttackDistance = attribute.AttackDistance;
            _bodyRadius = attribute.BodyRadius;
            _dropExp = attribute.ExpDrop;

            ResetAll();
        }

        /// <summary>只在本次出生完成基础属性初始化后调用；回池重生会重新读表，不累计倍率。</summary>
        public void ApplyStageMultipliers(float hp, float damage)
        {
            if (hp <= 0 || damage <= 0 || float.IsNaN(hp) || float.IsInfinity(hp) || float.IsNaN(damage) || float.IsInfinity(damage))
                throw new System.ArgumentOutOfRangeException(nameof(hp));
            _maxHp = _currentHp = Mathf.Max(1, Mathf.Round(_maxHp * hp));
            _baseDamage = Mathf.Max(1, Mathf.Round(_baseDamage * damage));
        }

        public override void Heal(float amount)
        {
            if (IsDead || !isActiveAndEnabled || _currentHp <= 0 || amount <= 0 ||
                float.IsNaN(amount) || float.IsInfinity(amount)) return;
            _currentHp = Mathf.Min(_maxHp, _currentHp + amount);
        }

        public override void TakeDamage(float damage) => ApplyDamage(damage, true);
        public override void TakePeriodicDamage(float damage) => ApplyDamage(damage, false);

        private void ApplyDamage(float damage, bool playHitSound)
        {
            if (IsDead || !isActiveAndEnabled || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            damage = Mathf.Max(0.1f, damage - _baseDefense);
            _currentHp = Mathf.Max(0, _currentHp - damage);
            if (playHitSound) AudioManager.Instance.PlaySfx(AudioId.EnemyHit);
            Damaged();
            if (_currentHp > 0) return;

            // 先标记死亡，同一帧的多次命中也只会触发一次死亡流程。
            IsDead = true;
            ClearBuffs();
            UnregisterFromSpatialGrid();
            LevelRuntime level = _spawnLevel;
            _spawnLevel = null;
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_brain == null || !_brain.TryEnterDeath()) Recycle();
            level?.RecordDefeat();
        }

        /// <summary>池中生成的敌人归还池；直接放在场景中的敌人则失活。</summary>
        public void Recycle()
        {
            UnregisterFromSpatialGrid();
            if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }

        public void DropExp()
        {
            EventBus.Publish<OnEnemyDead>(new OnEnemyDead(_attributeId, _dropExp));
        }

        private void ClearBuffs()
        {
            if (ASC == null) ASC = GetComponent<SimpleASC>();
            if (ASC != null) ASC.Clear();
        }

        protected override void Damaged()
        {
            if (_animation == null) _animation = GetComponent<EnemyAnimation>();
            if (_animation != null) _animation.PlayDamage();
        }
    }
}
