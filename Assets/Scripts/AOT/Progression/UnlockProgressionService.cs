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

    /// <summary>
    /// PvE 九 Boss 战役进度、技能卡解锁和牌库可用性规则。
    /// </summary>
    public static class PveCampaignService
    {
        public const int BossCount = 9;
        public const int NormalWinsBeforeBoss = 2;

        private static readonly string[] RewardCardIds =
        {
            "sk_p_heal_reverse", "sk_p_heal_holy", "sk_r_heal_karma",
            "sk_p_def_mirror", "sk_p_def_unyield", "sk_r_def_immortal",
            "sk_p_atk_void", "sk_p_atk_suppress", "sk_r_atk_flame"
        };

        private static readonly string[] RewardNames =
        {
            "逆转", "圣疗", "因果逆转", "镜像", "不屈", "不朽", "虚空打击", "压制", "狂风烈焰斩"
        };

        public static int GetBossWinsCompleted(UnlockProgressData progress)
        {
            return Math.Max(0, Math.Min(BossCount, progress != null ? progress.bossWinsCompleted : 0));
        }

        public static int GetNormalWinsSinceBoss(UnlockProgressData progress)
        {
            return Math.Max(0, Math.Min(NormalWinsBeforeBoss, progress != null ? progress.normalWinsSinceBoss : 0));
        }

        public static bool IsBossBattle(UnlockProgressData progress)
        {
            return progress != null && GetNormalWinsSinceBoss(progress) >= NormalWinsBeforeBoss &&
                   GetBossWinsCompleted(progress) < BossCount;
        }

        public static bool IsGatedCard(string cardId)
        {
            return GetRewardIndex(cardId) >= 0;
        }

        public static bool IsSkillUnlocked(UnlockProgressData progress, string cardId)
        {
            if (!IsGatedCard(cardId))
            {
                return true;
            }

            return progress != null && progress.unlockedSkillCardIds != null &&
                   progress.unlockedSkillCardIds.Contains(cardId);
        }

        public static IList<string> GetAvailableCardIds(UnlockProgressData progress, CardCatalog catalog)
        {
            var result = new List<string>();
            if (catalog == null || catalog.cards == null)
            {
                return result;
            }

            for (var i = 0; i < catalog.cards.Count; i++)
            {
                var card = catalog.cards[i];
                if (card != null && IsSkillUnlocked(progress, card.CardId))
                {
                    result.Add(card.CardId);
                }
            }

            return result;
        }

        public static string GetCurrentBossRewardName(UnlockProgressData progress)
        {
            var index = GetBossWinsCompleted(progress);
            return index < RewardNames.Length ? RewardNames[index] : string.Empty;
        }

        public static void RecordNormalWin(UnlockProgressData progress)
        {
            if (progress == null || GetBossWinsCompleted(progress) >= BossCount)
            {
                return;
            }

            progress.normalWinsSinceBoss = Math.Min(NormalWinsBeforeBoss, GetNormalWinsSinceBoss(progress) + 1);
        }

        public static string RecordBossWin(UnlockProgressData progress)
        {
            if (!IsBossBattle(progress))
            {
                return string.Empty;
            }

            var index = GetBossWinsCompleted(progress);
            var cardId = RewardCardIds[index];
            progress.unlockedSkillCardIds ??= new List<string>();
            if (!progress.unlockedSkillCardIds.Contains(cardId))
            {
                progress.unlockedSkillCardIds.Add(cardId);
            }

            progress.bossWinsCompleted = index + 1;
            progress.normalWinsSinceBoss = 0;
            return cardId;
        }

        private static int GetRewardIndex(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return -1;
            }

            for (var i = 0; i < RewardCardIds.Length; i++)
            {
                if (RewardCardIds[i] == cardId)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    public sealed class PveTaskState
    {
        public string targetText;
        public string hintText;
    }

    /// <summary>
    /// 根据 PvE 解锁进度生成主界面当前任务提示。
    /// </summary>
    public static class PveTaskSystem
    {
        public static PveTaskState GetCurrentTask(UnlockProgressData progress)
        {
            var completedBosses = PveCampaignService.GetBossWinsCompleted(progress);
            if (completedBosses >= PveCampaignService.BossCount)
            {
                return new PveTaskState
                {
                    targetText = "战役完成：9/9",
                    hintText = "全部强化卡已解锁，后续仅进行普通人机对战"
                };
            }

            var normalWins = PveCampaignService.GetNormalWinsSinceBoss(progress);
            var rewardName = PveCampaignService.GetCurrentBossRewardName(progress);
            if (normalWins >= PveCampaignService.NormalWinsBeforeBoss)
            {
                return new PveTaskState
                {
                    targetText = $"下一目标：BOSS {completedBosses + 1}/9",
                    hintText = $"击败 Boss 解锁：{rewardName}"
                };
            }

            return new PveTaskState
            {
                targetText = $"下一目标：BOSS {completedBosses + 1}/9",
                hintText = $"普通人机胜利：{normalWins}/{PveCampaignService.NormalWinsBeforeBoss}，完成后挑战 Boss"
            };
        }
    }
