#if UNITY_EDITOR
using System.Threading.Tasks;
using Sproutshot.Gameplay;
using Sproutshot.Utils;
using UnityEditor;

/// <summary>Only compiled in the editor. All simulated rewards still go through the local database transaction.</summary>
[InitializeOnLoad]
internal static class RewardedAdEditorSimulator
{
    static RewardedAdEditorSimulator() => Bind();
    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bind() => RewardedAdManager.EditorPlay = Play;
    private static Task<bool> Play()
    {
        int choice = EditorUtility.DisplayDialogComplex("激励视频 · 编辑器模拟",
            "这是本地测试，不会播放真实广告。请选择本次广告结果；只有完整观看才会调用数据库领奖。",
            "模拟完整观看", "模拟提前关闭", "模拟播放失败");
        if (choice == 2) throw new CloudBaseException("AD_PLAY_FAILED", "模拟广告播放失败，未发放奖励。");
        return Task.FromResult(choice == 0);
    }
}
#endif
