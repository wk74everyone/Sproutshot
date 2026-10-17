using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class RunContext
    {
        public static RunContext Current;

        public PlayerRuntime PlayerContext;
        public LevelRuntime LevelContext;

        public RunContext()
        {
            PlayerContext = new PlayerRuntime();
            LevelContext = new LevelRuntime();
        }
    }
}