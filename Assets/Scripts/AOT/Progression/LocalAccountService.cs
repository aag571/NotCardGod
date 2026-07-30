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
        public int staminaAdCount;
        public int pveSkipCards;
        public int pveWinCount;
        public int pveLoseCount;
        public int pveRewardPending;
    }

    /// <summary>
    /// 管理本地账号进度、体力消耗、每日奖励、跳过卡和 PVE 奖励。
    /// </summary>
    public sealed class LocalAccountService
    {
        private const string Key = "NCG.LocalAccount";

        private readonly BalanceConfig cfg;
        private LocalAccountState state;

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
            var added = StaminaCalculator.AddReward(state.stamina, amount, cfg != null ? cfg.maxStamina : 8);
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
                state.unlock.bossStreak = Math.Min(2, state.unlock.bossStreak + 1);
                state.unlock.nextUnlockType = ResolveNextUnlockType();
            }
            else
            {
                state.pveLoseCount++;
            }

            Save();
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
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(state));
            PlayerPrefs.Save();
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
            if (s.stamina.maxStamina <= 0)
            {
                s.stamina.maxStamina = 8;
            }

            if (s.stamina.updatedAtUnixMs <= 0)
            {
                s.stamina.currentStamina = s.stamina.maxStamina;
                s.stamina.updatedAtUnixMs = UnixTimeUtility.NowMilliseconds();
            }
        }
    }
