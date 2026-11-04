using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Sproutshot.Gameplay
{
    /// <summary>只属于当前对局的卡牌领取次数；出现为候选、翻页和关闭窗口均不计数。</summary>
    public sealed class CardRuntime
    {
        private readonly Dictionary<int, int> _acquired = new Dictionary<int, int>();
        public IReadOnlyDictionary<int, int> AcquiredCards { get; }
        public CardRuntime() => AcquiredCards = new ReadOnlyDictionary<int, int>(_acquired);
        public int GetAcquiredCount(int cardId) => _acquired.TryGetValue(cardId, out int count) ? count : 0;

        public bool CanAcquire(cfg.card.Buff card, int professionId) => card != null && card.Weight > 0 &&
            (card.ProfessionType == -1 || card.ProfessionType == professionId) &&
            (card.GetLimitTimes == -1 || (card.GetLimitTimes > 0 && GetAcquiredCount(card.Id) < card.GetLimitTimes));

        internal bool TryAcquire(cfg.card.Buff card, int professionId)
        {
            if (!CanAcquire(card, professionId)) return false;
            int count = GetAcquiredCount(card.Id);
            if (count == int.MaxValue) return false;
            _acquired[card.Id] = count + 1;
            return true;
        }
    }
}
