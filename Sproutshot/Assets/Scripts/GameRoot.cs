using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sproutshot.Utils;

namespace Sproutshot
{
    public class GameRoot : MonoBehaviour
    {
        [SerializeField] private string _gameSceneName = "GameScene";

        private IEnumerator Start()
        {
            yield return YooAssetManager.Instance.InitDefaultPackage();

            if (!YooAssetManager.Instance.IsInitialized)
            {
                Debug.LogError("[GameRoot] YooAsset 初始化失败");
                yield break;
            }

            Debug.Log("[GameRoot] YooAsset 初始化完成，准备加载游戏场景");
            yield return AudioManager.Instance.Initialize();
            SceneManager.LoadScene(_gameSceneName);
        }
    }
}
