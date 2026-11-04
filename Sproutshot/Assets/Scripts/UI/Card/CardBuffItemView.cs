using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>一个图标对应一张卡，角标显示领取次数，而非底层 Buff 层数。</summary>
    public sealed class CardBuffItemView : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _count;

        public void Bind(Sprite icon, int count)
        {
            _icon.sprite = icon;
            _icon.enabled = icon != null;
            _count.SetText("×{0}", count);
        }
    }
}
