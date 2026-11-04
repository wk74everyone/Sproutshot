using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>由 CueManager 播放和回收的独立世界坐标表现，不承担伤害结算。</summary>
    public abstract class WorldCueVisual : MonoBehaviour, IPoolable
    {
        public abstract bool Play(RunContext run, float radius, BulletElement elements = BulletElement.None);
        public abstract void Recycle();
        public abstract void ResetAll();
    }
}
