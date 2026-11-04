using UnityEngine;

namespace Sproutshot.Gameplay
{
    public abstract class BaseSkill : MonoBehaviour
    {
        public virtual void OnLaunch(SkillContext context) { }
        public virtual void BeforeHit(SkillContext context, ref BulletHitContext hit) { }
        public virtual void AfterHit(SkillContext context, ref BulletHitContext hit) { }
        public virtual void OnRecycle(SkillContext context) { }
    }
}