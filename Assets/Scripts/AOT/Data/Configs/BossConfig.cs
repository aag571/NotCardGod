using System.Collections.Generic;
using UnityEngine;

    /// <summary>
    /// Boss 或特殊 AI 的配置。
    /// 普通人机和 Boss 都可以通过它调整血量、出牌速度、倾向和固定卡牌偏好。
    /// </summary>
    [CreateAssetMenu(fileName = "BossConfig", menuName = "NotCardGod/Data/Boss Config")]
    public sealed class BossConfig : ScriptableObject
    {
        // Boss 的稳定 id，用于存档、服务器消息和图鉴引用。
        public string bossId;

        // Boss 在界面中显示的名称。
        public string bossName;

        // 在默认初始生命值基础上额外增加的生命值。
        public int extraInitialHp = 0;

        // Boss 两次出牌之间的最小等待时间。
        public float minPlayDelaySeconds = 1f;

        // Boss 两次出牌之间的最大等待时间。
        public float maxPlayDelaySeconds = 3f;

        [Range(0f, 1f)]
        // 攻击倾向，数值越高越偏向攻击牌。
        public float aggressionBias = 0.5f;

        [Range(0f, 1f)]
        // 防御倾向，数值越高越偏向防御选择。
        public float defenseBias = 0.5f;

        // Boss 需要优先使用或固定加入牌库的卡牌 id。
        public List<string> forcedCardIds = new List<string>();
    }
