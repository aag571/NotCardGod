using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HybridCLR;
using UnityEngine;
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
                if (RunMode == YooRunMode.Host || RunMode == YooRunMode.Web)
                {
                    yield return UpdateManifest();
                }
            }
            
            Debug.Log("更新下载进度");
            RefreshPendingDownloads();
            if (!HasPendingDownloads)
                yield return LoadHybridClrFiles();
            Ready = false;
            foreach (var item in packages.Values)
                Ready |= item.PackageValid;
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
            yield return LoadHybridClrFiles();
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
        private static T TryLoadEditorAsset<T>(string location) where T : UnityEngine.Object
        {
            if (string.IsNullOrWhiteSpace(location) ||
                UnityEditor.EditorApplication.isCompiling ||
                UnityEditor.EditorApplication.isUpdating)
            {
                return null;
            }

            var path = ResolveEditorAssetPath(location);
            return string.IsNullOrEmpty(path)
                ? null
                : UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="location"></param>
        /// <returns></returns>
        private static string ResolveEditorAssetPath(string location)
        {
            switch (location)
            {
                case "BalanceConfig": return "Assets/GameAssets/Config/ScriptObject/BalanceConfig.asset";
                case "MatchDeckConfig": return "Assets/GameAssets/Config/ScriptObject/MatchDeckConfig.asset";
                case "CardCatalog": return "Assets/GameAssets/Config/ScriptObject/CardCatalog.asset";
                case "CardArtConfig": return "Assets/GameAssets/Config/ScriptObject/CardArtConfig.asset";
                case "Card_Base_Attack": return "Assets/GameAssets/Prefab/Card_Base_Attack.prefab";
                case "Card_Base_Defense": return "Assets/GameAssets/Prefab/Card_Base_Defense.prefab";
                case "Card_Base_Heal": return "Assets/GameAssets/Prefab/Card_Base_Heal.prefab";
                case "Card_Skill_Silver": return "Assets/GameAssets/Prefab/Card_Skill_Silver.prefab";
                case "Card_Skill_Gold": return "Assets/GameAssets/Prefab/Card_Skill_Gold.prefab";
                case "Card_Skill_Platinum": return "Assets/GameAssets/Prefab/Card_Skill_Platinum.prefab";
                case "Card_Skill_Prismatic": return "Assets/GameAssets/Prefab/Card_Skill_Prismatic.prefab";
                case "BK": return "Assets/GameAssets/Audio/BK.mp3";
                case "intensify": return "Assets/GameAssets/Audio/intensify.mp3";
                case "send": return "Assets/GameAssets/Audio/send.mp3";
                case "po": return "Assets/GameAssets/Audio/po.mp3";
                case "Tag_Silver": return "Assets/GameAssets/UI/Gem/Tag_Silver.png";
                case "Tag_Gold": return "Assets/GameAssets/UI/Gem/Tag_Gold.png";
                case "Tag_Platinum": return "Assets/GameAssets/UI/Gem/Tag_Platinum.png";
                case "Tag_Prismatic": return "Assets/GameAssets/UI/Gem/Tag_Prismatic.png";
                default: return string.Empty;
            }
        }
#else
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
#if UNITY_WEBGL && !UNITY_EDITOR
                    return new WebPlayModeOptions
                    {
                        WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(),
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

        private FileSystemParameters CreateWebNetworkFileSystemParameters()
        {
#if UNITY_WEBGL && DOUYINMINIGAME
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

        /// <summary>
        /// 加载补充 AOT 元数据程序集。
        /// </summary>
        private IEnumerator LoadAotMetadata(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                yield break;
            }

            var handle = package.LoadAssetAsync<RawFileObject>(location);
            yield return handle;
            if (handle.Status == EOperationStatus.Succeeded)
            {
                var raw = handle.GetAssetObject<RawFileObject>();
                var code = RuntimeApi.LoadMetadataForAOTAssembly(raw.GetBytes(), HomologousImageMode.SuperSet);
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

            if (package == null || !package.PackageValid)
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

            var handle = package.LoadAssetAsync<RawFileObject>(location);
            yield return handle;
            if (handle.Status == EOperationStatus.Succeeded)
            {
                var raw = handle.GetAssetObject<RawFileObject>();
                try
                {
                    Assembly.Load(raw.GetBytes());
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
            var path = Path.Combine(Application.dataPath, "GameAssets/Hotfix/Dll", location);
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
            return new CosRemoteService(
                BuildHostServerUrl(cfg.cosRootUrl),
                BuildHostServerUrl(cfg.fallbackRootUrl));
        }

        private string BuildHostServerUrl(string rootUrl)
        {
            if (string.IsNullOrWhiteSpace(rootUrl))
                return string.Empty;

            var root = rootUrl.TrimEnd('/');
            var platform = CosRemoteService.PlatformName();
            var packageName = string.IsNullOrWhiteSpace(activePackageName)
                ? PackageName
                : activePackageName;
            var version = string.IsNullOrWhiteSpace(cfg.appVersion)
                ? "v1.0.0"
                : cfg.appVersion.Trim('/');

            return $"{root}/Bundles/{platform}/{packageName.Trim('/')}/{version}";
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
