using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class SkillContext : MonoBehaviour
    {
        public Entity Owner { get; private set; }
        public RunContext Run { get; private set; }
    }
}