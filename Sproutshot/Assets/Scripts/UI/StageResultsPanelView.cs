using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>一个窗口切换胜败面板；导航由战斗场景传入，关闭时恢复原时间倍率。</summary>
    [DisallowMultipleComponent, UIWindow("StageResultsPanel")]
    public sealed class StageResultsPanelView : UIView
    {
        public sealed class OpenArgs
        {
            public bool Victory { get; }
            public Action ReturnToMap { get; }
            public Action Continue { get; }

            public OpenArgs(bool victory, Action returnToMap = null, Action onContinue = null)
            {
                Victory = victory;
                ReturnToMap = returnToMap;
                Continue = onContinue;
            }
        }

        [SerializeField] private RectTransform _content;
        [SerializeField] private GameObject _victoryPanel;
        [SerializeField] private GameObject _defeatPanel;
        [SerializeField] private Button _returnButton;
        [SerializeField] private Button _nextButton;
        [SerializeField] private Button _retryButton;
        private OpenArgs _options;
        private bool _actionTaken;
        private bool _ownsPause;
        private float _previousTimeScale;
        private bool _settlementPending;
        private TMP_Text _saveStatus;
        private Button _saveRetryButton;
        private Action _saveRetry;

        public bool IsVictory { get; private set; }

        public static StageResultsPanelView Open(bool victory, Action returnToMap = null,
            Action onContinue = null) => UIManager.Instance.Open<StageResultsPanelView>(
                new OpenArgs(victory, returnToMap, onContinue));

        protected override void OnCreated()
        {
            _returnButton.onClick.AddListener(ReturnToMap);
            _nextButton.onClick.AddListener(Continue);
            _retryButton.onClick.AddListener(Continue);
            ResetScale();
        }

        protected override void OnOpen(object args)
        {
            _options = args as OpenArgs ?? new OpenArgs(args is bool victory && victory);
            SetResult(_options.Victory);
            _actionTaken = false;
            _settlementPending = false;
            if (_saveStatus != null) _saveStatus.text = string.Empty;
            if (_saveRetryButton != null) _saveRetryButton.gameObject.SetActive(false);
            SetButtonsInteractable(true);
            ResetScale();
            if (!_ownsPause)
            {
                _previousTimeScale = Time.timeScale;
                _ownsPause = true;
            }
            Time.timeScale = 0f;
        }

        public void SetResult(bool victory)
        {
            IsVictory = victory;
            _victoryPanel.SetActive(victory);
            _defeatPanel.SetActive(!victory);
        }

        private void ReturnToMap() => InvokeAction(_options?.ReturnToMap);
        private void Continue() => InvokeAction(_options?.Continue);

        private void InvokeAction(Action action)
        {
            if (!IsOpen || _actionTaken || _settlementPending || action == null) return;
            _actionTaken = true;
            SetButtonsInteractable(false);
            // 切场成功后由 BattleUIRoot 关闭；失败时保留结果页允许重试。
            try { action(); }
            finally
            {
                if (this != null && IsOpen)
                {
                    _actionTaken = false;
                    SetButtonsInteractable(true);
                }
            }
        }

        private void SetButtonsInteractable(bool enabled)
        {
            enabled &= !_settlementPending;
            _returnButton.interactable = enabled && _options?.ReturnToMap != null;
            _nextButton.interactable = _retryButton.interactable = enabled && _options?.Continue != null;
        }

        public void SetSettlementStatus(string text, bool completed, Action retry = null)
        {
            _settlementPending = !completed;
            _saveRetry = retry;
            if (_saveStatus == null)
            {
                var font = GetComponentInChildren<TMP_Text>(true)?.font ?? FindObjectOfType<TMP_Text>(true)?.font;
                var label = new GameObject("SaveStatus", typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(_content != null ? _content : transform, false);
                _saveStatus = label.GetComponent<TextMeshProUGUI>();
                _saveStatus.font = font;
                _saveStatus.fontSize = 30; _saveStatus.color = new Color(.12f,.22f,.16f);
                _saveStatus.alignment = TextAlignmentOptions.Center; _saveStatus.raycastTarget = false;
                _saveStatus.rectTransform.sizeDelta = new Vector2(820,110);
                _saveStatus.rectTransform.anchoredPosition = new Vector2(0,-310);
                var button = new GameObject("RetrySave",typeof(RectTransform),typeof(Image),typeof(Button));
                button.transform.SetParent(label.transform.parent,false);
                var rect=(RectTransform)button.transform; rect.sizeDelta=new Vector2(320,85); rect.anchoredPosition=new Vector2(0,-410);
                button.GetComponent<Image>().color=new Color(.75f,.94f,.4f);
                _saveRetryButton=button.GetComponent<Button>(); _saveRetryButton.onClick.AddListener(()=>_saveRetry?.Invoke());
                var caption = new GameObject("Label",typeof(RectTransform),typeof(TextMeshProUGUI)); caption.transform.SetParent(button.transform,false);
                var captionText=caption.GetComponent<TextMeshProUGUI>(); captionText.font=_saveStatus.font; captionText.text="重试保存";
                captionText.fontSize=30; captionText.color=_saveStatus.color; captionText.alignment=TextAlignmentOptions.Center;
                captionText.raycastTarget=false; captionText.rectTransform.sizeDelta=new Vector2(300,80);
            }
            _saveStatus.text = text;
            _saveRetryButton.gameObject.SetActive(!completed && retry != null);
            SetButtonsInteractable(completed);
        }

        private void ResetScale()
        {
            // 屏幕适配由框架 CanvasScaler 处理，不再额外缩放预制体内容。
            transform.localScale = Vector3.one;
            if (_content != null) _content.localScale = Vector3.one;
        }

        protected override void OnClose()
        {
            _saveRetry = null;
            _options = null;
            ResumeGame();
        }

        protected override void OnRelease()
        {
            _returnButton.onClick.RemoveListener(ReturnToMap);
            _nextButton.onClick.RemoveListener(Continue);
            _retryButton.onClick.RemoveListener(Continue);
            _options = null;
            ResumeGame();
        }

        private void OnDisable() => ResumeGame();
        private void OnDestroy() => ResumeGame();
        private void ResumeGame()
        {
            if (!_ownsPause) return;
            _ownsPause = false;
            Time.timeScale = _previousTimeScale;
        }
    }
}
