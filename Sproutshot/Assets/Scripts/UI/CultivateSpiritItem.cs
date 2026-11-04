using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>伙伴滚动列表的一行，只负责显示数据和转发按钮点击。</summary>
    [DisallowMultipleComponent]
    public sealed class CultivateSpiritItem : MonoBehaviour
    {
        public const float MinimumHeight = 680f;
        [SerializeField] private Image _frame;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _level;
        [SerializeField] private TMP_Text _bonus;
        [SerializeField] private TMP_Text _healthBonus;
        [SerializeField] private TMP_Text _defenseBonus;
        [SerializeField] private TMP_Text _selfAttack;
        [SerializeField] private RectTransform _skillRoot;
        [SerializeField] private TMP_Text _skillName;
        [SerializeField] private TMP_Text _skillCooldown;
        [SerializeField] private TMP_Text _skillDescription;
        [SerializeField] private TMP_Text _upgradeLabel;
        [SerializeField] private TMP_Text _deployLabel;
        [SerializeField] private GameObject _deployedCheck;
        [SerializeField] private Button _upgradeButton;
        [SerializeField] private Button _deployButton;
        [SerializeField] private Sprite _neutralFrame;
        [SerializeField] private Sprite _activeFrame;
        [SerializeField] private Sprite _deploySprite;
        [SerializeField] private Sprite _activeDeploySprite;
        private int _id;
        private bool _bound;
        private Action<int> _onUpgrade;
        private Action<int> _onDeploy;
        private bool _canUpgrade;
        private bool _deployed;
        private bool _interactionEnabled = true;
        private readonly List<SkillView> _skillViews = new List<SkillView>();
        private float _requestedHeight = MinimumHeight;
        private float _skillExtraHeight;

        private sealed class SkillView
        {
            public RectTransform Root;
            public TMP_Text Name, Cooldown, Description;
        }

        public int Id => _id;

        public void SetPreferredHeight(float height)
        {
            _requestedHeight = height;
            EnsureStatLayout();
        }

        public void Bind(CultivatePanelView.SpiritData data, bool deployed,
            Action<int> onUpgrade, Action<int> onDeploy)
        {
            EnsureStatLayout();
            if (!_bound)
            {
                _upgradeButton.onClick.AddListener(RequestUpgrade);
                _deployButton.onClick.AddListener(RequestDeploy);
                _bound = true;
            }
            _id = data.id;
            _canUpgrade = data.canUpgrade;
            _onUpgrade = onUpgrade;
            _onDeploy = onDeploy;
            _icon.sprite = data.icon;
            _icon.enabled = data.icon != null;
            _name.text = data.name;
            _level.text = $"Lv.{data.level}";
            int attack = data.attackBonus, health = data.healthBonus, defense = data.defenseBonus;
            if (attack == 0 && health == 0 && defense == 0 && data.bonus != 0)
            {
                if (data.statLabel == "生命") health = data.bonus;
                else if (data.statLabel == "防御") defense = data.bonus;
                else attack = data.bonus;
            }
            SetStat(_bonus, "主角攻击", attack, data.nextAttackBonus, data.hasUpgradePreview);
            SetStat(_healthBonus, "主角生命", health, data.nextHealthBonus, data.hasUpgradePreview);
            SetStat(_defenseBonus, "主角防御", defense, data.nextDefenseBonus, data.hasUpgradePreview);
            SetStat(_selfAttack, "自身攻击", data.selfAttack, data.nextSelfAttack, data.hasUpgradePreview, false);
            BindSkills(data.skills);
            EnsureStatLayout();
            _upgradeLabel.text = CultivatePanelView.UpgradeLabel(data.upgradeCost, data.isMaxLevel, data.unavailableReason);
            SetDeployed(deployed);
        }

        private void BindSkills(IReadOnlyList<SpiritSkillPresentation.Skill> skills)
        {
            if (_skillRoot == null) return;
            if (_skillViews.Count == 0)
                _skillViews.Add(new SkillView { Root = _skillRoot, Name = _skillName,
                    Cooldown = _skillCooldown, Description = _skillDescription });
            int count = Mathf.Max(1, skills?.Count ?? 0);
            float totalHeight = 0;
            for (int i = 0; i < count; i++)
            {
                if (i == _skillViews.Count)
                {
                    var root = Instantiate(_skillRoot, _skillRoot.parent);
                    root.name = "SkillInfo_" + i;
                    _skillViews.Add(new SkillView { Root = root,
                        Name = root.Find("Name").GetComponent<TMP_Text>(),
                        Cooldown = root.Find("Cooldown").GetComponent<TMP_Text>(),
                        Description = root.Find("Description").GetComponent<TMP_Text>() });
                }
                var view = _skillViews[i];
                view.Root.gameObject.SetActive(true);
                bool exists = skills != null && i < skills.Count;
                var skill = exists ? skills[i] : default;
                if (view.Name != null) view.Name.text = exists
                    ? skill.Name + (string.IsNullOrEmpty(skill.Role) ? string.Empty
                        : $"  <size=24><color=#60785B>{skill.Role}</color></size>") : "暂无技能";
                if (view.Cooldown != null) view.Cooldown.text = exists && skill.IsConfigured
                    ? $"冷却 {skill.CooldownSeconds.ToString("0.##", CultureInfo.InvariantCulture)} 秒" : string.Empty;
                if (view.Description != null)
                {
                    view.Description.text = exists ? skill.Description : "暂无技能信息";
                    float height = Mathf.Max(114f, view.Description.GetPreferredValues(view.Description.text,
                        view.Description.rectTransform.rect.width, Mathf.Infinity).y);
                    view.Description.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                    view.Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height + 76f);
                }
                totalHeight += view.Root.rect.height + (i > 0 ? 12f : 0f);
            }
            for (int i = count; i < _skillViews.Count; i++) _skillViews[i].Root.gameObject.SetActive(false);
            _skillExtraHeight = Mathf.Max(0f, totalHeight - 190f);
        }

        private static void SetStat(TMP_Text target, string label, int current, int next, bool preview, bool bonus = true)
        {
            if (target == null) return;
            // 固定列位置；箭头复用主角区的图形，避免字体缺少箭头字符时显示方框。
            string prefix = bonus ? "+" : string.Empty;
            target.text = preview
                ? $"<pos=0>{label}<pos=210>{prefix}{current}<pos=420><color=#397431>{prefix}{next}</color>"
                : $"<pos=0>{label}<pos=210>{prefix}{current}";
            var arrow = target.transform.Find("UpgradeArrow");
            if (arrow != null) arrow.gameObject.SetActive(preview);
        }

        public void SetDeployed(bool deployed)
        {
            _deployed = deployed;
            _frame.sprite = deployed ? _activeFrame : _neutralFrame;
            ((Image)_deployButton.targetGraphic).sprite = deployed ? _activeDeploySprite : _deploySprite;
            _deployLabel.text = deployed ? "卸下" : "出战";
            _deployLabel.margin = deployed ? new Vector4(28, 0, 0, 0) : Vector4.zero;
            _deployedCheck.SetActive(deployed);
            SetInteractionEnabled(_interactionEnabled);
        }

        public void SetInteractionEnabled(bool enabled)
        {
            _interactionEnabled = enabled;
            _upgradeButton.interactable = enabled && _canUpgrade;
            _deployButton.interactable = enabled;
        }

        private void EnsureStatLayout()
        {
            float height = Mathf.Max(_requestedHeight, MinimumHeight + _skillExtraHeight);
            var layout = GetComponent<LayoutElement>();
            if (layout != null) layout.minHeight = layout.preferredHeight = height;
            float offset = (height - MinimumHeight) * .5f;
            if (_selfAttack == null && _bonus != null)
            {
                _selfAttack = Instantiate(_bonus, _bonus.transform.parent);
                _selfAttack.name = "SelfAttack";
            }
            Place(_name.rectTransform, 300, 280 + offset);
            Place((RectTransform)_level.transform.parent, 740, 280 + offset);
            PlaceStat(_bonus, 210 + offset); PlaceStat(_healthBonus, 157 + offset);
            PlaceStat(_defenseBonus, 104 + offset); PlaceStat(_selfAttack, 51 + offset);
            Place(_icon.rectTransform, 18, 155 + offset);
            float skillTop = -15 + offset;
            if (_skillViews.Count == 0 && _skillRoot != null) Place(_skillRoot, 28, skillTop);
            foreach (var view in _skillViews)
            {
                if (!view.Root.gameObject.activeSelf) continue;
                Place(view.Root, 28, skillTop);
                skillTop -= view.Root.rect.height + 12f;
            }
            Place((RectTransform)_upgradeButton.transform, 300, -265 - offset);
            Place((RectTransform)_deployButton.transform, 640, -265 - offset);
        }

        private static void Place(RectTransform rect, float x, float y) => rect.anchoredPosition = new Vector2(x, y);

        private static void PlaceStat(TMP_Text text, float y)
        {
            if (text == null) return;
            Place(text.rectTransform, 300, y);
            text.fontSize = 34;
            text.fontSizeMax = 34;
            text.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 590);
            var arrow = text.transform.Find("UpgradeArrow") as RectTransform;
            if (arrow != null) arrow.anchoredPosition = new Vector2(350, -12);
        }

        public void ClearCallbacks() { _onUpgrade = null; _onDeploy = null; }
        private void RequestUpgrade() { if (_interactionEnabled && _canUpgrade) _onUpgrade?.Invoke(_id); }
        private void RequestDeploy() { if (_interactionEnabled) _onDeploy?.Invoke(_deployed ? 0 : _id); }

        private void OnDestroy()
        {
            if (_bound)
            {
                _upgradeButton.onClick.RemoveListener(RequestUpgrade);
                _deployButton.onClick.RemoveListener(RequestDeploy);
            }
            ClearCallbacks();
        }
    }
}
