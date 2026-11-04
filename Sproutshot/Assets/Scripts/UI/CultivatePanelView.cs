using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sproutshot.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;
using UnityEngine.UI;
using YooAsset;

namespace Sproutshot.UI
{
    /// <summary>养成页面的显示与列表管理；升级请求交给业务处理，不在 UI 中扣除种子。</summary>
    [DisallowMultipleComponent, UIWindow("CultivatePanel")]
    public sealed class CultivatePanelView : UIView
    {
        [Serializable]
        public sealed class SpiritData
        {
            public int id;
            public Sprite icon;
            public string name;
            public int level;
            public int attackBonus;
            public int healthBonus;
            public int defenseBonus;
            public bool hasUpgradePreview;
            public int nextAttackBonus;
            public int nextHealthBonus;
            public int nextDefenseBonus;
            public int selfAttack;
            public int nextSelfAttack;
            public IReadOnlyList<SpiritSkillPresentation.Skill> skills = Array.Empty<SpiritSkillPresentation.Skill>();
            public bool canUpgrade = true;
            public bool isMaxLevel;
            public string unavailableReason;
            // 兼容旧版只传入一项加成的调用；新界面使用上面的三项属性。
            [HideInInspector]
            public string statLabel;
            [HideInInspector]
            public int bonus;
            public int upgradeCost;
        }

        [SerializeField] private RectTransform _designRoot;
        [SerializeField] private ScrollRect _spiritScroll;
        [FormerlySerializedAs("_itemTemplate")]
        [SerializeField, Tooltip("独立的精灵伙伴卡片预制体；列表新增项统一从此资源创建。")]
        private CultivateSpiritItem _itemPrefab;
        // 运行时卡片缓存与玩家数据，不在预制体内保存示例列表。
        private readonly List<CultivateSpiritItem> _items = new List<CultivateSpiritItem>();
        private List<SpiritData> _spirits = new List<SpiritData>();
        private int _deployedSpiritId;
        [SerializeField] private Button _heroUpgradeButton;
        [SerializeField] private TMP_Text _heroLevel;
        [SerializeField] private TMP_Text _heroNextLevel;
        [SerializeField] private TMP_Text _attackCurrent;
        [SerializeField] private TMP_Text _attackNext;
        [SerializeField] private TMP_Text _healthCurrent;
        [SerializeField] private TMP_Text _healthNext;
        [SerializeField] private TMP_Text _defenseCurrent;
        [SerializeField] private TMP_Text _defenseNext;
        [SerializeField] private TMP_Text _heroUpgradeLabel;
        [SerializeField] private HeroProfessionSelector _heroProfessionSelector;
        private CultivatePanelData.HeroData _heroData;
        private EconomyProfile _boundProfile;
        private EconomyCatalog _boundCatalog;
        private bool _sceneManaged;
        private bool _resizing;
        private bool _subscribed;
        private bool _dataReady = true;
        private bool _busy;
        private bool _refreshing;
        private bool _preparing;
        private bool _heroCanUpgrade = true;
        private int _openVersion;
        private cfg.Tables _tables;
        private TMP_Text _status;
        private TMP_Text _empty;
        private Button _refreshButton;
        private SpiritPortraitAssets _portraits;
        private Coroutine _portraitLoading;
        private Coroutine _preparation;
        public bool IsBusy => _busy || _refreshing || _preparing;

        public UnityEvent heroUpgradeRequested = new UnityEvent();
        public UnityEvent<int> spiritUpgradeRequested = new UnityEvent<int>();
        public UnityEvent<int> deploymentChanged = new UnityEvent<int>();
        public int DeployedSpiritId => _deployedSpiritId;

        public static CultivatePanelView Open() => UIManager.Instance.Open<CultivatePanelView>();

        /// <summary>MainCamp 常驻页面入口：复用 UIView 初始化，使用营地统一的切页动画。</summary>
        public void OnOpened(float duration, Vector2? slideOffset = null)
        {
            _sceneManaged = true;
            InitializeView();
            OpenView(null);
            ProceduralAnimation.DoPanelVisibility(transform, true, duration, slideOffset);
        }

