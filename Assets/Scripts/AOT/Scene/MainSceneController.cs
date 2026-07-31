using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

    /// <summary>
    /// 主界面控制器，管理 PVE、匹配、好友房、福利、设置和玩家信息入口。
    /// </summary>
    public sealed class MainSceneController : MonoBehaviour
    {
        [SerializeField] private int maxDailyAdStaminaRefills = 3;
        [SerializeField] private int shareStaminaReward = 1;
        [SerializeField] private int threeBattleStaminaReward = 2;
        [SerializeField] private BalanceConfig balanceConfig;

        private readonly Dictionary<string, GameObject> objs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private LocalAccountService account;
        private GameAudioService audioService;
        private float taskRefreshTimer;
        private string lastTaskProgressKey;
        private bool adRequestInProgress;

        private static readonly string[] Panels =
        {
            "MatchRoomPanel",
            "FriendRoomPanel",
            "SettingsPanel",
            "LeaderboardPanel",
            "DailyWelfarePanel",
            "StaminaLimitTipPanel",
            "PlayerInfoPanel",
            "NameEditPanel",
            "AvatarSelectPanel",
            "RedeemPanel"
        };

        /// <summary>
        /// 组件唤醒时执行的初始化逻辑。
        /// </summary>
        private void Awake()
        {
            IndexObjs();
            InitLocal();
            audioService = GameAudioService.Ensure();
            HideAllPanels();
            RefreshUnlockTask();
        }

        /// <summary>
        /// 组件启用时的回调。
        /// </summary>
        private void OnEnable()
        {
            if (objs.Count > 0)
            {
                RefreshUnlockTask();
            }
        }

        private void Update()
        {
            taskRefreshTimer -= Time.unscaledDeltaTime;
            if (taskRefreshTimer <= 0f)
            {
                taskRefreshTimer = 0.5f;
                RefreshUnlockTask();
            }
        }

        /// <summary>
        /// 索引场景中所有绑定的对象引用。
        /// </summary>
        private void IndexObjs()
        {
            objs.Clear();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!objs.ContainsKey(t.name))
                    {
                        objs.Add(t.name, t.gameObject);
                    }
                }
            }
        }

        /// <summary>
        /// 开始 PvE 人机对战。
        /// </summary>
        public void StartPveBattle()
        {
            PlayerPrefs.SetString(LoadingSceneController.TargetScenePrefsKey, "Battle");
            SceneManager.LoadScene("Loding");
        }

        public void OnStaminaRefillRequested()
        {
            ShowRewardedAd("stamina_refill", () => account.AddStamina(1));
        }

        public void ClaimDailyRewardSlot1()
        {
            ShowRewardedAd("daily_reward_skip_card", () => account.ClaimSkipCardByAd(maxDailyAdStaminaRefills));
        }

        public void ClaimDailyRewardSlot3()
        {
            ShowRewardedAd("daily_reward_stamina", () => account.AddStamina(1));
        }

        private void ShowRewardedAd(string adSceneId, Action onReward)
        {
            if (adRequestInProgress)
                return;

            var sdk = DouyinTTSDKService.Instance;
            if (sdk == null || !sdk.IsInitialized || !sdk.IsLoggedIn)
            {
                Debug.LogWarning($"无法播放激励视频（TTSDK 未完成登录）：{adSceneId}");
                return;
            }

            adRequestInProgress = true;
            sdk.ShowRewardedVideo((completed, rewardType) =>
            {
                adRequestInProgress = false;
                if (completed)
                    onReward?.Invoke();
                else
                    Debug.Log("激励视频未完整播放，不发放奖励。");
            }, (errorCode, errorMessage) =>
            {
                adRequestInProgress = false;
                Debug.LogWarning($"激励视频播放失败（{adSceneId}）：{errorCode} {errorMessage}");
            });
        }
        
        

        /// <summary>
        /// 确认选择头像。
        /// </summary>
        public void ConfirmAvatar() => HidePanel("AvatarSelectPanel");
        /// <summary>
        /// 确认兑换码。
        /// </summary>
        public void ConfirmRedeem() => HidePanel("RedeemPanel");

        /// <summary>

        /// <summary>
        /// 显示独占面板。
        /// </summary>
        public void ShowExclusivePanel(string panelName)
        {
            HideAllPanels();
            ShowPanel(panelName);
        }

        /// <summary>
        /// 显示指定面板。
        /// </summary>
        public void ShowPanel(string panelName)
        {
            if (objs.TryGetValue(panelName, out var panel))
            {
                panel.SetActive(true);
            }
        }

        /// <summary>
        /// 隐藏指定面板。
        /// </summary>
        public void HidePanel(string panelName)
        {
            if (objs.TryGetValue(panelName, out var panel))
            {
                panel.SetActive(false);
            }
        }
        

        /// <summary>
        /// 隐藏所有面板。
        /// </summary>
        private void HideAllPanels()
        {
            for (var i = 0; i < Panels.Length; i++)
            {
                HidePanel(Panels[i]);
            }
        }

        /// <summary>
        /// 设置对象的激活状态。
        /// </summary>
        private void SetActive(string objectName, bool active)
        {
            if (objs.TryGetValue(objectName, out var target))
            {
                target.SetActive(active);
            }
        }

        private void RefreshSoundSetting()
        {
            if (audioService == null)
                return;

            SetText("SoundLabelText", $"Sound {Mathf.RoundToInt(audioService.Volume * 100f)}%");
            if (objs.TryGetValue("SoundSliderTrack", out var target))
            {
                var slider = target.GetComponent<Slider>();
                if (slider != null && Mathf.Abs(slider.value - audioService.Volume) > 0.001f)
                    slider.SetValueWithoutNotify(audioService.Volume);
            }
        }

        /// <summary>
        /// 初始化本地数据。
        /// </summary>
        private void InitLocal()
        {
            if (balanceConfig == null)
            {
                balanceConfig = ScriptableObject.CreateInstance<BalanceConfig>();
            }

            account = new LocalAccountService(balanceConfig);
        }

        private void RefreshUnlockTask()
        {
            if (account == null)
            {
                return;
            }

            var progress = account.UnlockProgress;
            var key = $"{PveCampaignService.GetBossWinsCompleted(progress)}:{PveCampaignService.GetNormalWinsSinceBoss(progress)}:{progress.unlockedSkillCardIds?.Count ?? 0}";
            if (key == lastTaskProgressKey)
            {
                return;
            }

            lastTaskProgressKey = key;
            var task = PveTaskSystem.GetCurrentTask(progress);
            SetText("UnlockTargetText", task.targetText);
            SetText("UnlockHintText", task.hintText);
        }
        
        

        /// <summary>
        /// 按对象名查找组件，找不到对象或组件时返回空。
        /// </summary>
        private T FindComponent<T>(string objectName) where T : Component
        {
            return objs.TryGetValue(objectName, out var target) ? target.GetComponent<T>() : null;
        }

        /// <summary>
        /// 设置 UI 文本内容。
        /// </summary>
        private void SetText(string objectName, string value)
        {
            var text = FindComponent<Text>(objectName);
            if (text != null && text.text != value)
            {
                text.text = value;
            }
        }
    }
