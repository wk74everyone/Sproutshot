using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class PlayerEntity : Entity
    {
        public override void TakeDamage(float damage)
        {
            RunContext.Current.PlayerContext.TakeDamage(damage);
        }
    }
}