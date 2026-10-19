using UnityEngine;

namespace Sproutshot.Utils
{
    public abstract class Brain : MonoBehaviour
    {
        protected abstract void ChangeState(BaseState nextState);
    }
}