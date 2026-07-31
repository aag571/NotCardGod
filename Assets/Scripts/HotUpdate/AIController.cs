using System;

    /// <summary>
    /// 保存 AI 本次决策结果，包括行动消息、延迟和失败原因。
    /// </summary>
    /// <summary>
    /// 根据 AI 配置、当前战斗状态和手牌内容选择出牌、合成或目标。
    /// </summary>
    public sealed class AIController : IAIController
    {
        private readonly AiBrainConfigData brainConfig;
        private readonly ICardManager cardManager;
        private readonly Random random;

        /// <summary>
        /// 构造 AI 控制器实例。
        /// </summary>
        public AIController(AiBrainConfigData brainConfig, ICardManager cardManager, int seed)
        {
            this.brainConfig = brainConfig ?? new AiBrainConfigData();
            this.cardManager = cardManager ?? throw new ArgumentNullException(nameof(cardManager));
            random = new Random(seed);
        }

        /// <summary>
        /// AI 决策主入口。
        /// </summary>
        public AiDecisionData Decide(PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent, GameState gameState)
        {
            var decision = new AiDecisionData
            {
                delaySeconds = NextDelay()
            };

            if (aiPlayer == null || aiPlayer.deck == null || aiPlayer.deck.hand == null)
            {
                decision.reason = "AI无手牌可用";
                return decision;
            }

            if (gameState != GameState.Preparation && gameState != GameState.Battle)
            {
                decision.reason = "人机只能在准备阶段和战斗阶段出牌";
                return decision;
            }

            var handIndex = brainConfig.aiStyle == AiStyle.Boss
                ? FindBossPreferredCardIndex(aiPlayer, opponent, gameState)
                : FindNormalCardIndex(aiPlayer, opponent, gameState);

            if (handIndex < 0)
            {
                decision.reason = "当前没有可以出的牌";
                return decision;
            }

            var instance = aiPlayer.deck.hand[handIndex];
            if (!cardManager.TryGetCard(instance.cardId, out var card))
            {
                decision.reason = "人机选择了一张未注册的卡牌";
                return decision;
            }

            decision.hasAction = true;
            decision.action.playerId = aiPlayer.playerId;
            decision.action.handIndex = handIndex;
            decision.action.actionType = card.ReleaseActionType;
            decision.action.targetPlayerId = TargetPlayer(card, aiPlayer, opponent);

            if (card.Category == CardCategory.Skill)
            {
                decision.action.targetIndex = FindTargetBaseCardIndex(aiPlayer, card as SkillCardBase, handIndex);
                decision.action.cardId = decision.action.targetIndex >= 0 ? aiPlayer.deck.hand[decision.action.targetIndex].cardId : string.Empty;
            }

            return decision;
        }

        /// <summary>
        /// 查找普通卡牌的出牌索引。
        /// </summary>
        private int FindNormalCardIndex(PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent, GameState gameState)
        {
            var hasBaseCard = HasCardCategory(aiPlayer, CardCategory.Hand);
            var canSynthesize = hasBaseCard && HasCardCategory(aiPlayer, CardCategory.Skill);
            if (canSynthesize && random.NextDouble() < Math.Max(0d, Math.Min(1d, brainConfig.skillPlayChance)))
            {
                var skillIndex = FindFirstPlayableSkillIndex(aiPlayer, opponent, gameState);
                if (skillIndex >= 0)
                {
                    return skillIndex;
                }
            }

            var weightedItems = new System.Collections.Generic.List<WeightedItem<int>>();
            for (var i = 0; i < aiPlayer.deck.hand.Count; i++)
            {
                var instance = aiPlayer.deck.hand[i];
                if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card) || card.Category != CardCategory.Hand)
                {
                    continue;
                }

                if (!CanReleaseCard(instance, card, aiPlayer, opponent, gameState))
                {
                    continue;
                }

                var weight = ResolveNormalBaseCardWeight(card, aiPlayer, opponent);
                weightedItems.Add(new WeightedItem<int>(i, weight));
            }

            if (weightedItems.Count > 0)
            {
                return WeightedRandomPicker.Pick(weightedItems, random);
            }

            return canSynthesize ? FindFirstPlayableSkillIndex(aiPlayer, opponent, gameState) : -1;
        }

        /// <summary>
        /// 查找 Boss 偏好的卡牌索引。
        /// </summary>
        private int FindBossPreferredCardIndex(PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent, GameState gameState)
        {
            var weightedItems = new System.Collections.Generic.List<WeightedItem<int>>();
            var hasBaseCard = HasCardCategory(aiPlayer, CardCategory.Hand);

            for (var i = 0; i < aiPlayer.deck.hand.Count; i++)
            {
                var instance = aiPlayer.deck.hand[i];
                if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card))
                {
                    continue;
                }

                if (card.Category == CardCategory.Skill && !hasBaseCard)
                {
                    continue;
                }

                if (!CanReleaseCard(instance, card, aiPlayer, opponent, gameState))
                {
                    continue;
                }

                var weight = card.Category == CardCategory.Skill ? 8 : 2;
                for (var j = 0; j < brainConfig.bossPreferences.Count; j++)
                {
                    if (brainConfig.bossPreferences[j].cardId == instance.cardId)
                    {
                        weight += Math.Max(1, (int)Math.Round(brainConfig.bossPreferences[j].preferenceWeight));
                    }
                }

                weightedItems.Add(new WeightedItem<int>(i, weight));
            }

            return weightedItems.Count == 0 ? -1 : WeightedRandomPicker.Pick(weightedItems, random);
        }

        /// <summary>
        /// 查找第一张可用的技能牌索引。
        /// </summary>
        private int FindFirstPlayableSkillIndex(PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent, GameState gameState)
        {
            for (var i = 0; i < aiPlayer.deck.hand.Count; i++)
            {
                var instance = aiPlayer.deck.hand[i];
                if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card) || card.Category != CardCategory.Skill)
                {
                    continue;
                }

                if (CanReleaseCard(instance, card, aiPlayer, opponent, gameState))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 判断是否可释放指定卡牌。
        /// </summary>
        private bool CanReleaseCard(
            CardInstanceData instance,
            CardBase card,
            PlayerCombatStateData aiPlayer,
            PlayerCombatStateData opponent,
            GameState gameState)
        {
            if (instance == null || card == null)
            {
                return false;
            }

            var targetCardId = string.Empty;
            var targetCardIndex = -1;
            if (card is SkillCardBase skillCard)
            {
                targetCardIndex = FindTargetBaseCardIndex(aiPlayer, skillCard, -1);
                if (targetCardIndex < 0)
                {
                    return false;
                }

                var targetInstance = aiPlayer.deck.hand[targetCardIndex];
                targetCardId = targetInstance.cardId;
                if (gameState == GameState.Preparation &&
                    (!card.CanUseOnPre ||
                     !cardManager.TryGetCard(targetInstance.cardId, out var targetDefinition) ||
                     !targetDefinition.CanUseOnPre))
                {
                    return false;
                }
            }

            var context = new CardReleaseContext
            {
                ownerPlayerId = aiPlayer.playerId,
                targetPlayerId = TargetPlayer(card, aiPlayer, opponent),
                targetCardId = targetCardId,
                gameState = gameState
            };

            return card.CanRelease(instance, context, out _);
        }

        /// <summary>
        /// 解析普通基础牌的权重。
        /// </summary>
        private int ResolveNormalBaseCardWeight(CardBase card, PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent)
        {
            var handCard = card as HandCardBase;
            if (handCard == null)
            {
                return 1;
            }

            switch (handCard.HandCardType)
            {
                case BaseCardType.Attack:
                    return opponent != null && opponent.hp <= 3 ? 6 : 3;
                case BaseCardType.Defense:
                    return aiPlayer.shield <= 0 ? 3 : 1;
                case BaseCardType.Heal:
                    return aiPlayer.hp <= 20 ? 5 : 2;
                default:
                    return 1;
            }
        }

        /// <summary>
        /// 判断手牌中是否包含某类卡牌。
        /// </summary>
        private bool HasCardCategory(PlayerCombatStateData aiPlayer, CardCategory category)
        {
            return FindFirstCardCategoryIndex(aiPlayer, category) >= 0;
        }

        /// <summary>
        /// 查找指定类型卡牌的第一张索引。
        /// </summary>
        private int FindFirstCardCategoryIndex(PlayerCombatStateData aiPlayer, CardCategory category)
        {
            if (aiPlayer == null || aiPlayer.deck == null || aiPlayer.deck.hand == null)
            {
                return -1;
            }

            for (var i = 0; i < aiPlayer.deck.hand.Count; i++)
            {
                var instance = aiPlayer.deck.hand[i];
                if (instance != null && cardManager.TryGetCard(instance.cardId, out var card) && card.Category == category)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 确定卡牌的目标玩家。
        /// </summary>
        private string TargetPlayer(CardBase card, PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent)
        {
            switch (card.TargetMode)
            {
                case CardTargetMode.Self:
                    return aiPlayer.playerId;
                case CardTargetMode.Enemy:
                case CardTargetMode.AnyPlayer:
                    return opponent != null ? opponent.playerId : string.Empty;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 查找目标基础牌的索引。
        /// </summary>
        private int FindTargetBaseCardIndex(PlayerCombatStateData aiPlayer, SkillCardBase skillCard, int excludedHandIndex)
        {
            for (var i = 0; i < aiPlayer.deck.hand.Count; i++)
            {
                if (i == excludedHandIndex)
                {
                    continue;
                }

                var instance = aiPlayer.deck.hand[i];
                if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card))
                {
                    continue;
                }

                if (card is HandCardBase handCard && (skillCard == null || handCard.HandCardType == skillCard.SkillCardType))
                {
                    if (instance.attachedSkillCards != null && instance.attachedSkillCards.Count >= cardManager.EnhSlotMax)
                    {
                        continue;
                    }

                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 生成 AI 下次行动的延迟秒数。
        /// </summary>
        private float NextDelay()
        {
            var min = Math.Max(0f, brainConfig.minActionDelaySeconds);
            var max = Math.Max(min, brainConfig.maxActionDelaySeconds);
            return min + (float)random.NextDouble() * (max - min);
        }
    }
