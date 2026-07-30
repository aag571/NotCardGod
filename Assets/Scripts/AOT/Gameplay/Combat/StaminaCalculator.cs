using System;

    /// <summary>
    /// 计算不同玩法体力消耗、每日恢复、奖励增加和扣减规则。
    /// </summary>
    public static class StaminaCalculator
    {
        public const int NormalPveCost = 1;
        public const int BossPveCost = 2;

        /// <summary>
        /// 判断是否可消耗指定数量的体力。
        /// </summary>
        public static bool CanSpend(StaminaStateData state, int cost)
        {
            return state != null && cost >= 0 && state.currentStamina >= cost;
        }

        /// <summary>
        /// 尝试消耗指定数量的体力。
        /// </summary>
        public static bool TrySpend(StaminaStateData state, int cost)
        {
            if (!CanSpend(state, cost))
            {
                return false;
            }

            state.currentStamina -= cost;
            state.updatedAtUnixMs = UnixTimeUtility.NowMilliseconds();
            return true;
        }

        /// <summary>
        /// 获取指定操作的体力消耗值。
        /// </summary>
        public static int GetCost(GameMode mode, bool isBoss)
        {
            switch (mode)
            {
                case GameMode.PvE:
                    return isBoss ? BossPveCost : NormalPveCost;
                default:
                    return NormalPveCost;
            }
        }

        /// <summary>
        /// 领取每日自动恢复的体力。
        /// </summary>
        public static int ClaimDailyRecovery(StaminaStateData state, BalanceConfig balanceConfig, DateTime nowUtc)
        {
            if (state == null || balanceConfig == null)
            {
                return 0;
            }

            var lastUpdate = UnixTimeUtility.FromMilliseconds(state.updatedAtUnixMs);
            if (lastUpdate.Date >= nowUtc.ToUniversalTime().Date)
            {
                return 0;
            }

            var before = state.currentStamina;
            state.maxStamina = balanceConfig.maxStamina;
            state.currentStamina = Math.Min(state.maxStamina, state.currentStamina + balanceConfig.dailyStaminaRegen);
            state.updatedAtUnixMs = UnixTimeUtility.ToMilliseconds(nowUtc);
            return state.currentStamina - before;
        }

        /// <summary>
        /// 增加奖励体力。
        /// </summary>
        public static int AddReward(StaminaStateData state, int amount, int maxStamina)
        {
            if (state == null || amount <= 0)
            {
                return 0;
            }

            var before = state.currentStamina;
            state.maxStamina = Math.Max(1, maxStamina);
            state.currentStamina = Math.Min(state.maxStamina, state.currentStamina + amount);
            state.updatedAtUnixMs = UnixTimeUtility.NowMilliseconds();
            return state.currentStamina - before;
        }
    }
