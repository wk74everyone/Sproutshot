using System.Collections;
using Sproutshot.Utils;
using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>音频业务映射随本局销毁，不让 Utils 反向引用玩法程序集。</summary>
    [DisallowMultipleComponent]
    public sealed class BattleAudioPresenter : MonoBehaviour
    {
        private RunContext _run;
        private bool _ended;
        private void OnEnable()
        {
            _run = RunContext.Current;
            AudioManager.Instance.BeginBattle();
            EventBus.Subscribe<BattleEndedEvent>(OnEnded);
            EventBus.Subscribe<CardAcquiredEvent>(OnCard);
        }
        private IEnumerator Start()
        {
            var audio = AudioManager.Instance;
            yield return audio.Initialize();
            if (_run != RunContext.Current || _ended) yield break;
            audio.PlayMusic(AudioId.BattleMusic);
            yield return audio.PreloadScope(AudioScope.Battle);
        }
        private void OnCard(CardAcquiredEvent evt)
        {
            if (evt.Context == _run && _run == RunContext.Current && !_ended)
                AudioManager.Instance.PlaySfx(AudioId.PositiveChime);
        }
        private void OnEnded(BattleEndedEvent evt)
        {
            if (_ended || _run != RunContext.Current) return;
            _ended = true;
            AudioManager.Instance.EndBattle();
            AudioManager.Instance.StopSfx(AudioId.PositiveChime);
            AudioManager.Instance.PlaySfx(evt.Victory ? AudioId.Victory : AudioId.Defeat);
        }
        private void OnDisable()
        {
            StopAllCoroutines();
            EventBus.Unsubscribe<BattleEndedEvent>(OnEnded);
            EventBus.Unsubscribe<CardAcquiredEvent>(OnCard);
            if (_run == RunContext.Current && AudioManager.HasInstance) AudioManager.Instance.EndBattle();
        }
    }
}
