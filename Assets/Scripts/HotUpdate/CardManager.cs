using System;
using System.Collections.Generic;

    /// <summary>
    /// 管理卡牌定义、玩家手牌、强化合成、出牌校验和弃牌逻辑。
    /// </summary>
    public sealed class CardManager : ICardManager
    {
        // 战斗数值配置，提供手牌上限、发牌数量和权重等基础规则。
        private readonly BalanceConfig balanceConfig;
        // 每局隐藏牌库配置，控制白金和棱彩库存以及发牌权重。
        private readonly MatchDeckConfig deckConfig;
        // 单局随机源，保证同一 seed 下发牌结果可复现。
        private readonly Random random;
        // 卡牌 id 到卡牌定义的索引。
        private readonly Dictionary<string, CardBase> cardMap = new Dictionary<string, CardBase>();
        // 可随机发放的基础手牌定义池。
        private readonly List<HandCardBase> basePool = new List<HandCardBase>();
        // 可随机发放的强化卡定义池。
        private readonly List<SkillCardBase> skillPool = new List<SkillCardBase>();
        // 棱彩强化卡每局剩余库存，按卡牌 id 计数。
        private readonly Dictionary<string, int> prismaticStock = new Dictionary<string, int>();
        private HashSet<string> availableSkillCardIds;
        // 白金强化卡每局总剩余库存。
        private int platinumLeft;

        /// <summary>
        /// 使用默认隐藏牌库规则创建卡牌管理器。
        /// </summary>
        /// <param name="balanceConfig">战斗数值配置，决定发牌数量和手牌上限。</param>
        /// <param name="cardCatalog">本局可使用的卡牌定义目录。</param>
        /// <param name="seed">随机种子，用来复现发牌和强制弃牌结果。</param>
        public CardManager(BalanceConfig balanceConfig, CardCatalog cardCatalog, int seed)
            : this(balanceConfig, null, cardCatalog, seed)
        {
        }

        /// <summary>
        /// 使用指定隐藏牌库规则创建卡牌管理器。
        /// 构造时会登记卡牌目录，并初始化白金、棱彩等有限库存。
        /// </summary>
        /// <param name="balanceConfig">战斗基础数值配置。</param>
        /// <param name="deckConfig">本局隐藏牌库配置；为空时使用默认库存和权重。</param>
        /// <param name="cardCatalog">卡牌定义目录。</param>
        /// <param name="seed">本局随机种子。</param>
        public CardManager(BalanceConfig balanceConfig, MatchDeckConfig deckConfig, CardCatalog cardCatalog, int seed)
        {
            this.balanceConfig = balanceConfig;
            this.deckConfig = deckConfig;
            random = new Random(seed);
            RegisterCatalog(cardCatalog);
            ResetMatchStock();
        }

        // 每张基础牌允许挂载的强化槽数量，至少为 1，避免错误配置导致无法合成。
        public int EnhSlotMax => Math.Max(1, balanceConfig.enhSlotMax);

        /// <summary>
        /// 重新登记卡牌目录，并拆分出基础牌池和强化牌池。
        /// </summary>
        /// <param name="cardCatalog">需要注册到本局战斗中的卡牌目录。</param>
        public void RegisterCatalog(CardCatalog cardCatalog)
        {
            cardMap.Clear();
            basePool.Clear();
            skillPool.Clear();

            if (cardCatalog == null || cardCatalog.cards == null)
            {
                return;
            }

            for (var i = 0; i < cardCatalog.cards.Count; i++)
            {
                var card = cardCatalog.cards[i];
                if (card == null || string.IsNullOrEmpty(card.CardId))
                {
                    continue;
                }

                cardMap[card.CardId] = card;
                if (card is HandCardBase handCard)
                {
                    basePool.Add(handCard);
                }
                else if (card is SkillCardBase skillCard)
                {
                    skillPool.Add(skillCard);
                }
            }
        }

        /// <summary>
        /// 处理 ResetMatchStock 对应的运行时流程。
        /// </summary>
        private void ResetMatchStock()
        {
            platinumLeft = Math.Max(0, deckConfig != null ? deckConfig.platinumTotalPerMatch : 50);
            prismaticStock.Clear();
            var copies = Math.Max(0, deckConfig != null ? deckConfig.prismaticCopiesPerCard : 1);
            for (var i = 0; i < skillPool.Count; i++)
            {
                var card = skillPool[i];
                if (card != null && card.Quality == CardQuality.Prismatic)
                {
                    prismaticStock[card.CardId] = copies;
                }
            }
        }

        /// <summary>
        /// 按卡牌 id 查找已注册的卡牌定义。
        /// </summary>
        /// <param name="cardId">要查找的卡牌 id。</param>
        /// <param name="card">成功时返回卡牌定义；失败时为 null。</param>
        public bool TryGetCard(string cardId, out CardBase card)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                card = null;
                return false;
            }

            return cardMap.TryGetValue(cardId, out card);
        }

        /// <summary>
        /// 初始化玩家牌组。
        /// 当前流程开局不预置手牌，所有卡牌都在发牌阶段进入手牌。
        /// </summary>
        /// <param name="player">需要初始化牌组的玩家状态。</param>
        /// <param name="unlockedStrengthenedCardIds">已解锁强化牌 id；当前由运行时随机牌池替代，保留参数给后续扩展。</param>
        public void InitializeDeck(PlayerCombatStateData player, IList<string> unlockedStrengthenedCardIds)
        {
            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            player.deck.drawPile.Clear();
            player.deck.hand.Clear();
            player.deck.discardPile.Clear();
            player.deck.board.Clear();
            availableSkillCardIds = unlockedStrengthenedCardIds != null
                ? new HashSet<string>(unlockedStrengthenedCardIds, StringComparer.Ordinal)
                : null;
        }

        /// <summary>
        /// 按回合规则给玩家发牌。
        /// 第一回合发 5 张基础牌和 5 张强化牌，后续回合按配置补牌。
        /// </summary>
        /// <param name="player">接收卡牌的玩家。</param>
        /// <param name="turnIndex">当前回合号，用来区分首回合和后续回合。</param>
        public List<CardInstanceData> DrawForRound(PlayerCombatStateData player, int turnIndex)
        {
            var drawnCards = new List<CardInstanceData>();
            GrowAttackCards(player);
            var baseCount = turnIndex <= 1 ? balanceConfig.firstRoundBaseCards : balanceConfig.cardsPerRoundBase;
            var skillCount = turnIndex <= 1 ? balanceConfig.firstRoundStrengthenedCards : balanceConfig.cardsPerRoundStrengthened;
            DrawBaseCards(player, baseCount, drawnCards);
            DrawSkillCards(player, skillCount, drawnCards);
            return drawnCards;
        }

        /// <summary>
        /// 尝试把一张强化牌挂到同类型基础牌的强化槽中。
        /// 成功后会从手牌移除强化牌，并把它记录到基础牌的 attachedSkillCards。
        /// </summary>
        /// <param name="player">执行合成的玩家。</param>
        /// <param name="skillHandIndex">强化牌在手牌中的索引。</param>
        /// <param name="targetBaseHandIndex">目标基础牌在手牌中的索引。</param>
        /// <param name="context">当前出牌上下文，包含阶段和目标等规则信息。</param>
        /// <param name="baseHandIdx">成功后返回基础牌在当前手牌中的新索引。</param>
        /// <param name="slotFull">成功后返回强化槽是否已经填满。</param>
        /// <param name="reason">失败时返回可展示的原因。</param>
        public bool TryAttachSkillToBase(
            PlayerCombatStateData player,
            int skillHandIndex,
            int targetBaseHandIndex,
            CardReleaseContext context,
            out int baseHandIdx,
            out bool slotFull,
            out string reason)
        {
            baseHandIdx = -1;
            slotFull = false;
            reason = string.Empty;

            if (player == null || player.deck == null || player.deck.hand == null)
            {
                reason = "玩家手牌数据不可用。";
                return false;
            }

            if (skillHandIndex < 0 || skillHandIndex >= player.deck.hand.Count ||
                targetBaseHandIndex < 0 || targetBaseHandIndex >= player.deck.hand.Count ||
                skillHandIndex == targetBaseHandIndex)
            {
                reason = "合成目标超出手牌范围。";
                return false;
            }

            var skillInstance = player.deck.hand[skillHandIndex];
            var baseInstance = player.deck.hand[targetBaseHandIndex];
            if (!TryGetCard(skillInstance.cardId, out var skillDefinition) || skillDefinition is not SkillCardBase skillCard)
            {
                reason = "选择的卡牌不是强化牌。";
                return false;
            }

            if (!TryGetCard(baseInstance.cardId, out var baseDefinition) || baseDefinition is not HandCardBase handCard)
            {
                reason = "目标卡牌不是基础牌。";
                return false;
            }

            var slotMax = EnhSlotMax;
            if (baseInstance.attachedSkillCards != null && baseInstance.attachedSkillCards.Count >= slotMax)
            {
                reason = "基础牌强化槽已满。";
                return false;
            }

            if (skillCard.SkillCardType != handCard.HandCardType)
            {
                reason = "强化牌类型和目标基础牌不匹配。";
                return false;
            }

            if (context != null && context.gameState == GameState.Preparation &&
                (!skillDefinition.CanUseOnPre || !baseDefinition.CanUseOnPre))
            {
                reason = "这次合成不能在备战期使用。";
                return false;
            }

            if (context == null)
            {
                context = new CardReleaseContext();
            }

            // 强化牌的释放规则需要知道目标基础牌 id，所以在校验前补齐上下文。
            context.targetCardId = baseInstance.cardId;
            if (!skillDefinition.CanRelease(skillInstance, context, out reason))
            {
                return false;
            }

            if (baseInstance.attachedSkillCards == null)
            {
                baseInstance.attachedSkillCards = new List<CardInstanceData>();
            }

            player.deck.hand.RemoveAt(skillHandIndex);
            baseInstance.attachedSkillCards.Add(skillInstance);
            baseHandIdx = player.deck.hand.IndexOf(baseInstance);
            slotFull = baseInstance.attachedSkillCards.Count >= slotMax;
            return true;
        }

        /// <summary>
        /// 尝试从手牌打出一张基础牌。
        /// 强化牌不能直接打出，只能先挂到基础牌上再随基础牌结算。
        /// </summary>
        /// <param name="player">出牌玩家。</param>
        /// <param name="handIndex">要打出的手牌索引。</param>
        /// <param name="context">出牌上下文，包含阶段、目标玩家和目标卡牌。</param>
        /// <param name="result">成功或失败的结算结果。</param>
        public bool TryPlayFromHand(PlayerCombatStateData player, int handIndex, CardReleaseContext context, out CardReleaseResult result)
        {
            result = null;

            if (player == null || player.deck == null || player.deck.hand == null)
            {
                result = CardReleaseResult.CreateFailure("玩家手牌数据不可用。");
                return false;
            }

            if (handIndex < 0 || handIndex >= player.deck.hand.Count)
            {
                result = CardReleaseResult.CreateFailure("手牌索引超出范围。");
                return false;
            }

            var instance = player.deck.hand[handIndex];
            if (!TryGetCard(instance.cardId, out var card))
            {
                result = CardReleaseResult.CreateFailure($"卡牌 {instance.cardId} 未注册。");
                return false;
            }

            if (card.Category == CardCategory.Skill)
            {
                result = CardReleaseResult.CreateFailure("强化牌必须先附加到基础牌上才能结算。");
                return false;
            }

            if (context != null && context.gameState == GameState.Preparation &&
                !CanPlayInstanceInPreparation(instance, card, out var preparationReason))
            {
                result = CardReleaseResult.CreateFailure(preparationReason);
                return false;
            }

            result = card.Release(instance, context);
            if (!result.success)
            {
                return false;
            }

            ApplyAttachedSkillCards(instance, result);
            MovePlayedBaseCardToDiscard(player, handIndex);

            return true;
        }

        /// <summary>
        /// 丢弃指定手牌。
        /// 如果丢弃的是已强化基础牌，会先把挂载的强化牌放入弃牌堆。
        /// </summary>
        /// <param name="player">执行弃牌的玩家。</param>
        /// <param name="handIndex">要弃掉的手牌索引。</param>
        /// <param name="discardedCard">成功时返回被弃掉的主牌实例。</param>
        public bool TryDiscard(PlayerCombatStateData player, int handIndex, out CardInstanceData discardedCard)
        {
            discardedCard = null;

            if (player == null || player.deck == null || player.deck.hand == null)
            {
                return false;
            }

            if (handIndex < 0 || handIndex >= player.deck.hand.Count)
            {
                return false;
            }

            discardedCard = player.deck.hand[handIndex];
            if (discardedCard.attachedSkillCards != null)
            {
                for (var i = 0; i < discardedCard.attachedSkillCards.Count; i++)
                {
                    player.deck.discardPile.Add(discardedCard.attachedSkillCards[i]);
                }

                discardedCard.attachedSkillCards.Clear();
            }

            MoveHandCardToDiscard(player, handIndex);
            return true;
        }

        /// <summary>
        /// 计算玩家当前超过手牌上限的数量。
        /// 基础手牌和强化手牌分别按自己的上限统计。
        /// </summary>
        /// <param name="player">要检查的玩家。</param>
        public int CountOverflowCards(PlayerCombatStateData player)
        {
            if (player == null || player.deck == null || player.deck.hand == null)
            {
                return 0;
            }

            var baseCount = 0;
            var strengthenedCount = 0;

            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                var card = player.deck.hand[i];
                if (card == null || !TryGetCard(card.cardId, out var definition))
                {
                    continue;
                }

                if (definition.Category == CardCategory.Hand)
                {
                    baseCount++;
                }
                else
                {
                    strengthenedCount++;
                }
            }

            var baseOverflow = Math.Max(0, baseCount - balanceConfig.baseHandLimit);
            var strengthenedOverflow = Math.Max(0, strengthenedCount - balanceConfig.strengthenedHandLimit);
            return baseOverflow + strengthenedOverflow;
        }

        /// <summary>
        /// 检查玩家是否已经完成弃牌要求。
        /// </summary>
        /// <param name="player">要检查的玩家。</param>
        public bool IsHandWithinLimits(PlayerCombatStateData player)
        {
            return CountOverflowCards(player) <= 0;
        }

        /// <summary>
        /// 超时后由系统强制弃牌。
        /// 强化牌优先弃最低品质，基础牌优先弃数量最多的类型。
        /// </summary>
        /// <param name="player">需要被强制弃牌的玩家。</param>
        public bool ForceDiscardRandomOverflow(PlayerCombatStateData player)
        {
            if (player == null || player.deck == null || player.deck.hand == null)
            {
                return false;
            }

            var baseCount = CountCategory(player, CardCategory.Hand);
            var skillCount = CountCategory(player, CardCategory.Skill);
            var category = CardCategory.Hand;
            if (skillCount > balanceConfig.strengthenedHandLimit)
            {
                category = CardCategory.Skill;
            }
            else if (baseCount <= balanceConfig.baseHandLimit)
            {
                return false;
            }

            var handIndex = category == CardCategory.Skill
                ? FindLowestQualitySkill(player)
                : FindMostCommonBaseTypeCard(player);
            if (handIndex < 0)
                return false;
            return TryDiscard(player, handIndex, out _);
        }

        /// <summary>
        /// 在手牌中找到品质最低的强化牌索引。
        /// 用于弃牌超时后的默认处理，避免误弃更稀有的强化牌。
        /// </summary>
        /// <param name="player">要扫描手牌的玩家。</param>
        private int FindLowestQualitySkill(PlayerCombatStateData player)
        {
            var best = -1;
            var bestQuality = int.MaxValue;
            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                var instance = player.deck.hand[i];
                if (instance == null || !TryGetCard(instance.cardId, out var card) || card is not SkillCardBase skill)
                {
                    continue;
                }

                var q = (int)skill.Quality;
                if (q < bestQuality)
                {
                    bestQuality = q;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// 找到当前数量最多的基础牌类型，并返回该类型第一张牌的索引。
        /// 用于基础牌超限时的系统弃牌策略。
        /// </summary>
        /// <param name="player">要扫描手牌的玩家。</param>
        private int FindMostCommonBaseTypeCard(PlayerCombatStateData player)
        {
            var atk = 0;
            var def = 0;
            var heal = 0;
            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                if (!TryGetBaseType(player.deck.hand[i], out var type))
                {
                    continue;
                }

                switch (type)
                {
                    case BaseCardType.Attack:
                        atk++;
                        break;
                    case BaseCardType.Defense:
                        def++;
                        break;
                    case BaseCardType.Heal:
                        heal++;
                        break;
                }
            }

            var target = BaseCardType.Attack;
            var max = atk;
            if (def > max)
            {
                target = BaseCardType.Defense;
                max = def;
            }

            if (heal > max)
            {
                target = BaseCardType.Heal;
            }

            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                if (TryGetBaseType(player.deck.hand[i], out var type) && type == target)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 读取卡牌实例对应的基础牌类型。
        /// </summary>
        /// <param name="instance">要识别的卡牌实例。</param>
        private bool TryGetBaseType(CardInstanceData instance, out BaseCardType type)
        {
            type = BaseCardType.Attack;
            if (instance == null || !TryGetCard(instance.cardId, out var card) || card is not HandCardBase hand)
            {
                return false;
            }

            type = hand.HandCardType;
            return true;
        }

        /// <summary>
        /// 按基础牌权重抽牌并直接放入玩家手牌。
        /// 发牌阶段不因超限丢牌，超限处理统一留到弃牌阶段。
        /// </summary>
        /// <param name="player">接收基础牌的玩家。</param>
        /// <param name="count">本次要抽取的基础牌数量。</param>
        /// <param name="drawnCards">用于返回本次实际发出的卡牌实例。</param>
        private void DrawBaseCards(PlayerCombatStateData player, int count, List<CardInstanceData> drawnCards)
        {
            for (var i = 0; i < count; i++)
            {
                var card = PickBaseCard();
                if (card == null)
                {
                    break;
                }

                var instance = CreateInstance(card);
                player.deck.hand.Add(instance);
                drawnCards.Add(instance);
            }
        }

        /// <summary>
        /// 按强化牌品质和类型权重抽牌，并消耗有限品质库存。
        /// </summary>
        /// <param name="player">接收强化牌的玩家。</param>
        /// <param name="count">本次要抽取的强化牌数量。</param>
        /// <param name="drawnCards">用于返回本次实际发出的卡牌实例。</param>
        private void DrawSkillCards(PlayerCombatStateData player, int count, List<CardInstanceData> drawnCards)
        {
            for (var i = 0; i < count; i++)
            {
                var card = PickSkillCard();
                if (card == null)
                {
                    break;
                }

                var instance = CreateInstance(card);
                player.deck.hand.Add(instance);
                drawnCards.Add(instance);
            }
        }

        /// <summary>
        /// 每回合开始时成长玩家所有未消耗的基础攻击牌。
        /// 手牌、抽牌堆和弃牌堆都会增长，避免不同区域的攻击牌数值不同步。
        /// </summary>
        /// <param name="player">需要成长攻击牌的玩家。</param>
        private void GrowAttackCards(PlayerCombatStateData player)
        {
            var add = Math.Max(0, balanceConfig.attackStackPerRound);
            if (add <= 0 || player?.deck == null)
            {
                return;
            }

            GrowAttackCards(player.deck.hand, add);
            GrowAttackCards(player.deck.drawPile, add);
            GrowAttackCards(player.deck.discardPile, add);
        }

        /// <summary>
        /// 对一组卡牌实例中的基础攻击牌增加层数。
        /// </summary>
        /// <param name="cards">要处理的卡牌集合。</param>
        /// <param name="add">本回合增加的攻击成长层数。</param>
        private void GrowAttackCards(IList<CardInstanceData> cards, int add)
        {
            if (cards == null)
            {
                return;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                var instance = cards[i];
                if (instance == null || !TryGetCard(instance.cardId, out var card) || card is not HandCardBase handCard)
                {
                    continue;
                }

                if (handCard.HandCardType == BaseCardType.Attack)
                {
                    instance.stackCount = Math.Max(1, instance.stackCount + add);
                }
            }
        }

        /// <summary>
        /// 处理 PickBaseCard 对应的运行时流程。
        /// </summary>
        private HandCardBase PickBaseCard()
        {
            var total = 0;
            for (var i = 0; i < basePool.Count; i++)
            {
                total += BaseWeight(basePool[i].HandCardType);
            }

            if (total <= 0)
            {
                return basePool.Count > 0 ? basePool[random.Next(basePool.Count)] : null;
            }

            var roll = random.Next(total);
            for (var i = 0; i < basePool.Count; i++)
            {
                roll -= BaseWeight(basePool[i].HandCardType);
                if (roll < 0)
                {
                    return basePool[i];
                }
            }

            return basePool.Count > 0 ? basePool[basePool.Count - 1] : null;
        }

        /// <summary>
        /// 处理 PickSkillCard 对应的运行时流程。
        /// </summary>
        private SkillCardBase PickSkillCard()
        {
            var total = 0;
            for (var i = 0; i < skillPool.Count; i++)
            {
                total += SkillWeight(skillPool[i]);
            }

            if (total <= 0)
            {
                return skillPool.Count > 0 ? skillPool[random.Next(skillPool.Count)] : null;
            }

            var roll = random.Next(total);
            for (var i = 0; i < skillPool.Count; i++)
            {
                roll -= SkillWeight(skillPool[i]);
                if (roll < 0)
                {
                    var card = skillPool[i];
                    ConsumeStock(card);
                    return card;
                }
            }

            var fallback = skillPool.Count > 0 ? skillPool[skillPool.Count - 1] : null;
            ConsumeStock(fallback);
            return fallback;
        }

        /// <summary>
        /// 计算一张强化牌在随机池中的综合权重。
        /// 权重由品质权重和基础类型权重相乘，库存为 0 时权重为 0。
        /// </summary>
        /// <param name="card">要计算权重的强化牌定义。</param>
        private int SkillWeight(SkillCardBase card)
        {
            if (card == null)
            {
                return 0;
            }

            if (availableSkillCardIds != null && !availableSkillCardIds.Contains(card.CardId))
            {
                return 0;
            }

            if (!HasStock(card))
            {
                return 0;
            }

            return Math.Max(0, SkillQualityWeight(card.Quality)) *
                   Math.Max(0, SkillTypeWeight(card.SkillCardType));
        }

        /// <summary>
        /// 判断一张强化牌在本局隐藏牌库中是否还有库存。
        /// </summary>
        /// <param name="card">要检查库存的强化牌。</param>
        private bool HasStock(SkillCardBase card)
        {
            if (card == null)
            {
                return false;
            }

            switch (card.Quality)
            {
                case CardQuality.Silver:
                    return deckConfig == null || deckConfig.silverUnlimited;
                case CardQuality.Gold:
                    return deckConfig == null || deckConfig.goldUnlimited;
                case CardQuality.Platinum:
                    return platinumLeft > 0;
                case CardQuality.Prismatic:
                    return prismaticStock.TryGetValue(card.CardId, out var left) && left > 0;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 消耗有限品质强化牌库存。
        /// 白银和黄金默认不计库存，只有白金总量和棱彩单卡数量会减少。
        /// </summary>
        /// <param name="card">本次抽中的强化牌。</param>
        private void ConsumeStock(SkillCardBase card)
        {
            if (card == null)
            {
                return;
            }

            switch (card.Quality)
            {
                case CardQuality.Platinum:
                    platinumLeft = Math.Max(0, platinumLeft - 1);
                    break;
                case CardQuality.Prismatic:
                    if (prismaticStock.TryGetValue(card.CardId, out var left))
                    {
                        prismaticStock[card.CardId] = Math.Max(0, left - 1);
                    }

                    break;
            }
        }

        /// <summary>
        /// 获取基础牌类型权重。
        /// 优先读取隐藏牌库配置，缺失时回退到战斗数值配置。
        /// </summary>
        private int BaseWeight(BaseCardType type)
        {
            return deckConfig != null ? deckConfig.BaseWeight(type) : balanceConfig.BaseWeight(type);
        }

        /// <summary>
        /// 获取强化牌品质权重。
        /// </summary>
        /// <param name="quality">强化牌品质。</param>
        private int SkillQualityWeight(CardQuality quality)
        {
            return deckConfig != null ? deckConfig.QualityWeight(quality) : balanceConfig.SkillQualityWeight(quality);
        }

        /// <summary>
        /// 获取强化牌对应基础类型的权重。
        /// </summary>
        private int SkillTypeWeight(BaseCardType type)
        {
            return deckConfig != null ? deckConfig.SkillTypeWeight(type) : balanceConfig.SkillTypeWeight(type);
        }

        /// <summary>
        /// 将一张手牌移动到弃牌堆。
        /// </summary>
        /// <param name="player">拥有这张手牌的玩家。</param>
        /// <param name="handIndex">手牌索引。</param>
        private void MoveHandCardToDiscard(PlayerCombatStateData player, int handIndex)
        {
            var card = player.deck.hand[handIndex];
            player.deck.hand.RemoveAt(handIndex);
            player.deck.discardPile.Add(card);
        }

        /// <summary>
        /// 判断一张基础牌是否能在备战期打出。
        /// 基础牌本身和所有已挂载强化牌都允许备战期使用时才返回 true。
        /// </summary>
        /// <param name="instance">要检查的基础牌实例。</param>
        /// <param name="card">基础牌定义。</param>
        /// <param name="reason">失败时返回原因。</param>
        private bool CanPlayInstanceInPreparation(CardInstanceData instance, CardBase card, out string reason)
        {
            reason = string.Empty;
            if (!card.CanUseOnPre)
            {
                reason = "这张牌不能在备战期使用。";
                return false;
            }

            if (instance?.attachedSkillCards == null)
            {
                return true;
            }

            for (var i = 0; i < instance.attachedSkillCards.Count; i++)
            {
                var attached = instance.attachedSkillCards[i];
                if (attached == null || !TryGetCard(attached.cardId, out var attachedCard))
                {
                    continue;
                }

                if (!attachedCard.CanUseOnPre)
                {
                    reason = "已附加的强化牌不能在备战期使用。";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 将基础牌上挂载的强化牌效果合并到本次出牌结果。
        /// </summary>
        /// <param name="baseInstance">打出的基础牌实例。</param>
        /// <param name="result">基础牌原始结算结果，会在此处叠加强化信息。</param>
        private void ApplyAttachedSkillCards(CardInstanceData baseInstance, CardReleaseResult result)
        {
            if (baseInstance?.attachedSkillCards == null || result == null)
            {
                return;
            }

            for (var i = 0; i < baseInstance.attachedSkillCards.Count; i++)
            {
                var skillInstance = baseInstance.attachedSkillCards[i];
                if (skillInstance == null || !TryGetCard(skillInstance.cardId, out var card) || card is not SkillCardBase skillCard)
                {
                    continue;
                }

                result.skillIds.Add(skillInstance.cardId);
                result.power += skillCard.SkillPower * Math.Max(1, skillInstance.stackCount);
                result.effects.AddRange(skillCard.Effects);
            }
        }

        /// <summary>
        /// 打出基础牌后，将基础牌和已使用的强化牌放入弃牌堆。
        /// </summary>
        /// <param name="player">出牌玩家。</param>
        /// <param name="handIndex">被打出的基础牌手牌索引。</param>
        private void MovePlayedBaseCardToDiscard(PlayerCombatStateData player, int handIndex)
        {
            var baseCard = player.deck.hand[handIndex];
            player.deck.hand.RemoveAt(handIndex);
            if (baseCard.attachedSkillCards != null)
            {
                for (var i = 0; i < baseCard.attachedSkillCards.Count; i++)
                {
                    player.deck.discardPile.Add(baseCard.attachedSkillCards[i]);
                }

                baseCard.attachedSkillCards.Clear();
            }

            player.deck.discardPile.Add(baseCard);
        }

        /// <summary>
        /// 统计玩家手牌中某一类卡牌的数量。
        /// </summary>
        /// <param name="player">要统计的玩家。</param>
        /// <param name="category">基础牌或强化牌分类。</param>
        private int CountCategory(PlayerCombatStateData player, CardCategory category)
        {
            var count = 0;
            if (player?.deck?.hand == null)
            {
                return count;
            }

            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                var instance = player.deck.hand[i];
                if (instance != null && TryGetCard(instance.cardId, out var definition) && definition.Category == category)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 向抽牌堆预置基础牌。
        /// 当前随机发牌流程不主动调用，保留给后续构筑式牌库模式。
        /// </summary>
        /// <param name="drawPile">要写入的抽牌堆集合。</param>
        private void AddBaseCardsToDrawPile(ICollection<CardInstanceData> drawPile)
        {
            foreach (var pair in cardMap)
            {
                if (pair.Value.Category != CardCategory.Hand)
                {
                    continue;
                }

                var copies = Math.Max(balanceConfig.firstRoundBaseCards, balanceConfig.cardsPerRoundBase) * 4;
                for (var i = 0; i < copies; i++)
                {
                    drawPile.Add(CreateInstance(pair.Value));
                }
            }
        }

        /// <summary>
        /// 向抽牌堆预置已解锁强化牌。
        /// 当前随机发牌流程不主动调用，保留给后续构筑式牌库模式。
        /// </summary>
        /// <param name="drawPile">要写入的抽牌堆集合。</param>
        /// <param name="unlockedCardIds">玩家已解锁强化牌 id 列表。</param>
        private void AddStrengthenedCardsToDrawPile(ICollection<CardInstanceData> drawPile, IList<string> unlockedCardIds)
        {
            if (unlockedCardIds == null)
            {
                return;
            }

            for (var i = 0; i < unlockedCardIds.Count; i++)
            {
                if (!TryGetCard(unlockedCardIds[i], out var card) || card.Category != CardCategory.Skill)
                {
                    continue;
                }

                var copies = Math.Max(balanceConfig.firstRoundStrengthenedCards, balanceConfig.cardsPerRoundStrengthened) * 4;
                for (var copy = 0; copy < copies; copy++)
                {
                    drawPile.Add(CreateInstance(card));
                }
            }
        }

        /// <summary>
        /// 根据卡牌定义创建一张本局内使用的卡牌实例。
        /// </summary>
        /// <param name="card">卡牌定义。</param>
        private CardInstanceData CreateInstance(CardBase card)
        {
            return new CardInstanceData
            {
                instanceId = Guid.NewGuid().ToString("N"),
                cardId = card.CardId,
                quality = card.Quality,
                stackCount = 1
            };
        }

        /// <summary>
        /// 使用 Fisher-Yates 算法打乱列表顺序。
        /// </summary>
        /// <param name="items">需要原地洗牌的列表。</param>
        private void Shuffle<T>(IList<T> items)
        {
            for (var i = items.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var temp = items[i];
                items[i] = items[j];
                items[j] = temp;
            }
        }
    }
