using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HybridCLR;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using YooAsset;

    /// <summary>
    /// 封装 YooAsset 初始化、资源下载、资源加载、场景加载和 HybridCLR 热更新程序集加载。
    /// </summary>
    public sealed class YooAssetService : IResourceUpdateService, IGameAssetService, ICodeHotUpdateService
    {
        // YooAsset配置信息
        private readonly YooAssetConfig cfg;
        // YooAsset运行模式
        private readonly YooRunMode? runModeOverride;
        // 资源包AssetHandle列表，用于统一释放所有已加载资源
        private readonly List<AssetHandle> handles = new List<AssetHandle>();
        private readonly List<SubAssetsHandle> subAssetHandles = new List<SubAssetsHandle>();
        // 当前激活的资源包（用于某些操作的上下文）
        private ResourcePackage package;
        // 所有资源包字典，键为包名
        private readonly Dictionary<string, ResourcePackage> packages = new Dictionary<string, ResourcePackage>(StringComparer.Ordinal);
        
        // 当前正在处理的包名（用于进度回调）
        private string activePackageName;
        // 是否正在下载
        private bool downloading;
        // 待下载总字节数
        private long pendingDownloadBytes;
        // 待下载资源数量
        private int pendingDownloadCount;

        
        // 资源下载进度变化事件，每帧或每次下载进度更新时触发
        public event Action<HotUpdateProgressData> ProgressChanged;
        // 服务是否就绪（至少一个资源包有效）
        public bool Ready { get; private set; }
        public bool DownloadsCompleted { get; private set; }
        // 获取配置的第一个包名（默认包名）
        public string PackageName => GetConfiguredPackageNames()[0];
        // 是否有待下载的资源
        public bool HasPendingDownloads => pendingDownloadCount > 0;
        // 待下载资源数量
        public int PendingDownloadCount => pendingDownloadCount;
        // 待下载总字节数
        public long PendingDownloadBytes => pendingDownloadBytes;

        
        
        /// <summary>
        /// 构造 YooAsset 服务实例。
        /// </summary>
        /// <param name="config">YooAsset 配置对象，包含包名、远程地址、下载参数、热更文件列表等。</param>
        /// <param name="runModeOverride">可选，覆盖配置文件中的运行模式，用于调试或特殊场景。</param>
        public YooAssetService(YooAssetConfig config, YooRunMode? runModeOverride = null)
        {
            cfg = config;
            this.runModeOverride = runModeOverride;
        }
        /// <summary>
        /// 获取实际使用的运行模式（优先使用构造时传入的覆盖值）
        /// </summary>
        private YooRunMode RunMode => runModeOverride ?? cfg.RunMode;
        /// <summary>
        /// 从配置中获取所有包名列表，若配置为空则返回默认包名 "Configs"。
        /// </summary>
        private List<string> GetConfiguredPackageNames()
        {
            return cfg != null ? cfg.GetPackageNames() : new List<string> { "DefaultPackage" };
        }

        private ResourcePackage GetHybridPackage()
        {
            var packageName = cfg == null || string.IsNullOrWhiteSpace(cfg.hotUpdatePackageName)
                ? "HotUpdate"
                : cfg.hotUpdatePackageName;
            return packages.TryGetValue(packageName, out var hybridPackage) ? hybridPackage : null;
        }

        /// <summary>
        /// 初始化 YooAsset 服务（协程）。
        /// </summary>
        /// <remarks>
        /// 该协程应作为启动流程的一部分调用。
        /// 依次初始化所有配置的资源包，若为 Host 或 Web 模式则尝试更新清单文件，
        /// 然后统计待下载资源，最后加载 HybridCLR 热更文件。
        /// 执行完毕后，<see cref="Ready"/> 属性指示是否有至少一个包有效。
        /// </remarks>
        /// <returns>协程迭代器。</returns>
        public IEnumerator Init()
        {
            
            Debug.Log("我正在初始化YooAsset");
            Ready = false;
            DownloadsCompleted = false;

            if (!YooAssets.IsInitialized)
            {
                YooAssets.Initialize();
            }

            packages.Clear();
            var packageNames = GetConfiguredPackageNames();
            for (var i = 0; i < packageNames.Count; i++)
            {
                var packageName = packageNames[i];
                if (string.IsNullOrWhiteSpace(packageName))
                    continue;
                activePackageName = packageName;
                var package = YooAssets.TryGetPackage(packageName, out var found)
                    ? found
                    : YooAssets.CreatePackage(packageName);
                this.package = package;
                packages[packageName] = package;
                var initOp = package.InitializePackageAsync(CreateInitOptions());
                yield return initOp;
                if (initOp.Status != EOperationStatus.Succeeded)
                {
                    Debug.LogError($"YooAsset包：({packageName})初始化失败: {initOp.Error}");
                    continue;
                }
                
                Debug.Log($"YooAsset初始化成功");
                if (RunMode == YooRunMode.EditorSimulate || RunMode == YooRunMode.Host || RunMode == YooRunMode.Web)
                {
                    yield return UpdateManifest();
                }
            }
            
            Debug.Log("更新下载进度");
            RefreshPendingDownloads();
            if (!HasPendingDownloads && package != null && package.PackageValid)
                yield return LoadHybridClrFiles();
            else if (!HasPendingDownloads)
                Debug.LogError("YooAsset 清单未激活，跳过 HybridCLR 程序集加载。请检查包版本和清单文件。");
            Ready = packages.Count > 0;
            foreach (var item in packages.Values)
                Ready &= item.PackageValid;
            if (Ready && !HasPendingDownloads)
                DownloadsCompleted = true;
            if (!Ready)
            {
                Debug.LogWarning("YooAsset 包已初始化，但未加载有效的清单文件。在收集器/包清单准备就绪之前，资源加载将被禁用");
            }
        }

        /// <summary>
        /// 异步初始化 YooAsset 资源包。
        /// </summary>
        public async Task<HotUpdateResultData> InitializeAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HotUpdateResultData
            {
                success = Ready,
                error = Ready ? string.Empty : "请从 Boot 流程调用 Init() 协程。"
            };
        }

        /// <summary>
        /// 异步检查并下载资源更新。
        /// </summary>
        public async Task<HotUpdateResultData> CheckAndDownloadAsync(CancellationToken cancellationToken)
        {
            await Task.Yield();
            return new HotUpdateResultData
            {
                success = Ready,
                error = Ready ? string.Empty : "请从 Boot 流程调用 Init() 协程。"
            };
        }

        
        /// <summary>
        /// 下载所有待更新的资源包（协程）
        /// </summary>
        /// <remarks>
        /// 依次处理每个资源包的下载队列，并在下载过程中通过 <see cref="ProgressChanged"/> 事件报告进度
        /// 下载完成后自动加载 HybridCLR 热更文件
        /// </remarks>
        public IEnumerator DownloadPending()
        {
            if (downloading)
                yield break;

            downloading = true;
            var completedBytes = 0L;
            var completedCount = 0;

            foreach (var item in packages)
            {
                activePackageName = item.Key;
                package = item.Value;
                if (!package.PackageValid)
                    continue;

                var downloader = package.CreateResourceDownloader(new ResourceDownloaderOptions(
                    Mathf.Max(1, cfg.downloadMaxConcurrency),
                    Mathf.Max(0, cfg.retryCount)));
                if (downloader.TotalDownloadCount == 0)
                    continue;

                var packageBaseBytes = completedBytes;
                var packageBaseCount = completedCount;
                downloader.DownloadProgressChanged += args =>
                {
                    completedBytes = packageBaseBytes + args.CurrentDownloadBytes;
                    completedCount = packageBaseCount + args.CurrentDownloadCount;
                    ProgressChanged?.Invoke(new HotUpdateProgressData
                    {
                        packageName = activePackageName,
                        downloadedBytes = completedBytes,
                        totalBytes = pendingDownloadBytes,
                        message = $"下载进度： {completedCount}/{pendingDownloadCount}"
                    });
                };

                downloader.StartDownload();
                yield return downloader;
                if (downloader.Status != EOperationStatus.Succeeded)
                {
                    downloading = false;
                    Debug.LogError($"YooAsset下载包：({activePackageName}失败): {downloader.Error}");
                    RefreshPendingDownloads();
                    yield break;
                }

                completedBytes = packageBaseBytes + downloader.TotalDownloadBytes;
                completedCount += downloader.TotalDownloadCount;
            }

            pendingDownloadBytes = completedBytes;
            pendingDownloadCount = 0;
            downloading = false;
            DownloadsCompleted = true;
            if (package != null && package.PackageValid)
                yield return LoadHybridClrFiles();
            else
                Debug.LogError("YooAsset 清单未激活，跳过 HybridCLR 程序集加载。请检查包版本和清单文件。");
        }

        /// <summary>
        /// 异步加载单个资源。
        /// </summary>
        public async Task<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            if (packages.Count == 0)
            {
                var editorAssetWithoutPackage = TryLoadEditorAsset<T>(location);
                if (editorAssetWithoutPackage != null)
                {
                    return editorAssetWithoutPackage;
                }

                throw new InvalidOperationException("YooAsset 资源包尚未初始化。");
            }

            Exception lastError = null;
            foreach (var item in packages)
            {
                try
                {
                    var handle = item.Value.LoadAssetAsync<T>(location);
                    handles.Add(handle);
                    var source = new TaskCompletionSource<T>();
            handle.Completed += op =>
            {
                if (op.Status == EOperationStatus.Succeeded)
                {
                    source.TrySetResult(op.GetAssetObject<T>());
                }
                else
                {
                    source.TrySetException(new InvalidOperationException(op.Error));
                }
            };

                    using (cancellationToken.Register(() => source.TrySetCanceled()))
                        return await source.Task;
                }
                catch (Exception e)
                {
                    lastError = e;
                }
            }
            var editorAsset = TryLoadEditorAsset<T>(location);
            if (editorAsset != null)
            {
                return editorAsset;
            }

            throw new InvalidOperationException($"在包 {PackageName} 中未找到资源： {location}", lastError);
        }

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        public async Task<T> LoadSubAssetAsync<T>(string location, string subAssetName,
            CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(subAssetName))
                throw new ArgumentException("Sub asset name is required.", nameof(subAssetName));

            Exception lastError = null;
            foreach (var item in packages)
            {
                try
                {
                    var handle = item.Value.LoadSubAssetsAsync<T>(location);
                    subAssetHandles.Add(handle);
                    var source = new TaskCompletionSource<T>();
                    handle.Completed += operation =>
                    {
                        if (operation.Status != EOperationStatus.Succeeded)
                        {
                            source.TrySetException(new InvalidOperationException(operation.Error));
                            return;
                        }

                        var asset = operation.GetSubAssetObject<T>(subAssetName);
                        if (asset != null)
                            source.TrySetResult(asset);
                        else
                            source.TrySetException(new InvalidOperationException(
                                $"Sub asset '{subAssetName}' was not found in '{location}'."));
                    };
                    using (cancellationToken.Register(() => source.TrySetCanceled()))
                        return await source.Task;
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }

            var editorAsset = TryLoadEditorSubAsset<T>(location, subAssetName);
            if (editorAsset != null)
                return editorAsset;
            throw new InvalidOperationException(
                $"Sub asset '{subAssetName}' was not found in '{location}'.", lastError);
        }

        public async Task LoadSceneAsync(string location, LoadSceneMode mode, CancellationToken cancellationToken)
        {
            if (packages.Count == 0)
            {
                var fallbackOperation = SceneManager.LoadSceneAsync(location, mode);
                if (fallbackOperation == null)
                {
                    throw new InvalidOperationException("场景不存在：" + location);
                }

                while (!fallbackOperation.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                return;
            }

            if (packages.Count == 0)
            {
                throw new InvalidOperationException("YooAsset 资源包尚未初始化。");
            }

            Exception lastError = null;
            foreach (var item in packages)
            {
                try
                {
                    var handle = item.Value.LoadSceneAsync(location, mode);
                    var source = new TaskCompletionSource<bool>();
            handle.Completed += op =>
            {
                if (op.Status == EOperationStatus.Succeeded)
                {
                    source.TrySetResult(true);
                }
                else
                {
                    source.TrySetException(new InvalidOperationException(op.Error));
                }
            };

                    using (cancellationToken.Register(() => source.TrySetCanceled()))
                    {
                        await source.Task;
                        return;
                    }
                }
                catch (Exception e)
                {
                    lastError = e;
                }
            }
            var fallback = SceneManager.LoadSceneAsync(location, mode);
            if (fallback != null)
            {
                while (!fallback.isDone)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Yield();
                }

                return;
            }

            throw new InvalidOperationException($"在包 {PackageName} 中未找到场景: {location}", lastError);
        }

