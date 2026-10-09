    /// <summary>
    /// 定义游戏模式，用于区分人机、匹配、好友房和 Boss 战等流程。
    /// </summary>
    public enum GameMode
    {
        PvE,
        P2P,
        FP2P
    }

    /// <summary>
    /// 定义项目内可跳转的场景标识。
    /// </summary>
    public enum SceneId
    {
        Boot,
        Splash,
        Main,
        Loading,
        Battle
    }

    /// <summary>
    /// 定义一局战斗的阶段状态，驱动发牌、弃牌、备战、战斗和结算。
    /// </summary>
    public enum GameState
    {
        Init,
        GameStart,
        Draw,
        Preparation,
        Battle,
        Discarding,
        TurnEnd,
        GameOver
    }

    /// <summary>
    /// 定义战斗参与者角色，用于区分本地玩家、对手和 AI。
    /// </summary>
    public enum PlayerRole
    {
        Host,
        Guest,
        AI
    }

    /// <summary>
    /// 定义卡牌大类，区分基础手牌和强化卡。
    /// </summary>
    public enum CardCategory
    {
        Hand,
        Skill
    }

    /// <summary>
    /// 定义基础牌和强化卡绑定的基础效果类型。
    /// </summary>
    public enum BaseCardType
    {
        Attack,
        Defense,
        Heal
    }

    /// <summary>
    /// 定义强化卡品级，影响抽取权重、库存和显示样式。
    /// </summary>
    public enum CardQuality
    {
        Silver,
        Gold,
        Platinum,
        Prismatic
    }

    /// <summary>
    /// 定义 Boss 连胜解锁奖励的类型。
    /// </summary>
    public enum UnlockRewardType
    {
        Platinum,
        Prismatic
    }

    /// <summary>
    /// 定义卡牌效果解析器支持的效果类型。
    /// </summary>
    public enum CardEffectKind
    {
        None,
        SetPower,
        DamageBoost,
        DamageMultiplier,
        HealBoost,
        ShieldBoost,
        AbsorbShield,
        DrawCards,
        ExtraTurn,
        CopyDamage,
        LifeSteal,
        LifeStealRatio,
        TimeBoost,
        DefensePierce,
        DefensePierceValue,
        IgnoreShield,
        TrueDamage,
        TrueDamageMultiplier,
        Burn,
        SelfDamage,
        HealEnemy,
        HealOverTime,
        CleanseDebuff,
        HolyShield,
        Thorn,
        ReflectBlockedDamage,
        ReduceNextDamage,
        DebuffImmune,
        HalfNextAttack,
        AbsorbBlockedHeal,
        WeakenNextAttack,
        ReduceDefense,
        ExecuteLowHp,
        DamageToHeal,
        RegenTurnStart,
        SacrificeHeal,
        ConditionalHealIfHit,
        DestroyShield,
        DisableCards,
        ShatterDefense,
        DoubleDefense,
        RevengeShield,
        FatalGuard,
        MirrorDamage,
        SetHpMin,
        SwapHp,
        MultiStrikeBurn,
        Stun
    }

    /// <summary>
    /// 定义战斗中可叠加或持续生效的状态修正类型。
    /// </summary>
    public enum CombatBuffKind
    {
        None,
        Burn,
        HealOverTime,
        DisableCards,
        HolyShield,
        Thorn,
        ReflectBlockedDamage,
        ReduceNextDamage,
        DebuffImmune,
        HalfNextAttack,
        AbsorbBlockedHeal,
        WeakenNextAttack,
        DamageToHeal,
        RegenTurnStart,
        DoubleDefense,
        RevengeShield,
        FatalGuard,
        MirrorDamage,
        LifeStealNextAttack
    }

    /// <summary>
    /// 定义卡牌释放时需要选择的目标范围。
    /// </summary>
    public enum CardTargetMode
    {
        None,
        Self,
        Ally,
        Enemy,
        AnyPlayer,
        SingleCard,
        AllAllies,
        AllEnemies,
        Global
    }

    /// <summary>
    /// 定义玩家或 AI 在战斗中提交的行动类型。
    /// </summary>
    public enum ActionType
    {
        PlayBase,
        Synthesize,
        Discard,
        EndPreparation
        ,EndBattleAction
    }

    /// <summary>
    /// 定义网络消息的操作语义，用于区分请求、响应和通知。
    /// </summary>
/// <summary>
    /// 定义游戏网络消息类型，用于房间、行动和同步消息分发。
    /// </summary>
/// <summary>
    /// 定义 AI 行为风格，影响出牌倾向和决策节奏。
    /// </summary>
    public enum AiStyle
    {
        Normal,
        Boss
    }

    /// <summary>
    /// 定义好友助战可转化出的卡牌效果类型。
    /// </summary>
/// <summary>
    /// 定义网络客户端连接状态。
    /// </summary>
