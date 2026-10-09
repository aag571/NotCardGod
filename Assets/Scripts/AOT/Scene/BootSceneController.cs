using System;
using System.Collections;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

    /// <summary>
    /// 启动场景控制器，完成服务注册后进入启动流程。
    /// </summary>
    public sealed class BootSceneController : MonoBehaviour
    {
        [SerializeField] private string nextSceneName = "Splash";
        [SerializeField] private float minimumBootSeconds = 0.15f;
        [SerializeField] private bool dontDestroyBootRoot = true;
        [SerializeField] private YooAssetConfig yooAssetConfig;
        [Header("Douyin TTSDK")]
        [SerializeField] private string rewardedVideoAdId;
        [SerializeField] private bool forceLogin = true;
        [SerializeField] private bool continueWhenLoginFails = true;
        [Header("YooAsset")]
        [SerializeField] private YooRunMode yooAssetRunMode = YooRunMode.EditorSimulate;
        [Header("Boot Progress")]
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text progressText;

        private bool runtimeReady;


        private void Awake()
        {
            if (progressSlider == null)
                progressSlider = FindSceneComponent<Slider>("Slider");
            if (progressText == null)
                progressText = FindSceneComponent<TMP_Text>("progress");

            if (progressSlider != null)
                progressSlider.gameObject.SetActive(true);
            SetBootProgress(0f, "正在初始化");
        }

        /// <summary>
        /// 组件启动时的初始化逻辑。
        /// </summary>
        private IEnumerator Start()
        {
            if (dontDestroyBootRoot)
            {
                DontDestroyOnLoad(gameObject);
            }

            yield return InitializeRuntime();

            if (!runtimeReady)
            {
                SetBootProgress(progressSlider != null ? progressSlider.value : 0f, "资源更新或加载失败");
                Debug.LogError("运行时资源初始化失败，未进入后续场景。请检查 YooAsset CDN 地址、版本清单和网络配置。");
                yield break;
            }
            
            Debug.Log("BootSceneController初始化完成");
            SetBootProgress(1f, "加载完成");
            yield return new WaitForSeconds(Mathf.Max(0f, minimumBootSeconds));

            if (!string.IsNullOrEmpty(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }

        /// <summary>
        /// 初始化运行时环境与服务。
        /// </summary>
        private IEnumerator InitializeRuntime()
        {
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            SetBootProgress(0.05f, "正在初始化运行环境");
            yield return InitializeDouyinSdk();
            SetBootProgress(0.15f, "正在检查资源更新");

            if (yooAssetConfig == null)
            {
                yooAssetConfig = ScriptableObject.CreateInstance<YooAssetConfig>();
                Debug.LogWarning("YooAssetConfig未找到，已使用默认配置");
            }

            var runMode = Application.isEditor ? yooAssetRunMode : yooAssetConfig.RunMode;
            var assetService = new YooAssetService(yooAssetConfig, runMode);
            // 先注册服务，再执行初始化，使音频和其他运行时模块可以在启动阶段获取同一个资源服务。
            GameApp.Register<IGameAssetService>(assetService);
            
            yield return assetService.Init();
            if (!assetService.Ready)
            {
                Debug.LogError("YooAsset 远端资源包未就绪，停止启动流程。无 StreamingAssets 时必须确保 CDN 清单可访问。");
                yield break;
            }
            Debug.Log("资源服务初始化完成，接下来开始检测下载");
            if (assetService.HasPendingDownloads)
            {
                yield return DownloadResourceUpdate(assetService);
                Debug.Log("资源下载完毕");
            }
            else
            {
                SetBootProgress(0.85f, "资源已是最新，正在加载资源");
            }
            if (!assetService.Ready || assetService.HasPendingDownloads)
            {
                Debug.LogError("YooAsset 资源未完成下载，停止启动流程。");
                yield break;
            }
            Debug.Log($"没有需要下载的资源");

            // Boot 阶段就创建全局音频播放器，避免进入后续场景前没有 AudioSource。
            SetBootProgress(0.9f, "正在加载运行资源");
            GameAudioService.Ensure();
            while (!RuntimeFontOverride.Ready)
            {
                if (!string.IsNullOrEmpty(RuntimeFontOverride.LoadError))
                {
                    Debug.LogError($"Font assets are not ready; Boot will not continue: {RuntimeFontOverride.LoadError}");
                    yield break;
                }
                yield return null;
            }

            Debug.Log("Remote UGUI and TMP fonts are ready.");
            SetBootProgress(0.98f, "资源加载完成");
            runtimeReady = true;
        }

        private IEnumerator InitializeDouyinSdk()
        {
            var sdk = DouyinTTSDKService.Instance;
            if (sdk == null)
            {
                var sdkObject = new GameObject("DouyinTTSDKService");
                sdk = sdkObject.AddComponent<DouyinTTSDKService>();
            }

            sdk.Configuration.rewardedVideoAdId = rewardedVideoAdId;
            sdk.Configuration.forceLogin = forceLogin;
            sdk.Configuration.autoLoginOnInitialize = false;

            var completed = false;
            var loginSucceeded = false;
            sdk.Initialize((initialized, initializeError) =>
            {
                if (!initialized)
                {
                    Debug.LogError($"TTSDK 初始化失败：{initializeError}");
                    completed = true;
                    return;
                }

                sdk.Login(forceLogin, (isLoggedIn, code, error) =>
                {
                    loginSucceeded = isLoggedIn;
                    if (!isLoggedIn)
                        Debug.LogWarning($"TTSDK 登录未成功：{error}");
                    else
                        Debug.Log("TTSDK 登录成功");

                    completed = true;
                });
            });

            while (!completed)
                yield return null;

            if (loginSucceeded && !sdk.IsGuest)
            {
                yield return LoadDouyinProfile(sdk);
                LocalAccountService.RequireCloudAccount(!Application.isEditor);
                LocalAccountService.SetCloudState(null);
                var saveObject = new GameObject("UosCrudSaveService");
                var saveService = saveObject.AddComponent<UosCrudSaveService>();
                var saveTask = saveService.InitializeAsync(sdk.UserId);
                while (!saveTask.IsCompleted)
                    yield return null;

                if (saveTask.IsFaulted)
                    Debug.LogError("UOS CRUD Save 初始化异常，正式账号数据不会回退到本地存储。");
                else if (saveTask.Result)
                {
                    var loadTask = saveService.ReadAccountAsync();
                    while (!loadTask.IsCompleted)
                        yield return null;

                    if (loadTask.Status == TaskStatus.RanToCompletion)
                    {
                        var cloudState = loadTask.Result;
                        if (cloudState == null)
                        {
                            cloudState = new LocalAccountState();
                            var createTask = saveService.UpdateAccountAsync(cloudState);
                            while (!createTask.IsCompleted)
                                yield return null;

                            if (createTask.Status == TaskStatus.RanToCompletion)
                                Debug.Log("UOS CRUD 玩家数据不存在，已为当前抖音账号创建默认数据");
                            else
                                Debug.LogError("UOS CRUD 玩家数据创建失败，当前账号不会回退到本地存储");
                        }

                        LocalAccountService.SetCloudState(cloudState);
                    }
                    else
                        Debug.LogError("UOS CRUD Save 读取账号失败，正式账号不会回退到本地存储。");
                }

                var onlineObject = new GameObject("UosOnlineService");
                var onlineService = onlineObject.AddComponent<UosOnlineService>();
                var onlineTask = onlineService.InitializeAsync(sdk.UserId);
                while (!onlineTask.IsCompleted)
                    yield return null;
                if (!onlineTask.Result)
                    Debug.LogWarning("UOS 联机服务未启动，请检查 UOS 房间配置和小游戏网络环境。");
                else
                    onlineService.JoinRoomFromQuery(sdk.GetLaunchQuery());
            }

            if (!loginSucceeded && !continueWhenLoginFails)
            {
                Debug.LogError("TTSDK 登录失败，Boot 将停止进入后续场景。");
                while (true)
                    yield return null;
            }
        }

        private IEnumerator LoadDouyinProfile(DouyinTTSDKService sdk)
        {
            var completed = false;
            sdk.GetUserInfo(
                _ => completed = true,
                error =>
                {
                    Debug.LogWarning($"抖音用户资料获取失败，将使用账号 ID 作为玩家名称: {error}");
                    completed = true;
                });

            var timeout = 0f;
            while (!completed && timeout < 5f)
            {
                timeout += Time.unscaledDeltaTime;
                yield return null;
            }

            sdk.ApplyFallbackProfile();
        }

        private IEnumerator DownloadResourceUpdate(YooAssetService assetService)
        {
            var totalBytes = assetService.PendingDownloadBytes;
            SetBootProgress(0.35f,
                $"发现 {assetService.PendingDownloadCount} 个资源更新，正在自动下载（{FormatBytes(totalBytes)}）");

            Action<HotUpdateProgressData> progressHandler = progress =>
            {
                var ratio = progress.totalBytes <= 0
                    ? 0f
                    : Mathf.Clamp01((float)progress.downloadedBytes / progress.totalBytes);
                SetBootProgress(Mathf.Lerp(0.35f, 0.85f, ratio),
                    $"正在更新资源  {FormatBytes(progress.downloadedBytes)} / {FormatBytes(progress.totalBytes)}");
            };

            assetService.ProgressChanged += progressHandler;
            yield return assetService.DownloadPending();
            assetService.ProgressChanged -= progressHandler;

            if (!assetService.HasPendingDownloads)
                SetBootProgress(0.85f, "资源更新完成，正在加载资源");
        }

        private static T FindSceneComponent<T>(string objectName) where T : Component
        {
            var components = Resources.FindObjectsOfTypeAll<T>();
            for (var i = 0; i < components.Length; i++)
            {
                var target = components[i];
                if (target.gameObject.name == objectName && target.gameObject.scene.IsValid())
                    return target;
            }

            return null;
        }

        private void SetBootProgress(float value, string status)
        {
            value = Mathf.Clamp01(value);
            if (progressSlider != null)
                progressSlider.value = value;
            if (progressText != null)
                progressText.text = $"{status}  {value * 100f:0}%";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024f:0.0} KB";
            return $"{bytes / 1048576f:0.0} MB";
        }
    }
