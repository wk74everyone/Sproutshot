using UnityEngine;

namespace Sproutshot.Gameplay
{
    public abstract class BaseCue : MonoBehaviour
    {
        public Entity Target { get; private set; }
        public CueType Type { get; private set; }
        public float Intensity { get; private set; }

        internal void Configure(Entity target, CueType type, float intensity)
        {
            Target = target; Type = type; Intensity = intensity;
        }
        /// <summary>
        /// 刚激活时调用
        /// </summary>
        public abstract void OnActive();
        /// <summary>
        /// 持续表现
        /// </summary>
        public abstract void WhileActive();
        /// <summary>
        /// 周期执行时的表现
        /// </summary>
        public virtual void OnExecute() => OnExcute();
        // 兼容原接口的拼写；新处理器覆写 OnExecute。
        public virtual void OnExcute() { }
        /// <summary>
        /// 移除的时候，视觉表现
        /// </summary>
        public abstract void OnRemove();
    }
}
