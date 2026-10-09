using System;
using UnityEngine;

    /// <summary>
    /// 保存本地账号的体力、解锁、每日福利和人机奖励状态。
    /// </summary>
    [Serializable]
    public sealed class LocalAccountState
    {
        public StaminaStateData stamina = new StaminaStateData();
        public UnlockProgressData unlock = new UnlockProgressData();
        public string dayKey;
        public int dailyBattleCount;
        public bool sharedToday;
        public bool claimedShareStamina;
        public bool claimedThreeBattleStamina;
        public bool claimedSidebarStamina;
        public int staminaAdCount;
        public int pveSkipCards;
        public int pveWinCount;
        public int pveLoseCount;
        public int pveRewardPending;
        public int pvpRankScore;
        public int pvpMatchCount;
    }

    /// <summary>
    /// 管理本地账号进度、体力消耗、每日奖励、跳过卡和 PVE 奖励。
    /// </summary>
    public sealed class LocalAccountService
    {
        private const string Key = "NCG.PlayerData";

        private readonly BalanceConfig cfg;
        private LocalAccountState state;
        private static LocalAccountState cloudState;
        private static bool cloudAccountRequired;
        private static System.Threading.Tasks.Task cloudSaveTail = System.Threading.Tasks.Task.CompletedTask;

        public static void RequireCloudAccount(bool required)
        {
            cloudAccountRequired = required;
        }

        public static void SetCloudState(LocalAccountState value)
        {
            if (value != null)
                Ensure(value);
            cloudState = value;
        }

        /// <summary>
        /// 构造本地账号服务实例。
        /// </summary>
        public LocalAccountService(BalanceConfig cfg)
        {
            this.cfg = cfg;
            state = Load();
            RefreshDay();
        }

        public LocalAccountState State
        {
            get
            {
                RefreshDay();
                return state;
            }
        }

        public int Stamina => State.stamina.currentStamina;
        public int MaxStamina => State.stamina.maxStamina;
        public int SkipCards => State.pveSkipCards;
        public int PendingPveReward => State.pveRewardPending;
        public UnlockProgressData UnlockProgress => State.unlock;
        public int PvpRankScore => Mathf.Max(0, State.pvpRankScore);
        public bool HasPvpRank => State.pvpMatchCount > 0;
        public string PvpRankDisplay => HasPvpRank ? $"{PvpRankScore}分" : "无";

        /// <summary>
        /// 尝试消耗指定数量的体力。
        /// </summary>
        public bool TrySpendStamina(int amount)
        {
            RefreshDay();
            if (!StaminaCalculator.TrySpend(state.stamina, amount))
            {
                Save();
                return false;
            }

            Save();
            return true;
        }

        /// <summary>
        /// 增加玩家体力。
        /// </summary>
        public int AddStamina(int amount)
        {
            RefreshDay();
            var added = StaminaCalculator.AddReward(state.stamina, amount, cfg != null ? cfg.maxStamina : 15);
            Save();
            return added;
        }

        /// <summary>
        /// 标记当日已分享。
        /// </summary>
        public void MarkShared()
        {
            RefreshDay();
            state.sharedToday = true;
            Save();
        }

        /// <summary>
        /// 领取分享奖励体力。
        /// </summary>
        public bool ClaimShareStamina(int amount, out int added)
        {
            RefreshDay();
            added = 0;
            if (!state.sharedToday || state.claimedShareStamina)
            {
                return false;
            }

            added = AddStamina(amount);
            state.claimedShareStamina = true;
            Save();
            return true;
        }

        /// <summary>
        /// 领取三场战斗奖励体力。
        /// </summary>
        public bool ClaimThreeBattleStamina(int amount, out int added)
        {
            RefreshDay();
            added = 0;
            if (state.dailyBattleCount < 3 || state.claimedThreeBattleStamina)
            {
                return false;
            }

            added = AddStamina(amount);
            state.claimedThreeBattleStamina = true;
            Save();
            return true;
        }

        public bool HasClaimedSidebarStamina => State.claimedSidebarStamina;

        /// <summary>
        /// 领取从抖音侧边栏回访后的每日体力奖励。
        /// </summary>
        public bool ClaimSidebarStamina(int amount, out int added)
        {
            RefreshDay();
            added = 0;
            if (state.claimedSidebarStamina)
            {
                return false;
            }

            added = AddStamina(amount);
            state.claimedSidebarStamina = true;
            Save();
            return true;
        }

        /// <summary>
        /// 领取广告跳过的强化卡。
        /// </summary>
        public bool ClaimSkipCardByAd(int dailyLimit)
        {
            RefreshDay();
            if (dailyLimit > 0 && state.staminaAdCount >= dailyLimit)
            {
                return false;
            }

            state.staminaAdCount++;
            state.pveSkipCards++;
            Save();
            return true;
        }

        /// <summary>
        /// 尝试使用一张跳过的强化卡。
        /// </summary>
        public bool TryUseSkipCard()
        {
            RefreshDay();
            if (state.pveSkipCards <= 0)
            {
                return false;
            }

            state.pveSkipCards--;
            RecordPveResult(true, true);
            Save();
            return true;
        }

        /// <summary>
        /// 记录 PvE 对战结果。
        /// </summary>
        public void RecordPveResult(bool win, bool skipped)
        {
            RefreshDay();
            state.dailyBattleCount++;
            if (win)
            {
                state.pveWinCount++;
                state.pveRewardPending++;
                PveCampaignService.RecordNormalWin(state.unlock);
                state.unlock.nextUnlockType = ResolveNextUnlockType();
            }
            else
            {
                state.pveLoseCount++;
            }

            Save();
        }

        public string RecordPveBossResult(bool win)
        {
            RefreshDay();
            state.dailyBattleCount++;
            if (win)
            {
                state.pveWinCount++;
                state.pveRewardPending++;
                var rewardId = PveCampaignService.RecordBossWin(state.unlock);
                Save();
                return rewardId;
            }

            state.pveLoseCount++;
            Save();
            return string.Empty;
        }

        public int RecordPvpResult(bool win)
        {
            RefreshDay();
            state.pvpMatchCount++;
            state.pvpRankScore = Mathf.Max(0, state.pvpRankScore + (win ? 10 : -10));
            Save();
            SubmitPvpRankScore();
            return state.pvpRankScore;
        }

        private void SubmitPvpRankScore()
        {
            var sdk = DouyinTTSDKService.Instance;
            if (sdk == null || !sdk.CanUsePlatformFeatures)
                return;

            var data = new TTSDK.UNBridgeLib.LitJson.JsonData();
            data["dataType"] = 0;
            data["value"] = state.pvpRankScore.ToString();
            data["priority"] = 0;
            data["zoneId"] = "default";
            TTSDK.TT.SetImRankData(data, (success, message) =>
            {
                if (!success)
                    Debug.LogWarning($"上传 PVP 段位分失败：{message}");
            });
        }

        /// <summary>
        /// 领取 PvE 奖励。
        /// </summary>
        public bool ClaimPveReward(out string msg)
        {
            RefreshDay();
            if (state.pveRewardPending <= 0)
            {
                msg = "No pending PVE reward.";
                return false;
            }

            state.pveRewardPending--;
            msg = "Claimed PVE reward.";
            Save();
            return true;
        }

        /// <summary>
        /// 解析下一次解锁的奖励类型。
        /// </summary>
        private UnlockRewardType ResolveNextUnlockType()
        {
            var owned = state.unlock.platinumIds.Count + state.unlock.prismaticIds.Count;
            return owned % 3 == 2 ? UnlockRewardType.Prismatic : UnlockRewardType.Platinum;
        }

        /// <summary>
        /// 刷新每日数据。
        /// </summary>
        private void RefreshDay()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");
            if (state.dayKey == today)
            {
                return;
            }

            state.dayKey = today;
            state.dailyBattleCount = 0;
            state.sharedToday = false;
            state.claimedShareStamina = false;
            state.claimedThreeBattleStamina = false;
            state.claimedSidebarStamina = false;
            state.staminaAdCount = 0;
            state.stamina.maxStamina = cfg != null ? cfg.maxStamina : Math.Max(1, state.stamina.maxStamina);
            if (state.stamina.updatedAtUnixMs <= 0)
            {
                state.stamina.updatedAtUnixMs = UnixTimeUtility.NowMilliseconds();
            }

            StaminaCalculator.ClaimDailyRecovery(state.stamina, cfg, DateTime.UtcNow);
            Save();
        }

        /// <summary>
        /// 从本地存储加载账号数据。
        /// </summary>
        private static LocalAccountState Load()
        {
            if (cloudState != null)
            {
                return cloudState;
            }

            var sdk = DouyinTTSDKService.Instance;
            if (cloudAccountRequired)
            {
                var cloudOnlyState = new LocalAccountState();
                Ensure(cloudOnlyState);
                return cloudOnlyState;
            }

            if (sdk != null && sdk.CanUsePlatformFeatures)
            {
                try
                {
                    var cloudState = sdk.Load<LocalAccountState>(Key);
                    if (cloudState != null)
                    {
                        Ensure(cloudState);
                        return cloudState;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"读取 TTSDK 存档失败，将使用本地存档：{exception.Message}");
                }
            }

            var json = PlayerPrefs.GetString(Key, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var loaded = JsonUtility.FromJson<LocalAccountState>(json);
                    if (loaded != null)
                    {
                        Ensure(loaded);
                        return loaded;
                    }
                }
                catch (ArgumentException)
                {
                }
            }

            var fresh = new LocalAccountState();
            Ensure(fresh);
            return fresh;
        }

        /// <summary>
        /// 将账号数据保存到本地存储。
        /// </summary>
        private void Save()
        {
            if (cloudAccountRequired && cloudState == null)
            {
                Debug.LogError("UOS CRUD Save 尚未载入账号数据，拒绝覆盖云端存档。");
                return;
            }

            if (UosCrudSaveService.Instance != null && UosCrudSaveService.Instance.IsReady)
            {
                cloudState = state;
                var snapshot = JsonUtility.FromJson<LocalAccountState>(JsonUtility.ToJson(state));
                var service = UosCrudSaveService.Instance;
                cloudSaveTail = SaveToCloudAsync(cloudSaveTail, service, service.UserId, snapshot);
                return;
            }

            if (cloudAccountRequired)
            {
                Debug.LogError("UOS CRUD Save 未就绪，正式账号数据不会写入本地存储。");
                return;
            }

            var sdk = DouyinTTSDKService.Instance;
            if (sdk != null && sdk.CanUsePlatformFeatures)
            {
                try
                {
                    if (sdk.Save(state, Key))
                        return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"写入 TTSDK 存档失败，将回退到本地存档：{exception.Message}");
                }
            }

            PlayerPrefs.SetString(Key, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
        }

        private static async System.Threading.Tasks.Task SaveToCloudAsync(System.Threading.Tasks.Task previous,
            UosCrudSaveService service, string userId, LocalAccountState value)
        {
            try
            {
                await previous;
                if (service == null || !service.IsReady || service.UserId != userId)
                    return;
                await service.UpdateAccountAsync(value);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"写入 UOS CRUD Save 失败：{exception.Message}");
            }
        }

        /// <summary>
        /// 确保账号数据已初始化。
        /// </summary>
        private static void Ensure(LocalAccountState s)
        {
            s.stamina ??= new StaminaStateData();
            s.unlock ??= new UnlockProgressData();
            s.unlock.platinumIds ??= new System.Collections.Generic.List<int>();
            s.unlock.prismaticIds ??= new System.Collections.Generic.List<int>();
            s.unlock.unlockedSkillCardIds ??= new System.Collections.Generic.List<string>();
            if (s.stamina.maxStamina <= 0)
            {
                s.stamina.maxStamina = 15;
            }

            if (s.stamina.updatedAtUnixMs <= 0)
            {
                s.stamina.currentStamina = s.stamina.maxStamina;
                s.stamina.updatedAtUnixMs = UnixTimeUtility.NowMilliseconds();
            }
        }
    }
