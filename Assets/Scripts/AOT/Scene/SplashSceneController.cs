using UnityEngine;
using UnityEngine.SceneManagement;

    /// <summary>
    /// 闪屏场景控制器，完成最短展示时间后进入加载或主流程。
    /// </summary>
    public sealed class SplashSceneController : MonoBehaviour
    {
        [SerializeField] private string nextSceneName = "Main";
        [SerializeField] private float durationSeconds = 2.0f;
        [SerializeField] private bool allowSkip = true;

        private float startedAt;
        private bool loadingNext;

        /// <summary>
        /// 组件唤醒时执行的初始化逻辑。
        /// </summary>
        private void Awake()
        {
            startedAt = Time.unscaledTime;
        }

        /// <summary>
        /// 每帧更新逻辑。
        /// </summary>
        private void Update()
        {
            if (loadingNext)
            {
                return;
            }

            if (allowSkip && (Input.GetMouseButtonDown(0) || Input.touchCount > 0 || Input.GetKeyDown(KeyCode.Space)))
            {
                LoadNextScene();
                return;
            }

            if (Time.unscaledTime - startedAt >= durationSeconds)
            {
                LoadNextScene();
            }
        }

        /// <summary>
        /// 加载下一个场景。
        /// </summary>
        private void LoadNextScene()
        {
            if (loadingNext)
            {
                return;
            }

            loadingNext = true;
            if (!string.IsNullOrEmpty(nextSceneName))
            {
                SceneManager.LoadScene(nextSceneName);
            }
        }
    }
