using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>GameScene 的本局入口。先创建数据容器，地图和玩家随后初始化。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-300)]
    public sealed class BattleRoot : MonoBehaviour
    {
        private RunContext _context;

        private void Awake()
        {
            _context = new RunContext();
            RunContext.Current = _context;
            EventBus.Subscribe<OnCardSelectedEvent>(AddBuff2Player);
        }

        private void OnDestroy()
        {
            // 旧场景销毁时，不清除后来创建的新对局。
            if (RunContext.Current == _context)
                RunContext.Current = null;
            if (_context != null)
            {
                _context.PlayerEntity = null;
                _context.SpatialGrid = null;
            }
            EventBus.Unsubscribe<OnCardSelectedEvent>(AddBuff2Player);
        }

        private void AddBuff2Player(OnCardSelectedEvent evt)
        {
            int cardId = evt.SelectedCardId;
            var hubTable = LubanConfigManager.Instance.Tables.BuffTbHub;
            var buffs = LubanConfigManager.Instance.Tables.BuffTbAttribute;
            var buffTable = LubanConfigManager.Instance.Tables.TbBuff.Get(cardId);
            var hubIds = buffTable.BuffHubId;
            for (int i = 0; i < hubIds.Count; i++)
            {
                var ids = hubTable.Get(hubIds[i]).BuffIds;
                foreach (var id in ids)
                {
                    BuffDefinition definition = new(buffs.Get(id));
                    RunContext.Current.PlayerEntity.ASC.Add(definition);
                }
            }
        }
    }
}
