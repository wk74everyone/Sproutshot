using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class EnemyIdle : BaseState
    {
        [SerializeField] private EnemyChase _chaseState;
        [SerializeField, Min(.02f)] private float _transitionCheckInterval = .1f;
        private bool _idling;
        private float _nextCheckTime;

        public override void OnEnter()
        {
            _brain = GetComponentInParent<EnemyBrain>();
            if (_chaseState == null && _brain != null)
                _chaseState = _brain.GetComponentInChildren<EnemyChase>(true);
            if (_brain != null) _brain.GetComponent<EnemyAnimation>()?.ResetPose();
            _idling = true;
            _nextCheckTime = 0;
        }

        public override void OnExit() => _idling = false;

        public override void OnUpdate()
        {
            if (!_idling || !(_brain is EnemyBrain brain) || _chaseState == null || Time.time < _nextCheckTime)
                return;
            _nextCheckTime = Time.time + Mathf.Max(.02f, _transitionCheckInterval);
            if (_chaseState.CanStartChasing()) brain.TryChangeState(_chaseState);
        }

        private void OnDisable() => _idling = false;
    }
}
