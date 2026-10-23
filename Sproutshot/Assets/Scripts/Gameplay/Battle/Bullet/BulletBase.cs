using UnityEngine;

namespace Sproutshot.Gameplay
{
    public abstract class BulletBase: MonoBehaviour
    {
        [SerializeField] protected bool _isAOE;

        public abstract void Launch(Vector3 direction);

        public abstract void Query();
    }
}