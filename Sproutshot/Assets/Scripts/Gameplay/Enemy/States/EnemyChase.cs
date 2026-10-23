using Sproutshot.MapGen;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyChase : BaseState
    {
        [SerializeField] private FlowFieldGenerator _flowField;
        [SerializeField] private Transform _moveRoot;
        [SerializeField] private EnemyAttack _attackState;

        private EnemyEntity _enemyEntity;
        private EnemyAnimation _animation;
        private EnemyIdle _idleState;
        private bool _chasing;
        private float _nextFlowSearchTime;
        public FlowFieldGenerator FlowField => _flowField;

        /// <summary>生成器也可以在生成敌人后注入共享流场，避免自行查找。</summary>
        public void BindFlowField(FlowFieldGenerator flowField)
        {
            _flowField = flowField;
            _nextFlowSearchTime = 0;
        }

        public override void OnEnter()
        {
            ResolveMoveRoot();
            _chasing = true;
            _nextFlowSearchTime = 0;
            ResolveFlowField();
        }

        /// <summary>待机状态在共享流场就绪且当前位置可达时才进入追赶。</summary>
        public bool CanStartChasing()
        {
            ResolveMoveRoot();
            ResolveFlowField();
            return RunContext.Current?.PlayerEntity != null && isActiveAndEnabled && _flowField != null && _flowField.isActiveAndEnabled &&
                _flowField.TryGetMoveDirection(_moveRoot.position, out _);
        }

        private void ResolveMoveRoot()
        {
            if (_attackState == null) _attackState = GetComponent<EnemyAttack>();
            if (_brain == null) _brain = GetComponentInParent<EnemyBrain>();
            if (_enemyEntity == null) _enemyEntity = GetComponentInParent<EnemyEntity>();
            if (_animation == null && _brain != null) _animation = _brain.GetComponent<EnemyAnimation>();
            if (_idleState == null && _brain != null) _idleState = _brain.GetComponentInChildren<EnemyIdle>(true);
            // 状态可以放在子物体上，实际移动敌人根物体。
            if (_moveRoot == null) _moveRoot = _brain != null ? _brain.transform : transform;
        }

        public override void OnExit()
        {
            _chasing = false;
            _animation?.ResetPose();
        }

        public override void OnUpdate()
        {
            if (!_chasing || _moveRoot == null || _enemyEntity == null) return;

            var player = RunContext.Current?.PlayerEntity;
            if (player == null || !player.isActiveAndEnabled)
            {
                _animation?.ResetPose();
                if (_brain is EnemyBrain idleBrain) idleBrain.TryChangeState(_idleState);
                return;
            }
            Vector2 toPlayer = player.transform.position - _moveRoot.position;
            if (toPlayer.sqrMagnitude <= _enemyEntity.AttackDistance * _enemyEntity.AttackDistance &&
                _brain is EnemyBrain brain && brain.TryChangeState(_attackState)) return;

            ResolveFlowField();
            if (_flowField == null || !_flowField.isActiveAndEnabled)
            {
                _animation?.ResetPose();
                return;
            }

            // 流场接口限制步长并检查整段通行，直接移动 Transform，无需刚体。
            Vector3 previous = _moveRoot.position;
            if (_flowField.TryGetMovePosition(previous, _enemyEntity.CurrentSpeed * Time.deltaTime, out Vector3 next))
            {
                _moveRoot.position = next;
                _animation?.PlayChase(next - previous, Time.deltaTime);
            }
            else _animation?.ResetPose();

            _enemyEntity.UpdateSpatialPosition();
        }

        private void ResolveFlowField()
        {
            if (_flowField != null || Time.time < _nextFlowSearchTime) return;
            _nextFlowSearchTime = Time.time + 1f;
            // 单流场场景默认自动获取；多地图或多体型时应通过 BindFlowField 显式绑定。
            _flowField = FindObjectOfType<FlowFieldGenerator>();
        }

        private void OnDisable()
        {
            OnExit();
        }
    }
}
