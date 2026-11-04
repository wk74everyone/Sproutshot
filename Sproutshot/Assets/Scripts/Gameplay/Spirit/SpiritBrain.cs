using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    [DisallowMultipleComponent, RequireComponent(typeof(SpiritEntity))]
    public sealed class SpiritBrain : Brain
    {
        [SerializeField] private SpiritIdle _idleState;
        [SerializeField] private SpiritFollow _followState;
        [SerializeField] private SpiritAttack _attackState;
        public BaseState CurrentState { get; private set; }
        public SpiritEntity Entity { get; private set; }
        public SpiritIdle IdleState => _idleState;
        public SpiritFollow FollowState => _followState;
        public SpiritAttack AttackState => _attackState;

        private void OnEnable()
        {
            Entity = GetComponent<SpiritEntity>();
            if (_idleState == null) _idleState = GetComponentInChildren<SpiritIdle>(true);
            if (_followState == null) _followState = GetComponentInChildren<SpiritFollow>(true);
            if (_attackState == null) _attackState = GetComponentInChildren<SpiritAttack>(true);
            ChangeState(_idleState);
        }

        public bool TryChangeState(BaseState nextState)
        {
            if (!isActiveAndEnabled || nextState == null || !nextState.isActiveAndEnabled ||
                nextState.GetComponentInParent<SpiritBrain>() != this) return false;
            if (nextState != _idleState && (Entity == null || !Entity.CanAct)) return false;
            ChangeState(nextState);
            return true;
        }

        private void Update()
        {
            if (Time.timeScale <= 0f || Time.deltaTime <= 0) return;
            if (Entity != null) Entity.BeginMotionFrame();
            if (Entity == null || !Entity.CanAct) { ChangeState(_idleState); return; }
            Entity.TickTimers(Time.deltaTime);
            CurrentState?.OnUpdate();
        }

        protected override void ChangeState(BaseState nextState)
        {
            if (CurrentState == nextState) return;
            CurrentState?.OnExit(); CurrentState = nextState; CurrentState?.OnEnter();
        }
        private void OnDisable() => ChangeState(null);
    }
}
