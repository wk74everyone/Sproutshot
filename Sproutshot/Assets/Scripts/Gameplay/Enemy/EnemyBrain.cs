using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyBrain : Brain
    {
        [SerializeField] private BaseState initialState;

        public BaseState CurrentState { get; private set; }

        private void OnEnable()
        {
            ChangeState(initialState);
        }

        private void Update()
        {
            CurrentState?.OnUpdate();
        }

        private void OnDisable()
        {
            ChangeState(null);
        }

        public void ChangeState(BaseState nextState)
        {
            if (CurrentState == nextState) return;

            CurrentState?.OnExit();
            CurrentState = nextState;
            CurrentState?.OnEnter();
        }
    }
}