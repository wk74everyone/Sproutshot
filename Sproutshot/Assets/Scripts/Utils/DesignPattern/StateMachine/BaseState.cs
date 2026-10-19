using UnityEngine;

namespace Sproutshot.Utils
{
    public abstract class BaseState : MonoBehaviour
    {
        protected Brain _brain;

        public abstract void OnEnter();

        public abstract void OnExit();

        public abstract void OnUpdate();
    }
}
