namespace Sproutshot.Utils
{
    public interface IPoolable
    {
        /// <summary>归还时清理速度、计时、目标等状态；不要再次销毁或归还自身。</summary>
        public void Reset();
    }
}
