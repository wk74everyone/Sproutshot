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

        public float LauncherDeltaTime;
        public string BulletPrefabPath;
        public int EnableShootPointCount;

        public void TakeDamage(float damage)
        {
            damage = Mathf.Max(0.1f, damage - _currentDefense);
            _currentHp = _currentHp - damage;
        }
    }
}