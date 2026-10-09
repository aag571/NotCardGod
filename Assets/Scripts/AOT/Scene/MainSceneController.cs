using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using Unity.Muninn.Model;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Networking;

    /// <summary>
    /// 主界面控制器，管理 PVE、匹配、好友房、福利、设置和玩家信息入口。
    /// </summary>
    public sealed class MainSceneController : MonoBehaviour
    {
        [SerializeField] private int maxDailyAdStaminaRefills = 3;
        [SerializeField] private int shareStaminaReward = 1;
        [SerializeField] private int threeBattleStaminaReward = 2;
        [SerializeField] private int sidebarStaminaReward = 8;
        [SerializeField] private BalanceConfig balanceConfig;

        private readonly Dictionary<string, GameObject> objs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private LocalAccountService account;
        private GameAudioService audioService;
        private float taskRefreshTimer;
        private string lastTaskProgressKey;
        private bool adRequestInProgress;
        private bool sidebarNavigationSupported;
        private bool sidebarAvailabilityRequested;
        private bool sidebarAvailabilityResolved;
        private UosOnlineService onlineService;
        private DouyinTTSDKService douyinSdk;
        private string loadedLocalAvatarUrl;
        private string loadedOpponentAvatarUrl;
        private bool matchingAnimationActive;
        private float matchingAnimationTimer;
        private int matchingDotCount;
        private bool pendingFriendInvite;
        private bool matchmakingFlowActive;
        private bool enteringOnlineBattle;
        private bool rankRequestInProgress;
        private readonly List<Text> friendRankRows = new List<Text>();
        private readonly List<Text> worldRankRows = new List<Text>();
        private int selectedAvatarIndex = 1;
        private GameObject mainFieldGuidePanel;
        private FieldGuidePanelPresenter mainFieldGuidePresenter;
        private CardCatalog mainFieldGuideCatalog;

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
            "RedeemPanel",
            "RewardPanel",
            "CardCollectionPanel",
            "TutorialPanel"
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
            BindStaticSupportButtons();
            RefreshSidebarReward();
            StartCoroutine(LoadMainFieldGuide());
            if (account.PendingPveReward > 0)
                ShowPanel("RewardPanel");
            douyinSdk = DouyinTTSDKService.Instance;
            if (douyinSdk != null)
            {
                douyinSdk.UserInfoUpdated += OnUserInfoUpdated;
                douyinSdk.ApplicationVisibilityChanged += OnDouyinApplicationVisibilityChanged;
            }
            RefreshLocalIdentity();
        }

        private void OnDestroy()
        {
            if (douyinSdk != null)
            {
                douyinSdk.UserInfoUpdated -= OnUserInfoUpdated;
                douyinSdk.ApplicationVisibilityChanged -= OnDouyinApplicationVisibilityChanged;
            }
            UnsubscribeOnlineEvents();
        }

        private void OnDouyinApplicationVisibilityChanged(bool visible)
        {
            if (visible)
                RefreshSidebarReward();
        }

        /// <summary>
        /// 组件启用时的回调。
        /// </summary>
        private void OnEnable()
        {
            if (objs.Count > 0)
            {
                RefreshUnlockTask();
                RefreshSidebarReward();
            }
        }

        private void Update()
        {
            UpdateMatchingAnimation();
            taskRefreshTimer -= Time.unscaledDeltaTime;
            if (taskRefreshTimer <= 0f)
            {
                taskRefreshTimer = 0.5f;
                RefreshUnlockTask();
                RefreshStaminaText();
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
            Debug.Log("开始进入人机对战");
            if (account == null || !account.TrySpendStamina(1))
            {
                ShowPanel("StaminaLimitTipPanel");
                RefreshStaminaText();
                return;
            }
            RefreshStaminaText();
            PlayerPrefs.SetInt("NotCardGod.Battle.Online", 0);
            PlayerPrefs.SetString(LoadingSceneController.TargetScenePrefsKey, "Battle");
            PlayerPrefs.Save();
            SceneManager.LoadScene("Loding");
        }

        public void StartTutorial()
        {
            PlayerPrefs.SetInt("NotCardGod.Tutorial.Active", 1);
            PlayerPrefs.SetInt("NotCardGod.Battle.Online", 0);
            PlayerPrefs.SetString(LoadingSceneController.TargetScenePrefsKey, "Battle");
            PlayerPrefs.Save();
            SceneManager.LoadScene("Loding");
        }

        public void OpenMatchRoom()
        {
            StopMatchingAnimation();
            matchmakingFlowActive = true;
            enteringOnlineBattle = false;
            ShowExclusivePanel("MatchRoomPanel");
            onlineService = UosOnlineService.Instance;
            if (onlineService == null)
            {
                SetText("MatchingStatusText", "联机服务尚未初始化");
                Debug.LogWarning("玩家对战无法开始：UOS 联机服务不存在");
                return;
            }

            // UosOnlineService survives scene changes. Clear the previous battle's
            // ticket/room before registering this matchmaking flow's callbacks.
            onlineService.ResetMatchmakingSession();
            onlineService.Error -= OnOnlineError;
            onlineService.Error += OnOnlineError;
            onlineService.MatchStateChanged -= OnMatchStateChanged;
            onlineService.MatchStateChanged += OnMatchStateChanged;
            onlineService.RoomJoined -= OnRoomJoined;
            onlineService.RoomJoined += OnRoomJoined;
            onlineService.PlayerJoined -= OnRoomPlayerChanged;
            onlineService.PlayerJoined += OnRoomPlayerChanged;
            onlineService.PlayerLeft -= OnRoomPlayerChanged;
            onlineService.PlayerLeft += OnRoomPlayerChanged;
            RefreshLocalIdentity();
            RefreshRoomPlayers();
            SetText("MatchingStatusText", "正在连接玩家对战服务器...");
            onlineService.StartMatchmaking();
        }

        public void CloseMatchRoom()
        {
            StopMatchingAnimation();
            matchmakingFlowActive = false;
            enteringOnlineBattle = false;
            if (onlineService != null)
            {
                UnsubscribeOnlineEvents();
                onlineService.StopMatchmaking();
                onlineService.LeaveCurrentRoom();
            }
            HidePanel("MatchRoomPanel");
        }

        private void OnMatchStateChanged(UosMatchState state)
        {
            if (state == UosMatchState.Creating || state == UosMatchState.Waiting)
            {
                StartMatchingAnimation();
                return;
            }

            StopMatchingAnimation();
            var text = state == UosMatchState.Creating ? "正在创建匹配..." :
                state == UosMatchState.Waiting ? "匹配中，请等待其他玩家..." :
                state == UosMatchState.Matched ? "匹配成功，正在进入对局..." :
                state == UosMatchState.Cancelled ? "匹配已取消" :
                state == UosMatchState.Failed ? "匹配失败" : string.Empty;
            if (!string.IsNullOrEmpty(text))
                SetText("MatchingStatusText", text);
        }

        private void StartMatchingAnimation()
        {
            matchingAnimationActive = true;
            matchingAnimationTimer = 0f;
            matchingDotCount = 1;
            SetText("MatchingStatusText", "匹配.");
        }

        private void StopMatchingAnimation()
        {
            matchingAnimationActive = false;
            matchingAnimationTimer = 0f;
            matchingDotCount = 0;
        }

        private void UpdateMatchingAnimation()
        {
            if (!matchingAnimationActive)
                return;

            matchingAnimationTimer -= Time.unscaledDeltaTime;
            if (matchingAnimationTimer > 0f)
                return;

            matchingAnimationTimer = 0.45f;
            matchingDotCount = matchingDotCount >= 3 ? 1 : matchingDotCount + 1;
            SetText("MatchingStatusText", "匹配" + new string('.', matchingDotCount));
        }

        private void OnOnlineError(string message)
        {
            StopMatchingAnimation();
            SetText("MatchingStatusText", message);
            Debug.LogError($"玩家对战失败：{message}");
        }

        private void OnUserInfoUpdated(TTUserInfo userInfo)
        {
            RefreshLocalIdentity();
            onlineService?.RefreshLocalPlayerInfo();
            RefreshRoomPlayers();
        }

        private void OnRoomJoined(MuninnRoomView roomView)
        {
            RefreshRoomPlayers();
            SetText("FriendInviteCodeText", $"邀请码：{onlineService?.CurrentJoinCode}");
            if (onlineService == null || !onlineService.IsInRoom)
                return;

            if (pendingFriendInvite)
            {
                pendingFriendInvite = false;
                onlineService.InviteFriend();
                return;
            }

            // Matchmaking reaches Matched before the room WebSocket join completes.
            // The room-joined callback is therefore the first safe point at which
            // both players can enter the battle scene.
            if (matchmakingFlowActive && !enteringOnlineBattle &&
                onlineService.CurrentRoom != null && onlineService.CurrentRoom.Players.Count == 2)
            {
                if (account == null || !account.TrySpendStamina(1))
                {
                    matchmakingFlowActive = false;
                    enteringOnlineBattle = false;
                    SetText("MatchingStatusText", "体力不足，无法开始玩家对战");
                    ShowPanel("StaminaLimitTipPanel");
                    RefreshStaminaText();
                    onlineService.StopMatchmaking();
                    return;
                }

                enteringOnlineBattle = true;
                matchmakingFlowActive = false;
                RefreshStaminaText();
                PlayerPrefs.SetInt("NotCardGod.Battle.Online", 1);
                PlayerPrefs.SetString(LoadingSceneController.TargetScenePrefsKey, "Battle");
                PlayerPrefs.Save();
                Debug.Log($"[UOS] 普通匹配已入房，开始加载 Battle 场景：roomId={onlineService.CurrentRoomId}");
                SceneManager.LoadScene("Loding");
            }
        }

        private void OnRoomPlayerChanged(MuninnPlayer player)
        {
            RefreshRoomPlayers();
            if (matchmakingFlowActive && onlineService?.CurrentRoom != null &&
                onlineService.CurrentRoom.Players.Count == 2)
            {
                OnRoomJoined(onlineService.CurrentRoom);
            }
        }

        private void UnsubscribeOnlineEvents()
        {
            if (onlineService == null)
                return;
            onlineService.Error -= OnOnlineError;
            onlineService.MatchStateChanged -= OnMatchStateChanged;
            onlineService.RoomJoined -= OnRoomJoined;
            onlineService.PlayerJoined -= OnRoomPlayerChanged;
            onlineService.PlayerLeft -= OnRoomPlayerChanged;
        }

        private void RefreshLocalIdentity()
        {
            var sdk = DouyinTTSDKService.Instance;
            var name = sdk != null && !string.IsNullOrWhiteSpace(sdk.DisplayName)
                ? sdk.DisplayName
                : sdk != null && !string.IsNullOrWhiteSpace(sdk.UserId) ? sdk.UserId : "游客";
            SetText("Name", name);
            SetText("MatchLocalNameText", name);
            SetText("FriendLocalNameText", name);
            SetText("PlayerName", name);
            SetText("PlayerNameText", $"{name}\n段位：{(account != null ? account.PvpRankDisplay : "无")}");
            var avatarUrl = sdk?.AvatarUrl ?? string.Empty;
            if (!string.Equals(avatarUrl, loadedLocalAvatarUrl, StringComparison.Ordinal))
            {
                loadedLocalAvatarUrl = avatarUrl;
                StartCoroutine(LoadAvatarInto("Photo", avatarUrl));
                StartCoroutine(LoadAvatarInto("PlayerAvatarEditButton", avatarUrl));
                StartCoroutine(LoadAvatarInto("MatchLocalAvatarImage", avatarUrl));
                StartCoroutine(LoadAvatarInto("FriendLocalAvatarImage", avatarUrl));
            }
        }

        private void RefreshRoomPlayers()
        {
            if (onlineService == null || !onlineService.IsInRoom || onlineService.CurrentRoom == null)
                return;

            var room = onlineService.CurrentRoom;
            var self = room.Self();
            MuninnPlayer opponent = null;
            var players = room.Players;
            for (var index = 0; index < players.Count; index++)
            {
                if (self == null || players[index].SenderId != self.SenderId)
                {
                    opponent = players[index];
                    break;
                }
            }

            var localName = self != null && !string.IsNullOrWhiteSpace(self.Name) ? self.Name : onlineService.LocalDisplayName;
            SetText("MatchLocalNameText", localName);
            SetText("FriendLocalNameText", localName);
            var opponentName = opponent != null && !string.IsNullOrWhiteSpace(opponent.Name) ? opponent.Name : "等待对手";
            SetText("MatchOpponentPlaceholderText", opponentName);
            SetText("FriendOpponentPlaceholderText", opponentName);
            var opponentAvatar = GetPlayerProperty(opponent, "avatarUrl");
            if (!string.Equals(opponentAvatar, loadedOpponentAvatarUrl, StringComparison.Ordinal))
            {
                loadedOpponentAvatarUrl = opponentAvatar;
                StartCoroutine(LoadAvatarInto("MatchOpponentAvatarPlaceholder", opponentAvatar));
                StartCoroutine(LoadAvatarInto("FriendOpponentAvatarPlaceholder", opponentAvatar));
            }
        }

        private static string GetPlayerProperty(MuninnPlayer player, string key)
        {
            if (player?.Properties != null && player.Properties.TryGetValue(key, out var value))
                return value ?? string.Empty;
            return string.Empty;
        }

        private IEnumerator LoadAvatarInto(string objectName, string url)
        {
            var image = FindComponent<Image>(objectName);
            if (image == null || string.IsNullOrWhiteSpace(url))
                yield break;

            using (var request = UnityWebRequestTexture.GetTexture(url))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"头像加载失败: {objectName}, {request.error}");
                    yield break;
                }

                var texture = DownloadHandlerTexture.GetContent(request);
                if (texture == null)
                    yield break;
                image.sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                image.color = Color.white;
            }
        }

        public void OpenFriendRoom()
        {
            ShowExclusivePanel("FriendRoomPanel");
            onlineService = UosOnlineService.Instance;
            if (onlineService == null || !onlineService.IsReady)
            {
                SetText("FriendInviteCodeText", "联机服务尚未就绪");
                return;
            }

            onlineService.RoomJoined -= OnRoomJoined;
            onlineService.RoomJoined += OnRoomJoined;
            onlineService.PlayerJoined -= OnRoomPlayerChanged;
            onlineService.PlayerJoined += OnRoomPlayerChanged;
            onlineService.PlayerLeft -= OnRoomPlayerChanged;
            onlineService.PlayerLeft += OnRoomPlayerChanged;
            if (!onlineService.IsInRoom)
                onlineService.CreatePrivateRoom(null);
            RefreshRoomPlayers();
        }

        public void CloseFriendRoom()
        {
            onlineService?.LeaveCurrentRoom();
            HidePanel("FriendRoomPanel");
        }

        public void InviteFriend()
        {
            onlineService = UosOnlineService.Instance;
            if (onlineService == null || !onlineService.IsReady)
            {
                SetText("FriendInviteCodeText", "联机服务尚未就绪");
                return;
            }

            if (!onlineService.IsInRoom)
            {
                pendingFriendInvite = true;
                SetText("FriendInviteCodeText", "正在创建好友房间...");
                onlineService.RoomJoined -= OnRoomJoined;
                onlineService.RoomJoined += OnRoomJoined;
                onlineService.CreatePrivateRoom(null);
                return;
            }

            pendingFriendInvite = false;
            onlineService.InviteFriend();
            SetText("FriendInviteCodeText", $"邀请码：{onlineService.CurrentJoinCode}");
        }

        public void ClaimPveReward()
        {
            var message = string.Empty;
            if (account == null || !account.ClaimPveReward(out message))
            {
                SetText("RewardMessageText", string.IsNullOrEmpty(message) ? "暂无可领取奖励" : message);
                return;
            }

            SetText("RewardMessageText", "奖励领取成功");
            lastTaskProgressKey = null;
            RefreshUnlockTask();
        }

        public void OnStaminaRefillRequested()
        {
            ShowRewardedAd("stamina_refill", () =>
            {
                account.AddStamina(1);
                RefreshStaminaText();
            });
        }

        public void ClaimDailyRewardSlot1()
        {
            ShowRewardedAd("daily_reward_skip_card", () => account.ClaimSkipCardByAd(maxDailyAdStaminaRefills));
        }

        public void ClaimDailyRewardSlot3()
        {
            ShowRewardedAd("daily_reward_stamina", () =>
            {
                account.AddStamina(1);
                RefreshStaminaText();
            });
        }

        public void ClaimCeBianLanReward()
        {
            if (account == null || account.HasClaimedSidebarStamina)
            {
                RefreshSidebarReward();
                return;
            }

            var sdk = DouyinTTSDKService.Instance;
            if (sdk != null && sdk.CanUsePlatformFeatures && sdk.IsLaunchedFromSidebar())
            {
                account.ClaimSidebarStamina(sidebarStaminaReward, out _);
                RefreshSidebarReward();
                lastTaskProgressKey = null;
                RefreshUnlockTask();
                return;
            }

            if (sdk != null && sdk.CanUsePlatformFeatures && sidebarNavigationSupported)
            {
                sdk.CheckAndEnterSidebar(null, null, (errorCode, errorMessage) =>
                {
                    Debug.LogWarning($"跳转抖音侧边栏失败：{errorCode} {errorMessage}");
                });
                return;
            }

            RefreshSidebarReward();
        }

        private void RefreshSidebarReward()
        {
            if (account == null)
                return;

            var button = FindComponent<Button>("CeBianLanRewardActionButton");
            if (button == null)
                return;

            if (account.HasClaimedSidebarStamina)
            {
                SetText("CeBianLanRewardActionButton_Label", "已领取");
                button.interactable = false;
                return;
            }

            var sdk = DouyinTTSDKService.Instance;
            if (sdk != null && sdk.CanUsePlatformFeatures && sdk.IsLaunchedFromSidebar())
            {
                SetText("CeBianLanRewardActionButton_Label", "领取");
                button.interactable = true;
                return;
            }

            if (sdk == null || !sdk.CanUsePlatformFeatures)
            {
                SetText("CeBianLanRewardActionButton_Label", "去完成");
                button.interactable = false;
                return;
            }

            if (!sidebarAvailabilityRequested)
            {
                sidebarAvailabilityRequested = true;
                sdk.CheckSidebar(supported =>
                {
                    sidebarNavigationSupported = supported;
                    sidebarAvailabilityResolved = true;
                    RefreshSidebarReward();
                }, null, (errorCode, errorMessage) =>
                {
                    sidebarNavigationSupported = false;
                    sidebarAvailabilityResolved = true;
                    Debug.LogWarning($"侧边栏入口不可用：{errorCode} {errorMessage}");
                    RefreshSidebarReward();
                });
            }

            SetText("CeBianLanRewardActionButton_Label", !sidebarAvailabilityResolved || sidebarNavigationSupported ? "添加" : "去完成");
            button.interactable = sidebarNavigationSupported;
        }

        private void ShowRewardedAd(string adSceneId, Action onReward)
        {
            if (adRequestInProgress)
                return;

            var sdk = DouyinTTSDKService.Instance;
            if (sdk == null || !sdk.CanUsePlatformFeatures)
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
        public void ConfirmAvatar()
        {
            PlayerPrefs.SetInt("NotCardGod.Player.AvatarIndex", selectedAvatarIndex);
            PlayerPrefs.Save();
            HidePanel("AvatarSelectPanel");
        }
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
            if (mainFieldGuidePanel != null)
                mainFieldGuidePanel.SetActive(false);
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

            SetSliderValue("SoundSliderTrack", audioService.BgmVolume);
            SetSliderValue("SfxSliderTrack", audioService.SfxVolume);
            SetSliderValue("BrightnessSliderTrack", PlayerPrefs.GetFloat("NotCardGod.Display.Brightness", 1f));
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
            var pendingReward = account.PendingPveReward;
            var rankScore = account.PvpRankScore;
            var stamina = account.Stamina;
            var maxStamina = account.MaxStamina;
            SetText("DanValue", account.HasPvpRank ? $"{rankScore}分" : "无");
            SetText("PowerValue", $"{stamina}/{maxStamina}");
            var key = $"{PveCampaignService.GetBossWinsCompleted(progress)}:{PveCampaignService.GetNormalWinsSinceBoss(progress)}:{progress.unlockedSkillCardIds?.Count ?? 0}:{pendingReward}:{rankScore}:{stamina}:{maxStamina}";
            if (key == lastTaskProgressKey)
            {
                return;
            }

            lastTaskProgressKey = key;
            var task = PveTaskSystem.GetCurrentTask(progress);
            SetText("UnlockTargetText", task.targetText);
            SetText("UnlockHintText", pendingReward > 0
                ? $"{task.hintText}  待领取奖励：{pendingReward}"
                : task.hintText);
            SetText("RewardMessageText", pendingReward > 0 ? $"有 {pendingReward} 份人机胜利奖励待领取" : "暂无待领取奖励");
            mainFieldGuidePresenter?.SetProgress(progress);
        }

        private void RefreshStaminaText()
        {
            if (account == null)
                return;

            SetText("PowerValue", $"{account.Stamina}/{account.MaxStamina}");
        }

        private void RefreshCollection(UnlockProgressData progress)
        {
            var lines = new List<string>(PveCampaignService.BossCount + 1)
            {
                $"卡牌解锁进度 {PveCampaignService.GetBossWinsCompleted(progress)}/{PveCampaignService.BossCount}"
            };
            for (var index = 0; index < PveCampaignService.BossCount; index++)
            {
                var cardId = PveCampaignService.GetRewardCardId(index);
                var unlocked = PveCampaignService.IsSkillUnlocked(progress, cardId);
                lines.Add($"{index + 1}. {(unlocked ? PveCampaignService.GetRewardCardName(index) : "未解锁")}");
            }

            SetText("CardCollectionText", string.Join("\n", lines));
        }

        private void OpenSettings()
        {
            ShowExclusivePanel("SettingsPanel");
            RefreshSoundSetting();
        }

        private IEnumerator LoadMainFieldGuide()
        {
            IGameAssetService assets = null;
            while (!GameApp.TryGet<IGameAssetService>(out assets))
                yield return null;

            var prefabTask = assets.LoadAssetAsync<GameObject>("FieldGuidePanel", CancellationToken.None);
            var catalogTask = assets.LoadAssetAsync<CardCatalog>("CardCatalog", CancellationToken.None);
            while (!prefabTask.IsCompleted || !catalogTask.IsCompleted)
                yield return null;

            if (prefabTask.Status != System.Threading.Tasks.TaskStatus.RanToCompletion || prefabTask.Result == null)
            {
                Debug.LogError("主菜单图鉴预制体加载失败。", this);
                yield break;
            }

            mainFieldGuideCatalog = ScriptableObject.CreateInstance<CardCatalog>();
            if (catalogTask.Status == System.Threading.Tasks.TaskStatus.RanToCompletion &&
                catalogTask.Result != null && catalogTask.Result.cards != null)
            {
                for (var index = 0; index < catalogTask.Result.cards.Count; index++)
                {
                    var card = catalogTask.Result.cards[index];
                    if (card != null)
                        mainFieldGuideCatalog.cards.Add(card);
                }
            }
            SkillHot.AddTo(mainFieldGuideCatalog);

            var parent = objs.TryGetValue("MainCanvas", out var canvas) ? canvas.transform : transform;
            mainFieldGuidePanel = Instantiate(prefabTask.Result, parent, false);
            mainFieldGuidePanel.name = "MainFieldGuidePanel";
            var rect = mainFieldGuidePanel.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            mainFieldGuidePresenter = mainFieldGuidePanel.AddComponent<FieldGuidePanelPresenter>();
            mainFieldGuidePresenter.Initialize(mainFieldGuideCatalog, account != null ? account.UnlockProgress : null);
            mainFieldGuidePanel.SetActive(false);
        }

        private void OpenMainFieldGuide()
        {
            if (mainFieldGuidePresenter == null)
            {
                Debug.LogWarning("主菜单图鉴资源仍在加载，请稍后重试。", this);
                return;
            }

            HideAllPanels();
            mainFieldGuidePresenter.Open(account != null ? account.UnlockProgress : null);
        }

        private static void ApplyBrightness(float value)
        {
            value = Mathf.Clamp01(value);
            PlayerPrefs.SetFloat("NotCardGod.Display.Brightness", value);
            PlayerPrefs.Save();
            Screen.brightness = Mathf.Lerp(0.25f, 1f, value);
        }

        private void SetSliderValue(string objectName, float value)
        {
            var slider = FindComponent<Slider>(objectName);
            if (slider != null)
                slider.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        private void ShowRankList(bool friends)
        {
            SetActive("DouyinFriendRankListPanel", friends);
            SetActive("WorldRankListPanel", !friends);
            RefreshRankList(friends);
        }

        private void OpenLeaderboard()
        {
            ShowExclusivePanel("LeaderboardPanel");
            ShowRankList(true);
        }

        private void RefreshRankList(bool friends)
        {
            SetText("LeaderboardTitleText", friends ? "好友榜 · 段位分" : "世界榜 · 段位分");
            SetRankRows(friends, null);

            var sdk = DouyinTTSDKService.Instance;
            if (rankRequestInProgress || sdk == null || !sdk.CanUsePlatformFeatures)
                return;

            rankRequestInProgress = true;
            var request = new JsonData();
            request["rankType"] = "all";
            request["dataType"] = 0;
            request["relationType"] = friends ? "friend" : "all";
            request["pageNum"] = 1;
            request["pageSize"] = 10;
            request["zoneId"] = "default";
            TT.GetImRankData(request, (ref TTRank.RankData data) => OnRankDataReceived(friends, ref data), error =>
            {
                rankRequestInProgress = false;
                Debug.LogWarning($"读取段位排行榜失败：{error}");
            });
        }

        private void OnRankDataReceived(bool friends, ref TTRank.RankData data)
        {
            rankRequestInProgress = false;
            SetRankRows(friends, ParseRankEntries(JsonConvert.SerializeObject(data)));
        }

        private void SetRankRows(bool friends, List<RankEntry> entries)
        {
            var rows = EnsureRankRows(friends);
            for (var index = 0; index < rows.Count; index++)
            {
                var text = "暂无已定段玩家";
                if (entries != null && index < entries.Count)
                {
                    var entry = entries[index];
                    text = $"第{index + 1}名  {entry.name}  段位分 {entry.score}";
                }
                else if ((entries == null || entries.Count == 0) && index == 0 && account != null && account.HasPvpRank)
                {
                    text = $"第1名  {GetLocalDisplayName()}  段位分 {account.PvpRankScore}";
                }

                rows[index].text = text;
            }
        }

        private List<Text> EnsureRankRows(bool friends)
        {
            var rows = friends ? friendRankRows : worldRankRows;
            if (rows.Count >= 10)
                return rows;

            var prefix = friends ? "FriendRank" : "WorldRank";
            var panelName = friends ? "DouyinFriendRankListPanel" : "WorldRankListPanel";
            var source = FindComponent<Text>($"{prefix}1Text");
            if (source == null || !objs.ContainsKey(panelName))
                return rows;

            var parent = source.transform.parent as RectTransform;
            if (parent == null)
                return rows;

            for (var index = 0; index < 10; index++)
            {
                Text row;
                var existing = FindComponent<Text>($"{prefix}{index + 1}Text");
                if (existing != null)
                {
                    row = existing;
                }
                else
                {
                    var clone = Instantiate(source.gameObject, parent, false);
                    clone.name = $"{prefix}{index + 1}Text";
                    row = clone.GetComponent<Text>();
                }

                if (row != null && !rows.Contains(row))
                    rows.Add(row);
            }

            var rowHeight = parent.rect.height / 10f;
            for (var index = 0; index < rows.Count; index++)
            {
                var rect = rows[index].rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(0f, parent.rect.height * 0.5f - rowHeight * (index + 0.5f));
                rect.sizeDelta = new Vector2(parent.rect.width, rowHeight);
                rows[index].text = string.Empty;
            }

            return rows;
        }

        private string GetLocalDisplayName()
        {
            var sdk = DouyinTTSDKService.Instance;
            if (sdk != null && !string.IsNullOrWhiteSpace(sdk.DisplayName))
                return sdk.DisplayName;
            if (sdk != null && !string.IsNullOrWhiteSpace(sdk.UserId))
                return sdk.UserId;
            return "游客";
        }

        private static List<RankEntry> ParseRankEntries(JsonData data)
        {
            return data == null ? new List<RankEntry>() : ParseRankEntries(JsonMapper.ToJson(data));
        }

        private static List<RankEntry> ParseRankEntries(string json)
        {
            var entries = new List<RankEntry>();
            if (string.IsNullOrEmpty(json))
                return entries;

            try
            {
                var token = JToken.Parse(json);
                CollectRankEntries(token, entries);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"解析段位排行榜数据失败：{exception.Message}");
            }

            entries.Sort((left, right) => right.score.CompareTo(left.score));
            return entries;
        }

        private static void CollectRankEntries(JToken token, List<RankEntry> entries)
        {
            if (token is JObject obj)
            {
                var scoreToken = FindToken(obj, "value", "score", "rankValue", "rank_score", "pvpRankScore");
                if (scoreToken != null && int.TryParse(scoreToken.ToString(), out var score))
                {
                    var nameToken = FindToken(obj, "nickname", "nickName", "name", "userName", "displayName", "user_id");
                    var name = nameToken != null ? nameToken.ToString() : "玩家";
                    if (!string.IsNullOrWhiteSpace(name))
                        entries.Add(new RankEntry { name = name, score = Mathf.Max(0, score) });
                }

                foreach (var property in obj.Properties())
                    CollectRankEntries(property.Value, entries);
                return;
            }

            if (token is JArray array)
            {
                foreach (var child in array)
                    CollectRankEntries(child, entries);
            }
        }

        private static JToken FindToken(JObject obj, params string[] names)
        {
            for (var index = 0; index < names.Length; index++)
            {
                if (obj.TryGetValue(names[index], StringComparison.OrdinalIgnoreCase, out var token))
                    return token;
            }
            return null;
        }

        private sealed class RankEntry
        {
            public string name;
            public int score;
        }

        private void GiveFriendStamina(int friendIndex)
        {
            SetText($"GiveStaminaFriend{friendIndex}Button_Label", "暂未开放");
            Debug.LogWarning($"好友体力赠送接口尚未配置，未向第 {friendIndex} 位好友提交虚假请求。");
        }

        private void ClaimShareReward()
        {
            account.MarkShared();
            SetText("DailyWelfareRefreshText", account.ClaimShareStamina(shareStaminaReward, out var added)
                ? $"分享奖励已领取：体力 +{added}"
                : "今日分享奖励已经领取");
            RefreshStaminaText();
        }

        private void ClaimThreeBattleReward()
        {
            SetText("DailyWelfareRefreshText", account.ClaimThreeBattleStamina(threeBattleStaminaReward, out var added)
                ? $"三场战斗奖励已领取：体力 +{added}"
                : "完成三场战斗后可领取该奖励");
            RefreshStaminaText();
        }

        private void CopyInviteCode()
        {
            var code = onlineService != null ? onlineService.CurrentJoinCode : string.Empty;
            GUIUtility.systemCopyBuffer = code;
            SetText("PlayerInviteCodeText", string.IsNullOrWhiteSpace(code) ? "暂无邀请码" : $"邀请码已复制：{code}");
        }

        private void ConfirmRename()
        {
            var input = FindComponentInChildren<InputField>("NameEditInputFieldFrame");
            var value = input != null ? input.text.Trim() : string.Empty;
            if (value.Length < 2 || value.Length > 12)
            {
                SetText("NameEditRuleText", "名称长度需要为 2～12 个字符");
                return;
            }
            PlayerPrefs.SetString("NotCardGod.Player.LocalName", value);
            PlayerPrefs.Save();
            SetText("Name", value);
            SetText("PlayerNameText", $"{value}\n段位：{(account != null ? account.PvpRankDisplay : "无")}");
            HidePanel("NameEditPanel");
        }

        private void SelectAvatar(int avatarIndex)
        {
            selectedAvatarIndex = avatarIndex;
            SetText("AvatarSelectTitleText", $"选择头像 · 当前第 {avatarIndex} 个");
        }

        private void BindStaticSupportButtons()
        {
            BindButton("PVE", StartPveBattle);
            BindButton("P2P", OpenMatchRoom);
            BindButton("FriendsP2P", OpenFriendRoom);
            BindButton("SettingBT", OpenSettings);
            BindButton("Photo", () => ShowExclusivePanel("PlayerInfoPanel"));
            BindButton("DailyWelfareButton", () => ShowExclusivePanel("DailyWelfarePanel"));
            BindButton("StaminaRefillButton", OnStaminaRefillRequested);
            BindButton("LeaderboardButton", OpenLeaderboard);
            BindButton("CancelMatchButton", CloseMatchRoom);
            BindButton("ExitMatchRoomButton", CloseMatchRoom);
            BindButton("InviteFriendButton", InviteFriend);
            BindButton("ExitFriendRoomButton", CloseFriendRoom);
            BindButton("CloseSettingsButton", () => HidePanel("SettingsPanel"));
            BindButton("CloseLeaderboardButton", () => HidePanel("LeaderboardPanel"));
            BindButton("DouyinFriendRankTabButton", () => ShowRankList(true));
            BindButton("WorldRankTabButton", () => ShowRankList(false));
            for (var friendIndex = 1; friendIndex <= 4; friendIndex++)
            {
                var capturedIndex = friendIndex;
                BindButton($"GiveStaminaFriend{capturedIndex}Button", () => GiveFriendStamina(capturedIndex));
            }
            BindButton("CloseDailyWelfareButton", () => HidePanel("DailyWelfarePanel"));
            BindButton("DailyRewardSlot1ActionButton", ClaimDailyRewardSlot1);
            BindButton("CeBianLanRewardActionButton", ClaimCeBianLanReward);
            BindButton("DailyRewardSlot2ActionButton", ClaimShareReward);
            BindButton("DailyRewardSlot3ActionButton", ClaimDailyRewardSlot3);
            BindButton("DailyRewardSlot4ActionButton", ClaimThreeBattleReward);
            BindButton("CloseStaminaLimitTipButton", () => HidePanel("StaminaLimitTipPanel"));
            BindButton("ClosePlayerInfoButton", () => HidePanel("PlayerInfoPanel"));
            BindButton("PlayerAvatarEditButton", () => ShowExclusivePanel("AvatarSelectPanel"));
            BindButton("EditPlayerNameButton", () => ShowExclusivePanel("NameEditPanel"));
            BindButton("CopyInviteCodeButton", CopyInviteCode);
            BindButton("OpenRedeemPanelButton", () => ShowExclusivePanel("RedeemPanel"));
            BindButton("CloseNameEditButton", () => HidePanel("NameEditPanel"));
            BindButton("ConfirmRenameButton", ConfirmRename);
            BindButton("CancelRenameButton", () => HidePanel("NameEditPanel"));
            BindButton("CloseAvatarSelectButton", () => HidePanel("AvatarSelectPanel"));
            for (var avatarIndex = 1; avatarIndex <= 6; avatarIndex++)
            {
                var capturedIndex = avatarIndex;
                BindButton($"AvatarOption{capturedIndex}Button", () => SelectAvatar(capturedIndex));
            }
            BindButton("ConfirmAvatarButton", ConfirmAvatar);
            BindButton("CloseRedeemButton", () => HidePanel("RedeemPanel"));
            BindButton("ConfirmRedeemButton", ConfirmRedeem);
            BindButton("CancelRedeemButton", () => HidePanel("RedeemPanel"));
            BindButton("TutorialButton", StartTutorial);
            BindButton("ClaimRewardButton", ClaimPveReward);
            BindButton("CloseRewardPanelButton", () => HidePanel("RewardPanel"));
            BindButton("OpenCollectionButton", OpenMainFieldGuide);
            BindButton("CloseCollectionButton", () => HidePanel("CardCollectionPanel"));
            BindSlider("SoundSliderTrack", audioService.SetBgmVolume);
            BindSlider("SfxSliderTrack", audioService.SetSfxVolume);
            BindSlider("BrightnessSliderTrack", ApplyBrightness);
        }

        private void BindButton(string objectName, UnityEngine.Events.UnityAction action)
        {
            var button = FindComponent<Button>(objectName);
            if (button == null)
                return;

            button.onClick.RemoveAllListeners();
            var persistentTarget = action.Target as UnityEngine.Object;
            for (var index = 0; index < button.onClick.GetPersistentEventCount(); index++)
            {
                if (button.onClick.GetPersistentTarget(index) == persistentTarget &&
                    button.onClick.GetPersistentMethodName(index) == action.Method.Name)
                    return;
            }

            button.onClick.AddListener(action);
        }

        private void BindSlider(string objectName, UnityEngine.Events.UnityAction<float> action)
        {
            var slider = FindComponent<Slider>(objectName);
            if (slider == null)
                return;

            slider.onValueChanged.RemoveAllListeners();
            var persistentTarget = action.Target as UnityEngine.Object;
            for (var index = 0; index < slider.onValueChanged.GetPersistentEventCount(); index++)
            {
                if (slider.onValueChanged.GetPersistentTarget(index) == persistentTarget &&
                    slider.onValueChanged.GetPersistentMethodName(index) == action.Method.Name)
                    return;
            }

            slider.onValueChanged.AddListener(action);
        }
        
        

        /// <summary>
        /// 按对象名查找组件，找不到对象或组件时返回空。
        /// </summary>
        private T FindComponent<T>(string objectName) where T : Component
        {
            return objs.TryGetValue(objectName, out var target) ? target.GetComponent<T>() : null;
        }

        private T FindComponentInChildren<T>(string objectName) where T : Component
        {
            return objs.TryGetValue(objectName, out var target) ? target.GetComponentInChildren<T>(true) : null;
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
