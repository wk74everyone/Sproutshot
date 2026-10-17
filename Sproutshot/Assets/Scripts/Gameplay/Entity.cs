using UnityEngine;

namespace Sproutshot.Gameplay
{
    public abstract class Entity : MonoBehaviour
    {
        public abstract void TakeDamage(float damage);
    }
}