#if UNITY_EDITOR
        private static T TryLoadEditorSubAsset<T>(string location, string subAssetName) where T : UnityEngine.Object
        {
            var guids = UnityEditor.AssetDatabase.FindAssets($"{location} t:Texture2D", new[] { "Assets/GameAssets" });
            for (var guidIndex = 0; guidIndex < guids.Length; guidIndex++)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[guidIndex]);
                if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), location, StringComparison.OrdinalIgnoreCase))
                    continue;
                var assets = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path);
                for (var assetIndex = 0; assetIndex < assets.Length; assetIndex++)
                {
                    if (assets[assetIndex] is T typed && string.Equals(typed.name, subAssetName, StringComparison.Ordinal))
                        return typed;
                }
            }
            return null;
        }

        private static T TryLoadEditorAsset<T>(string location) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(location) ||
                UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorApplication.isUpdating)
            {
                return null;
            }

            if (!location.StartsWith("Assets/", StringComparison.Ordinal))
            {
                return null;
            }

            return string.IsNullOrEmpty(location)
                ? null
                : UnityEditor.AssetDatabase.LoadAssetAtPath<T>(location);
        }

#else
        private static T TryLoadEditorSubAsset<T>(string location, string subAssetName) where T : UnityEngine.Object
        {
            return null;
        }

        private static T TryLoadEditorAsset<T>(string location) where T : UnityEngine.Object
        {
            return null;
        }
