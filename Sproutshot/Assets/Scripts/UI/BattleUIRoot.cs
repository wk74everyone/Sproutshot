using System.Collections;
using System;
using Sproutshot.Gameplay;
using Sproutshot.MapGen;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.UI
{
    /// <summary>随战斗场景打开和关闭 HUD，复用 UIManager 的资源与窗口管理。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(BattleRoot))]
    public sealed class BattleUIRoot : MonoBehaviour
    {
        private UIManager _uiManager;
        private BattleRoot _battle;
        private bool _saving;
        private bool _navigating;
        private RunContext _context;
        private int _pendingRaffles;
        private Coroutine _raffleRoutine;
        private bool _raffleOwnsPause;
        private float _rafflePreviousTimeScale;
        public int PendingRaffleCount => _pendingRaffles;

        private void OnEnable()
        {
            _battle = GetComponent<BattleRoot>();
            _context = RunContext.Current;
            EventBus.Subscribe<BattleEndedEvent>(OnBattleEnded);
            EventBus.Subscribe<OnSatisfiedRaffle>(OnRaffleReady);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BattleEndedEvent>(OnBattleEnded);
            EventBus.Unsubscribe<OnSatisfiedRaffle>(OnRaffleReady);
            StopRaffles();
        }

        private bool CanRaffle => isActiveAndEnabled && _battle != null && !_battle.HasEnded &&
            _context != null && _context == RunContext.Current && !_context.LevelContext.IsStopped &&
            _context.PlayerContext.IsInitialized && _context.PlayerContext.CurrentHp > 0;

        private void OnRaffleReady(OnSatisfiedRaffle evt)
        {
            if (!CanRaffle || (evt.Context != null && evt.Context != _context)) return;
            // 保留 new OnSatisfiedRaffle() 的单次通知用法。
            int count = evt.Count == 0 ? 1 : evt.Count;
            if (count < 0) return;
            _pendingRaffles = (int)Math.Min(int.MaxValue, (long)_pendingRaffles + count);
            if (_raffleRoutine == null) _raffleRoutine = StartCoroutine(ProcessRaffles());
        }

        private IEnumerator ProcessRaffles()
        {
            // 保证句柄先写入，且避免在发布事件的调用栈内直接创建界面。
            yield return null;
            try
            {
                _uiManager = UIManager.Instance;
                while (CanRaffle && _pendingRaffles > 0)
                {
                    // E 键已打开的卡照常选择，经验奖励保留到它关闭后再发。
                    while (CanRaffle && _uiManager.TryGet<CardRafflePanelView>(out var open) && open.IsOpen)
                        yield return null;
                    if (!CanRaffle) yield break;
                    if (!_raffleOwnsPause) { _rafflePreviousTimeScale = Time.timeScale; Time.timeScale = 0; _raffleOwnsPause = true; }
                    bool confirmed = false;
                    var view = CardRafflePanelView.Open(_ => confirmed = true);
                    if (view == null) { Debug.LogError("[BattleUIRoot] 选卡界面加载失败，待领取次数已保留。", this); yield break; }
                    bool hadOffer = view.HasOffer;
                    if (hadOffer) AudioManager.Instance.PlaySfx(AudioId.PositiveChime);
                    while (CanRaffle && view != null && view.IsOpen && !confirmed) yield return null;
                    if (!confirmed) yield break;
                    if (!hadOffer) { _pendingRaffles = 0; yield break; }
                    _pendingRaffles--;
                }
            }
            finally
            {
                ReleaseRafflePause();
                _raffleRoutine = null;
            }
        }

        private void StopRaffles()
        {
            _pendingRaffles = 0;
            // 先关闭子窗口归还其暂停，再停止队列归还本层暂停。
            if (_uiManager != null) _uiManager.Close<CardRafflePanelView>();
            if (_raffleRoutine != null) StopCoroutine(_raffleRoutine);
            _raffleRoutine = null;
            ReleaseRafflePause();
        }

        private void ReleaseRafflePause()
        {
            if (!_raffleOwnsPause) return;
            _raffleOwnsPause = false;
            Time.timeScale = _rafflePreviousTimeScale;
        }

        private void OnBattleEnded(BattleEndedEvent result)
        {
            if (_battle == null || !_battle.HasEnded) return;
            _uiManager = UIManager.Instance;
            StopRaffles();
            // 先关闭抽卡恢复它持有的暂停，再由结果页持有暂停，避免倍率恢复顺序冲突。
            _uiManager.Close<CardRafflePanelView>();
            int stage = MapSceneSelection.CurrentStage;
            var view = StageResultsPanelView.Open(result.Victory, ReturnToMap,
                () => ContinueBattle(result.Victory, stage));
            if (stage <= 0) return;
#if UNITY_EDITOR
            if (!CloudBaseClient.IsEditorTestMode)
            {
                view.SetSettlementStatus("本地战斗预览，奖励与进度未保存。", true);
                return;
            }
#endif
            string battleId = CloudEconomy.BattleId;
            int defeated = RunContext.Current?.LevelContext?.DefeatedCount ?? 0;
            SaveSettlement(view, new EconomyPayload { battleId = battleId, victory = result.Victory, defeated = defeated });
        }

        private async void SaveSettlement(StageResultsPanelView view, EconomyPayload payload)
        {
            if (_saving || view == null) return;
            _saving = true;
            view.SetSettlementStatus("正在保存战斗结果…", false);
            try
            {
                var data = await CloudEconomy.ExecuteAsync("settleBattle", payload);
                if (view != null) view.SetSettlementStatus($"萌芽种子 +{data.receipt.rewardSeeds}    星露 +{data.receipt.rewardDew}", true);
            }
            catch (Exception error)
            {
                if (view != null) view.SetSettlementStatus("保存失败：" + error.Message, false, () => SaveSettlement(view,payload));
            }
            finally { _saving = false; }
        }

        private void ReturnToMap() => FlowerSceneTransition.Instance.EnterScene("MainCamp",
            onLoaded: () => FindObjectOfType<PanelSelection>()?.ShowMapPanel());

        private async void ContinueBattle(bool victory, int stage)
        {
            if (_navigating || FlowerSceneTransition.Instance.IsTransitioning) return;
            if (victory && stage >= StageProgressData.TotalStageCount)
            {
                ReturnToMap();
                return;
            }
            // 训练入口保留训练模式；普通关卡胜利进下一关，失败重进当前关。
            int targetStage = victory ? stage + 1 : stage;
            _navigating = true;
            try
            {
#if UNITY_EDITOR
                if (CloudBaseClient.IsEditorTestMode)
#endif
                if (stage > 0) await CloudEconomy.BeginBattleAsync(targetStage);
                if (this == null) return;
                if (stage > 0) MapSceneSelection.SelectStage(targetStage);
                FlowerSceneTransition.Instance.EnterScene("GameScene", reloadCurrent: true);
            }
            catch (Exception error) { CloudFeedback.Show(error.Message, () => ContinueBattle(victory,stage)); }
            finally { _navigating = false; }
        }

        private IEnumerator Start()
        {
            RunContext context = RunContext.Current;
            if (context == null) yield break;

            // 地图会先准备资源并生成玩家，避免 HUD 首帧显示尚未初始化的 0/0。
            while (!context.PlayerContext.IsInitialized)
            {
                if (RunContext.Current != context || _battle.HasEnded) yield break;
                yield return null;
            }

            if (RunContext.Current != context || _battle.HasEnded) yield break;
            _uiManager = UIManager.Instance;
            _uiManager.OpenAsync<MainUIView>();
        }

        private void OnDestroy()
        {
            // UIManager 跨场景保留；这里也会取消尚未完成的异步窗口加载。
            if (_uiManager != null) _uiManager.Close<MainUIView>();
            if (_uiManager != null) _uiManager.Close<StageResultsPanelView>();
        }
    }
}
