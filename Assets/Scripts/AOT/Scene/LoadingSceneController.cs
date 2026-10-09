using System.Collections;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

    /// <summary>
    /// 加载场景控制器，读取目标场景并显示加载进度。
    /// </summary>
    public sealed class LoadingSceneController : MonoBehaviour
    {
        public const string TargetScenePrefsKey = "NotCardGod.Loading.TargetScene";

        [SerializeField] private string defaultTargetSceneName = "Main";
        [SerializeField] private float minimumLoadingSeconds = 0.2f;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private Text loadingHintText;
        [SerializeField] private Text loadingProgressText;

        /// <summary>
        /// 组件启动时的初始化逻辑。
        /// </summary>
        private IEnumerator Start()
        {
            var targetScene = PlayerPrefs.GetString(TargetScenePrefsKey, defaultTargetSceneName);
            if (string.IsNullOrEmpty(targetScene))
            {
                targetScene = defaultTargetSceneName;
            }

            SetText("LoadingHintText", ResolveLoadingHint(targetScene));
            SetProgress(0f);

            if (string.Equals(targetScene, "Battle", System.StringComparison.OrdinalIgnoreCase) &&
                GameApp.TryGet<IGameAssetService>(out var assets))
            {
                var preloadCompleted = false;
                var preloadSucceeded = false;
                yield return BattleAssetPreloader.Preload(assets,
                    progress => SetProgress(Mathf.Clamp01(progress * 0.8f)),
                    succeeded =>
                    {
                        preloadSucceeded = succeeded;
                        preloadCompleted = true;
                    });

                while (!preloadCompleted)
                    yield return null;

                if (!preloadSucceeded)
                    Debug.LogWarning("Battle 资源预加载未完整成功，进入场景后将继续尝试加载。");
            }

            var startedAt = Time.unscaledTime;
            var operation = SceneManager.LoadSceneAsync(targetScene, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError($"场景加载失败，Build Settings 中不存在场景: {targetScene}");
                yield break;
            }

            while (!operation.isDone)
            {
                // Unity 的异步场景加载在允许激活前最高只报告到 0.9。
                SetProgress(0.8f + Mathf.Clamp01(operation.progress / 0.9f) * 0.2f);
                yield return null;
            }

            while (Time.unscaledTime - startedAt < Mathf.Max(0f, minimumLoadingSeconds))
            {
                SetProgress(1f);
                yield return null;
            }
        }

        /// <summary>
        /// 设置 UI 文本内容。
        /// </summary>
        private void SetText(string objectName, string text)
        {
            if (loadingHintText != null)
                loadingHintText.text = text;
        }

        private static string ResolveLoadingHint(string targetScene)
        {
            if (string.Equals(targetScene, "Main", System.StringComparison.OrdinalIgnoreCase))
                return "返回主菜单";
            if (string.Equals(targetScene, "Battle", System.StringComparison.OrdinalIgnoreCase))
                return "前往战场";
            if (string.Equals(targetScene, "Splash", System.StringComparison.OrdinalIgnoreCase))
                return "正在进入游戏";
            return "正在加载场景";
        }

        /// <summary>
        /// 设置加载进度条的值。
        /// </summary>
        private void SetProgress(float value)
        {
            value = Mathf.Clamp01(value);
            if (progressSlider != null)
                progressSlider.value = value;
            if (loadingProgressText != null)
                loadingProgressText.text = $"{value * 100f:0}%";
        }
    }

public static class BattleAssetPreloader
{
    private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Object> Cache =
        new System.Collections.Generic.Dictionary<string, UnityEngine.Object>(System.StringComparer.Ordinal);

    private static readonly string[] Locations =
    {
        "BalanceConfig", "MatchDeckConfig", "CardCatalog", "CardArtConfig",
        "Card_Base_Attack", "Card_Base_Defense", "Card_Base_Heal",
        "Card_Skill_Silver", "Card_Skill_Gold", "Card_Skill_Platinum", "Card_Skill_Prismatic",
        "Tag_Silver", "Tag_Gold", "Tag_Platinum", "Tag_Prismatic"
    };

    public static IEnumerator Preload(IGameAssetService assets, System.Action<float> progress, System.Action<bool> completed)
    {
        if (assets == null)
        {
            completed?.Invoke(false);
            yield break;
        }

        for (var i = 0; i < Locations.Length; i++)
        {
            var task = LoadByLocation(assets, Locations[i]);
            while (!task.IsCompleted)
            {
                progress?.Invoke((i + 0.5f) / Locations.Length);
                yield return null;
            }

            if (task.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || task.Result == null)
            {
                Debug.LogWarning($"Battle 资源预加载失败: {Locations[i]}");
                completed?.Invoke(false);
                yield break;
            }

            Cache[Locations[i]] = task.Result;
            progress?.Invoke((i + 1f) / Locations.Length);
        }

        completed?.Invoke(true);
    }

    public static bool TryGet<T>(string location, out T asset) where T : UnityEngine.Object
    {
        if (Cache.TryGetValue(location, out var value) && value is T typed)
        {
            asset = typed;
            return true;
        }

        asset = null;
        return false;
    }

    private static System.Threading.Tasks.Task<UnityEngine.Object> LoadByLocation(IGameAssetService assets, string location)
    {
        switch (location)
        {
            case "BalanceConfig": return Load<BalanceConfig>(assets, location);
            case "MatchDeckConfig": return Load<MatchDeckConfig>(assets, location);
            case "CardCatalog": return Load<CardCatalog>(assets, location);
            case "CardArtConfig": return Load<CardArtConfig>(assets, location);
            case "Card_Base_Attack":
            case "Card_Base_Defense":
            case "Card_Base_Heal":
            case "Card_Skill_Silver":
            case "Card_Skill_Gold":
            case "Card_Skill_Platinum":
            case "Card_Skill_Prismatic": return Load<GameObject>(assets, location);
            default: return Load<Sprite>(assets, location);
        }
    }

    private static async System.Threading.Tasks.Task<UnityEngine.Object> Load<T>(IGameAssetService assets, string location)
        where T : UnityEngine.Object
    {
        return await assets.LoadAssetAsync<T>(location, System.Threading.CancellationToken.None);
    }
}
