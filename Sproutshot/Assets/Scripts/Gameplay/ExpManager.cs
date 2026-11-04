using System;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class ExpManager : SingletonMono<ExpManager>
    {
        public const int ExpPerRaffle = 100;
        private double _currentExp;
        private int _earnedRaffles;
        private RunContext _context;
        public float CurrentExp => (float)_currentExp;
        /// <summary>本局由经验发放的选卡机会，包含尚未领取的机会，不统计 E 键调试选卡。</summary>
        public int EarnedRaffleCount => _earnedRaffles;
        public int RequiredExp => (int)Math.Min(int.MaxValue, (long)ExpPerRaffle * (_earnedRaffles + 1L));
        public RunContext Context => _context;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            EventBus.Subscribe<OnEnemyDead>(OnEnemyDead);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            EventBus.Unsubscribe<OnEnemyDead>(OnEnemyDead);
            _currentExp = 0;
            _earnedRaffles = 0;
            _context = null;
        }

        public void BeginBattle(RunContext context)
        {
            _context = context; _currentExp = 0; _earnedRaffles = 0;
            EventBus.Publish(new ExperienceChangedEvent(context, CurrentExp, RequiredExp));
        }
        public void EndBattle(RunContext context)
        {
            if (_context != context) return;
            _context = null; _currentExp = 0; _earnedRaffles = 0;
            EventBus.Publish(new ExperienceChangedEvent(context, 0, RequiredExp));
        }

        private void OnEnemyDead(OnEnemyDead evt)
        {
            if (_context == null || _context != RunContext.Current || _context.LevelContext.IsStopped ||
                !_context.PlayerContext.IsInitialized || _context.PlayerContext.CurrentHp <= 0 || evt.DropExp <= 0) return;
            var addition = _context.PlayerContext.ExpAddition;
            if (addition <= 0 || float.IsNaN(addition) || float.IsInfinity(addition)) return;
            float finalExp = Mathf.Floor(evt.DropExp * addition);
            if (finalExp <= 0 || float.IsNaN(finalExp) || float.IsInfinity(finalExp)) return;
            _currentExp += finalExp;
            int count = AwardRaffles();
            var context = _context;
            EventBus.Publish(new ExperienceChangedEvent(context, CurrentExp, RequiredExp));
            if (count > 0 && _context == context && RunContext.Current == context)
                EventBus.Publish(new OnSatisfiedRaffle(context, count));
        }

        private int AwardRaffles()
        {
            if (_currentExp < RequiredExp || _earnedRaffles == int.MaxValue) return 0;
            // 连续门槛为100、200、300……，必须扣除各次成本之和。
            // 二分计算可发次数，避免异常大额经验导致逐次循环卡住主线程。
            int low = 0, high = int.MaxValue - _earnedRaffles;
            while (low < high)
            {
                int middle = low + (int)(((long)high - low + 1) / 2);
                if (CostForRaffles(middle) <= _currentExp) low = middle;
                else high = middle - 1;
            }
            _currentExp = Math.Max(0, _currentExp - CostForRaffles(low));
            _earnedRaffles += low;
            return low;
        }

        private double CostForRaffles(int count)
        {
            // 等差数列求和；接近int上限时，将后续门槛固定在int.MaxValue，防止溢出。
            int growing = Math.Min(count, Math.Max(0, int.MaxValue / ExpPerRaffle - _earnedRaffles));
            double growingCost = ExpPerRaffle * (double)growing * (_earnedRaffles + (growing + 1d) / 2d);
            return growingCost + (count - growing) * (double)int.MaxValue;
        }
    }
}
