using Sproutshot.Utils;
using Sproutshot.MapGen;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyAttack : BaseState
    {
        [SerializeField] private EnemyChase _chaseState;
        [SerializeField] private EnemyIdle _idleState;
        [SerializeField, Min(.01f)] private float _windupDuration = .35f;
        [SerializeField, Min(.01f)] private float _lungeDuration = .15f;
        [SerializeField, Min(.01f)] private float _recoveryDuration = .5f;
        [SerializeField, Min(0)] private float _lungeDistance = .75f;

        private enum Phase { Inactive, Windup, Lunge, Recovery }
        private Phase _phase;
        private float _elapsed, _distance;
        private Vector2 _direction;
        private Transform _moveRoot;
        private EnemyAnimation _animation;
        private PlayerEntity _target;
        private RunContext _context;
        private FlowFieldGenerator _flowField;

        public override void OnEnter()
        {
            _brain = GetComponentInParent<EnemyBrain>();
            if (_brain == null) return;
            _moveRoot = _brain.transform;
            if (_animation == null) _animation = _brain.GetComponent<EnemyAnimation>();
            if (_chaseState == null) _chaseState = _brain.GetComponentInChildren<EnemyChase>(true);
            if (_idleState == null) _idleState = _brain.GetComponentInChildren<EnemyIdle>(true);
            _flowField = _chaseState != null ? _chaseState.FlowField : null;
            _context = RunContext.Current;
            _target = _context?.PlayerEntity;
            if (_target == null || !_target.isActiveAndEnabled) { Finish(); return; }
            Vector2 delta = _target.transform.position - _moveRoot.position;
            // 蓄力开始即锁定方向，不在扑撞中途追踪，给玩家躲避的机会。
            _direction = delta.sqrMagnitude > .000001f ? delta.normalized : Vector2.up;
            _distance = Mathf.Min(Mathf.Max(0, _lungeDistance), delta.magnitude);
            _elapsed = 0;
            _phase = Phase.Windup;
            _animation?.PlayWindup(_direction, 0);
        }

        public override void OnExit()
        {
            _phase = Phase.Inactive;
            _elapsed = 0;
            _target = null;
            _context = null;
            _animation?.ResetAll();
        }

        public override void OnUpdate()
            => Tick(Time.deltaTime);

        private void Tick(float deltaTime)
        {
            if (_phase == Phase.Inactive || deltaTime <= 0) return;
            if (_context != RunContext.Current || _target == null || !_target.isActiveAndEnabled ||
                _context?.PlayerEntity != _target) { Finish(); return; }

            // 消费跨阶段的剩余时间，低帧率下也保持完整的 0.35 / 0.15 / 0.5 秒节奏。
            for (int i = 0; i < 3 && _phase != Phase.Inactive; i++)
            {
                float duration = Mathf.Max(.01f, _phase == Phase.Windup ? _windupDuration :
                    _phase == Phase.Lunge ? _lungeDuration : _recoveryDuration);
                float step = Mathf.Min(deltaTime, duration - _elapsed);
                float previous = _elapsed / duration;
                _elapsed += step;
                deltaTime -= step;
                float progress = Mathf.Clamp01(_elapsed / duration);
                switch (_phase)
                {
                    case Phase.Windup:
                        _animation?.PlayWindup(_direction, progress);
                        break;
                    case Phase.Lunge:
                        // 前快后慢的短扑；使用整段地图扫掠，避免高速穿过障碍。
                        float distance = _distance * ((2 * progress - progress * progress) - (2 * previous - previous * previous));
                        if (_flowField != null && _flowField.isActiveAndEnabled &&
                            _flowField.TryGetStraightMovePosition(_moveRoot.position, _direction, distance, out Vector3 next))
                            _moveRoot.position = next;
                        _animation?.PlayLunge(_direction, progress);
                        break;
                    case Phase.Recovery:
                        _animation?.PlayRecovery(_direction, progress);
                        break;
                }
                if (_elapsed < duration) return;
                _elapsed = 0;
                if (_phase == Phase.Windup)
                {
                    _phase = Phase.Lunge;
                    _animation?.PlayLunge(_direction, 0);
                }
                else if (_phase == Phase.Lunge)
                {
                    _phase = Phase.Recovery;
                    _animation?.PlayRecovery(_direction, 0);
                }
                else { Finish(); return; }
                if (deltaTime <= 0) return;
            }
        }

        private void Finish()
        {
            OnExit();
            if (_brain is EnemyBrain brain)
                brain.TryChangeState(_chaseState != null && _chaseState.CanStartChasing() ? (BaseState)_chaseState : _idleState);
        }

        private void OnDisable() => OnExit();
    }
}
