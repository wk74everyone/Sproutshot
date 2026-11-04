using System;
using System.Collections.Generic;
using Sproutshot.Utils;

namespace Sproutshot.Gameplay
{
    public class LevelRuntime
    {
        private int[] _enemyIds = Array.Empty<int>();

        public bool IsInitialized { get; private set; }
        public bool IsStopped { get; private set; }
        public int StageNumber { get; private set; }
        /// <summary>本局累计生成上限；训练模式为 -1，不限制数量。</summary>
        public int LimitCount { get; private set; }
        public int SpawnedCount { get; private set; }
        public int DefeatedCount { get; private set; }
        /// <summary>只统计本关成功生成的小怪被击败，失活、回池或取消网格登记不算击杀。</summary>
        public bool IsCleared => IsInitialized && !IsEndless &&
            SpawnedCount >= LimitCount && DefeatedCount >= LimitCount;
        public event Action Cleared;
        private bool _clearNotified;
        /// <summary>尚未生成的敌人数；无尽模式为 -1，不包含场上存活敌人。</summary>
        public int RemainingSpawnCount => IsEndless ? -1 : Math.Max(0, LimitCount - SpawnedCount);
        public int MaxAliveCount { get; private set; } = 10;
        public bool IsEndless => IsInitialized && StageNumber == 0;
        public float SpawnInterval { get; private set; } = 1f;
        public int BatchCount { get; private set; } = 10;
        public float EnemyHpMultiplier { get; private set; } = 1f;
        public float EnemyDamageMultiplier { get; private set; } = 1f;
        public IReadOnlyList<int> EnemyIds => _enemyIds;
        public bool CanSpawn => IsInitialized && !IsStopped && (IsEndless || SpawnedCount < LimitCount);

        /// <summary>每局只初始化一次，重新生成地图不会重置已使用的配额。</summary>
        public void Initialize(int stageNumber, cfg.level_spawner configuration, int maxAliveCount = 10)
        {
            if (IsInitialized) return;
            if (stageNumber < 0) throw new ArgumentOutOfRangeException(nameof(stageNumber));
            if (maxAliveCount < 1) throw new ArgumentOutOfRangeException(nameof(maxAliveCount));

            if (stageNumber == 0)
            {
                _enemyIds = new[] { 11, 12 };
                LimitCount = -1;
            }
            else
            {
                if (configuration is null || configuration.Level != stageNumber)
                    throw new ArgumentException($"第 {stageNumber} 关缺少对应的刷怪配置。", nameof(configuration));
                if (configuration.LimitCount < 0)
                    throw new ArgumentException($"第 {stageNumber} 关的 LimitCount 不能为负数。", nameof(configuration));
                if (configuration.LimitCount > 0 && configuration.Enemys.Count == 0)
                    throw new ArgumentException($"第 {stageNumber} 关没有配置小怪类型。", nameof(configuration));

                _enemyIds = configuration.Enemys.ToArray();
                foreach (int id in _enemyIds)
                    if (id <= 0) throw new ArgumentException($"第 {stageNumber} 关的小怪 ID 必须大于零。", nameof(configuration));
                LimitCount = configuration.LimitCount;
                if (!ValidMultiplier(configuration.SpawnInterval) || configuration.BatchCount < 1 ||
                    !ValidMultiplier(configuration.EnemyHpMultiplier) || !ValidMultiplier(configuration.EnemyDamageMultiplier))
                    throw new ArgumentException("关卡刷怪间隔、批次和敌人倍率必须为有限正数。", nameof(configuration));
                SpawnInterval = configuration.SpawnInterval;
                BatchCount = configuration.BatchCount;
                EnemyHpMultiplier = configuration.EnemyHpMultiplier;
                EnemyDamageMultiplier = configuration.EnemyDamageMultiplier;
            }

            StageNumber = stageNumber;
            MaxAliveCount = maxAliveCount;
            SpawnedCount = 0;
            DefeatedCount = 0;
            IsInitialized = true;
            EventBus.Publish(new EnemySpawnCountChangedEvent(this));
            NotifyCleared();
        }

        /// <summary>所有刷怪点共享；创建前预占一个名额，防止多个核心超过全关总数。</summary>
        public bool TryConsumeSpawn(int aliveCount)
        {
            if (!CanSpawn || aliveCount < 0 || aliveCount >= MaxAliveCount) return false;
            if (SpawnedCount < int.MaxValue) SpawnedCount++;
            EventBus.Publish(new EnemySpawnCountChangedEvent(this));
            return true;
        }

        /// <summary>仅在创建失败时回滚；敌人死亡或回池不会返还本关累计名额。</summary>
        internal void CancelSpawn()
        {
            if (SpawnedCount <= 0) return;
            SpawnedCount--;
            EventBus.Publish(new EnemySpawnCountChangedEvent(this));
        }

        internal void RecordDefeat()
        {
            if (!IsInitialized || IsStopped || DefeatedCount >= SpawnedCount) return;
            DefeatedCount++;
            NotifyCleared();
        }

        private void NotifyCleared()
        {
            if (_clearNotified || IsStopped || !IsCleared) return;
            _clearNotified = true;
            Cleared?.Invoke();
        }

        public void StopSpawning() => IsStopped = true;
        private static bool ValidMultiplier(float value) => value > 0 && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
