using UnityEngine;

    /// <summary>
    /// 保存单局战斗的基础数值、阶段时长、手牌上限、体力奖励和发牌权重。
    /// 这个配置会被战斗状态机、卡牌管理器和本地进度系统共同读取。
    /// </summary>
    [CreateAssetMenu(fileName = "BalanceConfig", menuName = "NotCardGod/Data/Balance Config")]
    public sealed class BalanceConfig : ScriptableObject
    {
        // 玩家进入一局战斗时的初始生命值；生命值没有固定上限。
        public float initialHp = 50f;

        // 玩家进入一局战斗时的初始防御值；防御只能通过卡牌或效果增加。
        public float initialDefense = 0f;

        // 备战和战斗阶段允许持有的基础手牌上限，弃牌阶段会把数量压回该值。
        public int baseHandLimit = 7;

        // 备战和战斗阶段允许持有的强化卡上限，弃牌阶段会把数量压回该值。
        public int strengthenedHandLimit = 7;

        // 每张基础手牌最多可挂载的强化卡数量。
        public int enhSlotMax = 2;

        // 强化槽满后是否自动打出基础手牌；当前设计要求玩家手动点击出牌。
        public bool autoPlayFullSlot = false;

        // 第一回合发牌阶段给每名玩家发放的基础手牌数量。
        public int firstRoundBaseCards = 5;

        // 第一回合发牌阶段给每名玩家发放的强化卡数量。
        public int firstRoundStrengthenedCards = 5;

        // 第二回合起，每回合发牌阶段给每名玩家发放的基础手牌数量。
        public int cardsPerRoundBase = 3;

        // 第二回合起，每回合发牌阶段给每名玩家发放的强化卡数量。
        public int cardsPerRoundStrengthened = 5;

        // 弃牌阶段持续秒数；超时后由系统按规则自动弃牌。
        public int discardSeconds = 10;

        // 备战阶段持续秒数；超时后自动进入战斗阶段。
        public int preparationSeconds = 15;

        // 战斗阶段持续秒数；超时后进入下一回合。
        public int battleSeconds = 30;

        // 防御减伤公式的 k 值，当前参考创新文档使用 10。
        public float damageReductionK = 10f;

        // 每日福利中自然补充的体力数量。
        public int dailyStaminaRegen = 5;

        // 观看广告后领取的体力数量。
        public int adStaminaReward = 2;

        // 本地账号可持有的体力上限。
        public int maxStamina = 15;

        [Header("基础手牌概率")]
        // 基础攻击手牌的发牌权重；数值越高越容易被抽到。
        public int baseAttackWeight = 75;

        // 基础防御手牌的发牌权重。
        public int baseDefenseWeight = 15;

        // 基础治疗手牌的发牌权重。
        public int baseHealWeight = 10;

        // 每进入新回合时，所有攻击手牌的攻击力提升数值
        public int attackStackPerRound = 1;

        [Header("强化卡牌品质概率")]
        // 白银强化卡的发牌权重。
        public int skillSilverWeight = 68;

        // 黄金强化卡的发牌权重。
        public int skillGoldWeight = 22;

        // 白金强化卡的发牌权重。
        public int skillPlatinumWeight = 8;

        // 棱彩强化卡的发牌权重。
        public int skillPrismaticWeight = 2;

        [Header("强化卡牌种类权重")]
        // 攻击类强化卡的发牌权重。
        public int skillAttackWeight = 60;

        // 防御类强化卡的发牌权重。
        public int skillDefenseWeight = 25;

        // 治疗类强化卡的发牌权重。
        public int skillHealWeight = 15;

        /// <summary>
        /// 返回指定基础手牌类型的发牌权重。
        /// </summary>
        public int BaseWeight(BaseCardType type)
        {
            switch (type)
            {
                case BaseCardType.Attack:
                    // 权重不允许为负，避免随机池出现无效区间。
                    return Mathf.Max(0, baseAttackWeight);
                case BaseCardType.Defense:
                    return Mathf.Max(0, baseDefenseWeight);
                case BaseCardType.Heal:
                    return Mathf.Max(0, baseHealWeight);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 返回指定强化卡品质的发牌权重。
        /// </summary>
        /// <param name="quality">需要查询的强化卡品质。</param>
        public int SkillQualityWeight(CardQuality quality)
        {
            switch (quality)
            {
                case CardQuality.Silver:
                    return Mathf.Max(0, skillSilverWeight);
                case CardQuality.Gold:
                    return Mathf.Max(0, skillGoldWeight);
                case CardQuality.Platinum:
                    return Mathf.Max(0, skillPlatinumWeight);
                case CardQuality.Prismatic:
                    return Mathf.Max(0, skillPrismaticWeight);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 返回指定强化卡类型的发牌权重。
        /// </summary>
        public int SkillTypeWeight(BaseCardType type)
        {
            switch (type)
            {
                case BaseCardType.Attack:
                    return Mathf.Max(0, skillAttackWeight);
                case BaseCardType.Defense:
                    return Mathf.Max(0, skillDefenseWeight);
                case BaseCardType.Heal:
                    return Mathf.Max(0, skillHealWeight);
                default:
                    return 0;
            }
        }
    }
