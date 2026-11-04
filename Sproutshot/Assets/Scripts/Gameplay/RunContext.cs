namespace Sproutshot.Gameplay
{
    public class RunContext
    {
        public static RunContext Current;

        public PlayerRuntime PlayerContext;
        public LevelRuntime LevelContext;
        public PlayerEntity PlayerEntity;
        public SpiritEntity SpiritEntity;
        public readonly CardRuntime Cards = new CardRuntime();

        public SpatialGrid SpatialGrid;

        public RunContext()
        {
            PlayerContext = new PlayerRuntime();
            LevelContext = new LevelRuntime();
        }
    }
}
