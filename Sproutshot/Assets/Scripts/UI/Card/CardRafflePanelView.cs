using System;
using System.Collections.Generic;
using cfg;
using DG.Tweening;
using Sproutshot.Gameplay;
using Sproutshot.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using YooAsset;

namespace Sproutshot.UI
{
    [DisallowMultipleComponent, UIWindow("CardRafflePanel")]
    public sealed class CardRafflePanelView : UIView
    {
        /// <summary>单次打开的参数；本次确认回调不会累加到下一次打开。</summary>
        public sealed class OpenArgs
        {
            public Action<int> OnConfirmed { get; }
            public bool CloseOnConfirmed { get; }

            public OpenArgs(Action<int> onConfirmed = null, bool closeOnConfirmed = true)
            {
                OnConfirmed = onConfirmed;
                CloseOnConfirmed = closeOnConfirmed;
            }
        }

        /// <summary>资源包已由游戏启动流程初始化后调用。选中并确认时回传配置 ID，默认关闭界面。</summary>
        public static CardRafflePanelView Open(Action<int> onConfirmed = null, bool closeOnConfirmed = true)
        {
            var args = new OpenArgs(onConfirmed, closeOnConfirmed);
            return UIManager.Instance.Open<CardRafflePanelView>(args);
        }

        /// <summary>通过框架异步加载预制体；回调含义与 Open 相同。</summary>
        public static Coroutine OpenAsync(Action<int> onConfirmed = null,
            Action<CardRafflePanelView> onOpened = null, bool closeOnConfirmed = true)
        {
            var args = new OpenArgs(onConfirmed, closeOnConfirmed);
            return UIManager.Instance.OpenAsync<CardRafflePanelView>(onOpened, args);
        }

        [Serializable] public sealed class Choice
        {
            public int id = -1;
            public string title;
            [TextArea] public string effect;
            [TextArea] public string lore;
            public Button button;
            public GameObject selectionOutline;
            public GameObject selectionCheck;
            public Image icon;
            public TMP_Text titleLabel;
            public TMP_Text effectLabel;
            public TMP_Text rarityLabel;
            public CardRaffleShape rarityBadge;
            public CardRaffleFrame frame;

            // 兼容已有预制体；新建预制体也可直接在 Inspector 绑定。
            public void CacheComponents()
            {
                if (button == null) return;
                if (icon == null) icon = Find<Image>("Icon");
                if (titleLabel == null) titleLabel = Find<TMP_Text>("Title");
                if (effectLabel == null) effectLabel = Find<TMP_Text>("Effect");
                if (rarityLabel == null) rarityLabel = Find<TMP_Text>("Rarity");
                if (rarityBadge == null) rarityBadge = Find<CardRaffleShape>("RarityBadge");
                if (frame == null) frame = button.GetComponent<CardRaffleFrame>();
            }

            private T Find<T>(string name) where T : Component
            {
                Transform child = button.transform.Find(name);
                return child != null ? child.GetComponent<T>() : null;
            }
        }

        public Choice[] choices;
        public int selectedIndex = -1;
        public TMP_Text detailTitle;
        public TMP_Text detailBody;
        public TMP_Text lore;
        public TMP_Text confirmLabel;
        public TMP_Text note;
        public Button confirmButton;
        public UnityEvent<int> onConfirmed = new UnityEvent<int>();
        [Header("Card Entrance")]
        [SerializeField, Min(0f)] private float _cardEnterDuration = .5f;
        [SerializeField, Min(0f)] private float _cardEnterStagger = .08f;
        [SerializeField, Range(0f, 90f)] private float _cardEnterAngle = 20f;
        public int SelectedCardId => selectedIndex >= 0 && choices != null && selectedIndex < choices.Length && choices[selectedIndex] != null ? choices[selectedIndex].id : -1;
        public bool IsConfirmed { get; private set; }

        private readonly Dictionary<string, AssetHandle> _assets = new Dictionary<string, AssetHandle>();
        private readonly List<KeyValuePair<Button, UnityAction>> _buttonBindings = new List<KeyValuePair<Button, UnityAction>>();
        private readonly System.Random _random = new System.Random();
        private Tables _tables;
        private Action<int> _confirmationCallback;
        private bool _closeOnConfirmed;
        private int _openVersion;
        private bool _hasOffer;
        private string _status;
        private Sequence _entranceAnimation;
        private Transform[] _cardTransforms;
        private Vector3[] _cardScales;
        private Quaternion[] _cardRotations;
        private bool _isEntering;
        private bool _ownsPause;
        private float _previousTimeScale;

