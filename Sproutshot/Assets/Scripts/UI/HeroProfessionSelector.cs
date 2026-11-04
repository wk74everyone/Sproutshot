using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sproutshot.Utils;

namespace Sproutshot.UI
{
    /// <summary>浏览和已出战状态分开；只有服务器确认后才更新出战标记。</summary>
    [DisallowMultipleComponent]
    public sealed class HeroProfessionSelector : MonoBehaviour
    {
        [SerializeField] private Image _portrait;
        [SerializeField] private Sprite _archerPortrait;
        [SerializeField] private Sprite _guardianPortrait;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _specialty;
        [SerializeField] private TMP_Text _description;
        [SerializeField] private TMP_Text _page;
        [SerializeField] private Button _previous;
        [SerializeField] private Button _next;
        [SerializeField] private Button _deploy;
        [SerializeField] private TMP_Text _deployLabel;

        private int _selectedIndex;
        private bool _initialized;
        private bool _busy;
        private bool _dataReady;
        private bool _restoreSelection = true;
        private string _playerId;
        private int _equippedId;
        public event Action SelectionChanged;
        public event Action<int> DeployRequested;
        public int SelectedProfessionId => HeroProfessionCatalog.All[_selectedIndex].id;
        public bool IsShooterSelected => SelectedProfessionId == HeroProfessionCatalog.DefaultProfessionId;

        public void Initialize()
        {
            if (!_initialized)
            {
                _previous.onClick.AddListener(Previous);
                _next.onClick.AddListener(Next);
                _deploy.onClick.AddListener(RequestDeploy);
                _initialized = true;
            }
            Refresh();
        }

        public void Select(int index)
        {
            if (_busy || index < 0 || index >= HeroProfessionCatalog.All.Count || index == _selectedIndex) return;
            _selectedIndex = index;
            Refresh();
            SelectionChanged?.Invoke();
        }

        private void Previous() => Select((_selectedIndex + HeroProfessionCatalog.All.Count - 1) % HeroProfessionCatalog.All.Count);
        private void Next() => Select((_selectedIndex + 1) % HeroProfessionCatalog.All.Count);

        public void PrepareForOpen() { _restoreSelection = true; SetDataReady(false); }
        public void BindProfile(EconomyProfile profile)
        {
            if (profile == null) { SetDataReady(false); return; }
            _equippedId = HeroProfessionCatalog.EquippedId(profile);
            if (_restoreSelection || _playerId != profile.playerId)
            {
                _selectedIndex = 0;
                for (int i = 0; i < HeroProfessionCatalog.All.Count; i++)
                    if (HeroProfessionCatalog.All[i].id == _equippedId) { _selectedIndex = i; break; }
                _restoreSelection = false;
            }
            _playerId = profile.playerId;
            _dataReady = true;
            Refresh();
        }
        public void SetDataReady(bool ready) { _dataReady = ready; Refresh(); }
        private bool CanDeploy => _dataReady && !_busy && HeroProfessionCatalog.All[_selectedIndex].available && SelectedProfessionId != _equippedId;
        private void RequestDeploy() { if (CanDeploy) DeployRequested?.Invoke(SelectedProfessionId); }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            Refresh();
        }

        private void Refresh()
        {
            bool archer = IsShooterSelected;
            var profession = HeroProfessionCatalog.All[_selectedIndex];
            _portrait.sprite = archer ? _archerPortrait : _guardianPortrait;
            _portrait.preserveAspect = true;
            _name.text = profession.name;
            _specialty.text = profession.specialty;
            _description.text = profession.description;
            _page.text = $"{_selectedIndex + 1} / {HeroProfessionCatalog.All.Count}";
            _deployLabel.text = !profession.available ? "未开放" : !_dataReady ? "等待数据" :
                _busy ? "处理中…" : profession.id == _equippedId ? "已出场" : "出场";
            _deploy.interactable = CanDeploy;
            _previous.interactable = _next.interactable = !_busy;
        }

        private void OnDestroy()
        {
            if (!_initialized) return;
            _previous.onClick.RemoveListener(Previous);
            _next.onClick.RemoveListener(Next);
            _deploy.onClick.RemoveListener(RequestDeploy);
        }
    }
}
