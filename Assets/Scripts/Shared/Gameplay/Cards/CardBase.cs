// System 字段。
using System;
using System.Collections.Generic;
// UnityEngine 字段。
using UnityEngine;

    /// <summary>
    /// 保存一次卡牌释放所需的玩家、目标、回合、模式和状态上下文。
    /// </summary>
    [Serializable]
    public sealed class CardReleaseContext
    {
        // ownerPlayerId 字段。
        public string ownerPlayerId;
        // targetPlayerId 字段。
        public string targetPlayerId;
        // targetCardId 字段。
        public string targetCardId;
        // turnIndex 字段。
        public int turnIndex;
        // gameMode 字段。
        public GameMode gameMode = GameMode.PvE;
        // gameState 字段。
        public GameState gameState = GameState.Init;
        // isAutoRelease 字段。
        public bool isAutoRelease;
        // isPreview 字段。
        public bool isPreview;
    }

    /// <summary>
    /// 保存卡牌释放后的结果、消耗、目标、数值和附加效果。
    /// </summary>
    [Serializable]
    public sealed class CardReleaseResult
    {
        // success 字段。
        public bool success;
        // reason 字段。
        public string reason;
        // cardId 字段。
        public string cardId;
        // cardName 字段。
        public string cardName;
        // category 字段。
        public CardCategory category;
        // quality 字段。
        public CardQuality quality;
        // baseType 字段。
        public BaseCardType? baseType;
        // actionType 字段。
        public ActionType actionType;
        // targetMode 字段。
        public CardTargetMode targetMode;
        // ownerPlayerId 字段。
        public string ownerPlayerId;
        // targetPlayerId 字段。
        public string targetPlayerId;
        // targetCardId 字段。
        public string targetCardId;
        // cooldownApplied 字段。
        public int cooldownApplied;
        // power 字段。
        public float power;
        // gameState 字段。
        public GameState gameState = GameState.Init;
        // skillIds 字段。
        public List<string> skillIds = new List<string>();
        // effects 字段。
        public List<CardEffectData> effects = new List<CardEffectData>();

        /// <summary>
        /// 执行 CreateFailure 相关逻辑。
        /// </summary>
        public static CardReleaseResult CreateFailure(string reason)
        {
            return new CardReleaseResult
            {
                success = false,
                reason = reason
            };
        }

        /// <summary>
        /// 执行 CreateSuccess 相关逻辑。
        /// </summary>
        public static CardReleaseResult CreateSuccess()
        {
            return new CardReleaseResult
            {
                success = true
            };
        }
    }

    /// <summary>
    /// 所有卡牌定义的基类，提供身份信息、费用、冷却、目标校验和释放流程。
    /// </summary>
    public abstract class CardBase : ScriptableObject
    {
        [Header("Identity")]
        // cardId 字段。
        [SerializeField] private string cardId;
        // displayName 字段。
        [SerializeField] private string displayName;
        [TextArea]
        // description 字段。
        [SerializeField] private string description;
        // iconKey 字段。
        [SerializeField] private string iconKey;

        [Header("Core")]
        // quality 字段。
        [SerializeField] private CardQuality quality = CardQuality.Silver;
        // targetMode 字段。
        [SerializeField] private CardTargetMode targetMode = CardTargetMode.None;
        // energyCost 字段。
        [SerializeField] private int energyCost;
        // cooldownTurns 字段。
        [SerializeField] private int cooldownTurns;
        // allowPreview 字段。
        [SerializeField] private bool allowPreview = true;
        // canUseOnPre 字段。
        [SerializeField] private bool canUseOnPre;
        // effects 字段。
        [SerializeField] private List<CardEffectData> effects = new List<CardEffectData>();

        // CardId 属性。
        public string CardId => cardId;
        // DisplayName 属性。
        public string DisplayName => displayName;
        // Description 属性。
        public string Description => description;
        // IconKey 属性。
        public string IconKey => iconKey;
        // Quality 属性。
        public virtual CardQuality Quality => quality;
        // Category 属性。
        public abstract CardCategory Category { get; }
        // ReleaseActionType 属性。
        public abstract ActionType ReleaseActionType { get; }
        // TargetMode 属性。
        public virtual CardTargetMode TargetMode => targetMode;
        // EnergyCost 属性。
        public virtual int EnergyCost => energyCost;
        // CooldownTurns 属性。
        public virtual int CooldownTurns => cooldownTurns;
        // AllowPreview 属性。
        public virtual bool AllowPreview => allowPreview;
        // CanUseOnPre 属性。
        public virtual bool CanUseOnPre => canUseOnPre;
        // Effects 属性。
        public IReadOnlyList<CardEffectData> Effects => effects;

        /// <summary>
        /// 执行 ConfigureRuntimeCore 相关逻辑。
        /// </summary>
        public void ConfigureRuntimeCore(
            string runtimeCardId,
            string runtimeDisplayName,
            string runtimeDescription,
            string runtimeIconKey,
            CardQuality runtimeQuality,
            CardTargetMode runtimeTargetMode,
            int runtimeEnergyCost,
            int runtimeCooldownTurns,
            bool runtimeAllowPreview,
            bool runtimeCanUseOnPre,
            IEnumerable<CardEffectData> runtimeEffects)
        {
            cardId = runtimeCardId;
            displayName = runtimeDisplayName;
            description = runtimeDescription;
            iconKey = runtimeIconKey;
            quality = runtimeQuality;
            targetMode = runtimeTargetMode;
            energyCost = runtimeEnergyCost;
            cooldownTurns = runtimeCooldownTurns;
            allowPreview = runtimeAllowPreview;
            canUseOnPre = runtimeCanUseOnPre;
            effects = runtimeEffects != null ? new List<CardEffectData>(runtimeEffects) : new List<CardEffectData>();
        }

        /// <summary>
        /// 执行 Release 相关逻辑。
        /// </summary>
        public CardReleaseResult Release(CardInstanceData instanceData, CardReleaseContext context)
        {
            if (!CanRelease(instanceData, context, out var reason))
            {
                return CardReleaseResult.CreateFailure(reason);
            }

            // result 字段。
            var result = CreateReleaseResult(instanceData, context);
            if (result == null)
            {
                return CardReleaseResult.CreateFailure("Card release result was not created.");
            }

            if (result.success && string.IsNullOrEmpty(result.reason))
            {
                result.cardId = CardId;
                result.cardName = DisplayName;
                result.category = Category;
                result.quality = ResolveQuality(instanceData);
                result.actionType = ReleaseActionType;
                result.targetMode = TargetMode;
                result.ownerPlayerId = context.ownerPlayerId;
                result.targetPlayerId = context.targetPlayerId;
                result.targetCardId = context.targetCardId;
                result.gameState = context.gameState;
            }

            // result 字段。
            return result;
        }

        /// <summary>
        /// 执行 CanRelease 相关逻辑。
        /// </summary>
        public bool CanRelease(CardInstanceData instanceData, CardReleaseContext context, out string reason)
        {
            reason = string.Empty;

            if (context == null)
            {
                reason = "Release context is null.";
                // false 字段。
                return false;
            }

            if (instanceData != null && instanceData.isLocked)
            {
                reason = "Card is locked.";
                // false 字段。
                return false;
            }

            if (instanceData != null && instanceData.currentCooldownTurns > 0)
            {
                reason = "Card is cooling down.";
                // false 字段。
                return false;
            }

            if (!CanReleaseInState(context.gameState))
            {
                reason = $"Card cannot be released in state {context.gameState}.";
                // false 字段。
                return false;
            }

            if (!IsTargetValid(context))
            {
                reason = "Target is invalid for this card.";
                // false 字段。
                return false;
            }

            /// <summary>
            /// 处理 CanReleaseOverride 方法的业务逻辑。
            /// </summary>
            return CanReleaseOverride(instanceData, context, out reason);
        }

        /// <summary>
        /// 执行 CanReleaseInState 相关逻辑。
        /// </summary>
        protected virtual bool CanReleaseInState(GameState gameState)
        {
            // gameState 字段。
            return gameState == GameState.Battle || (gameState == GameState.Preparation && CanUseOnPre);
        }

        /// <summary>
        /// 执行 IsTargetValid 相关逻辑。
        /// </summary>
        protected virtual bool IsTargetValid(CardReleaseContext context)
        {
            switch (TargetMode)
            {
                case CardTargetMode.None:
                case CardTargetMode.Global:
                    // true 字段。
                    return true;
                case CardTargetMode.Self:
                    return !string.IsNullOrEmpty(context.ownerPlayerId);
                case CardTargetMode.AnyPlayer:
                case CardTargetMode.Ally:
                case CardTargetMode.Enemy:
                    return !string.IsNullOrEmpty(context.targetPlayerId);
                case CardTargetMode.SingleCard:
                    return !string.IsNullOrEmpty(context.targetCardId);
                case CardTargetMode.AllAllies:
                case CardTargetMode.AllEnemies:
                    // true 字段。
                    return true;
                default:
                    // false 字段。
                    return false;
            }
        }

        /// <summary>
        /// 执行 CanReleaseOverride 相关逻辑。
        /// </summary>
        protected virtual bool CanReleaseOverride(CardInstanceData instanceData, CardReleaseContext context, out string reason)
        {
            reason = string.Empty;
            // true 字段。
            return true;
        }

        /// <summary>
        /// 执行 CreateReleaseResult 相关逻辑。
        /// </summary>
        protected abstract CardReleaseResult CreateReleaseResult(CardInstanceData instanceData, CardReleaseContext context);

        /// <summary>
        /// 执行 NormalizeResult 相关逻辑。
        /// </summary>
        protected virtual void NormalizeResult(CardReleaseResult result, CardInstanceData instanceData, CardReleaseContext context)
        {
            result.cardId = CardId;
            result.cardName = DisplayName;
            result.category = Category;
            result.quality = ResolveQuality(instanceData);
            result.actionType = ReleaseActionType;
            result.targetMode = TargetMode;
            result.ownerPlayerId = context.ownerPlayerId;
            result.targetPlayerId = context.targetPlayerId;
            result.targetCardId = context.targetCardId;
            result.gameState = context.gameState;
        }

        /// <summary>
        /// 执行 CreateBaseSuccessResult 相关逻辑。
        /// </summary>
        protected CardReleaseResult CreateBaseSuccessResult(CardInstanceData instanceData, CardReleaseContext context)
        {
            // result 字段。
            var result = CardReleaseResult.CreateSuccess();
            NormalizeResult(result, instanceData, context);
            // result 字段。
            return result;
        }

        /// <summary>
        /// 执行 ResolveQuality 相关逻辑。
        /// </summary>
        protected CardQuality ResolveQuality(CardInstanceData instanceData)
        {
            return instanceData != null ? instanceData.quality : Quality;
        }
    }

    /// <summary>
    /// 基础手牌基类，定义基础牌类型、基础数值、消耗和目标规则。
    /// </summary>
    public abstract class HandCardBase : CardBase
    {
        [Header("Hand Card")]
        // handCardType 字段。
        [SerializeField] private BaseCardType handCardType = BaseCardType.Attack;
        // basePower 字段。
        [SerializeField] private int basePower = 1;
        // consumesCardOnRelease 字段。
        [SerializeField] private bool consumesCardOnRelease = true;
        // requiresTarget 字段。
        [SerializeField] private bool requiresTarget = true;

        // HandCardType 属性。
        public BaseCardType HandCardType => handCardType;
        // BasePower 属性。
        public int BasePower => basePower;
        // ConsumesCardOnRelease 属性。
        public bool ConsumesCardOnRelease => consumesCardOnRelease;
        // RequiresTarget 属性。
        public bool RequiresTarget => requiresTarget;

        // Category 属性。
        public override CardCategory Category => CardCategory.Hand;
        // ReleaseActionType 属性。
        public override ActionType ReleaseActionType => ActionType.PlayBase;
        public override CardTargetMode TargetMode
        {
            get
            {
                if (!RequiresTarget)
                {
                    return CardTargetMode.None;
                }

                switch (handCardType)
                {
                    case BaseCardType.Attack:
                        return CardTargetMode.Enemy;
                    case BaseCardType.Defense:
                    case BaseCardType.Heal:
                        return CardTargetMode.Self;
                    default:
                        return CardTargetMode.AnyPlayer;
                }
            }
        }

        /// <summary>
        /// 执行 ConfigureRuntime 相关逻辑。
        /// </summary>
        public void ConfigureRuntime(
            string runtimeCardId,
            string runtimeDisplayName,
            string runtimeDescription,
            BaseCardType runtimeHandCardType,
            int runtimeBasePower,
            bool runtimeCanUseOnPre,
            // runtimeRequiresTarget 字段。
            bool runtimeRequiresTarget = true,
            // runtimeEffects 字段。
            IEnumerable<CardEffectData> runtimeEffects = null)
        {
            handCardType = runtimeHandCardType;
            basePower = runtimeBasePower;
            requiresTarget = runtimeRequiresTarget;
            consumesCardOnRelease = true;
            ConfigureRuntimeCore(
                runtimeCardId,
                runtimeDisplayName,
                runtimeDescription,
                string.Empty,
                CardQuality.Silver,
                CardTargetMode.None,
                0,
                0,
                true,
                runtimeCanUseOnPre,
                runtimeEffects);
        }

        /// <summary>
        /// 执行 CanReleaseInState 相关逻辑。
        /// </summary>
        protected override bool CanReleaseInState(GameState gameState)
        {
            // gameState 字段。
            return gameState == GameState.Battle || (gameState == GameState.Preparation && CanUseOnPre);
        }

        /// <summary>
        /// 执行 CanReleaseOverride 相关逻辑。
        /// </summary>
        protected override bool CanReleaseOverride(CardInstanceData instanceData, CardReleaseContext context, out string reason)
        {
            reason = string.Empty;
            // true 字段。
            return true;
        }

        /// <summary>
        /// 执行 CreateReleaseResult 相关逻辑。
        /// </summary>
        protected override CardReleaseResult CreateReleaseResult(CardInstanceData instanceData, CardReleaseContext context)
        {
            // result 字段。
            var result = CreateBaseSuccessResult(instanceData, context);
            result.baseType = handCardType;
            result.power = BasePower * Mathf.Max(1, instanceData != null ? instanceData.stackCount : 1);
            result.cooldownApplied = CooldownTurns;
            if (ConsumesCardOnRelease)
            {
                result.effects.AddRange(Effects);
            }

            // result 字段。
            return result;
        }
    }

    /// <summary>
    /// 强化卡基类，定义合成需求、消耗数量、替换规则和强化释放逻辑。
    /// </summary>
    public abstract class SkillCardBase : CardBase
    {
        [Header("Skill Card")]
        // skillCardType 字段。
        [SerializeField] private BaseCardType skillCardType = BaseCardType.Attack;
        // skillPower 字段。
        [SerializeField] private int skillPower = 2;
        // requiredMergeCount 字段。
        [SerializeField] private int requiredMergeCount = 2;
        // consumeHandCardCount 字段。
        [SerializeField] private int consumeHandCardCount = 2;
        // canReplaceBaseCard 字段。
        [SerializeField] private bool canReplaceBaseCard = true;

        // SkillCardType 属性。
        public BaseCardType SkillCardType => skillCardType;
        // SkillPower 属性。
        public int SkillPower => skillPower;
        // RequiredMergeCount 属性。
        public int RequiredMergeCount => requiredMergeCount;
        // ConsumeHandCardCount 属性。
        public int ConsumeHandCardCount => consumeHandCardCount;
        // CanReplaceBaseCard 属性。
        public bool CanReplaceBaseCard => canReplaceBaseCard;

        // Category 属性。
        public override CardCategory Category => CardCategory.Skill;
        // ReleaseActionType 属性。
        public override ActionType ReleaseActionType => ActionType.Synthesize;
        // TargetMode 属性。
        public override CardTargetMode TargetMode => CardTargetMode.SingleCard;

        /// <summary>
        /// 执行 ConfigureRuntime 相关逻辑。
        /// </summary>
        public void ConfigureRuntime(
            string runtimeCardId,
            string runtimeDisplayName,
            string runtimeDescription,
            BaseCardType runtimeSkillCardType,
            int runtimeSkillPower,
            CardQuality runtimeQuality,
            bool runtimeCanUseOnPre,
            // runtimeEffects 字段。
            IEnumerable<CardEffectData> runtimeEffects = null)
        {
            skillCardType = runtimeSkillCardType;
            skillPower = runtimeSkillPower;
            requiredMergeCount = 1;
            consumeHandCardCount = 1;
            canReplaceBaseCard = true;
            ConfigureRuntimeCore(
                runtimeCardId,
                runtimeDisplayName,
                runtimeDescription,
                string.Empty,
                runtimeQuality,
                CardTargetMode.SingleCard,
                0,
                0,
                true,
                runtimeCanUseOnPre,
                runtimeEffects);
        }

        /// <summary>
        /// 执行 CanReleaseOverride 相关逻辑。
        /// </summary>
        protected override bool CanReleaseOverride(CardInstanceData instanceData, CardReleaseContext context, out string reason)
        {
            reason = string.Empty;

            if (context == null)
            {
                reason = "Skill release context is null.";
                // false 字段。
                return false;
            }

            if (string.IsNullOrEmpty(context.targetCardId))
            {
                reason = "Skill cards require a target base card.";
                // false 字段。
                return false;
            }

            // true 字段。
            return true;
        }

        /// <summary>
        /// 执行 CanReleaseInState 相关逻辑。
        /// </summary>
        protected override bool CanReleaseInState(GameState gameState)
        {
            // gameState 字段。
            return gameState == GameState.Battle || (gameState == GameState.Preparation && CanUseOnPre);
        }

        /// <summary>
        /// 执行 CreateReleaseResult 相关逻辑。
        /// </summary>
        protected override CardReleaseResult CreateReleaseResult(CardInstanceData instanceData, CardReleaseContext context)
        {
            // result 字段。
            var result = CreateBaseSuccessResult(instanceData, context);
            result.baseType = skillCardType;
            result.power = SkillPower * Mathf.Max(1, instanceData != null ? instanceData.stackCount : 1);
            result.cooldownApplied = CooldownTurns;
            result.effects.AddRange(Effects);
            // result 字段。
            return result;
        }
    }