        protected override void OnCreated()
        {
            int count = choices != null ? choices.Length : 0;
            _cardTransforms = new Transform[count];
            _cardScales = new Vector3[count];
            _cardRotations = new Quaternion[count];
            if (choices != null) for (int i = 0; i < choices.Length; i++)
            {
                if (choices[i] == null || choices[i].button == null) continue;
                choices[i].CacheComponents();
                Transform card = choices[i].button.transform;
                _cardTransforms[i] = card;
                _cardScales[i] = card.localScale;
                _cardRotations[i] = card.localRotation;
                int index = i;
                BindClick(choices[i].button, () => Select(index));
            }
            BindClick(confirmButton, Confirm);
            ClearChoices();
            Refresh();
        }

        private void BindClick(Button button, UnityAction action)
        {
            if (button == null) return;
            button.onClick.AddListener(action);
            _buttonBindings.Add(new KeyValuePair<Button, UnityAction>(button, action));
        }

        protected override void OnOpen(object args)
        {
            if (args != null && !(args is OpenArgs))
                throw new ArgumentException("卡牌界面参数应为 OpenArgs，请使用 CardRafflePanelView.Open(onConfirmed)。", nameof(args));

            var options = args as OpenArgs;
            _openVersion++;
            _tables = LoadTables();
            _confirmationCallback = options?.OnConfirmed;
            _closeOnConfirmed = options?.CloseOnConfirmed ?? true;
            PauseGame();
            DrawChoices();
        }

        private void PauseGame()
        {
            // 同一个已打开界面再次 Open 时，保留第一次打开前的倍率。
            if (!_ownsPause)
            {
                _previousTimeScale = Time.timeScale;
                _ownsPause = true;
            }
            Time.timeScale = 0f;
        }

        private void ResumeGame()
        {
            if (!_ownsPause) return;
            _ownsPause = false;
            Time.timeScale = _previousTimeScale;
        }

        private static Tables LoadTables()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
                return new Tables(name =>
                {
                    var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Datas/Configs/Data/" + name + ".bytes");
                    if (asset == null) throw new InvalidOperationException("找不到配置：" + name);
                    return new Luban.ByteBuf(asset.bytes);
                });
#endif
            return LubanConfigManager.Instance.Tables;
        }

        /// <summary>生成下一次候选。ResetSelection 只清除选择，不会重抽或重复发奖。</summary>
        public void DrawChoices()
        {
            if (!IsCreated || !IsOpen || _tables == null) return;
            StopEntranceAnimation();
            ClearChoices();
            _status = null;
            int slotCount = 0;
            if (choices != null) foreach (Choice choice in choices)
                if (choice != null && choice.button != null) slotCount++;
            cfg.card.Display[] cards = DrawCards(_tables, slotCount, _random);
            int next = 0;
            if (choices != null) foreach (Choice choice in choices)
            {
                if (choice == null || choice.button == null || next >= cards.Length) continue;
                Bind(choice, cards[next++]);
            }
            _hasOffer = cards.Length > 0;
            if (!_hasOffer) _status = "暂无可选卡牌";
            if (Application.isPlaying && _hasOffer) PlayEntranceAnimation();
            Refresh();
        }

        private void PlayEntranceAnimation()
        {
            StopEntranceAnimation();
            if (!_hasOffer || _cardEnterDuration <= 0f) return;

            _isEntering = true;
            int version = _openVersion;
            Sequence sequence = DOTween.Sequence().SetRecyclable(false).SetUpdate(true);
            _entranceAnimation = sequence;
            int visibleIndex = 0;
            for (int i = 0; i < _cardTransforms.Length; i++)
            {
                Transform card = _cardTransforms[i];
                if (card == null || choices[i].id < 0) continue;
                float direction = visibleIndex % 2 == 0 ? -1f : 1f;
                card.localScale = _cardScales[i] * .15f;
                card.localRotation = _cardRotations[i] * Quaternion.Euler(0f, 55f * direction, _cardEnterAngle * direction);
                float delay = visibleIndex++ * _cardEnterStagger;
                sequence.Insert(delay, card.DOScale(_cardScales[i], _cardEnterDuration).SetEase(Ease.OutBack, 1.1f));
                sequence.Insert(delay, card.DOLocalRotateQuaternion(_cardRotations[i], _cardEnterDuration).SetEase(Ease.OutCubic));
            }
            sequence.OnComplete(() =>
            {
                if (_entranceAnimation != sequence || version != _openVersion) return;
                _entranceAnimation = null;
                _isEntering = false;
                RestoreCardTransforms();
                Refresh();
            });
            sequence.Play();
            Refresh();
        }

