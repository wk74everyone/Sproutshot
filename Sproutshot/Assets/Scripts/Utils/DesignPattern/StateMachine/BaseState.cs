using UnityEngine;

namespace Sproutshot.Utils
{
    public abstract class BaseState : MonoBehaviour
    {
        public abstract void OnEnter();

        public abstract void OnExit();

        public abstract void OnUpdate();
    }
}