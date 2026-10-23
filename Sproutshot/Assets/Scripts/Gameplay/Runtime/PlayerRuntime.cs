using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class PlayerRuntime
    {
        // 基础属性
        private float _currentHp;
        private float _maxHp;
        private float _currentDefense;
        private float _maxDefense;
        private float _currentSpeed;
        private float _maxSpeed;
        private float _currentDamage;
        private float _maxDamage;
        // 其他的属性
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

        public float LauncherDeltaTime;
        public string BulletPrefabPath;
        public int EnableShootPointCount;
        private float _attackDistance;
        private float _bodyRadius;

        public bool IsInitialized { get; private set; }
        public float CurrentDamage => _currentDamage;
        /// <summary>配置表中的身体半径，使用角色根物体缩放前的局部单位。</summary>
        public float BodyRadius => _bodyRadius;

        public void InitialAttribute(int id = 1)
        {
            var attribute = LubanConfigManager.Instance.Tables.EntityTbAttribute.Get(id);
            _maxHp = attribute.Hp;
            _maxDefense = attribute.Defense;
            _maxDamage = attribute.Damage;
            _maxSpeed = attribute.Speed;
            _attackDistance = attribute.AttackDistance;
            _bodyRadius = attribute.BodyRadius;

            _currentHp = _maxHp;
            _currentDefense = _maxDefense;
            _currentDamage = _maxDamage;
            _currentSpeed = _maxSpeed;
            IsInitialized = true;
        }

        public void TakeDamage(float damage)
        {
            damage = Mathf.Max(0.1f, damage - _currentDefense);
            _currentHp = _currentHp - damage;
        }
    }
}