#endif

        /// <summary>
        /// 异步加载热更新程序集。
        /// </summary>
        public async Task<HotUpdateResultData> LoadHotfixAssembliesAsync(IList<byte[]> assemblyBytes, CancellationToken cancellationToken)
        {
            var result = new HotUpdateResultData { success = true };
            for (var i = 0; i < assemblyBytes.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assembly.Load(assemblyBytes[i]);
                result.loadedAssemblies.Add($"hotfix_{i}");
                await Task.Yield();
            }

            return result;
        }

        /// <summary>
        /// 释放所有已加载的资源。
        /// </summary>
        public void ReleaseAll()
        {
            for (var i = 0; i < handles.Count; i++)
            {
                handles[i]?.Release();
            }

            handles.Clear();
            for (var i = 0; i < subAssetHandles.Count; i++)
                subAssetHandles[i]?.Release();
            subAssetHandles.Clear();
        }

        /// <summary>
        /// 创建初始化参数对象。
        /// </summary>
        private InitializePackageOptions CreateInitOptions()
        {
            switch (RunMode)
            {
                case YooRunMode.EditorSimulate:
                    return CreateEditorOptions();
                case YooRunMode.Offline:
#if !UNITY_WEBGL && !UNITY_EDITOR
                    return new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                    };
#endif
                case YooRunMode.Web:
#if UNITY_WEBGL
                    return new WebPlayModeOptions
                    {
                        WebServerFileSystemParameters = null,
                        WebNetworkFileSystemParameters = CreateWebNetworkFileSystemParameters()
                    };
#endif
                case YooRunMode.Host:
                    return new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters = CreateBuiltinFileSystem(),
                        CacheFileSystemParameters = CreateCacheFileSystem()
                    };
                default:
                    return new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters = CreateBuiltinFileSystem(),
                        CacheFileSystemParameters = CreateCacheFileSystem()
                    };
            }
        }

        private FileSystemParameters CreateWebServerFileSystemParameters()
        {
#if UNITY_WEBGL
            var streamingAssetsRoot = Application.streamingAssetsPath.Replace('\\', '/').TrimEnd('/');
            const string unityWebglPath = "/webgl/unzip/StreamingAssets";
            var markerIndex = streamingAssetsRoot.IndexOf(unityWebglPath, StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                streamingAssetsRoot = streamingAssetsRoot.Substring(0, markerIndex) + "/StreamingAssets";
            }

            var packageName = string.IsNullOrWhiteSpace(activePackageName)
                ? PackageName
                : activePackageName;
            var packageRoot = $"{streamingAssetsRoot}/yoo/{packageName.Trim('/')}";
            Debug.Log($"YooAsset web server root: {packageRoot}");
            return TiktokFileSystemCreater.CreateWebServerFileSystemParameters(packageRoot);
#else
            return FileSystemParameters.CreateDefaultWebServerFileSystemParameters();
#endif
        }

        private FileSystemParameters CreateWebNetworkFileSystemParameters()
        {
#if UNITY_WEBGL
            return TiktokFileSystemCreater.CreateFileSystemParameters(CreateRemoteService());
#else
            return FileSystemParameters.CreateDefaultWebNetworkFileSystemParameters(CreateRemoteService(), true);
#endif
        }

        /// <summary>
        /// 创建内置文件系统。
        /// </summary>
        private FileSystemParameters CreateBuiltinFileSystem()
        {
            var fileSystem = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters();
            fileSystem.AddParameter(EFileSystemParameter.CopyBuiltinPackageManifest, true);
            return fileSystem;
        }

        /// <summary>
        /// 创建缓存文件系统。
        /// </summary>
        private FileSystemParameters CreateCacheFileSystem()
        {
            var fileSystem = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(CreateRemoteService());
            fileSystem.AddParameter(EFileSystemParameter.DownloadMaxConcurrency, Mathf.Max(1, cfg.downloadMaxConcurrency));
            fileSystem.AddParameter(EFileSystemParameter.DownloadMaxRequestPerFrame, 1);
            fileSystem.AddParameter(EFileSystemParameter.DownloadWatchdogTimeout, Mathf.Max(5, cfg.timeoutSeconds));
            return fileSystem;
        }

        /// <summary>
        /// 创建编辑器模式的初始化选项。
        /// </summary>
        private InitializePackageOptions CreateEditorOptions()
        {
#if UNITY_EDITOR
            var root = BuildEditorSimulateRoot(activePackageName);
            if (string.IsNullOrEmpty(root))
            {
                Debug.LogWarning("YooAsset 编辑器模拟根目录为空，将回退到离线模式。");
                return new OfflinePlayModeOptions
                {
                    BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
                };
            }

            var editorFs = FileSystemParameters.CreateDefaultEditorFileSystemParameters(root);
            editorFs.AddParameter(EFileSystemParameter.VirtualWebglMode, true);
            editorFs.AddParameter(EFileSystemParameter.VirtualDownloadMode, false);
            editorFs.AddParameter(EFileSystemParameter.AsyncSimulateMinFrame, 1);
            editorFs.AddParameter(EFileSystemParameter.AsyncSimulateMaxFrame, 3);
            return new EditorSimulateModeOptions
            {
                EditorFileSystemParameters = editorFs
            };
#else
            return new OfflinePlayModeOptions
            {
                BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters()
            };
#endif
        }