        public void OnClosed(float duration, Vector2? slideOffset = null)
        {
            _spiritScroll.StopMovement();
            ProceduralAnimation.DoPanelVisibility(transform, false, duration, slideOffset);
        }

        private void OnDisable()
        {
            // 切页动画完成后才关闭 UIView，保留退出动画并清理行回调。
            if (_sceneManaged && IsOpen) CloseView();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            ReleasePortraits();
            if (_sceneManaged) ReleaseView();
        }

        protected override void OnCreated()
        {
            _heroUpgradeButton.onClick.AddListener(RequestHeroUpgrade);
            if (_heroProfessionSelector != null)
            {
                _heroProfessionSelector.Initialize();
                _heroProfessionSelector.SelectionChanged += RefreshHeroProfession;
                _heroProfessionSelector.DeployRequested += RequestProfessionDeploy;
            }
            EnsureStatusControls();
            Resize();
            RefreshSpirits();
        }

        protected override void OnOpen(object args)
        {
            _heroProfessionSelector?.PrepareForOpen();
            Resize();
            RefreshSpirits();
            if (!Application.isPlaying) return;
            ++_openVersion;
            if (_preparation != null) StopCoroutine(_preparation);
            _refreshing = false;
            if (!_subscribed) { CloudEconomy.Changed += OnProfileChanged; _subscribed = true; }
            _dataReady = false;
            ShowUnavailable("正在读取养成数据…");
            _preparation = StartCoroutine(PrepareAndRefresh());
        }

        private IEnumerator PrepareAndRefresh()
        {
            _preparing = true;
            UpdateInteraction();
            var manager = YooAssetManager.Instance;
            if (!manager.IsInitialized) yield return manager.InitDefaultPackage();
            _preparing = false;
            _preparation = null;
            if (!manager.IsInitialized) { ShowUnavailable("属性资源加载失败，请刷新重试。"); yield break; }
            try { _tables = LubanConfigManager.Instance.Tables; }
            catch (Exception error) { ShowUnavailable(error.Message); yield break; }
            OnProfileChanged();
            _ = RefreshAsync();
        }

        /// <summary>只读取最新档案。升级、出战成功后 CloudEconomy.Changed 也会自动刷新本页。</summary>
        public async Task RefreshAsync()
        {
            if (_refreshing || _preparing) return;
            int version = _openVersion;
            _refreshing = true;
            if (_status != null) _status.text = "正在更新养成数据…";
            UpdateInteraction();
            try
            {
                await CloudEconomy.RefreshAsync();
                if (this != null && IsOpen && version == _openVersion) OnProfileChanged();
            }
            catch (Exception error)
            {
                if (this == null || !IsOpen || version != _openVersion) return;
                if (_dataReady) _status.text = "更新失败，当前显示上次读取的数据，请刷新重试。";
                else ShowUnavailable(CloudBaseClient.IsSupportedPlatform ? error.Message : "暂未连接存档，请连接后刷新。");
            }
            finally
            {
                if (this != null && version == _openVersion) { _refreshing = false; UpdateInteraction(); }
            }
        }

        private void OnProfileChanged()
        {
            if (!IsOpen || _tables == null) return;
            var profile = CloudEconomy.Profile;
            var catalog = CloudEconomy.Catalog;
            if (profile == null || catalog == null) return;
            try { BindProfile(profile, catalog, _tables); }
            catch (Exception error) { ShowUnavailable(error.Message); }
        }

