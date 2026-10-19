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
        private float _currentDamage;
        private float _maxDamage;
        public float AttackDistance { get; private set; }
        private float _bodyRadius;
        private EnemyBrain _brain;

        public float CurrentHp => _currentHp;
        public bool IsDead { get; private set; }

        public void ResetAll()
        {
            IsDead = false;
            _currentHp = _maxHp;
            _currentDefense = _maxDefense;
            _currentDamage = _maxDamage;
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
            if (_currentHp > 0) return;

            // 先标记死亡，同一帧的多次命中也只会触发一次死亡流程。
            IsDead = true;
            if (_brain == null) _brain = GetComponent<EnemyBrain>();
            if (_brain == null || !_brain.TryEnterDeath()) Recycle();
        }

        /// <summary>池中生成的敌人归还池；直接放在场景中的敌人则失活。</summary>
        public void Recycle()
        {
            if (PoolManager.HasInstance) PoolManager.Instance.Put(gameObject);
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
