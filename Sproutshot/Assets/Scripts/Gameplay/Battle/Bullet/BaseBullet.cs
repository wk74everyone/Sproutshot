using UnityEngine;

namespace Sproutshot.Gameplay
{
    [RequireComponent(typeof(SimpleASC))]
    public abstract class BaseBullet : MonoBehaviour
    {
        [SerializeField] protected bool _isAOE;

        protected SimpleASC _asc;
        public SimpleASC ASC => _asc;

        protected virtual void Awake()
        {
            _asc = GetComponent<SimpleASC>();
            _asc.TickEnabled = false;
        }

        public abstract void AddBuffs(SimpleASC ownerASC);

        public abstract void Launch(Vector3 direction);

        public abstract void Query();
    }
}