        /// <summary>从档案生成整页展示；不扣资源、不升级、不改变出战精灵。也可用于独立预览。</summary>
        public void BindProfile(EconomyProfile profile, EconomyCatalog catalog, cfg.Tables tables)
        {
            var data = CultivatePanelData.Create(profile, catalog, tables);
            if (!ReferenceEquals(_tables, tables)) ReleasePortraits();
            _tables = tables;
            if (_portraits == null) _portraits = new SpiritPortraitAssets(tables);
            EnsureStatusControls();
            _dataReady = true;
            _boundProfile = profile;
            _boundCatalog = catalog;
            _heroProfessionSelector?.BindProfile(profile);
            _heroData = data.Hero;
            RefreshHeroProfession();
            foreach (var spirit in data.Spirits)
            {
                spirit.icon = _portraits.Get(spirit.id);
            }
            bool changedOwner = _displayedPlayer != profile.playerId;
            _displayedPlayer = profile.playerId;
            SetSpirits(data.Spirits, profile.equippedSpiritId, changedOwner);
            _empty.text = data.Spirits.Count == 0 ? "尚未获得精灵伙伴\n前往精灵召唤，寻找你的伙伴吧" : string.Empty;
            _empty.gameObject.SetActive(data.Spirits.Count == 0);
            _status.text = string.Empty;
            UpdateInteraction();
            bool missingPortrait = false;
            foreach (var spirit in data.Spirits)
                if (!_portraits.IsLoaded(spirit.id)) missingPortrait = true;
            if (Application.isPlaying && isActiveAndEnabled && _portraitLoading == null && missingPortrait)
                _portraitLoading = StartCoroutine(LoadPortraits());
        }

        private string _displayedPlayer;

        /// <summary>由你自己的升级/出战处理在请求前后设置，统一禁止重复点击。</summary>
        public void SetBusy(bool busy) { _busy = busy; UpdateInteraction(); }

        public static string UpgradeLabel(int cost, bool maxLevel, string reason) => maxLevel ? "已满级" :
            reason == "配置缺失" ? reason : reason == "种子不足" ? $"种子不足 · {cost}" : $"升级 · {cost}种子";

        private void RefreshClicked()
        {
            if (IsBusy || !Application.isPlaying) return;
            if (_tables == null) _preparation = StartCoroutine(PrepareAndRefresh());
            else _ = RefreshAsync();
        }

        /// <summary>替换伙伴数据；列表按需要复用或创建行，超出的行会隐藏。</summary>
        public void SetSpirits(IReadOnlyList<SpiritData> spirits, int deployedSpiritId = -1, bool resetScroll = true)
        {
            _spirits = spirits == null ? new List<SpiritData>() : new List<SpiritData>(spirits);
            _deployedSpiritId = deployedSpiritId;
            RefreshSpirits();
            _spiritScroll.StopMovement();
            if (resetScroll) _spiritScroll.verticalNormalizedPosition = 1f;
        }

        public void SetHero(int level, int attack, int nextAttack, int health, int nextHealth, int upgradeCost)
        {
            _heroLevel.text = $"Lv.{level}";
            _heroNextLevel.text = $"Lv.{level + 1}";
            _attackCurrent.text = attack.ToString(); _attackNext.text = nextAttack.ToString();
            _healthCurrent.text = health.ToString(); _healthNext.text = nextHealth.ToString();
            if (_defenseCurrent != null) _defenseCurrent.text = "--";
            if (_defenseNext != null) _defenseNext.text = "--";
            _heroUpgradeLabel.text = $"升级 · {upgradeCost}种子";
            _heroCanUpgrade = true;
            SetHeroPreview(true);
        }

        /// <summary>显示主角三项总属性及升级后的预览；数值由调用方提供。</summary>
        public void SetHero(int level, int attack, int nextAttack, int health, int nextHealth,
            int defense, int nextDefense, int upgradeCost)
        {
            SetHero(level, attack, nextAttack, health, nextHealth, upgradeCost);
            if (_defenseCurrent != null) _defenseCurrent.text = defense.ToString();
            if (_defenseNext != null) _defenseNext.text = nextDefense.ToString();
        }

        public void SetDeployedSpirit(int id)
        {
            _deployedSpiritId = id;
            foreach (CultivateSpiritItem item in _items)
                if (item != null && item.gameObject.activeSelf) item.SetDeployed(item.Id == id);
        }

