using Sproutshot.Utils;
using Sproutshot.MapGen;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>无敌的出战伙伴。自身攻击独立成长，不继承玩家最终伤害。</summary>
    [DisallowMultipleComponent]
    public sealed class SpiritEntity : Entity
    {
        [SerializeField] private SpriteRenderer _renderer;
        [SerializeField] private PlayerBullet _bullet;
        [SerializeField, Min(.1f)] private float _visualHeight = .6f;
        [SerializeField] private Vector2 _formationOffset = new Vector2(-.8f, -.35f);
        [SerializeField, Min(.1f)] private float _followSpeed = 5f;
        [SerializeField, Min(.05f)] private float _stopDistance = .12f;
        [SerializeField, Min(1f)] private float _teleportDistance = 8f;
        [SerializeField, Min(.1f)] private float _attackRange = 6f;
        [SerializeField, Min(.1f)] private float _attackInterval = 1f;
        [SerializeField, Min(.02f)] private float _searchInterval = .15f;
        private RunContext _context;
        private PlayerEntity _owner;
        private PlayerClickToMove _ownerMovement;
        private float _cooldown, _searchTimer;
        private EnemyEntity _target;
        private uint _targetVersion;
        private bool _initialized;

        private float _skillCD;

        public RunContext Context => _context;
        public PlayerBullet BulletPrefab => _bullet;
        public SpiritSkillReleaser Skills { get; private set; }
        public SpiritAnimation Animation { get; private set; }
        /// <summary>当前状态这一帧的实际跟随速度；瞬移不算奔跑。</summary>
        public Vector2 ActualVelocity { get; private set; }
        /// <summary>读取不改变基础冷却；卡牌通过玩家 ASC 的技能冷却属性生效。</summary>
        public float SkillCD => GetSkillCooldown(_skillCD);
        public float GetSkillCooldown(float baseCooldown) => _owner == null || _owner.ASC == null
            ? baseCooldown : _owner.ASC.Attributes.GetValue(baseCooldown, cfg.ValueBonusType.JLSkillCD,
                (value, bonus) => value * Mathf.Max(.01f, 1f - bonus));
        public int SpiritId { get; private set; }
        public int Level { get; private set; }
        public float CurrentDamage { get; private set; }
        public PlayerEntity Owner => _owner;
        public Vector3 FormationPosition => _owner == null ? transform.position : _owner.transform.position + (Vector3)_formationOffset;
        public bool CanAct => _initialized && _context != null && _context == RunContext.Current && _owner != null &&
            _owner.isActiveAndEnabled && _context.PlayerEntity == _owner && _context.PlayerContext.IsInitialized &&
            _context.PlayerContext.CurrentHp > 0 && !_context.LevelContext.IsStopped;
        public bool NeedsFollow => CanAct && (FormationPosition - transform.position).sqrMagnitude > _stopDistance * _stopDistance;
        public bool TargetValid => CanAct && _target != null && _target.isActiveAndEnabled && !_target.IsDead &&
            _target.LifeVersion == _targetVersion && (_target.transform.position - transform.position).sqrMagnitude <= _attackRange * _attackRange &&
            _context.SpatialGrid != null && _context.SpatialGrid.IsVisibleToCamera(_target, Camera.main);
        public bool CanShoot => CanAct && _bullet != null && _cooldown <= 0;

        public void Initialize(RunContext context, PlayerEntity owner, int id, int level, Sprite portrait)
        {
            _context = context; _owner = owner;
            _ownerMovement = owner != null ? owner.GetComponent<PlayerClickToMove>() : null;
            Level = Mathf.Max(1, level);
            InitialAttribute(id);
            if (_renderer == null) _renderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_renderer != null && portrait != null) _renderer.sprite = portrait;
            Animation = GetComponent<SpiritAnimation>();
            if (Animation != null) Animation.Bind(this, _renderer);
            if (_renderer != null && _renderer.sprite != null && _renderer.transform != transform)
                _renderer.transform.localScale = Vector3.one * (_visualHeight / Mathf.Max(.01f, _renderer.sprite.bounds.size.y));
            transform.position = FormationPosition;
            UpdateSorting();
            _target = null; _cooldown = _searchTimer = 0; ActualVelocity = Vector2.zero;
            _initialized = true;
            Skills = GetComponent<SpiritSkillReleaser>();
            if (Skills == null) Skills = gameObject.AddComponent<SpiritSkillReleaser>();
            Skills.Initialize(this, LubanConfigManager.Instance.Tables);
        }

        public override void InitialAttribute(int id)
        {
            var tables = LubanConfigManager.Instance.Tables;
            var attribute = tables.EntityTbAttribute.Get(id);
            var growth = tables.EntityTbGrowth.Get(id);
            // 兼容当前单技能释放器：冷却已迁移到技能表，使用列表中的首个技能。
            _skillCD = attribute.OwnedSkill.Count > 0
                ? tables.EntityTbSkill.GetOrDefault(attribute.OwnedSkill[0])?.SkillCD ?? 0f : 0f;
            SpiritId = id;
            CurrentDamage = attribute.Damage + growth.SelfDamage * Mathf.Max(1, Level);
        }

        internal void TickTimers(float deltaTime)
        {
            _cooldown = Mathf.Max(0, _cooldown - deltaTime);
            _searchTimer = Mathf.Max(0, _searchTimer - deltaTime);
        }

        public void FollowOwner(float deltaTime)
        {
            ActualVelocity = Vector2.zero;
            if (!CanAct || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            var destination = FormationPosition;
            float distance = Vector3.Distance(transform.position, destination);
            if (distance > _teleportDistance) transform.position = destination;
            else if (distance > _stopDistance)
            {
                Vector3 before = transform.position;
                transform.position = Vector3.MoveTowards(transform.position, destination, _followSpeed * deltaTime);
                ActualVelocity = (transform.position - before) / deltaTime;
            }
        }

        internal void BeginMotionFrame() => ActualVelocity = Vector2.zero;

        private void LateUpdate() => UpdateSorting();

        private void UpdateSorting()
        {
            var map = _ownerMovement != null ? _ownerMovement.MapView : null;
            if (_renderer != null && map != null && map.Map != null)
                _renderer.sortingOrder = map.SortingOrderForWorldPosition(transform.position) + 1;
        }

        public bool TryAcquireTarget()
        {
            if (!CanShoot) return false;
            if (TargetValid) return true;
            _target = null;
            if (_searchTimer > 0) return false;
            _searchTimer = _searchInterval;
            var grid = _context.SpatialGrid;
            if (grid == null || !grid.isActiveAndEnabled || !grid.IsInitialized ||
                !grid.TryQueryNearestVisible(transform.position, _attackRange, Camera.main, out var target)) return false;
            _target = target; _targetVersion = target.LifeVersion;
            return true;
        }

        public bool TryShoot()
        {
            if (!CanShoot || !TargetValid) return false;
            var bullet = PoolManager.Instance.Get(_bullet, transform.position + Vector3.up * .25f, Quaternion.identity);
            if (!bullet.TryLaunch(this, _target, CurrentDamage)) return false;
            AudioManager.Instance.PlaySfx(AudioId.SeedShoot, .7f);
            _cooldown = _attackInterval;
            return true;
        }

        // 伙伴不参与敌人的受击目标和生命系统。
        public override void TakeDamage(float damage) { }
        protected override void Damaged() { }
        private void OnDisable()
        {
            _initialized = false; _target = null;
            ActualVelocity = Vector2.zero;
            if (Animation != null) Animation.Clear();
            if (Skills != null) Skills.Clear();
            if (_context != null && _context.SpiritEntity == this) _context.SpiritEntity = null;
        }
    }
}
