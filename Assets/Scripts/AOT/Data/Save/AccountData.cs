using System;
using System.Collections.Generic;

    /// <summary>
    /// 本地玩家资料。
    /// 服务器接入后，openId、昵称和头像可以由平台登录结果覆盖。
    /// </summary>
    [Serializable]
    public sealed class PlayerProfileData
    {
        // 本地稳定用户 id。
        public string id;

        // 平台 openId，例如抖音用户 id。
        public string openId;

        // 玩家昵称。
        public string nickname;

        // 玩家头像地址。
        public string avatarUrl;

        // 匹配分，预留给好友对战或玩家对战。
        public int elo = 1000;

        // 当前体力快照。
        public int stamina = 8;

        // 体力最后更新时间，Unix 毫秒。
        public long staminaUpdatedAtUnixMs;
    }

    /// <summary>
    /// 卡牌解锁进度。
    /// 用于记录白金、棱彩解锁情况，以及下一次目标奖励类型。
    /// </summary>
    [Serializable]
    public sealed class UnlockProgressData
    {
        // 该进度所属的用户 id。
        public string userId;

        // 已解锁的白金卡牌 id。
        public List<int> platinumIds = new List<int>();

        // 已解锁的棱彩卡牌 id。
        public List<int> prismaticIds = new List<int>();

        // 九个 Boss 按顺序解锁的强化卡 ID。
        public List<string> unlockedSkillCardIds = new List<string>();

        // 当前 Boss 前已经完成的普通人机胜场数，达到 2 后进入 Boss。
        public int normalWinsSinceBoss;

        // 已完成的 Boss 数量，范围为 0 到 9。
        public int bossWinsCompleted;

        // Boss 或目标战斗连胜计数。
        public int bossStreak;

        public UnlockRewardType nextUnlockType = UnlockRewardType.Platinum;
    }

    /// <summary>
    /// 本地体力状态。
    /// 每日奖励、广告奖励和进入战斗前的体力检查都读取这里。
    /// </summary>
    [Serializable]
    public sealed class StaminaStateData
    {
        // 当前体力值。
        public int currentStamina = 8;

        // 自然恢复可以达到的体力上限。
        public int maxStamina = 8;

        // 上次重新计算体力的时间，Unix 毫秒。
        public long updatedAtUnixMs;

        // 今日已经领取的体力奖励次数。
        public int claimedToday;
    }
