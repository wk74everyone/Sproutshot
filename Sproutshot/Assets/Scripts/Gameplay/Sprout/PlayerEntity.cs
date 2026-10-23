using UnityEngine;

namespace Sproutshot.Gameplay
{
    [RequireComponent(typeof(SimpleASC))]
    public class PlayerEntity : Entity
    {
        [SerializeField] private int _attributeId = 1;
        private RunContext _context;
        private PlayerAnimation _animation;

        private void OnEnable()
        {
            if (Application.isPlaying) BindContext();
        }

        protected override void Awake()
        {
            base.Awake();
            // 兼容还没有配置 ASC 的旧玩家预制体。
            if (ASC == null) ASC = gameObject.AddComponent<SimpleASC>();
        }

        private void Start()
        {
            if (!BindContext()) return;
            // 玩家由地图在资源就绪后生成。换实例仍使用本局已有的属性。
            if (!_context.PlayerContext.IsInitialized)
                InitialAttribute(_attributeId);
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
            if (_context != null && _context.PlayerEntity == this)
                _context.PlayerEntity = null;
            _context = null;
        }

        public override void InitialAttribute(int id)
        {
            if (_context == null && !BindContext()) return;
            _context.PlayerContext.InitialAttribute(id);
        }

        public override void TakeDamage(float damage)
        {
            if (!isActiveAndEnabled || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (_context != null && _context.PlayerEntity == this && _context.PlayerContext.IsInitialized)
            {
                _context.PlayerContext.TakeDamage(damage);
                Damaged();
            }
        }

        protected override void Damaged()
        {
            if (_animation == null) _animation = GetComponent<PlayerAnimation>();
            if (_animation != null) _animation.PlayDamage();
        }
    }
}
