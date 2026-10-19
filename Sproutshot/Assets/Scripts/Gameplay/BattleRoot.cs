using UnityEngine;

namespace Sproutshot.Gameplay
{
    /// <summary>GameScene 的本局入口。先创建数据容器，地图和玩家随后初始化。</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-300)]
    public sealed class BattleRoot : MonoBehaviour
    {
        private RunContext _context;

        private void Awake()
        {
            _context = new RunContext();
            RunContext.Current = _context;
        }

        private void OnDestroy()
        {
            // 旧场景销毁时，不清除后来创建的新对局。
            if (RunContext.Current == _context)
                RunContext.Current = null;
            if (_context != null)
                _context.PlayerEntity = null;
        }
    }
}
