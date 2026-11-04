using Sproutshot.Utils;

namespace Sproutshot.Gameplay
{
    /// <summary>刷怪配额初始化、消耗或回滚后通知 HUD；保留来源以隔离不同对局。</summary>
    public readonly struct EnemySpawnCountChangedEvent : IGameEvent
    {
        public readonly LevelRuntime Level;
        public EnemySpawnCountChangedEvent(LevelRuntime level) => Level = level;
    }

    /// <summary>本局只发布一次；由战斗入口判定胜负，UI 负责展示。</summary>
    public readonly struct BattleEndedEvent : IGameEvent
    {
        public readonly bool Victory;
        public BattleEndedEvent(bool victory) => Victory = victory;
    }

    public readonly struct OnCardSelectedEvent : IGameEvent
    {
        public readonly int SelectedCardId;
        public readonly RunContext Context;

        public OnCardSelectedEvent(int selectedCardId, RunContext context = null)
        {
            SelectedCardId = selectedCardId;
            Context = context;
        }
    }

    /// <summary>卡牌通过领取校验且效果已添加后通知 HUD；候选展示不触发。</summary>
    public readonly struct CardAcquiredEvent : IGameEvent
    {
        public readonly RunContext Context;
        public readonly int CardId;
        public CardAcquiredEvent(RunContext context, int cardId) { Context = context; CardId = cardId; }
    }

    public readonly struct OnHpChanged : IGameEvent
    {
        public readonly int Current;
        public readonly int Delta;
        public OnHpChanged(int current, int delta)
        {
            Current = current;
            Delta = delta;
        }
    }

    public readonly struct OnShieldChanged : IGameEvent
    {
        /// <summary>变化后的苔衣层数；Delta 是本次变化量，不应再加到 Current 上。</summary>
        public readonly int Current;
        public readonly int Delta;
        public readonly PlayerRuntime Source;
        public OnShieldChanged(int current, int delta, PlayerRuntime source = null)
        {
            Current = current;
            Delta = delta;
            Source = source;
        }
    }

    public readonly struct OnEnemyDead : IGameEvent
    {
        public readonly int EntityId;
        public readonly int DropExp;

        public OnEnemyDead(int entityId, int dropExp)
        {
            EntityId = entityId;
            DropExp = dropExp;
        }
    }

    public readonly struct OnSatisfiedRaffle : IGameEvent
    {
        public readonly RunContext Context;
        public readonly int Count;
        public OnSatisfiedRaffle(RunContext context, int count = 1) { Context = context; Count = count; }
    }

    /// <summary>到下一次选卡的经验进度；Context 用于隔离不同对局。</summary>
    public readonly struct ExperienceChangedEvent : IGameEvent
    {
        public readonly RunContext Context;
        public readonly float Current;
        public readonly int Required;
        public ExperienceChangedEvent(RunContext context, float current, int required)
        { Context = context; Current = current; Required = required; }
    }

    public class GameplayEvents
    {

    }
}
