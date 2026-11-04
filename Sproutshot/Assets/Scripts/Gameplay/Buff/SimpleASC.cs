using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    public class SimpleASC : MonoBehaviour
    {
        // Buff 添加、叠层或移除后，发出通知
        public event System.Action BuffsChanged;

        private readonly AttributeSet _attributeSet = new AttributeSet();

        private readonly List<ActiveBuff> _buffs = new();
        private readonly List<ActiveBuff> _tickingBuffs = new();
        private bool _isTicking;
        private const double PeriodicInterval = 1d;
        private const double TimeEpsilon = 1e-6; // 容忍 float 帧时间累计产生的微秒级边界误差。

        public AttributeSet Attributes => _attributeSet;
        public IReadOnlyList<ActiveBuff> ActiveBuffs => _buffs;
        // 子弹使用发射时快照，其效果不随主人剩余持续时间一起消失。
        public bool TickEnabled { get; set; } = true;

        private Entity _owner;

        private void Awake()
        {
            _owner = GetComponent<Entity>();
        }

        private void OnEnable() => SyncCues();
        private void OnDisable()
        {
            if (_owner == null) _owner = GetComponent<Entity>();
            if (_owner != null && CueManager.HasInstance) CueManager.Instance.ClearTarget(_owner);
        }

        private void SyncCues()
        {
            if (!Application.isPlaying) return;
            if (_owner == null) _owner = GetComponent<Entity>();
            if (_owner == null) return; // 子弹/纯数据 ASC 没有实体表现目标。
            if (!CueManager.HasInstance && !CueManager.HasDefaultBuff(_buffs)) return;
            var manager = CueManager.Instance;
            if (manager != null && manager.isActiveAndEnabled) manager.SyncBuffs(_owner, _buffs);
        }

        private void NotifyBuffsChanged()
        {
            SyncCues();
            BuffsChanged?.Invoke();
        }

        private void Update()
        {
            if (!TickEnabled || Time.timeScale <= 0f) return;
            Tick(Time.deltaTime);
        }

        private bool CanAffectOwner() => _owner != null && _owner.isActiveAndEnabled &&
            !(_owner is EnemyEntity enemy && enemy.IsDead);

        private void ApplyPeriodicEffect(ActiveBuff buff)
        {
            if (buff.BuffDefinition.ValueBonusType != cfg.ValueBonusType.Hp) return;
            float value;
            switch (buff.BuffDefinition.PeriodicValueMode)
            {
                case 0: value = buff.TotalBonus; break;
                case 1: value = _owner.MaxHp * buff.TotalBonus; break;
                case 2:
                    // 传入值已是正数实际伤害，不再乘层数，也不从 bonus 回退取值。
                    if (buff.PeriodicDamage > 0 && !float.IsNaN(buff.PeriodicDamage) && !float.IsInfinity(buff.PeriodicDamage))
                        _owner.TakePeriodicDamage(buff.PeriodicDamage);
                    return;
                default: return;
            }
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            if (value < 0) _owner.TakePeriodicDamage(-value);
            else if (value > 0) _owner.Heal(value);
        }

        private void Tick(float deltaTime)
        {
            if (_isTicking || !TickEnabled || Time.timeScale <= 0 || deltaTime <= 0 ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            if (_owner == null) _owner = GetComponent<Entity>();
            _isTicking = true;
            _tickingBuffs.AddRange(_buffs);
            try
            {
                // 扣血可能导致死亡、回池并清空 ASC，使用独立遍历列表并检查原状态是否仍有效。
                foreach (ActiveBuff buff in _tickingBuffs)
                {
                    if (!_buffs.Contains(buff)) continue;
                    double effectiveTime = buff.IsPermanent ? deltaTime :
                        System.Math.Min(deltaTime, System.Math.Max(0, buff.RemainingDuration));
                    if (!buff.IsPermanent) buff.RemainingDuration -= effectiveTime;
                    if (buff.BuffDefinition.BuffType == cfg.BuffType.ContinusValue)
                    {
                        buff.PeriodicElapsed += effectiveTime;
                        uint life = _owner is EnemyEntity enemy ? enemy.LifeVersion : 0;
                        while (buff.PeriodicElapsed + TimeEpsilon >= PeriodicInterval &&
                            _buffs.Contains(buff) && CanAffectOwner())
                        {
                            buff.PeriodicElapsed = System.Math.Max(0, buff.PeriodicElapsed - PeriodicInterval);
                            ApplyPeriodicEffect(buff);
                            if (_buffs.Contains(buff) && CanAffectOwner() && CueManager.HasInstance)
                                CueManager.Instance.ExecuteBuff(_owner, buff);
                            // 同一帧补结算时，不能继续伤害死亡对象或对象池中的下一次生命。
                            if (!CanAffectOwner() || (_owner is EnemyEntity current && current.LifeVersion != life)) return;
                        }
                    }
                    // 先结算有效时间内的周期伤害，再移除到期状态，包含恰好到期的最后一跳。
                    if (!buff.IsPermanent && buff.RemainingDuration <= TimeEpsilon && _buffs.Contains(buff)) Remove(buff);
                }
            }
            finally { _tickingBuffs.Clear(); _isTicking = false; }
        }

        public ActiveBuff Add(BuffDefinition buff) => Add(buff, 0);

        /// <summary>施加状态时携带本次命中计算好的周期伤害，刷新时替换为本次传入值。</summary>
        public ActiveBuff Add(BuffDefinition buff, float periodicDamage)
        {
            if (buff == null)
                throw new System.ArgumentNullException(nameof(buff));
            if (periodicDamage < 0 || float.IsNaN(periodicDamage) || float.IsInfinity(periodicDamage))
                throw new System.ArgumentOutOfRangeException(nameof(periodicDamage));

            // 相同配置的 Buff 合并叠层，并刷新持续时间。
            ActiveBuff existing = _buffs.Find(active => active.BuffDefinition.Id == buff.Id);
            if (existing != null)
            {
                BuffDefinition definition = existing.BuffDefinition;
                existing.StackCount += definition.AddStack;

                if (definition.MaxStack != -1)
                {
                    existing.StackCount = Mathf.Min(existing.StackCount, definition.MaxStack);
                }

                existing.RemainingTime = definition.Duration;
                existing.PeriodicDamage = periodicDamage;
                NotifyBuffsChanged();
                return existing;
            }

            ActiveBuff activeBuff = new ActiveBuff(buff) { PeriodicDamage = periodicDamage };
            _buffs.Add(activeBuff);
            _attributeSet.Add(activeBuff);

            NotifyBuffsChanged();
            return activeBuff;
        }

        // 查询角色身上是否存在指定 Buff
        public bool HasBuff(int buffId)
        {
            return _buffs.Exists(buff => buff.BuffDefinition.Id == buffId);
        }

        public void Remove(ActiveBuff buff)
        {
            if (_buffs.Remove(buff))
            {
                _attributeSet.Remove(buff);
                NotifyBuffsChanged();
            }
        }

        public void AddSnapshot(ActiveBuff source)
        {
            if (source == null || source.StackCount <= 0 || (!source.IsPermanent && source.RemainingTime <= 0)) return;
            var copy = new ActiveBuff(source.BuffDefinition) { StackCount = source.StackCount, RemainingDuration = source.RemainingDuration, PeriodicElapsed = source.PeriodicElapsed, PeriodicDamage = source.PeriodicDamage };
            _buffs.Add(copy);
            _attributeSet.Add(copy);
            NotifyBuffsChanged();
        }

        public void Clear()
        {
            if (_buffs.Count == 0) return;
            foreach (var buff in _buffs) _attributeSet.Remove(buff);
            _buffs.Clear();
            NotifyBuffsChanged();
        }
    }
}
