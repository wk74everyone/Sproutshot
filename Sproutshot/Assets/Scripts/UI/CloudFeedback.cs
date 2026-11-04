using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>云端操作失败时的轻量提示，允许显式重试同一操作。</summary>
    public static class CloudFeedback
    {
        private static GameObject _current;
        public static void Confirm(string message, Action confirm, TMP_FontAsset font = null)
        {
            Show(message, confirm, font);
            var button = _current.transform.Find("Message/重试");
            if (button != null) button.GetComponentInChildren<TMP_Text>().text = "观看广告";
        }
        public static void Show(string message, Action retry = null, TMP_FontAsset font = null)
        {
            if (_current != null) UnityEngine.Object.Destroy(_current);
            if (font == null) font = UnityEngine.Object.FindObjectOfType<TMP_Text>(true)?.font;
            _current = new GameObject("CloudFeedback", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var host = _current;
            var canvas = host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31000;
            var scaler = host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080,1920); scaler.matchWidthOrHeight = .5f;
            var backdrop = Image("Backdrop", host.transform, new Vector2(1080,1920), Color.black * .45f);
            backdrop.rectTransform.anchorMin = Vector2.zero; backdrop.rectTransform.anchorMax = Vector2.one;
            backdrop.rectTransform.sizeDelta = Vector2.zero;
            var box = Image("Message", host.transform, new Vector2(900,370), new Color(1f,.98f,.9f));
            var label = Label("Text", box.transform, font, message, new Vector2(800,180));
            label.rectTransform.anchoredPosition = new Vector2(0,55);
            AddButton(box.transform, font, retry == null ? "确定" : "关闭", retry == null ? 0 : -200,
                () => UnityEngine.Object.Destroy(host));
            if (retry != null) AddButton(box.transform, font, "重试", 200, () => { UnityEngine.Object.Destroy(host); retry(); });
        }
        private static Image Image(string name, Transform parent, Vector2 size, Color color)
        {
            var go = new GameObject(name,typeof(RectTransform),typeof(Image)); go.transform.SetParent(parent,false);
            var image=go.GetComponent<Image>(); image.color=color; image.rectTransform.sizeDelta=size; return image;
        }
        private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, Vector2 size)
        {
            var go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.transform.SetParent(parent,false);
            var text=go.GetComponent<TextMeshProUGUI>(); text.font=font; text.text=value; text.fontSize=34;
            text.color=new Color(.1f,.2f,.15f); text.alignment=TextAlignmentOptions.Center; text.raycastTarget=false;
            text.rectTransform.sizeDelta=size; return text;
        }
        private static void AddButton(Transform parent,TMP_FontAsset font,string name,float x,Action click)
        {
            var image=Image(name,parent,new Vector2(300,90),new Color(.75f,.94f,.4f));
            image.rectTransform.anchoredPosition=new Vector2(x,-110);
            image.gameObject.AddComponent<Button>().onClick.AddListener(()=>click());
            image.gameObject.AddComponent<AudioButtonFeedback>();
            Label("Label",image.transform,font,name,new Vector2(280,80));
        }
    }
}
