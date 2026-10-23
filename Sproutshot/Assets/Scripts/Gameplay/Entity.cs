using UnityEngine;

namespace Sproutshot.Gameplay
{
    public abstract class Entity : MonoBehaviour
    {
        [HideInInspector] public SimpleASC ASC;

        protected virtual void Awake()
        {
            ASC = GetComponent<SimpleASC>();
        }

        public abstract void InitialAttribute(int id);

        public abstract void TakeDamage(float damage);

        protected abstract void Damaged();
    }
}