using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class RuntimeFontOverride : MonoBehaviour
{
    private const float ScanInterval = 0.5f;

    private static RuntimeFontOverride instance;
    public static bool Ready { get; private set; }
    public static string LoadError { get; private set; }
    private Font legacyFont;
    private TMP_FontAsset tmpFont;
    private float nextScanTime;
    private bool loadStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateAtStartup()
    {
        if (instance != null)
        {
            return;
        }

        GameObject fontObject = new GameObject(nameof(RuntimeFontOverride));
        DontDestroyOnLoad(fontObject);
        instance = fontObject.AddComponent<RuntimeFontOverride>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        Ready = false;
        LoadError = string.Empty;
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyFonts();
    }

    private IEnumerator Start()
    {
        IGameAssetService assets = null;
        while (!GameApp.TryGet<IGameAssetService>(out assets))
        {
            yield return null;
        }

        while (!assets.Ready || !assets.DownloadsCompleted)
        {
            yield return null;
        }

        loadStarted = true;
        Task<Font> legacyTask = LoadAssetWithFallbackAsync<Font>(assets,
            "simkai",
            "simkai.ttf",
            "Assets/GameAssets/Font/simkai.ttf");
        Task<TMP_FontAsset> tmpTask = LoadAssetWithFallbackAsync<TMP_FontAsset>(assets,
            "simkai SDF",
            "simkai SDF.asset",
            "Assets/GameAssets/Font/simkai SDF.asset");

        while (!legacyTask.IsCompleted || !tmpTask.IsCompleted)
        {
            yield return null;
        }

        if (legacyTask.Status == TaskStatus.RanToCompletion)
        {
            legacyFont = legacyTask.Result;
        }
        else
        {
            Debug.LogError($"远端 UGUI 字体加载失败: {legacyTask.Exception?.GetBaseException().Message}");
        }

        if (tmpTask.Status == TaskStatus.RanToCompletion)
        {
            tmpFont = tmpTask.Result;
        }
        else
        {
            Debug.LogError($"远端 TMP 字体加载失败: {tmpTask.Exception?.GetBaseException().Message}");
        }

        ApplyFonts();
        Ready = legacyFont != null && tmpFont != null;
        if (!Ready)
            LoadError = legacyTask.Exception?.GetBaseException().Message ??
                        tmpTask.Exception?.GetBaseException().Message ??
                        "Remote font assets are incomplete.";
    }

    private static async Task<T> LoadAssetWithFallbackAsync<T>(IGameAssetService assets, params string[] locations)
        where T : UnityEngine.Object
    {
        Exception lastError = null;
        for (int index = 0; index < locations.Length; index++)
        {
            try
            {
                var asset = await assets.LoadAssetAsync<T>(locations[index], CancellationToken.None);
                if (asset != null)
                {
                    Debug.Log($"字体资源加载成功: {typeof(T).Name}, address={locations[index]}");
                    return asset;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"字体资源地址加载失败: {typeof(T).Name}, address={locations[index]}, error={exception.Message}");
                lastError = exception;
            }
        }

        throw new InvalidOperationException($"无法从 YooAsset 找到字体资源: {typeof(T).Name}", lastError);
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this)
        {
            instance = null;
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScanTime)
        {
            return;
        }

        nextScanTime = Time.unscaledTime + ScanInterval;
        ApplyFonts();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplyFonts();
    }

    private void ApplyFonts()
    {
        if (!loadStarted)
        {
            return;
        }

        if (legacyFont != null)
        {
            Text[] legacyTexts = FindObjectsOfType<Text>(true);
            for (int index = 0; index < legacyTexts.Length; index++)
            {
                if (legacyTexts[index].font != legacyFont)
                {
                    legacyTexts[index].font = legacyFont;
                }
            }
        }

        if (tmpFont != null)
        {
            TMP_Text[] tmpTexts = FindObjectsOfType<TMP_Text>(true);
            for (int index = 0; index < tmpTexts.Length; index++)
            {
                if (tmpTexts[index].font != tmpFont)
                {
                    tmpTexts[index].font = tmpFont;
                }

                if (tmpFont.material != null && tmpTexts[index].fontSharedMaterial != tmpFont.material)
                {
                    tmpTexts[index].fontSharedMaterial = tmpFont.material;
                }
            }
        }
    }
}
