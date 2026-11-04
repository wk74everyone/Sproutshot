using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>每个苔衣层数分别抵挡一次伤害、分别恢复；不消耗作为容量来源的 ASC Buff。</summary>
    public sealed class MossGuardState
    {
        // 对应当前卡牌 5 的描述；配置表尚无行为冷却字段，不借用 Buff 持续时间。
        public const float RechargeSeconds = 18f;
        private readonly List<float> _cooldowns = new();
        public int Capacity => _cooldowns.Count;
        public int Charges
        {
            get
            {
                int count = 0;
                foreach (float remaining in _cooldowns) if (remaining <= 0) count++;
                return count;
            }
        }

        public void SetCapacity(int capacity)
        {
            capacity = Mathf.Max(0, capacity);
            // 容量减少时优先去掉未恢复的层数；新增容量立即就绪，不刷新旧层的倒计时。
            for (int i = _cooldowns.Count - 1; i >= 0 && _cooldowns.Count > capacity; i--)
                if (_cooldowns[i] > 0) _cooldowns.RemoveAt(i);
            if (_cooldowns.Count > capacity) _cooldowns.RemoveRange(capacity, _cooldowns.Count - capacity);
            while (_cooldowns.Count < capacity) _cooldowns.Add(0);
        }

        public bool TryConsume()
        {
            for (int i = 0; i < _cooldowns.Count; i++)
                if (_cooldowns[i] <= 0)
                {
                    _cooldowns[i] = RechargeSeconds;
                    return true;
                }
            return false;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            for (int i = 0; i < _cooldowns.Count; i++)
                if (_cooldowns[i] > 0) _cooldowns[i] = Mathf.Max(0, _cooldowns[i] - deltaTime);
        }

        public void Reset() => _cooldowns.Clear();
    }
}
