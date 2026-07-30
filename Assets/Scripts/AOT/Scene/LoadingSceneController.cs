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

            SetText("LoadingHintText", $"{targetScene}");
            SetProgress(0f);

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
                SetProgress(Mathf.Clamp01(operation.progress / 0.9f));
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
            var target = GameObject.Find(objectName);
            if (target != null && target.TryGetComponent<Text>(out var textComponent))
            {
                textComponent.text = text;
            }
        }

        /// <summary>
        /// 设置加载进度条的值。
        /// </summary>
        private void SetProgress(float value)
        {
            var target = GameObject.Find("LoadingProgressSlider");
            if (target != null && target.TryGetComponent<Slider>(out var slider))
            {
                slider.value = value;
            }
        }
    }