        private void RefreshSpirits()
        {
            if (_itemPrefab == null || _spiritScroll == null) return;
            for (int i = 0; i < _spirits.Count; i++)
            {
                if (i >= _items.Count)
                    _items.Add(Instantiate(_itemPrefab, _spiritScroll.content));
                else if (_items[i] == null)
                    _items[i] = Instantiate(_itemPrefab, _spiritScroll.content);
                CultivateSpiritItem item = _items[i];
                item.gameObject.SetActive(true);
                item.transform.SetSiblingIndex(i);
                item.Bind(_spirits[i], _spirits[i].id == _deployedSpiritId, RequestSpiritUpgrade, RequestDeploy);
            }
            for (int i = _spirits.Count; i < _items.Count; i++)
            {
                if (_items[i] == null) continue;
                _items[i].ClearCallbacks();
                _items[i].gameObject.SetActive(false);
            }
            Resize();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_spiritScroll.content);
            UpdateInteraction();
        }

        public async void Request2Update() => await RunCultivationOperationAsync(CloudEconomy.UpgradePlayerAsync);

        public async void Request2Update(int spiritId) =>
            await RunCultivationOperationAsync(() => CloudEconomy.UpgradeSpiritAsync(spiritId));

        public async void Deploy(int spiritId) =>
            await RunCultivationOperationAsync(() => CloudEconomy.EquipSpiritAsync(spiritId));

        private async void RequestProfessionDeploy(int professionId)
        {
            if (!_dataReady || IsBusy || _boundProfile == null ||
                professionId != SelectedProfessionId || HeroProfessionCatalog.Find(professionId)?.available != true ||
                HeroProfessionCatalog.EquippedId(_boundProfile) == professionId) return;
            await RunCultivationOperationAsync(() => CloudEconomy.EquipProfessionAsync(professionId));
        }

        private async Task RunCultivationOperationAsync(Func<Task<CloudBaseData>> operation)
        {
            if (this == null || IsBusy) return;
            int version = _openVersion;
            SetBusy(true);
            try
            {
                // 接口成功后会发布 Changed，由现有订阅刷新等级、余额和出战标记。
                await operation();
                if (this != null && IsOpen && version == _openVersion)
                    AudioManager.Instance.PlaySfx(AudioId.PositiveChime);
            }
            catch (Exception error)
            {
                // 请求可在离开页面后完成，避免在其他页面上弹出过期提示。
                if (this != null && IsOpen && version == _openVersion)
                    CloudFeedback.Show(error.Message, font: _heroUpgradeLabel != null ? _heroUpgradeLabel.font : null);
            }
            finally
            {
                if (this != null) SetBusy(false);
            }
        }

        private void RequestHeroUpgrade()
        {
            if (_dataReady && !IsBusy && _heroCanUpgrade && _heroData?.available == true) heroUpgradeRequested.Invoke();
        }

        private int SelectedProfessionId => _heroProfessionSelector == null ? HeroProfessionCatalog.EquippedId(_boundProfile) : _heroProfessionSelector.SelectedProfessionId;

        private void RefreshHeroProfession()
        {
            if (_dataReady && _boundProfile != null && _boundCatalog != null && _tables != null)
                _heroData = CultivatePanelData.CreateHero(_boundProfile, _boundCatalog, _tables, SelectedProfessionId);
            if (HeroProfessionCatalog.Find(SelectedProfessionId)?.available != true ||
                (_dataReady && _heroData != null && !_heroData.available))
            {
                string reason = _heroData?.professionId == SelectedProfessionId ? _heroData.unavailableReason : "尚未开放";
                _heroLevel.text = reason == "配置缺失" ? reason : "未开放";
                _heroNextLevel.text = string.Empty;
                _attackCurrent.text = _attackNext.text = _healthCurrent.text = _healthNext.text = "--";
                if (_defenseCurrent != null) _defenseCurrent.text = _defenseNext.text = "--";
                _heroCanUpgrade = false;
                _heroUpgradeLabel.text = reason ?? "尚未开放";
                SetHeroPreview(false);
            }
            else if (_dataReady && _heroData != null)
            {
                var hero = _heroData;
                SetHero(hero.level, hero.attack, hero.nextAttack, hero.health, hero.nextHealth,
                    hero.defense, hero.nextDefense, hero.cost);
                _heroCanUpgrade = hero.canUpgrade;
                _heroUpgradeLabel.text = UpgradeLabel(hero.cost, hero.maxLevel, hero.unavailableReason);
                _heroNextLevel.text = hero.maxLevel ? "满级" : $"Lv.{hero.level + 1}";
                SetHeroPreview(!hero.maxLevel && hero.unavailableReason != "配置缺失");
            }
            else if (!_dataReady)
            {
                _heroLevel.text = _heroNextLevel.text = "Lv.--";
                _attackCurrent.text = _attackNext.text = _healthCurrent.text = _healthNext.text = "--";
                if (_defenseCurrent != null) _defenseCurrent.text = _defenseNext.text = "--";
                _heroUpgradeLabel.text = "等待数据";
                SetHeroPreview(false);
            }
            UpdateInteraction();
        }
        private void RequestSpiritUpgrade(int id)
        {
            if (!_dataReady || IsBusy) return;
            foreach (var spirit in _spirits)
                if (spirit.id == id && spirit.canUpgrade) { spiritUpgradeRequested.Invoke(id); return; }
        }
        private void RequestDeploy(int id)
        {
            if (!_dataReady || IsBusy || id == _deployedSpiritId) return;
            // 0 是服务端约定的空出战位；只有当前确实出战了伙伴时才发送卸下请求。
            if (id == 0)
            {
                if (_deployedSpiritId > 0) deploymentChanged.Invoke(0);
                return;
            }
            foreach (var spirit in _spirits)
                if (spirit.id == id) { deploymentChanged.Invoke(id); return; }
        }

