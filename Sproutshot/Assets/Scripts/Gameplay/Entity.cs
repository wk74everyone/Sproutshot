using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public abstract class Entity : MonoBehaviour
    {
        public abstract void InitialAttribute(int id);

        public abstract void TakeDamage(float damage);
    }
}