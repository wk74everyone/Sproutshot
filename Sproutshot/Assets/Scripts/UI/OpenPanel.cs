using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.UI
{
    public class OpenPanel : MonoBehaviour
    {
        public void OpenSettingPanel()
        {
            UIManager.Instance.Open<SettingPanel>("SettingPanel");
        }
    }
}