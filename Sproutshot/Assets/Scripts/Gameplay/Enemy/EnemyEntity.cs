using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyEntity : Entity
    {
        // 基础属性
        private float _currentHp;
        private float _maxHp;
        private float _currentDefense;
        private float _maxDefense;

        public override void TakeDamage(float damage)
        {
            damage = Mathf.Max(0.1f, damage - _currentDefense);
            _currentHp -= damage;
        }
    }
}