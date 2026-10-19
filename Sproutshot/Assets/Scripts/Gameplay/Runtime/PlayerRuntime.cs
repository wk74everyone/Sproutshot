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

        public float LauncherDeltaTime;
        public string BulletPrefabPath;
        public int EnableShootPointCount;
        private float _attackDistance;
        private float _bodyRadius;

        public bool IsInitialized { get; private set; }

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
