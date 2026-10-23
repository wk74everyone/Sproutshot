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

        public AttributeSet Attributes => _attributeSet;

        private void Update()
        {
            if (Time.timeScale <= 0f) return;
            Tick();
        }

        private void Tick()
        {
            for (int i = _buffs.Count - 1; i >= 0; i--)
            {
                if (_buffs[i].IsPermanent)
                {
                    continue;
                }
                _buffs[i].RemainingTime -= Time.deltaTime;
                if (_buffs[i].RemainingTime <= 0)
                {
                    Remove(_buffs[i]);
                }
            }
        }

        public ActiveBuff Add(BuffDefinition buff)
        {
            if (buff == null)
                throw new System.ArgumentNullException(nameof(buff));

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
                BuffsChanged?.Invoke();
                return existing;
            }

            ActiveBuff activeBuff = new ActiveBuff(buff);
            _buffs.Add(activeBuff);
            _attributeSet.Add(activeBuff);

            BuffsChanged?.Invoke();
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
                BuffsChanged?.Invoke();
            }
        }
    }
}
