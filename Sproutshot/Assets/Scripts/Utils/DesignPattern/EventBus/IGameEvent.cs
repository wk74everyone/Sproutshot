namespace Sproutshot.Utils
{
    /// <summary>
    /// 事件接口，所有事件结构体需实现此接口。
    /// 使用 struct 避免堆分配，零 GC 开销。
    /// </summary>
    public interface IGameEvent { }
}