#if UNITY_EDITOR
        /// <summary>
        /// 构建编辑器模拟根目录。
        /// </summary>
        private static string BuildEditorSimulateRoot(string packageName)
        {
            PackageBuildResult result = EditorSimulateBuildInvoker.Build(packageName, (int)EBundleType.VirtualAssetBundle);

            if (result == null || string.IsNullOrEmpty(result.PackageRootDirectory))
            {
                Debug.LogError($"YooAsset 编辑器模拟构建返回了空的包根目录： {packageName}");
                return string.Empty;
            }

            return result.PackageRootDirectory;
        }
        
#endif

        /// <summary>
        /// 更新资源清单文件。
        /// </summary>
        private IEnumerator UpdateManifest()
        {
            Debug.Log("开始更新清单文件");
            var packageName = activePackageName;
            var versionOp = package.RequestPackageVersionAsync(new RequestPackageVersionOptions(false, cfg.timeoutSeconds));
            yield return versionOp;
            if (versionOp.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"YooAsset获取({packageName})版本失败， url： {cfg.cosRootUrl}: {versionOp.Error}");
                var localVersionUrl = CombineStreamingAssetsUrl("yoo", packageName, packageName + ".version");
                using (var localVersionRequest = UnityWebRequest.Get(localVersionUrl))
                {
                    localVersionRequest.timeout = Mathf.Max(1, cfg.timeoutSeconds);
                    yield return localVersionRequest.SendWebRequest();
                    if (localVersionRequest.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"YooAsset内置({packageName})版本读取失败: {localVersionUrl}, {localVersionRequest.error}");
                        yield break;
                    }

                    var localVersion = localVersionRequest.downloadHandler.text.Trim();
                    if (string.IsNullOrEmpty(localVersion))
                    {
                        Debug.LogError($"YooAsset内置({packageName})版本文件为空: {localVersionUrl}");
                        yield break;
                    }

                    var localManifestOp = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(localVersion, cfg.timeoutSeconds));
                    yield return localManifestOp;
                    if (localManifestOp.Status != EOperationStatus.Succeeded)
                    {
                        Debug.LogError($"YooAsset加载({packageName})内置清单失败: {localManifestOp.Error}");
                    }
                    else
                    {
                        Debug.LogWarning($"YooAsset({packageName})使用内置版本 {localVersion}，远程请求失败: {versionOp.Error}");
                    }
                }
                yield break;
            }

            var manifestOp = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(versionOp.PackageVersion, cfg.timeoutSeconds));
            yield return manifestOp;
            if (manifestOp.Status != EOperationStatus.Succeeded)
            {
                Debug.LogError($"YooAsset加载({packageName})的清单文件失败: {manifestOp.Error}");
            }
        }

        /// <summary>
        /// 下载所有待更新的资源包。
        /// </summary>
        private void RefreshPendingDownloads()
        {
            pendingDownloadBytes = 0;
            pendingDownloadCount = 0;
            foreach (var item in packages)
            {
                if (!item.Value.PackageValid)
                    continue;

                var downloader = item.Value.CreateResourceDownloader(new ResourceDownloaderOptions(
                    Mathf.Max(1, cfg.downloadMaxConcurrency),
                    Mathf.Max(0, cfg.retryCount)));
                Debug.Log($"YooAsset download check {item.Key}: files={downloader.TotalDownloadCount}, bytes={downloader.TotalDownloadBytes}");
                pendingDownloadCount += downloader.TotalDownloadCount;
                pendingDownloadBytes += downloader.TotalDownloadBytes;
            }
        }

        /// <summary>
        /// 加载 HybridCLR 热更相关文件。
        /// </summary>
        private IEnumerator LoadHybridClrFiles()
        {
            if (cfg.aotMetadataFiles != null)
            {
                for (var i = 0; i < cfg.aotMetadataFiles.Count; i++)
                {
                    yield return LoadAotMetadata(cfg.aotMetadataFiles[i]);
                }
            }

            if (cfg.hotUpdateDllFiles != null)
            {
                for (var i = 0; i < cfg.hotUpdateDllFiles.Count; i++)
                {
                    yield return LoadHotfixDll(cfg.hotUpdateDllFiles[i]);
                }
            }
        }

        private static string CombineStreamingAssetsUrl(params string[] parts)
        {
            var root = Application.streamingAssetsPath.TrimEnd('/');
            return root + "/" + string.Join("/", parts);
        }

        private static string ResolveHotfixLocation(ResourcePackage hybridPackage, string location)
        {
            if (hybridPackage.IsLocationValid(location))
                return location;

            const string bytesExtension = ".bytes";
            var alternateLocation = location.EndsWith(bytesExtension, StringComparison.OrdinalIgnoreCase)
                ? location.Substring(0, location.Length - bytesExtension.Length)
                : location + bytesExtension;
            if (hybridPackage.IsLocationValid(alternateLocation))
            {
                Debug.LogWarning($"YooAsset 热更新地址 {location} 不存在，使用清单中的地址 {alternateLocation}");
                return alternateLocation;
            }

            return location;
        }

        /// <summary>
        /// 加载补充 AOT 元数据程序集。
        /// </summary>
        private IEnumerator LoadAotMetadata(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                yield break;
            }

            var hybridPackage = GetHybridPackage();
            if (hybridPackage == null || !hybridPackage.PackageValid)
            {
                Debug.LogError($"YooAsset 清单未激活，无法加载 AOT 元数据: {location}");
                yield break;
            }

            var resolvedLocation = ResolveHotfixLocation(hybridPackage, location);
            Debug.Log($"HybridCLR AOT 元数据加载位置: {resolvedLocation}");
            var handle = hybridPackage.LoadAssetAsync(resolvedLocation);
            yield return handle;
            if (handle == null)
            {
                Debug.LogError($"HybridCLR AOT metadata handle is null: location={location}, resolvedLocation={resolvedLocation}");
                yield break;
            }

            Debug.Log($"HybridCLR AOT metadata asset load completed: location={resolvedLocation}, status={handle.Status}, error={handle.Error}");
            if (handle.Status == EOperationStatus.Succeeded)
            {
                var rawFile = handle.AssetObject as RawFileObject;
                var bytes = rawFile?.GetBytes();
                if (rawFile == null)
                {
                    Debug.LogError($"HybridCLR AOT metadata RawFileObject is null: location={location}, resolvedLocation={resolvedLocation}, assetType={handle.AssetObject?.GetType().FullName}, error={handle.Error}");
                    handle.Release();
                    yield break;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogError($"HybridCLR AOT metadata content is empty: location={location}, resolvedLocation={resolvedLocation}");
                    handle.Release();
                    yield break;
                }

                var code = RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet);
                Debug.Log($"HybridCLR AOT元数据已加载: {location}, {code}");
            }
            else
            {
                Debug.LogError($"HybridCLR AOT元数据加载失败: {location}, {handle.Error}");
            }

            handle.Release();
        }

        /// <summary>
        /// 加载热更 DLL 程序集。
        /// </summary>
        private IEnumerator LoadHotfixDll(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                yield break;
            }

            var hybridPackage = GetHybridPackage();
            if (hybridPackage == null || !hybridPackage.PackageValid)
            {
#if UNITY_EDITOR
                if (TryLoadEditorHotfixDll(location))
                {
                    yield break;
                }
#endif
                Debug.LogWarning($"由于 YooAsset 声明文件未激活，热修复组件被跳过: {location}");
                yield break;
            }

            var resolvedLocation = ResolveHotfixLocation(hybridPackage, location);
            Debug.Log($"HybridCLR 热更新程序集加载位置: {resolvedLocation}");
            var handle = hybridPackage.LoadAssetAsync(resolvedLocation);
            yield return handle;
            if (handle.Status == EOperationStatus.Succeeded)
            {
                var rawFile = handle.AssetObject as RawFileObject;
                var bytes = rawFile?.GetBytes();
                if (rawFile == null)
                {
                    Debug.LogError($"HybridCLR 热更新程序集 TextAsset 为空: location={location}, resolvedLocation={resolvedLocation}, assetType={handle.AssetObject?.GetType().FullName}, error={handle.Error}");
                    handle.Release();
                    yield break;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogError($"HybridCLR 热更新程序集内容为空: location={location}, resolvedLocation={resolvedLocation}");
                    handle.Release();
                    yield break;
                }
                try
                {
                    Assembly.Load(bytes);
                    Debug.Log($"HybridCLR 热修复程序集已加载: {location}");
                }
                catch (Exception e)
                {
                    Debug.LogError($"HybridCLR 热修复程序集加载失败: {location}, {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"HybridCLR 热修复程序集未找到: {location}, {handle.Error}");
            }

            handle.Release();
        }

#if UNITY_EDITOR
        /// <summary>
        /// 尝试在编辑器中加载热更 DLL。
        /// </summary>
        private static bool TryLoadEditorHotfixDll(string location)
        {
            var folder = string.Equals(location, "Shared.dll.bytes", StringComparison.OrdinalIgnoreCase)
                ? "GameAssets/Hotfix/DependDll"
                : "GameAssets/Hotfix/Dll";
            var path = Path.Combine(Application.dataPath, folder, location);
            if (!File.Exists(path) && !location.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
            {
                path += ".bytes";
            }

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                Assembly.Load(File.ReadAllBytes(path));
                Debug.Log($"从编辑器文件加载的 HybridCLR 热修复程序集： {path} 成功");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"编辑器热修复程序集加载{path}失败 , {e.Message}");
                return false;
            }
        }
