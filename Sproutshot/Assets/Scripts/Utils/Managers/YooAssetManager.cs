using System.Collections;
using UnityEngine;
using YooAsset;

namespace Sproutshot.Utils
{
    /// <summary>
    /// 资源管理器，封装 YooAsset v3 接口。
    /// 业务层统一通过这里加载资源，不直接依赖 YooAsset。
    /// </summary>
    public class YooAssetManager : Singleton<YooAssetManager>
    {
        private const string DefaultPackageName = "DefaultPackage";

        private ResourcePackage _defaultPackage;
        private bool _initializingDefault;

        /// <summary>
        /// 默认资源包是否已初始化完成（文件系统 + 清单都就绪）。
        /// </summary>
        public bool IsInitialized => _defaultPackage != null && _defaultPackage.InitializeStatus == EOperationStatus.Succeeded && _defaultPackage.PackageValid;

        protected override void OnSingletonInit()
        {
            YooAssets.Initialize();
        }

        /// <summary>
        /// 使用当前运行环境对应的模式初始化默认资源包。业务层无需依赖 YooAsset 的模式枚举。
        /// </summary>
        public IEnumerator InitDefaultPackage()
        {
            if (IsInitialized) yield break;
            if (_initializingDefault)
            {
                while (_initializingDefault) yield return null;
                yield break;
            }
            _initializingDefault = true;
            try
            {
#if UNITY_EDITOR
            yield return InitPackage(EPlayMode.EditorSimulateMode);
#else
            yield return InitPackage(EPlayMode.OfflinePlayMode);
#endif
            }
            finally { _initializingDefault = false; }
        }

        /// <summary>
        /// 初始化默认资源包（协程版本）。游戏启动时调用一次。
        /// 用法：StartCoroutine(YooAssetManager.Instance.InitPackage(EPlayMode.EditorSimulateMode))
        /// </summary>
        /// <param name="playMode">运行模式</param>
        public IEnumerator InitPackage(EPlayMode playMode)
        {
            // 创建或获取资源包
            if (!YooAssets.TryGetPackage(DefaultPackageName, out _defaultPackage))
                _defaultPackage = YooAssets.CreatePackage(DefaultPackageName);

            InitializePackageOperation initOperation = null;

            switch (playMode)
            {
                case EPlayMode.EditorSimulateMode:
                    initOperation = CreateEditorSimulateModeOperation(_defaultPackage);
                    break;

                case EPlayMode.OfflinePlayMode:
                    initOperation = CreateOfflinePlayModeOperation(_defaultPackage);
                    break;

                default:
                    Debug.LogError($"[YooAssetManager] 暂不支持的运行模式: {playMode}");
                    yield break;
            }

            if (initOperation == null) yield break;

            // 第一步：初始化文件系统
            yield return initOperation;

            if (initOperation.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[YooAssetManager] 资源包初始化失败: {initOperation.Error}");
                yield break;
            }

            // 第二步：请求资源版本号
            var requestVersionOp = _defaultPackage.RequestPackageVersionAsync();
            yield return requestVersionOp;

            if (requestVersionOp.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[YooAssetManager] 请求资源版本失败: {requestVersionOp.Error}");
                yield break;
            }

            // 第三步：加载资源清单（设置 ActiveManifest）
            var loadManifestOptions = new LoadPackageManifestOptions(requestVersionOp.PackageVersion, 60);
            var loadManifestOp = _defaultPackage.LoadPackageManifestAsync(loadManifestOptions);
            yield return loadManifestOp;

            if (loadManifestOp.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[YooAssetManager] 加载资源清单失败: {loadManifestOp.Error}");
                yield break;
            }

            Debug.Log($"[YooAssetManager] 资源包 {DefaultPackageName} 初始化成功，模式: {playMode}，版本: {requestVersionOp.PackageVersion}");
        }

        /// <summary>
        /// 同步加载资源。
        /// </summary>
        /// <param name="location">资源定位地址（Address 或资源路径）</param>
        public T LoadAssetSync<T>(string location) where T : Object
        {
            if (!CheckInitialized()) return null;

            var handle = _defaultPackage.LoadAssetSync<T>(location);
            if (handle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[YooAssetManager] 同步加载失败: {location}, 错误: {handle.Error}");
                handle.Release();
                return null;
            }
            return handle.AssetObject as T;
        }

        /// <summary>
        /// 同步加载资源并返回句柄（调用方需自行 Release）。
        /// </summary>
        public AssetHandle LoadAssetHandleSync<T>(string location) where T : Object
        {
            if (!CheckInitialized()) return null;

            var handle = _defaultPackage.LoadAssetSync<T>(location);
            if (handle.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"[YooAssetManager] 同步加载失败: {location}, 错误: {handle.Error}");
                handle.Release();
                return null;
            }
            return handle;
        }

        /// <summary>
        /// 异步加载资源并立即返回可 yield 的句柄（调用方需自行 Release）。
        /// </summary>
        public AssetHandle LoadAssetHandleAsync<T>(string location, uint priority = 0) where T : Object
        {
            if (!CheckInitialized()) return null;

            return _defaultPackage.LoadAssetAsync<T>(location, priority);
        }

        /// <summary>
        /// 释放资源句柄。
        /// </summary>
        public void Release(AssetHandle handle)
        {
            if (handle != null && handle.IsValid)
                handle.Release();
        }

        private bool CheckInitialized()
        {
            if (_defaultPackage == null)
            {
                Debug.LogError("[YooAssetManager] 资源包未创建，请先调用 InitPackage()");
                return false;
            }
            if (_defaultPackage.InitializeStatus != EOperationStatus.Succeeded)
            {
                Debug.LogError("[YooAssetManager] 资源包未初始化成功");
                return false;
            }
            return true;
        }

        /// <summary>
        /// 构造编辑器模拟模式的初始化操作。
        /// </summary>
        private InitializePackageOperation CreateEditorSimulateModeOperation(ResourcePackage package)
        {
#if UNITY_EDITOR
            var buildResult = EditorSimulateBuildInvoker.Build(DefaultPackageName, (int)EBundleType.VirtualAssetBundle);
            var packageRoot = buildResult.PackageRootDirectory;

            var options = new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot)
            };
            return package.InitializePackageAsync(options);
#else
            Debug.LogError("[YooAssetManager] EditorSimulateMode 只能在编辑器下使用");
            return null;
#endif
        }

        /// <summary>
        /// 构造离线模式的初始化操作。
        /// </summary>
        private InitializePackageOperation CreateOfflinePlayModeOperation(ResourcePackage package)
        {
            var options = new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
            };
            return package.InitializePackageAsync(options);
        }
    }
}
