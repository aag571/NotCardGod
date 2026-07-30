using System;
using System.Collections.Generic;

    /// <summary>
    /// 保存 Boss 连胜解锁奖励的结果数据。
    /// </summary>
    public sealed class UnlockRewardData
    {
        public bool unlocked;
        public UnlockRewardType rewardType;
        public int rewardId;
        public int bossWinsCompleted;
        public string displayText;
    }

    /// <summary>
    /// 根据普通 PVE 胜场和 Boss 胜场推进白金和棱彩卡解锁。
    /// </summary>
    public sealed class UnlockProgressionService
    {
        private static readonly UnlockRewardType[] RewardOrder =
        {
            UnlockRewardType.Platinum,
            UnlockRewardType.Platinum,
            UnlockRewardType.Prismatic,
            UnlockRewardType.Platinum,
            UnlockRewardType.Platinum,
            UnlockRewardType.Prismatic,
            UnlockRewardType.Platinum,
            UnlockRewardType.Platinum,
            UnlockRewardType.Prismatic
        };

        private readonly IList<int> platinumUnlockOrder;
        private readonly IList<int> prismaticUnlockOrder;

        /// <summary>
        /// 构造解锁进度服务实例。
        /// </summary>
        public UnlockProgressionService(IList<int> platinumUnlockOrder, IList<int> prismaticUnlockOrder)
        {
            this.platinumUnlockOrder = platinumUnlockOrder ?? new List<int>();
            this.prismaticUnlockOrder = prismaticUnlockOrder ?? new List<int>();
        }

        /// <summary>
        /// 判断是否可以挑战 Boss。
        /// </summary>
        public bool CanChallengeBoss(UnlockProgressData progress)
        {
            return progress != null && progress.bossStreak >= 2 && GetBossWinsCompleted(progress) < RewardOrder.Length;
        }

        /// <summary>
        /// 记录普通 PvE 胜利。
        /// </summary>
        public void RecordNormalPveWin(UnlockProgressData progress)
        {
            if (progress == null)
            {
                return;
            }

            progress.bossStreak = Math.Min(2, progress.bossStreak + 1);
            progress.nextUnlockType = GetNextRewardType(progress);
        }

        /// <summary>
        /// 记录 Boss 胜利。
        /// </summary>
        public UnlockRewardData RecordBossWin(UnlockProgressData progress)
        {
            var reward = new UnlockRewardData
            {
                unlocked = false,
                displayText = "No unlock available."
            };

            if (!CanChallengeBoss(progress))
            {
                return reward;
            }

            var bossWins = GetBossWinsCompleted(progress);
            var rewardType = RewardOrder[bossWins];
            var rewardId = ResolveRewardId(progress, rewardType);

            if (rewardId < 0)
            {
                return reward;
            }

            if (rewardType == UnlockRewardType.Platinum)
            {
                progress.platinumIds.Add(rewardId);
            }
            else
            {
                progress.prismaticIds.Add(rewardId);
            }

            progress.bossStreak = 0;
            progress.nextUnlockType = GetNextRewardType(progress);

            reward.unlocked = true;
            reward.rewardType = rewardType;
            reward.rewardId = rewardId;
            reward.bossWinsCompleted = bossWins + 1;
            reward.displayText = $"{rewardType} unlocked: {rewardId}";
            return reward;
        }

        /// <summary>
        /// 获取下一次可领取的奖励类型。
        /// </summary>
        public UnlockRewardType GetNextRewardType(UnlockProgressData progress)
        {
            var bossWins = GetBossWinsCompleted(progress);
            return bossWins >= RewardOrder.Length ? UnlockRewardType.Prismatic : RewardOrder[bossWins];
        }

        /// <summary>
        /// 构建进度文本描述。
        /// </summary>
        public string BuildProgressText(UnlockProgressData progress)
        {
            if (progress == null)
            {
                return "Unlock progress unavailable.";
            }

            var bossWins = GetBossWinsCompleted(progress);
            if (bossWins >= RewardOrder.Length)
            {
                return "All rare cards unlocked.";
            }

            var remainingNormalWins = Math.Max(0, 2 - progress.bossStreak);
            return $"{RewardOrder[bossWins]} target, {remainingNormalWins} normal PvE wins before Boss.";
        }

        /// <summary>
        /// 获取已完成 Boss 胜利次数。
        /// </summary>
        private int GetBossWinsCompleted(UnlockProgressData progress)
        {
            if (progress == null)
            {
                return 0;
            }

            return progress.platinumIds.Count + progress.prismaticIds.Count;
        }

        /// <summary>
        /// 解析奖励类型对应的具体奖励 Id。
        /// </summary>
        private int ResolveRewardId(UnlockProgressData progress, UnlockRewardType rewardType)
        {
            var order = rewardType == UnlockRewardType.Platinum ? platinumUnlockOrder : prismaticUnlockOrder;
            var owned = rewardType == UnlockRewardType.Platinum ? progress.platinumIds : progress.prismaticIds;

            for (var i = 0; i < order.Count; i++)
            {
                if (!owned.Contains(order[i]))
                {
                    return order[i];
                }
            }

            return -1;
        }
    }
