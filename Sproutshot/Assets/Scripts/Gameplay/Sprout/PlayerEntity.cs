using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    [RequireComponent(typeof(SimpleASC))]
    public class PlayerEntity : Entity
    {
        public BulletLauncher BulletLauncher { get; private set; }
        private RunContext _context;
        private PlayerAnimation _animation;
        public int ProfessionId { get; private set; } = HeroProfessionCatalog.DefaultProfessionId;
        public override float MaxHp => _context != null && ReferenceEquals(_context, RunContext.Current) &&
            _context.PlayerEntity == this && _context.PlayerContext.IsInitialized ? _context.PlayerContext.MaxHp : 0;

        private void OnEnable()
        {
            if (Application.isPlaying && BindContext())
            {
                ASC.BuffsChanged += RefreshShield;
                RefreshShield();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            InitializeBuffVisuals();
            BulletLauncher = GetComponentInChildren<BulletLauncher>();
            // 兼容还没有配置 ASC 的旧玩家预制体。
            if (ASC == null) ASC = gameObject.AddComponent<SimpleASC>();
        }

        private void Start()
        {
            if (!BindContext()) return;
            // 玩家由地图在资源就绪后生成。换实例仍使用本局已有的属性。
            if (!_context.PlayerContext.IsInitialized)
            {
                var spawner = GetComponentInParent<Sproutshot.MapGen.MapPlayerSpawner>();
                if (spawner != null && spawner.ProfessionId > 0)
                {
                    ProfessionId = spawner.ProfessionId;
                    InitialAttribute(spawner.AttributeId);
                }
                else
                {
                    try
                    {
                        var profession = HeroProfessionCatalog.Equipped(CloudEconomy.Profile);
                        ProfessionId = profession.id;
                        InitialAttribute(profession.attributeId);
                    }
                    catch (System.Exception error) { Debug.LogError("[PlayerEntity] " + error.Message, this); }
                }
            }
            else ProfessionId = _context.PlayerContext.ProfessionId;
            GetComponent<Sproutshot.MapGen.PlayerClickToMove>()?.SetMoveSpeed(_context.PlayerContext.BaseSpeed);
        }

        private bool BindContext()
        {
            _context = RunContext.Current;
            if (_context == null)
            {
                Debug.LogError("[PlayerEntity] 缺少本局上下文，请在 GameScene 挂载 BattleRoot。", this);
                return false;
            }
            _context.PlayerEntity = this;
            return true;
        }

        private void OnDisable()
        {
            if (ASC != null) ASC.BuffsChanged -= RefreshShield;
            if (_context != null && _context.PlayerEntity == this)
                _context.PlayerEntity = null;
            _context = null;
        }

        public override void InitialAttribute(int id)
        {
            if (_context == null && !BindContext()) return;
            _context.PlayerContext.InitialAttribute(id, ProfessionId);
            GetComponent<Sproutshot.MapGen.PlayerClickToMove>()?.SetMoveSpeed(_context.PlayerContext.BaseSpeed);
            RefreshShield();
        }

        private void RefreshShield()
        {
            if (_context != null && ReferenceEquals(_context, RunContext.Current) && _context.PlayerEntity == this)
                _context.PlayerContext.RefreshShield(ASC);
        }

        private void Update()
        {
            if (Time.timeScale <= 0 || _context == null || !ReferenceEquals(_context, RunContext.Current) ||
                _context.PlayerEntity != this || _context.LevelContext.IsStopped) return;
            int shield = _context.PlayerContext.Shield;
            _context.PlayerContext.TickShield(Time.deltaTime);
            if (_context.PlayerContext.Shield > shield) AudioManager.Instance.PlaySfx(AudioId.Recovery);
        }

        public override void Heal(float amount)
        {
            if (!isActiveAndEnabled || _context == null || !ReferenceEquals(_context, RunContext.Current) ||
                _context.PlayerEntity != this || _context.LevelContext.IsStopped) return;
            int health = _context.PlayerContext.CurrentHp;
            _context.PlayerContext.Heal(amount);
            if (_context.PlayerContext.CurrentHp > health) AudioManager.Instance.PlaySfx(AudioId.Recovery);
        }

        public override void TakeDamage(float damage)
        {
            if (!isActiveAndEnabled || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (_context != null && ReferenceEquals(_context, RunContext.Current) && !_context.LevelContext.IsStopped &&
                _context.PlayerEntity == this && _context.PlayerContext.IsInitialized &&
                _context.PlayerContext.CurrentHp > 0)
            {
                int health = _context.PlayerContext.CurrentHp;
                _context.PlayerContext.TakeDamage(damage, out bool isShield);
                if (isShield) AudioManager.Instance.PlaySfx(AudioId.ShieldBlock);
                if (!isShield && _context.PlayerContext.CurrentHp < health)
                {
                    AudioManager.Instance.PlaySfx(AudioId.PlayerHurt);
                    Damaged();
                }
            }
        }

        protected override void Damaged()
        {
            if (_animation == null) _animation = GetComponent<PlayerAnimation>();
            if (_animation != null) _animation.PlayDamage();
        }
    }
}
