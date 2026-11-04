using UnityEngine;
using Sproutshot.Utils;

namespace Sproutshot.Gameplay
{
    public abstract class Entity : MonoBehaviour
    {
        [HideInInspector] public SimpleASC ASC;
        public CharacterBuffVisual BuffVisuals { get; private set; }

        protected virtual void Awake()
        {
            ASC = GetComponent<SimpleASC>();
        }

        protected void InitializeBuffVisuals()
        {
            BuffVisuals = GetComponent<CharacterBuffVisual>();
            if (BuffVisuals == null) BuffVisuals = gameObject.AddComponent<CharacterBuffVisual>();
        }

        public abstract void InitialAttribute(int id);

        public abstract void TakeDamage(float damage);
        /// <summary>持续伤害沿用生命结算，允许表现层省略每跳普通命中声音。</summary>
        public virtual void TakePeriodicDamage(float damage) => TakeDamage(damage);

        /// <summary>没有生命系统的实体保持默认值和空治疗行为（例如精灵伙伴）。</summary>
        public virtual float MaxHp => 0;
        public virtual void Heal(float amount) { }

        protected abstract void Damaged();
    }
}