        private void StopEntranceAnimation()
        {
            _entranceAnimation?.Kill();
            _entranceAnimation = null;
            _isEntering = false;
            RestoreCardTransforms();
        }

        private void RestoreCardTransforms()
        {
            if (_cardTransforms == null) return;
            for (int i = 0; i < _cardTransforms.Length; i++)
            {
                if (_cardTransforms[i] == null) continue;
                _cardTransforms[i].localScale = _cardScales[i];
                _cardTransforms[i].localRotation = _cardRotations[i];
            }
        }

        /// <summary>只按配置权重抽取，不累计获取次数；同一轮候选不重复。</summary>
        internal static cfg.card.Display[] DrawCards(Tables tables, int count, System.Random random)
        {
            if (count <= 0) return Array.Empty<cfg.card.Display>();
            var candidates = new List<cfg.card.Buff>();
            long totalWeight = 0;
            foreach (cfg.card.Buff buff in tables.TbBuff.DataList)
            {
                if (buff.Weight <= 0 || tables.TbDisplay.GetOrDefault(buff.Id) == null) continue;
                candidates.Add(buff);
                totalWeight += buff.Weight;
            }

            var result = new cfg.card.Display[Math.Min(count, candidates.Count)];
            for (int slot = 0; slot < result.Length; slot++)
            {
                double roll = random.NextDouble() * totalWeight;
                int index = 0;
                for (; index < candidates.Count - 1; index++)
                {
                    if (roll < candidates[index].Weight) break;
                    roll -= candidates[index].Weight;
                }
                cfg.card.Buff selected = candidates[index];
                result[slot] = tables.TbDisplay.Get(selected.Id);
                totalWeight -= selected.Weight;
                candidates.RemoveAt(index);
            }
            return result;
        }

        private void ClearChoices()
        {
            selectedIndex = -1;
            IsConfirmed = false;
            _hasOffer = false;
            if (choices == null) return;
            foreach (Choice choice in choices)
            {
                if (choice == null) continue;
                choice.id = -1;
                choice.title = choice.effect = choice.lore = "";
                if (choice.button != null) choice.button.gameObject.SetActive(false);
            }
        }

        private void Bind(Choice choice, cfg.card.Display card)
        {
            choice.CacheComponents();
            choice.id = card.Id;
            choice.title = card.Name;
            choice.effect = card.EffectDesc;
            choice.lore = card.Desc;
            SetText(choice.titleLabel, card.Name);
            SetText(choice.effectLabel, card.EffectDesc);
            ConfigureText(choice.titleLabel);
            ConfigureText(choice.effectLabel);
            if (choice.icon != null)
            {
                choice.icon.sprite = LoadAsset<Sprite>(card.IconPath);
                choice.icon.enabled = choice.icon.sprite != null;
                choice.icon.preserveAspect = true;
            }
            bool rare = card.Rarity == CardRarityType.Rare;
            bool legendary = card.Rarity == CardRarityType.Legendary;
            SetText(choice.rarityLabel, legendary ? "传说" : rare ? "稀有" : "普通");
            if (choice.rarityLabel != null)
                choice.rarityLabel.color = legendary ? Hex("856025") : rare ? Hex("286B66") : Hex("536344");
            if (choice.rarityBadge != null)
            {
                choice.rarityBadge.color = legendary ? Hex("FFF0C8") : rare ? Hex("E2F4EF") : Hex("EDF2DF");
                choice.rarityBadge.strokeColor = legendary ? Hex("CFAD61") : rare ? Hex("8FC6BD") : Hex("B3C4A1");
            }
            if (choice.frame != null)
            {
                string rarity = legendary ? "legendary" : rare ? "rare" : "normal";
                choice.frame.frameTexture = LoadAsset<Texture2D>("card_frame_" + rarity + "_512x128");
                choice.frame.SetAllDirty();
            }
            choice.button.gameObject.SetActive(true);
        }