        private void OnRectTransformDimensionsChange() => Resize();
        private void Resize()
        {
            if (_designRoot == null || _spiritScroll == null || _resizing) return;
            Vector2 size = ((RectTransform)transform).rect.size;
            if (size.x <= 0 || size.y <= 0) return;
            _resizing = true;
            try
            {
                // 正常竖屏按宽度排版，伙伴区使用剩余高度；只在很矮的窗口中整体缩小。
                float scale = Mathf.Min(size.x / 1080f, size.y / 1280f);
                float designHeight = size.y / scale;
                _designRoot.localScale = Vector3.one * scale;
                _designRoot.sizeDelta = new Vector2(1080f, designHeight);
                var partners = (RectTransform)_spiritScroll.transform.parent;
                float panelHeight = Mathf.Max(360f, designHeight + partners.anchoredPosition.y - 18f);
                partners.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight);
                ((RectTransform)_spiritScroll.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, panelHeight - 204f);
                // 默认三位伙伴也有可滚动内容；卡片保持最小高度，不再压成三条细行。
                float itemHeight = Mathf.Max(CultivateSpiritItem.MinimumHeight, (_spiritScroll.viewport.rect.height - 16f) / 2.2f);
                foreach (CultivateSpiritItem item in _items)
                    if (item != null) item.SetPreferredHeight(itemHeight);
            }
            finally { _resizing = false; }
        }

        protected override void OnClose()
        {
            ++_openVersion;
            _refreshing = _preparing = false;
            Unsubscribe();
            if (_preparation != null) { StopCoroutine(_preparation); _preparation = null; }
            ReleasePortraits();
            _spiritScroll.StopMovement();
            foreach (CultivateSpiritItem item in _items) item.ClearCallbacks();
        }

        protected override void OnRelease()
        {
            _heroUpgradeButton.onClick.RemoveListener(RequestHeroUpgrade);
            if (_heroProfessionSelector != null)
            {
                _heroProfessionSelector.SelectionChanged -= RefreshHeroProfession;
                _heroProfessionSelector.DeployRequested -= RequestProfessionDeploy;
            }
            if (_refreshButton != null) _refreshButton.onClick.RemoveListener(RefreshClicked);
            Unsubscribe();
            ReleasePortraits();
            foreach (CultivateSpiritItem item in _items) if (item != null) item.ClearCallbacks();
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            CloudEconomy.Changed -= OnProfileChanged;
            _subscribed = false;
        }

        private void UpdateInteraction()
        {
            bool enabled = _dataReady && !IsBusy;
            if (_heroUpgradeButton != null) _heroUpgradeButton.interactable = enabled && _heroCanUpgrade && _heroData?.available == true;
            if (_heroProfessionSelector != null)
            {
                _heroProfessionSelector.SetDataReady(_dataReady);
                _heroProfessionSelector.SetBusy(IsBusy);
            }
            foreach (var item in _items) if (item != null) item.SetInteractionEnabled(enabled);
            if (_refreshButton != null) _refreshButton.interactable = !IsBusy;
        }

        private void ShowUnavailable(string message)
        {
            _dataReady = false;
            _heroLevel.text = _heroNextLevel.text = "Lv.--";
            _attackCurrent.text = _attackNext.text = _healthCurrent.text = _healthNext.text = "--";
            if (_defenseCurrent != null) _defenseCurrent.text = _defenseNext.text = "--";
            _heroUpgradeLabel.text = "等待数据";
            SetHeroPreview(false);
            SetSpirits(null, -1, false);
            _status.text = message;
            _empty.text = message;
            _empty.gameObject.SetActive(true);
            RefreshHeroProfession();
        }

        private void SetHeroPreview(bool enabled)
        {
            foreach (var text in new[] { _attackNext, _healthNext, _defenseNext })
            {
                if (text == null) continue;
                text.gameObject.SetActive(enabled);
                var arrow = text.transform.parent.Find("Arrow");
                if (arrow != null) arrow.gameObject.SetActive(enabled);
            }
            var levelArrow = _heroNextLevel.transform.parent.Find("LevelArrow");
            if (levelArrow != null) levelArrow.gameObject.SetActive(enabled);
        }

        private void EnsureStatusControls()
        {
            if (_status != null || _designRoot == null || _spiritScroll == null) return;
            _status = CreateLabel("CultivateStatus", _designRoot, new Vector2(790, 44), 26);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = new Vector2(0, 1);
            _status.rectTransform.pivot = new Vector2(0, .5f);
            _status.rectTransform.anchoredPosition = new Vector2(40, -160);
            _status.alignment = TextAlignmentOptions.MidlineLeft;
            _status.enableWordWrapping = false;
            _status.overflowMode = TextOverflowModes.Ellipsis;
            var refresh = new GameObject("RefreshProfile", typeof(RectTransform), typeof(Image), typeof(Button));
            refresh.transform.SetParent(_designRoot, false);
            var rect = (RectTransform)refresh.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.sizeDelta = new Vector2(150, 44); rect.anchoredPosition = new Vector2(-120, -160);
            var image = refresh.GetComponent<Image>();
            image.sprite = ((Image)_heroUpgradeButton.targetGraphic).sprite; image.type = Image.Type.Sliced;
            _refreshButton = refresh.GetComponent<Button>();
            _refreshButton.onClick.AddListener(RefreshClicked);
            var label = CreateLabel("Label", refresh.transform, new Vector2(140, 44), 28);
            label.text = "刷新";
            _empty = CreateLabel("EmptyState", _spiritScroll.viewport, Vector2.zero, 34);
            _empty.rectTransform.anchorMin = Vector2.zero; _empty.rectTransform.anchorMax = Vector2.one;
            _empty.rectTransform.sizeDelta = Vector2.zero; _empty.enableWordWrapping = true;
            _empty.gameObject.SetActive(false);
            var hint = _spiritScroll.transform.parent.Find("DuplicateHint")?.GetComponent<TMP_Text>();
            if (hint != null) { hint.text = "伙伴加成仅在出战后生效"; hint.fontSize = 26; }
        }

        private TMP_Text CreateLabel(string name, Transform parent, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.font = _heroUpgradeLabel.font; text.fontSize = fontSize;
            text.color = new Color(.1f, .23f, .18f); text.raycastTarget = false;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = size;
            return text;
        }

        private IEnumerator LoadPortraits()
        {
            // 先 yield，确保同步完成的协程不会留下已经结束的引用。
            yield return null;
            var ids = new List<int>();
            foreach (var spirit in _spirits) ids.Add(spirit.id);
            if (_portraits != null) yield return _portraits.Load(ids);
            _portraitLoading = null;
            OnProfileChanged();
        }

        private void ReleasePortraits()
        {
            if (_portraitLoading != null) { StopCoroutine(_portraitLoading); _portraitLoading = null; }
            _portraits?.Dispose();
            _portraits = null;
        }
    }
}
