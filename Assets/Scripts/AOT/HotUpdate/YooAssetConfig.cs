using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

    /// <summary>
    /// 定义 YooRunMode 类型，承载对应模块的运行时职责。
    /// </summary>
    public enum YooRunMode
    {
        EditorSimulate,
        Offline,
        Host,
        Web
    }

    /// <summary>
    /// YooAsset 和 HybridCLR 的运行配置。
    /// 资源包名、远端地址、下载参数和热更 DLL 列表都放在这里，避免散落在场景代码中。
    /// </summary>
    [CreateAssetMenu(fileName = "YooAssetConfig", menuName = "NotCardGod/HotUpdate/Yoo Asset Config")]
    public sealed class YooAssetConfig : ScriptableObject
    {
        [Header("Package")]
        // YooAsset 资源包名称，必须和 BundleCollectorSetting 中的包名一致。
        public string packageName = "DefaultPackage";

        [Tooltip("YooAsset package names configured in BundleCollectorSetting.")]
        public List<string> groupNames = new List<string>
        {
            "Sprites",
            "Prefabs",
            "Configs",
            "Audios",
            "Hotfix",
            "Effects"
        };

        // 应用版本号，用于 YooAsset 检查资源清单。
        public string appVersion = "v1.0.0";

        // Unity 编辑器中使用的资源运行模式。
        public YooRunMode editorRunMode = YooRunMode.EditorSimulate;

        // 真机或打包后使用的资源运行模式。
        public YooRunMode playerRunMode = YooRunMode.Web;

        [Header("Remote")]
        // Host/Web 模式下的主远端资源地址。
        [FormerlySerializedAs("cloudflareRootUrl")]
        public string cosRootUrl = "https://a.unity.cn/client_api/v1/buckets/6df4c6bc-48c7-4f40-876a-19c8b9c9d2e8/releases/a70d98e3-8839-4f4b-a104-d8fe26662a8d/content/";

        // 主地址失败时使用的备用远端资源地址。
        public string fallbackRootUrl = "";

        [Header("Download")]
        // 同时下载的最大文件数量。
        public int downloadMaxConcurrency = 5;

        // 单个下载失败后的重试次数。
        public int retryCount = 3;

        // 网络请求超时时间，单位为秒。
        public int timeoutSeconds = 60;

        [Header("HybridCLR")]
        // HybridCLR 需要补充加载的 AOT 元数据 dll.bytes 文件。
        public List<string> aotMetadataFiles = new List<string>();

        // 需要加载的热更程序集 dll.bytes 文件。
        public List<string> hotUpdateDllFiles = new List<string>();

        /// <summary>
        /// 获取当前平台实际使用的 YooAsset 运行模式。
        /// </summary>
        public YooRunMode RunMode
        {
            get
            {
#if UNITY_EDITOR
                return editorRunMode;
#else
                return playerRunMode;
#endif
            }
        }

        public List<string> GetPackageNames()
        {
            if (!string.IsNullOrWhiteSpace(packageName))
                return new List<string> { packageName };

            return new List<string> { "DefaultPackage" };
        }
    }
