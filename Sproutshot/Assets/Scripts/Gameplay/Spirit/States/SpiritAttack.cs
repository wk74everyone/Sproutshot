using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public sealed class SpiritAttack : BaseState
    {
        [SerializeField, Min(.01f)] private float _windupDuration = .15f;
        private float _elapsed;
        public override void OnEnter() { _brain = GetComponentInParent<SpiritBrain>(); _elapsed = 0; }
        public override void OnExit() => _elapsed = 0;
        public override void OnUpdate()
        {
            if (!(_brain is SpiritBrain brain)) return;
            if (!brain.Entity.CanAct) { brain.TryChangeState(brain.IdleState); return; }
            brain.Entity.FollowOwner(Time.deltaTime);
            if (!brain.Entity.TargetValid) { Finish(brain); return; }
            _elapsed += Time.deltaTime;
            if (_elapsed < _windupDuration) return;
            brain.Entity.TryShoot();
            Finish(brain);
        }
        private static void Finish(SpiritBrain brain) =>
            brain.TryChangeState(brain.Entity.NeedsFollow ? (BaseState)brain.FollowState : brain.IdleState);
    }
}
