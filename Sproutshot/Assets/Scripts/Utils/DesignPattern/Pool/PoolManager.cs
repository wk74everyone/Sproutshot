using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Sproutshot.Utils
{
    /// <summary>主线程使用的预制体对象池。仅限制闲置缓存，不限制同时使用的对象数量。</summary>
    public sealed class PoolManager : Singleton<PoolManager>, IDisposable
    {
        private sealed class Pool
        {
            public readonly Stack<Entry> Idle = new Stack<Entry>(4);
            public bool Retired;
        }

        private sealed class Entry
        {
            public GameObject Instance;
            public Pool Owner;
            public IPoolable[] Resetters;
            public Vector3 Scale;
            public bool Returned;
        }

        private readonly Dictionary<GameObject, Pool> pools = new Dictionary<GameObject, Pool>();
        private readonly Dictionary<GameObject, Entry> instances = new Dictionary<GameObject, Entry>();
        private readonly List<GameObject> destroyed = new List<GameObject>();
        private Transform cacheRoot;
        private int maxInactivePerPrefab = 32;

        public int MaxInactivePerPrefab
        {
            get => maxInactivePerPrefab;
            set
            {
                maxInactivePerPrefab = Mathf.Max(0, value);
                foreach (Pool pool in pools.Values)
                    while (pool.Idle.Count > maxInactivePerPrefab) DestroyEntry(pool.Idle.Pop());
            }
        }

        protected override void OnSingletonInit() => SceneManager.sceneUnloaded += OnSceneUnloaded;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Reset();

        public T Get<T>(T prefab) where T : Component =>
            Get(prefab, prefab != null ? prefab.transform.position : Vector3.zero,
                prefab != null ? prefab.transform.rotation : Quaternion.identity);

        public T Get<T>(T prefab, Vector3 position, Quaternion rotation, Transform parent = null) where T : Component
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            return Get(prefab.gameObject, position, rotation, parent).GetComponent<T>();
        }

        public GameObject Get(GameObject prefab) =>
            Get(prefab, prefab != null ? prefab.transform.position : Vector3.zero,
                prefab != null ? prefab.transform.rotation : Quaternion.identity);

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        {
            if (prefab == null) throw new ArgumentNullException(nameof(prefab));
            if (!pools.TryGetValue(prefab, out Pool pool))
            {
                pool = new Pool();
                pools.Add(prefab, pool);
            }

            Entry entry = null;
            while (pool.Idle.Count > 0)
            {
                Entry candidate = pool.Idle.Pop();
                if (candidate.Instance != null) { entry = candidate; break; }
                instances.Remove(candidate.Instance);
            }
            if (entry == null)
            {
                // 在失活父节点下创建，配置出生位置后才触发 OnEnable。
                GameObject instance = Object.Instantiate(prefab, GetCacheRoot(), false);
                instance.SetActive(false);
                entry = new Entry { Instance = instance, Owner = pool, Scale = prefab.transform.localScale,
                    Resetters = instance.GetComponentsInChildren<IPoolable>(true) };
                instances.Add(instance, entry);
            }

            GameObject result = entry.Instance;
            entry.Returned = false;
            result.transform.SetParent(parent, false);
            if (parent == null && result.scene != SceneManager.GetActiveScene())
                SceneManager.MoveGameObjectToScene(result, SceneManager.GetActiveScene());
            result.transform.localScale = entry.Scale;
            result.transform.SetPositionAndRotation(position, rotation);
            result.SetActive(true);
            return result;
        }

        public void Put<T>(T instance) where T : Component => Put(instance != null ? instance.gameObject : null);

        public void Put(IPoolable instance)
        {
            if (instance is Component component && component != null) Put(component.gameObject);
        }

        public void Put(GameObject instance)
        {
            if (instance == null || !instances.TryGetValue(instance, out Entry entry) || entry.Returned) return;
            // 提前标记，防止 OnDisable/Reset 中的重复归还。
            entry.Returned = true;
            instance.SetActive(false);
            try
            {
                foreach (IPoolable resetter in entry.Resetters)
                    if (resetter is Object component && component != null) resetter.ResetAll();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, instance);
                DestroyEntry(entry);
                return;
            }

            if (instance == null || entry.Owner.Retired || entry.Owner.Idle.Count >= maxInactivePerPrefab)
            {
                DestroyEntry(entry);
                return;
            }
            instance.transform.SetParent(GetCacheRoot(), false);
            entry.Owner.Idle.Push(entry);
        }

        /// <summary>清理该预制体的闲置缓存；正在使用的实例在之后归还时销毁。</summary>
        public void Clear(GameObject prefab)
        {
            if (ReferenceEquals(prefab, null) || !pools.TryGetValue(prefab, out Pool pool)) return;
            pool.Retired = true;
            while (pool.Idle.Count > 0) DestroyEntry(pool.Idle.Pop());
            pools.Remove(prefab);
        }

        public void ClearAll()
        {
            foreach (Pool pool in pools.Values)
            {
                pool.Retired = true;
                while (pool.Idle.Count > 0) DestroyEntry(pool.Idle.Pop());
            }
            pools.Clear();
            if (cacheRoot != null) DestroyObject(cacheRoot.gameObject);
            cacheRoot = null;
            // 清除被外部销毁或随场景卸载的实例引用；复用列表，避免每次清理分配数组。
            destroyed.Clear();
            foreach (var pair in instances)
                if (pair.Key == null) destroyed.Add(pair.Key);
            foreach (GameObject instance in destroyed) instances.Remove(instance);
            destroyed.Clear();
        }

        private Transform GetCacheRoot()
        {
            if (cacheRoot == null)
            {
                var root = new GameObject("[Pool Cache]");
                root.SetActive(false);
                cacheRoot = root.transform;
            }
            return cacheRoot;
        }

        private void DestroyEntry(Entry entry)
        {
            instances.Remove(entry.Instance);
            if (entry.Instance != null) DestroyObject(entry.Instance);
        }

        private static void DestroyObject(Object instance)
        {
            if (Application.isPlaying) Object.Destroy(instance);
            else Object.DestroyImmediate(instance);
        }

        private void OnSceneUnloaded(Scene scene) => ClearAll();

        void IDisposable.Dispose()
        {
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            ClearAll();
            instances.Clear();
        }
    }
}