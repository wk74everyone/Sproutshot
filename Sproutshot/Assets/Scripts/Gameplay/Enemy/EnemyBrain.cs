using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyBrain : Brain
    {
        [SerializeField] private BaseState initialState;
        [SerializeField] private EnemyDeath _deathState;
        private EnemyEntity _entity;

        public BaseState CurrentState { get; private set; }

        /// <summary>供当前敌人的状态请求转换，不接受其他敌人身上的状态。</summary>
        public bool TryChangeState(BaseState nextState)
        {
            if (nextState == null || nextState.GetComponentInParent<EnemyBrain>() != this) return false;
            // 死亡是本次生命的终态，只能在回池、重生后重新进入普通状态。
            if (CurrentState is EnemyDeath || (_entity != null && _entity.IsDead)) return false;
            ChangeState(nextState);
            return true;
        }

        public bool TryEnterDeath()
        {
            ResolveReferences();
            if (_entity == null || !_entity.IsDead || !isActiveAndEnabled || _deathState == null ||
                !_deathState.isActiveAndEnabled || _deathState.GetComponentInParent<EnemyBrain>() != this) return false;
            ChangeState(_deathState);
            return true;
        }

        private void ResolveReferences()
        {
            if (_entity == null) _entity = GetComponent<EnemyEntity>();
            if (_deathState == null) _deathState = GetComponentInChildren<EnemyDeath>(true);
        }

        private void OnEnable()
        {
            ResolveReferences();
            if (_entity != null && _entity.IsDead) TryEnterDeath();
            else ChangeState(initialState);
        }

        private void Update()
        {
            CurrentState?.OnUpdate();
        }

        private void OnDisable()
        {
            ChangeState(null);
        }

        protected override void ChangeState(BaseState nextState)
        {
            if (CurrentState == nextState) return;

            CurrentState?.OnExit();
            CurrentState = nextState;
            CurrentState?.OnEnter();
        }
    }
}