#endif

        /// <summary>
        /// 创建远程资源服务。
        /// </summary>
        private CosRemoteService CreateRemoteService()
        {
            var mainRoot = BuildHostServerUrl(cfg.cosRootUrl);
            var fallbackRoot = BuildHostServerUrl(cfg.fallbackRootUrl);
            Debug.Log($"YooAsset remote root: {mainRoot}");
            if (!string.IsNullOrEmpty(fallbackRoot))
                Debug.Log($"YooAsset fallback root: {fallbackRoot}");
            return new CosRemoteService(mainRoot, fallbackRoot);
        }

        private string BuildHostServerUrl(string rootUrl)
        {
            if (string.IsNullOrWhiteSpace(rootUrl))
                return string.Empty;
            var platform = CosRemoteService.PlatformName();
            var packageName = string.IsNullOrWhiteSpace(activePackageName)
                ? PackageName
                : activePackageName;
            var version = string.IsNullOrWhiteSpace(cfg.appVersion)
                ? "v1.0.0"
                : cfg.appVersion.Trim('/');
            var normalizedRoot = rootUrl.TrimEnd('/');
            var separator = normalizedRoot.EndsWith("=") || normalizedRoot.EndsWith("?", StringComparison.Ordinal)
                ? string.Empty
                : "/";
            return $"{normalizedRoot}{separator}Bundles/{platform}/{packageName.Trim('/')}/{version}";
        }

        /// <summary>
        /// 为 YooAsset 提供远端资源地址生成逻辑，支持主地址和备用地址。
        /// </summary>
        private sealed class CosRemoteService : IRemoteService
        {
            private readonly string mainRoot;
            private readonly string fallbackRoot;
            /// <summary>
            /// 创建 Cloudflare 远程服务实例。
            /// </summary>
            public CosRemoteService(string mainRoot, string fallbackRoot)
            {
                this.mainRoot = Trim(mainRoot);
                this.fallbackRoot = Trim(fallbackRoot);
            }

            /// <summary>
            /// 获取远程资源地址列表。
            /// </summary>
            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                var relativeFileName = fileName.TrimStart('/');
                var urls = new List<string>(2) { $"{mainRoot}/{relativeFileName}" };
                if (!string.IsNullOrEmpty(fallbackRoot))
                {
                    urls.Add($"{fallbackRoot}/{relativeFileName}");
                }

                return urls;
            }

            /// <summary>
            /// 清理字符串空格。
            /// </summary>
            private static string Trim(string value)
            {
                return string.IsNullOrWhiteSpace(value) ? string.Empty : value.TrimEnd('/');
            }

            /// <summary>
            /// 获取当前平台名称。
            /// </summary>
            public static string PlatformName()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return "Android";
#elif UNITY_IOS && !UNITY_EDITOR
                return "iOS";
#elif UNITY_WEBGL && !UNITY_EDITOR
                return "WebGL";
#elif UNITY_EDITOR
                var target = UnityEditor.EditorUserBuildSettings.activeBuildTarget;
                if (target == UnityEditor.BuildTarget.Android)
                    return "Android";
                if (target == UnityEditor.BuildTarget.iOS)
                    return "iOS";
                if (target == UnityEditor.BuildTarget.WebGL)
                    return "WebGL";
                return "PC";
#else
                return "PC";
#endif
            }
        }
    }
