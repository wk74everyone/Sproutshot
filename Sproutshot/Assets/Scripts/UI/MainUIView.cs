using Sproutshot.Gameplay;
using Sproutshot.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>战斗 HUD；读取本局快照并由事件刷新生命、防御、经验和待生成敌人数。</summary>
    [DisallowMultipleComponent, UIWindow("MainUI")]
    public sealed class MainUIView : UIView
    {
        [SerializeField] private Image _healthFill;
        [SerializeField] private TMP_Text _healthValue;
        [SerializeField] private TMP_Text _shieldCount;
        [SerializeField] private TMP_Text _defenseValue;
        [SerializeField] private TMP_Text _remainingEnemyCount;
        [SerializeField] private Image _experienceFill;
        [SerializeField] private TMP_Text _experienceValue;
        [SerializeField] private TMP_Text _experienceHint;
        [SerializeField] private CardBuffBarView _cardBuffBar;
        [SerializeField, Min(0f)] private float _currentHealth = 80f;
        [SerializeField, Min(0f)] private float _maximumHealth = 100f;
        [SerializeField, Min(0)] private int _shieldAmount = 1;
        private bool _eventsSubscribed;

        protected override void OnCreated() => Refresh();

        protected override void OnOpen(object args)
        {
            if (!_eventsSubscribed)
            {
                EventBus.Subscribe<OnHpChanged>(SetHealth);
                EventBus.Subscribe<OnShieldChanged>(SetShieldCount);
                EventBus.Subscribe<EnemySpawnCountChangedEvent>(OnEnemySpawnCountChanged);
                EventBus.Subscribe<ExperienceChangedEvent>(OnExperienceChanged);
                EventBus.Subscribe<CardAcquiredEvent>(OnCardAcquired);
                _eventsSubscribed = true;
            }
            Refresh();
            if (_cardBuffBar != null) _cardBuffBar.Bind(RunContext.Current);
        }

        protected override void OnClose() => UnsubscribeEvents();
        protected override void OnRelease()
        {
            UnsubscribeEvents();
            if (_cardBuffBar != null) _cardBuffBar.Release();
        }
        private void OnDisable() => UnsubscribeEvents();

        private void UnsubscribeEvents()
        {
            if (_cardBuffBar != null) _cardBuffBar.Unbind();
            if (!_eventsSubscribed) return;
            EventBus.Unsubscribe<OnHpChanged>(SetHealth);
            EventBus.Unsubscribe<OnShieldChanged>(SetShieldCount);
            EventBus.Unsubscribe<EnemySpawnCountChangedEvent>(OnEnemySpawnCountChanged);
            EventBus.Unsubscribe<ExperienceChangedEvent>(OnExperienceChanged);
            EventBus.Unsubscribe<CardAcquiredEvent>(OnCardAcquired);
            _eventsSubscribed = false;
        }

        private void OnCardAcquired(CardAcquiredEvent evt)
        {
            if (evt.Context != null && ReferenceEquals(evt.Context, RunContext.Current) && _cardBuffBar != null)
                _cardBuffBar.Bind(evt.Context);
        }

        private void OnEnemySpawnCountChanged(EnemySpawnCountChangedEvent evt)
        {
            if (ReferenceEquals(evt.Level, RunContext.Current?.LevelContext)) RefreshEnemyCount();
        }

        private void OnExperienceChanged(ExperienceChangedEvent evt)
        {
            if (evt.Context != null && ReferenceEquals(evt.Context, RunContext.Current))
                SetExperience(evt.Current, evt.Required);
        }

        private void RefreshExperience()
        {
            // HUD 可能晚于经验事件创建；打开或重新打开时从当前对局补读快照。
            if (ExpManager.HasInstance && ExpManager.Instance.Context != null &&
                ReferenceEquals(ExpManager.Instance.Context, RunContext.Current))
                SetExperience(ExpManager.Instance.CurrentExp, ExpManager.Instance.RequiredExp);
            else SetExperience(0, ExpManager.ExpPerRaffle);
        }

        private void SetExperience(float current, int required)
        {
            bool valid = required > 0 && !float.IsNaN(current) && !float.IsInfinity(current);
            current = valid ? Mathf.Clamp(current, 0, required) : 0;
            float ratio = valid ? current / required : 0;
            if (_experienceFill != null)
            {
                _experienceFill.rectTransform.anchorMax = new Vector2(ratio, 1);
                _experienceFill.enabled = ratio > 0;
            }
            if (_experienceValue != null)
                _experienceValue.text = valid ? $"{Mathf.FloorToInt(current)} / {required}" : "-- / --";
            if (_experienceHint != null)
                _experienceHint.text = !valid ? "等待经验数据" : current >= required ? "已获得选卡机会" :
                    $"再获 {Mathf.CeilToInt(required - current)} 经验选卡";
        }

        private void RefreshEnemyCount()
        {
            if (_remainingEnemyCount == null) return;
            LevelRuntime level = RunContext.Current?.LevelContext;
            if (level == null || !level.IsInitialized) _remainingEnemyCount.text = "--";
            else if (level.IsEndless) _remainingEnemyCount.text = "无尽";
            else _remainingEnemyCount.text = level.RemainingSpawnCount.ToString();
        }

        private void SetHealth(OnHpChanged evt)
        {
            Refresh();
        }

        private void SetShieldCount(OnShieldChanged evt)
        {
            if (evt.Source != null && !ReferenceEquals(evt.Source, RunContext.Current?.PlayerContext)) return;
            _shieldAmount = Mathf.Max(0, evt.Current);
            if (_shieldCount != null) _shieldCount.SetText("{0}", _shieldAmount);
        }

        private void Refresh()
        {
            PlayerRuntime player = RunContext.Current?.PlayerContext;
            bool initialized = player != null && player.IsInitialized;
            _maximumHealth = initialized ? Mathf.Max(0, player.MaxHp) : 0;
            _currentHealth = initialized ? Mathf.Clamp(player.CurrentHp, 0, _maximumHealth) : 0;
            RefreshHealth();
            SetShieldCount(new OnShieldChanged(initialized ? player.Shield : 0, 0));
            if (_defenseValue != null) _defenseValue.SetText("{0}", initialized ? player.BaseDefense : 0);
            RefreshEnemyCount();
            RefreshExperience();
        }

        private void RefreshHealth()
        {
            float ratio = _maximumHealth > 0f ? Mathf.Clamp01(_currentHealth / _maximumHealth) : 0f;
            if (_healthFill != null)
            {
                // 调整切片图片的宽度，保留填充末端的圆角。
                RectTransform rect = _healthFill.rectTransform;
                rect.anchorMax = new Vector2(ratio, 1f);
                _healthFill.enabled = ratio > 0f;
            }
            if (_healthValue != null)
                _healthValue.SetText("{0}/{1}", Mathf.CeilToInt(_currentHealth), Mathf.CeilToInt(_maximumHealth));
        }
    }
}
