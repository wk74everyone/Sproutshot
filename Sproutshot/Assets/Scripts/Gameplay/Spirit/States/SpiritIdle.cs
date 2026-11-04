using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public sealed class SpiritIdle : BaseState
    {
        public override void OnEnter() => _brain = GetComponentInParent<SpiritBrain>();
        public override void OnExit() { }
        public override void OnUpdate()
        {
            if (!(_brain is SpiritBrain brain) || !brain.Entity.CanAct) return;
            if (brain.Entity.NeedsFollow) brain.TryChangeState(brain.FollowState);
            else if (brain.Entity.TryAcquireTarget()) brain.TryChangeState(brain.AttackState);
        }
    }
}
