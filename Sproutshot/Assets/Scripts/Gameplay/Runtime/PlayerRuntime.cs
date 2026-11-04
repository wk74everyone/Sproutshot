using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class PlayerRuntime
    {
        // 基础属性
        public int CurrentHp { get; private set; }
        public int MaxHp { get; private set; }
        public int BaseDefense { get; private set; }
        public float BaseSpeed { get; private set; }
        private float _currentDamage;
        private float _maxDamage;
        private double _healingRemainder;
        // 其他的属性
        private readonly MossGuardState _mossGuard = new();
        public int Shield => _mossGuard.Charges;
        public int ShieldCapacity => _mossGuard.Capacity;
        public int BulletCount
        {
            get
            {
                var player = RunContext.Current?.PlayerEntity;
                // 尚未绑定 Buff 组件时，基础射击仍然可用。
                if (player == null || player.ASC == null) return 1;

                var asc = player.ASC;
                // 子弹数量，无论什么时候，最开始都是1个
                return asc.Attributes.GetValue(1, cfg.ValueBonusType.BulletCount, (value, bonus) => Mathf.RoundToInt(value + bonus));
            }
        }

        public float ExpAddition
        {
            get
            {
                var player = RunContext.Current?.PlayerEntity;
                // 尚未绑定 Buff 组件时，基础射击仍然可用。
                if (player == null || player.ASC == null) return 1;

                var asc = player.ASC;
                return asc.Attributes.GetValue<float>(1, cfg.ValueBonusType.Exp, (value, bonus) => value + bonus);
            }
        }

        public float LauncherDeltaTime;
        public string BulletPrefabPath;
        public int EnableShootPointCount;
        private float _attackDistance;
        private float _bodyRadius;

        public bool IsInitialized { get; private set; }
        public int ProfessionId { get; private set; }
        public int AttributeId { get; private set; }
        public float CurrentDamage => _currentDamage;
        /// <summary>配置表中的身体半径，使用角色根物体缩放前的局部单位。</summary>
        public float BodyRadius => _bodyRadius;

        public void InitialAttribute(int id = 1, int professionId = HeroProfessionCatalog.DefaultProfessionId)
        {
            var attribute = LubanConfigManager.Instance.Tables.EntityTbAttribute.Get(id);
            ProfessionId = professionId;
            AttributeId = id;
            CloudEconomy.GetGrowthBonuses(out int damageBonus, out int hpBonus, out int defenseBonus, id);
            MaxHp = attribute.Hp + hpBonus;
            _maxDamage = attribute.Damage + damageBonus;
            BaseSpeed = attribute.Speed;
            _attackDistance = attribute.AttackDistance;
            _bodyRadius = attribute.BodyRadius;

            CurrentHp = MaxHp;
            _healingRemainder = 0;
            BaseDefense = attribute.Defense + defenseBonus;
            _currentDamage = _maxDamage;
            _mossGuard.Reset();
            IsInitialized = true;
            EventBus.Publish(new OnShieldChanged(0, 0, this));
            // UI 可能先于玩家打开，初始化完成后同步一次完整血量。
            EventBus.Publish<OnHpChanged>(new OnHpChanged(CurrentHp, 0));
        }

        /// <summary>ASC 的 Shield 加成表示可恢复的苔衣容量，不是当前剩余层数。</summary>
        public void RefreshShield(SimpleASC asc)
        {
            if (!IsInitialized) return;
            int before = Shield;
            int capacity = asc == null ? 0 : asc.Attributes.GetValue(0, cfg.ValueBonusType.Shield,
                (value, bonus) => Mathf.Max(0, Mathf.RoundToInt(value + bonus)));
            _mossGuard.SetCapacity(capacity);
            if (Shield != before) EventBus.Publish(new OnShieldChanged(Shield, Shield - before, this));
        }

        public void TickShield(float deltaTime)
        {
            if (!IsInitialized || CurrentHp <= 0) return;
            int before = Shield;
            _mossGuard.Tick(deltaTime);
            if (Shield != before) EventBus.Publish(new OnShieldChanged(Shield, Shield - before, this));
        }

        /// <summary>整数生命保留小数治疗余量；满血时丢弃余量，不预存未来治疗。</summary>
        public void Heal(float amount)
        {
            if (!IsInitialized || CurrentHp <= 0 || amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            if (CurrentHp >= MaxHp) { _healingRemainder = 0; return; }
            double total = amount + _healingRemainder;
            int restored = (int)System.Math.Min(MaxHp - CurrentHp, System.Math.Floor(total + 1e-6));
            _healingRemainder = System.Math.Max(0, total - restored);
            CurrentHp += restored;
            if (CurrentHp >= MaxHp) _healingRemainder = 0;
            // Delta 延续伤害为正的约定；治疗为负，仅报告实际增加的整数生命。
            if (restored > 0) EventBus.Publish(new OnHpChanged(CurrentHp, -restored));
        }

        // 保留现有单参数入口；需要区分格挡和掉血的表现时使用 out 参数。
        public void TakeDamage(float damage) => TakeDamage(damage, out _);

        public void TakeDamage(float damage, out bool isShield)
        {
            isShield = false;
            if (!IsInitialized || CurrentHp <= 0 || damage <= 0 || float.IsNaN(damage) || float.IsInfinity(damage)) return;
            if (_mossGuard.TryConsume())
            {
                isShield = true;
                EventBus.Publish(new OnShieldChanged(Shield, -1, this));
                return;
            }
            int damageInt = Mathf.RoundToInt(damage);
            damageInt = Mathf.Min(CurrentHp, Mathf.Max(1, damageInt - BaseDefense));
            CurrentHp -= damageInt;

            EventBus.Publish<OnHpChanged>(new OnHpChanged(CurrentHp, damageInt));
        }
    }
}
