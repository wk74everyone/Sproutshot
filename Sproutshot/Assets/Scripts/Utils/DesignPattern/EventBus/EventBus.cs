using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sproutshot.Utils
{
    /// <summary>
    /// 类型安全的泛型事件总线。
    /// 使用 Type 作为 key，避免字符串硬编码和装箱。
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, IEventHandler> _handlers = new();

        /// <summary>订阅事件</summary>
        public static void Subscribe<T>(Action<T> handler) where T : IGameEvent
        {
            var type = typeof(T);
            if (_handlers.TryGetValue(type, out var existing))
            {
                ((EventHandler<T>)existing).Add(handler);
            }
            else
            {
                var wrapper = new EventHandler<T>();
                wrapper.Add(handler);
                _handlers[type] = wrapper;
            }
        }

        /// <summary>取消订阅</summary>
        public static void Unsubscribe<T>(Action<T> handler) where T : IGameEvent
        {
            if (_handlers.TryGetValue(typeof(T), out var existing))
            {
                var wrapper = (EventHandler<T>)existing;
                wrapper.Remove(handler);
                if (wrapper.IsEmpty)
                {
                    _handlers.Remove(typeof(T));
                }
            }
        }

        /// <summary>发布事件，通知所有订阅者</summary>
        public static void Publish<T>(T evt) where T : IGameEvent
        {
            if (_handlers.TryGetValue(typeof(T), out var existing))
            {
                ((EventHandler<T>)existing).Invoke(evt);
            }
        }

        /// <summary>清除某类型事件的所有订阅</summary>
        public static void Clear<T>() where T : IGameEvent
        {
            _handlers.Remove(typeof(T));
        }

        /// <summary>清除全部订阅（场景切换时调用）</summary>
        public static void ClearAll()
        {
            _handlers.Clear();
        }

        #region --- 内部处理包装器 ---

        private interface IEventHandler { }

        private class EventHandler<T> : IEventHandler
        {
            private List<Action<T>> _actions = new();

            public void Add(Action<T> action)
            {
                if (!_actions.Contains(action))
                    _actions.Add(action);
            }

            public void Remove(Action<T> action)
            {
                _actions.Remove(action);
            }

            public bool IsEmpty => _actions.Count == 0;

            public void Invoke(T evt)
            {
                // 从后往前遍历，允许在回调中安全取消订阅
                for (int i = _actions.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _actions[i]?.Invoke(evt);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[EventBus] 处理事件 {typeof(T).Name} 时异常: {e}");
                    }
                }
            }
        }
        #endregion
    }
}