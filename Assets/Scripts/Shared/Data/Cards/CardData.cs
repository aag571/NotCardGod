using System;
using System.Collections.Generic;

    /// <summary>
    /// 保存一条卡牌附加效果的数据。
    /// 解析器会根据 effectKind 决定 value、value2、value3 的具体含义。
    /// </summary>
    [Serializable]
    public sealed class CardEffectData
    {
        // 效果类型，决定 CombatResolver 使用哪条结算分支。
        public CardEffectKind effectKind = CardEffectKind.None;

        // 主数值，通常表示伤害倍率、防御量、治疗量或抽牌数量。
        public float value = 0f;

        // 第二数值，用于需要两个参数的效果，例如区间、倍率或附加阈值。
        public float value2 = 0f;

        // 第三数值，用于少数复合效果的额外参数。
        public float value3 = 0f;

        // 按秒计时的持续时间。
        public float seconds = 0f;

        public bool untilTurnEnd;

        // 按回合计时的持续回合数。
        public int durationTurns = 1;

        // 是否允许同类效果叠加。
        public bool stackable;

        // 效果目标标签，默认指向对手。
        public string targetTag = "opponent";
    }

    /// <summary>
    /// 保存一张运行时卡牌实例。
    /// 同一张卡牌定义可以在一局中生成多个实例，每个实例有独立 id、成长层数和附加强化卡。
    /// </summary>
    [Serializable]
    public sealed class CardInstanceData
    {
        // 本局内唯一实例 id，用于 UI 判断新发牌和对象池复用。
        public string instanceId;

        // 指向 CardCatalog 中卡牌定义的 id。
        public string cardId;

        // 当前实例的品质，主要用于强化卡展示和自动弃牌排序。
        public CardQuality quality = CardQuality.Silver;

        // 成长数值；基础牌实例固定为 1，基础攻击的实际基础数值单独成长。
        public int stackCount = 1;

        // 当前实例的基础数值；基础攻击牌每回合增加该值。
        public int currentBasePower;

        // 是否被效果锁定，锁定时不能正常打出或弃掉。
        public bool isLocked;

        // 当前剩余冷却回合数。
        public int currentCooldownTurns;

        // 已经附加到这张基础牌上的强化卡实例。
        public List<CardInstanceData> attachedSkillCards = new List<CardInstanceData>();
    }

    /// <summary>
    /// 保存玩家一局内的牌组区域。
    /// 发牌、弃牌、出牌和同步都会围绕这些列表移动卡牌实例。
    /// </summary>
    [Serializable]
    public sealed class DeckData
    {
        // 抽牌堆中的卡牌实例列表。
        public List<CardInstanceData> drawPile = new List<CardInstanceData>();

        // 当前手牌区中的卡牌实例列表。
        public List<CardInstanceData> hand = new List<CardInstanceData>();

        // 弃牌堆中的卡牌实例列表。
        public List<CardInstanceData> discardPile = new List<CardInstanceData>();

        // 已经进入场上区域的卡牌实例列表。
        public List<CardInstanceData> board = new List<CardInstanceData>();
    }
