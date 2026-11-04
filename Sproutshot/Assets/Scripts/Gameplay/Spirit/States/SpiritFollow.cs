using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public sealed class SpiritFollow : BaseState
    {
        public override void OnEnter() => _brain = GetComponentInParent<SpiritBrain>();
        public override void OnExit() { }
        public override void OnUpdate()
        {
            if (!(_brain is SpiritBrain brain) || !brain.Entity.CanAct) return;
            brain.Entity.FollowOwner(Time.deltaTime);
            if (brain.Entity.TryAcquireTarget()) brain.TryChangeState(brain.AttackState);
            else if (!brain.Entity.NeedsFollow) brain.TryChangeState(brain.IdleState);
        }
    }
}
