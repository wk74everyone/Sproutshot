using System;

namespace Sproutshot.Utils
{
    /// <summary>
    /// 纯 C# 泛型单例基类。
    /// 不依赖 MonoBehaviour，不受 Unity 生命周期约束。
    /// 线程安全，延迟初始化，首次访问时创建。
    /// </summary>
    public abstract class Singleton<T> where T : class
    {
        private static readonly object _lock = new object();
        private static T _instance;
        private static bool _isDisposed;

        public static T Instance
        {
            get
            {
                if (_isDisposed)
                {
                    throw new ObjectDisposedException(
                        $"[Singleton] {typeof(T).Name} 已被释放，请勿继续访问。");
                }

                if (_instance == null)
                {
                    lock (_lock)
                    {
                        // double-check，避免多线程下重复创建
                        if (_instance == null)
                        {
                            // 通过反射调用无参构造
                            // 约束子类必须有 protected 或 public 无参构造
                            _instance = (T)Activator.CreateInstance(
                                typeof(T),
                                nonPublic: true);
                            (_instance as Singleton<T>)?.OnSingletonInit();
                        }
                    }
                }
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null && !_isDisposed;

        /// <summary>
        /// 首次创建时回调，替代构造函数逻辑，避免子类在构造中访问 Instance 导致递归。
        /// </summary>
        protected virtual void OnSingletonInit() { }

        /// <summary>
        /// 手动释放实例，释放后不可再访问，需重新创建时调用 Reset。
        /// </summary>
        public static void Dispose()
        {
            lock (_lock)
            {
                if (_instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _instance = null;
                _isDisposed = true;
            }
        }

        /// <summary>
        /// 重置实例，允许重新创建。用于测试或场景切换。
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                if (_instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _instance = null;
                _isDisposed = false;
            }
        }
    }
}