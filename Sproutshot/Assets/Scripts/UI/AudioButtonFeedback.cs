using System.Collections;
using Sproutshot.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sproutshot.UI
{
    /// <summary>监听实际 onClick，支持鼠标、触摸和键盘提交；不替换业务回调。</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Button))]
    public sealed class AudioButtonFeedback : MonoBehaviour
    {
        private Button _button;
        public bool Silent { get; set; }
        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(Play);
        }
        private void Play()
        {
            // Button 已在提交点击前校验 interactable，业务回调可能先禁用按钮或关闭页面。
            if (!Silent) AudioManager.Instance.PlaySfx(AudioId.UiTap);
        }
        private void OnDestroy() { if (_button != null) _button.onClick.RemoveListener(Play); }
        public static void SetSilent(Button button, bool silent)
        {
            if (button == null) return;
            var feedback = button.GetComponent<AudioButtonFeedback>();
            if (feedback == null) feedback = button.gameObject.AddComponent<AudioButtonFeedback>();
            feedback.Silent = silent;
        }
        public static void Bind(Transform root)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
                if (button.GetComponent<AudioButtonFeedback>() == null) button.gameObject.AddComponent<AudioButtonFeedback>();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= BindScene;
            SceneManager.sceneLoaded += BindScene;
        }
        private static void BindScene(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects()) Bind(root.transform);
            if (scene.name == "MainCamp")
            {
                AudioManager.Instance.BeginBattle(); // 清除上一局结算的压低状态。
                AudioManager.Instance.PlayMusic(AudioId.CampMusic);
                AudioManager.Instance.StartCoroutine(PrewarmScene(scene));
            }
        }
        private static IEnumerator PrewarmScene(Scene scene)
        {
            yield return AudioManager.Instance.Initialize();
            if (!scene.isLoaded) yield break;
            yield return AudioManager.Instance.PreloadScope(AudioScope.Scene);
        }
    }
}
