using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>所有攻击类型共用的死亡状态，表现完成后归还对象池。</summary>
    public class EnemyDeath : BaseState, IPoolable
    {
        [SerializeField, Min(.01f)] private float _duration = .4f;
        private EnemyEntity _entity;
        private EnemyAnimation _animation;
        private Collider2D[] _colliders;
        private bool[] _colliderEnabled;
        private bool _collidersCaptured;
        private float _elapsed;
        private bool _dying;

        private void Initialize()
        {
            if (_brain == null) _brain = GetComponentInParent<EnemyBrain>();
            if (_brain == null) return;
            if (_entity == null) _entity = _brain.GetComponent<EnemyEntity>();
            if (_animation == null) _animation = _brain.GetComponent<EnemyAnimation>();
            if (_colliders != null) return;
            _colliders = _brain.GetComponentsInChildren<Collider2D>(true);
            _colliderEnabled = new bool[_colliders.Length];
        }

        public override void OnEnter()
        {
            Initialize();
            if (_dying || _entity == null || !_entity.IsDead) return;
            _dying = true;
            _elapsed = 0;
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] == null) continue;
                _colliderEnabled[i] = _colliders[i].enabled;
                _colliders[i].enabled = false;
            }
            _collidersCaptured = true;
            _animation?.PlayDeath(0);
        }

        public override void OnUpdate() => Tick(Time.deltaTime);

        private void Tick(float deltaTime)
        {
            if (!_dying || deltaTime <= 0) return;
            _elapsed += deltaTime;
            float progress = Mathf.Clamp01(_elapsed / Mathf.Max(.01f, _duration));
            _animation?.PlayDeath(progress);
            if (progress < 1) return;
            _dying = false;
            _entity.Recycle();
        }

        public override void OnExit()
        {
            _entity.DropExp();
            ResetAll();
        }

        public void ResetAll()
        {
            // 未参与过死亡的组件不应改动其他系统设置的碰撞开关。
            if (_collidersCaptured)
                for (int i = 0; i < _colliders.Length; i++)
                    if (_colliders[i] != null) _colliders[i].enabled = _colliderEnabled[i];
            _collidersCaptured = false;
            _elapsed = 0;
            _dying = false;
            _animation?.ResetAll();
        }

        private void OnDisable() => ResetAll();
    }
}
