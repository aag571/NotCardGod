using System;
using System.Collections;
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
        [Header("YooAsset")]
        [SerializeField] private YooRunMode yooAssetRunMode = YooRunMode.EditorSimulate;

        private GameObject downloadPanel;
        private Text downloadTipText;
        private Button downloadButton;
        private Slider downloadSlider;
        private Text downloadProgressText;
        private bool downloadRequested;


        private void Awake()
        {
            
            ResolveDownloadUi();
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
            
            Debug.Log("BootSceneController初始化完成");
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
            Debug.Log("资源服务初始化完成，接下来开始检测下载");
            if (assetService.HasPendingDownloads)
            {
                yield return WaitForResourceDownload(assetService);
                Debug.Log("资源下载完毕");
            }
            Debug.Log($"没有需要下载的资源");

            // Boot 阶段就创建全局音频播放器，避免进入后续场景前没有 AudioSource。
            GameAudioService.Ensure();
        }

        /// <summary>
        /// 资源下载协程
        /// </summary>
        /// <param name="assetService"></param>
        /// <returns></returns>
        private IEnumerator WaitForResourceDownload(YooAssetService assetService)
        {
            ConfigureDownloadUi(assetService.PendingDownloadCount, assetService.PendingDownloadBytes);
            
            int count = 0;
            while (assetService.HasPendingDownloads)
            {
                while (!downloadRequested)
                    yield return null;

                downloadPanel.SetActive(false);
                downloadSlider.gameObject.SetActive(true);
                downloadSlider.value = 0f;
                downloadProgressText.text = "0%";
                Action<HotUpdateProgressData> progressHandler = progress =>
                {
                    var ratio = progress.totalBytes <= 0
                        ? 0f
                        : Mathf.Clamp01((float)progress.downloadedBytes / progress.totalBytes);
                    downloadSlider.value = ratio;
                    downloadProgressText.text = $"{ratio * 100f:0.0}%  {FormatBytes(progress.downloadedBytes)} / {FormatBytes(progress.totalBytes)}";
                };
                assetService.ProgressChanged += progressHandler;
                yield return assetService.DownloadPending();
                assetService.ProgressChanged -= progressHandler;

                if (assetService.HasPendingDownloads)
                {
                    ConfigureDownloadUi(assetService.PendingDownloadCount, assetService.PendingDownloadBytes);
                    downloadTipText.text = "资源下载失败，请重试。";
                }
            }

            downloadSlider.value = 1f;
            downloadProgressText.text = "100%";
            yield return null;
        }

        private void ResolveDownloadUi()
        {
            downloadPanel = FindSceneObject("Panel");
            downloadTipText = FindSceneComponent<Text>("Tip");
            downloadButton = FindSceneComponent<Button>("DownLoad");
            downloadSlider = FindSceneComponent<Slider>("Slider");
            downloadProgressText = FindSceneComponent<Text>("progress");

            if (downloadButton != null)
            {
                downloadButton.onClick.RemoveListener(OnDownloadButtonClicked);
                downloadButton.onClick.AddListener(OnDownloadButtonClicked);
            }

            if (downloadPanel != null)
                downloadPanel.SetActive(false);
            if (downloadSlider != null)
                downloadSlider.gameObject.SetActive(false);
        }

        private void ConfigureDownloadUi(int fileCount, long totalBytes)
        {
            downloadRequested = false;
            if (downloadPanel == null || downloadTipText == null || downloadButton == null || downloadSlider == null || downloadProgressText == null)
            {
                Debug.LogError("下载界面不完整");
                downloadRequested = true;
                return;
            }

            downloadPanel.SetActive(true);
            downloadSlider.gameObject.SetActive(false);
            downloadSlider.value = 0f;
            downloadProgressText.text = string.Empty;
            downloadTipText.text = $"检测到 {fileCount} 个文件，共 {FormatBytes(totalBytes)}。";
            downloadButton.interactable = true;
        }

        private void OnDownloadButtonClicked()
        {
            downloadRequested = true;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            var objects = Resources.FindObjectsOfTypeAll<Transform>();
            for (var i = 0; i < objects.Length; i++)
            {
                var target = objects[i];
                if (target.name == objectName && target.gameObject.scene.IsValid())
                    return target.gameObject;
            }

            return null;
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

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";
            if (bytes < 1024 * 1024)
                return $"{bytes / 1024f:0.0} KB";
            return $"{bytes / 1048576f:0.0} MB";
        }
    }
