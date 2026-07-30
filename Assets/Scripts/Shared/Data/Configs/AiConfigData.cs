using System;
using System.Collections.Generic;

    /// <summary>
    /// AI 行动权重配置项。
    /// AIController 在选择出牌、合成或弃牌时会参考这些权重。
    /// </summary>
    [Serializable]
    public sealed class AiActionWeightData
    {
        public ActionType actionType = ActionType.PlayBase;
        // 行动被选中的相对概率权重。
        public float weight = 1f;
    }

    /// <summary>
    /// Boss 对指定卡牌的偏好配置。
    /// 数值越高，AI 越倾向选择这张卡。
    /// </summary>
    [Serializable]
    public sealed class BossCardPreferenceData
    {
        // 受到 Boss 偏好影响的卡牌 id。
        public string cardId;
        // 这张卡的相对偏好权重。
        public float preferenceWeight = 1f;
    }

    /// <summary>
    /// AI 行为配置。
    /// 普通人机和后续 Boss 战的行动速度、判断倾向和卡牌偏好都放在这里。
    /// </summary>
    [Serializable]
    public sealed class AiBrainConfigData
    {
        // AI 难度风格，用于调整反应速度和判断质量。
        public AiStyle aiStyle = AiStyle.Normal;
        // AI 做出下一次决策前的最小等待时间。
        public float minActionDelaySeconds = 1.0f;
        // AI 做出下一次决策前的最大等待时间。
        public float maxActionDelaySeconds = 1.5f;
        // 多个行动都可用时的行动权重列表。
        public List<AiActionWeightData> actionWeights = new List<AiActionWeightData>();
        // Boss 类 AI 使用的卡牌偏好列表。
        public List<BossCardPreferenceData> bossPreferences = new List<BossCardPreferenceData>();
    }