        private T LoadAsset<T>(string address) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
#if UNITY_EDITOR
            // 预制体工具可在非运行模式下预览真实配置，不需要启动资源包。
            if (!Application.isPlaying)
            {
                string path = address.StartsWith("Assets/", StringComparison.Ordinal) ? address :
                    address.StartsWith("CardIcon_", StringComparison.Ordinal) ?
                    "Assets/Arts/Sprites/CardIcon/" + address.Substring("CardIcon_".Length) + ".png" :
                    "Assets/Arts/Sprites/UI/CardPanel/Frames/" + address + ".png";
                return UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
            }
#endif
            if (!_assets.TryGetValue(address, out AssetHandle handle))
            {
                handle = YooAssetManager.Instance.LoadAssetHandleSync<T>(address);
                if (handle != null) _assets.Add(address, handle);
            }
            return handle != null ? handle.AssetObject as T : null;
        }

        public void Select(int index)
        {
            if (!IsOpen || _isEntering || IsConfirmed || !_hasOffer || choices == null || index < 0 || index >= choices.Length || choices[index] == null || choices[index].id < 0) return;
            selectedIndex = index;
            Refresh();
        }

        public void Confirm()
        {
            if (!IsOpen || _isEntering || IsConfirmed || !_hasOffer || SelectedCardId < 0) return;
            int cardId = SelectedCardId;
            IsConfirmed = true;
            Refresh();
            int version = _openVersion;
            bool close = _closeOnConfirmed;
            Action<int> callback = _confirmationCallback;
            try
            {
                onConfirmed?.Invoke(cardId);
                callback?.Invoke(cardId);
            }
            finally
            {
                // 回调可以立即打开下一轮，不能把回调新打开的界面一并关闭。
                if (close && this != null && IsOpen && version == _openVersion) CloseSelf();
                EventBus.Publish<OnCardSelectedEvent>(new OnCardSelectedEvent(cardId));
            }
        }

        public void ResetSelection(int index = -1)
        {
            if (!IsOpen || _isEntering || IsConfirmed) return;
            selectedIndex = choices != null && index >= 0 && index < choices.Length && choices[index] != null && choices[index].id >= 0 ? index : -1;
            Refresh();
        }

        public void Refresh()
        {
            bool hasChoice = SelectedCardId >= 0;
            if (choices != null) for (int i = 0; i < choices.Length; i++)
            {
                Choice choice = choices[i];
                if (choice == null) continue;
                if (choice.selectionOutline != null) choice.selectionOutline.SetActive(hasChoice && i == selectedIndex);
                if (choice.selectionCheck != null) choice.selectionCheck.SetActive(hasChoice && i == selectedIndex);
                if (choice.button != null) choice.button.interactable = IsOpen && !_isEntering && choice.id >= 0 && !IsConfirmed;
            }
            SetText(detailTitle, hasChoice ? choices[selectedIndex].title : _status ?? "点选一张卡牌");
            SetText(detailBody, hasChoice ? choices[selectedIndex].effect : _hasOffer ? "查看完整效果" : "");
            SetText(lore, hasChoice ? choices[selectedIndex].lore : "");
            ConfigureText(detailBody);
            ConfigureText(lore);
            SetText(confirmLabel, IsConfirmed && hasChoice ? "已获得 · " + choices[selectedIndex].title : hasChoice ? "带上这份力量" : _status ?? "请选择一张卡牌");
            SetText(note, IsConfirmed ? "卡牌已加入本次冒险" : _hasOffer ? "本次只能选择一张" : "");
            if (confirmButton != null) confirmButton.interactable = IsOpen && !_isEntering && hasChoice && !IsConfirmed;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void ConfigureText(TMP_Text text)
        {
            if (text == null) return;
            if (!text.enableAutoSizing) text.fontSizeMax = text.fontSize;
            text.enableWordWrapping = true;
            text.enableAutoSizing = true;
            text.fontSizeMin = text.fontSizeMax * .72f;
            text.overflowMode = TextOverflowModes.Ellipsis;
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out Color color);
            return color;
        }

        protected override void OnClose()
        {
            _openVersion++;
            StopEntranceAnimation();
            ResumeGame();
            _confirmationCallback = null;
            _tables = null;
            _closeOnConfirmed = false;
            _status = null;
            ClearChoices();
            Refresh();
        }

        protected override void OnRelease()
        {
            StopEntranceAnimation();
            ResumeGame();
            foreach (KeyValuePair<Button, UnityAction> binding in _buttonBindings)
                if (binding.Key != null) binding.Key.onClick.RemoveListener(binding.Value);
            _buttonBindings.Clear();

            if (choices != null) foreach (Choice choice in choices)
            {
                if (choice == null) continue;
                if (choice.icon != null) choice.icon.sprite = null;
                if (choice.frame != null) choice.frame.frameTexture = null;
            }
            foreach (AssetHandle handle in _assets.Values)
                if (handle.IsValid) handle.Release();
            _assets.Clear();
            _confirmationCallback = null;
            _tables = null;
            onConfirmed?.RemoveAllListeners();
        }

        // 非框架的直接失活或销毁也必须归还暂停，避免场景停在 timeScale = 0。
        private void OnDisable()
        {
            StopEntranceAnimation();
            ResumeGame();
        }

        private void OnDestroy()
        {
            StopEntranceAnimation();
            ResumeGame();
        }
    }
}
