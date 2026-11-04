using Sproutshot.Utils;
using Sproutshot.MapGen;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>GameScene 的本局入口。先创建数据容器，地图和玩家随后初始化。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-300)]
    public sealed class BattleRoot : MonoBehaviour
    {
        [SerializeField, Min(1)] private int _maxAliveEnemies = 10;
        private RunContext _context;
        public bool HasEnded { get; private set; }
        public bool Victory { get; private set; }

        private void Awake()
        {
            _context = new RunContext();
            RunContext.Current = _context;
            // 在上下文建立后启用音频适配，兼容直接打开战斗场景。
            if (GetComponent<BattleAudioPresenter>() == null) gameObject.AddComponent<BattleAudioPresenter>();
            ExpManager.Instance.BeginBattle(_context);
            _context.LevelContext.Cleared += TryEndVictory;
            EventBus.Subscribe<OnCardSelectedEvent>(AddBuff2Player);
            EventBus.Subscribe<OnHpChanged>(OnHpChanged);
            EventBus.Subscribe<MapGeneratedEvent>(OnMapGenerated);
        }

        private void OnMapGenerated(MapGeneratedEvent evt)
        {
            if (evt.Map == null || HasEnded || RunContext.Current != _context || _context.LevelContext.IsInitialized) return;
            int stage = MapSceneSelection.CurrentStage;
            var tables = stage > 0 ? LubanConfigManager.Instance.Tables : null;
            var configuration = tables?.LevelTbSpawner.GetOrDefault(stage);
            if (configuration is not null)
                foreach (int id in configuration.Enemys)
                    if (tables.EntityTbAttribute.GetOrDefault(id) is null)
                    {
                        Debug.LogError($"[BattleRoot] 第 {stage} 关配置的小怪 ID {id} 不存在，停止刷怪。", this);
                        return;
                    }
            _context.LevelContext.Initialize(stage, configuration, _maxAliveEnemies);
        }

        /// <summary>关卡小怪全部击败时胜利，玩家死亡时失败；每局只结算一次。</summary>
        public void EndBattle(bool victory)
        {
            if (_context == null || HasEnded || RunContext.Current != _context) return;
            HasEnded = true;
            Victory = victory;
            if (_context.PlayerEntity != null)
                _context.PlayerEntity.GetComponent<PlayerClickToMove>()?.SetInputEnabled(false);
            _context.LevelContext.StopSpawning();
            EventBus.Publish(new BattleEndedEvent(victory));
        }

        private void OnHpChanged(OnHpChanged evt)
        {
            if (_context == null || !_context.PlayerContext.IsInitialized) return;
            if (_context.PlayerContext.CurrentHp <= 0)
                EndBattle(false);
            else
                TryEndVictory();
        }

        private void TryEndVictory()
        {
            // 也覆盖零敌人关卡先初始化、玩家随后生成的顺序；死亡和训练模式不能触发胜利。
            if (_context == null || !_context.PlayerContext.IsInitialized ||
                _context.PlayerContext.CurrentHp <= 0 || !_context.LevelContext.IsCleared) return;
            EndBattle(true);
        }

        private void OnDestroy()
        {
            if (ExpManager.HasInstance) ExpManager.Instance.EndBattle(_context);
            // 旧场景销毁时，不清除后来创建的新对局。
            if (RunContext.Current == _context)
                RunContext.Current = null;
            if (_context != null)
            {
                _context.LevelContext.Cleared -= TryEndVictory;
                _context.PlayerEntity = null;
                _context.SpiritEntity = null;
                _context.SpatialGrid = null;
            }
            EventBus.Unsubscribe<OnCardSelectedEvent>(AddBuff2Player);
            EventBus.Unsubscribe<OnHpChanged>(OnHpChanged);
            EventBus.Unsubscribe<MapGeneratedEvent>(OnMapGenerated);
        }

        private void AddBuff2Player(OnCardSelectedEvent evt)
        {
            if (HasEnded || _context == null || _context != RunContext.Current ||
                (evt.Context != null && evt.Context != _context) || !_context.PlayerContext.IsInitialized ||
                _context.PlayerContext.CurrentHp <= 0 || _context.PlayerEntity == null || _context.PlayerEntity.ASC == null) return;
            int cardId = evt.SelectedCardId;
            var hubTable = LubanConfigManager.Instance.Tables.BuffTbHub;
            var buffs = LubanConfigManager.Instance.Tables.BuffTbAttribute;
            var buffTable = LubanConfigManager.Instance.Tables.TbBuff.GetOrDefault(cardId);
            if (!_context.Cards.CanAcquire(buffTable, _context.PlayerContext.ProfessionId)) return;
            // 先验证完整效果链，配置不完整时不消耗本局获取次数。
            var definitions = new System.Collections.Generic.List<BuffDefinition>();
            var hubIds = buffTable.BuffHubId;
            for (int i = 0; i < hubIds.Count; i++)
            {
                var hub = hubTable.GetOrDefault(hubIds[i]);
                if (hub == null) return;
                var ids = hub.BuffIds;
                foreach (var id in ids)
                {
                    var attribute = buffs.GetOrDefault(id);
                    if (attribute == null) return;
                    definitions.Add(new BuffDefinition(attribute));
                }
            }
            if (definitions.Count == 0 || !_context.Cards.TryAcquire(buffTable, _context.PlayerContext.ProfessionId)) return;
            foreach (var definition in definitions) _context.PlayerEntity.ASC.Add(definition);
            EventBus.Publish(new CardAcquiredEvent(_context, cardId));
        }
    }
}
