using System;
using System.Collections.Generic;

    [Serializable]
    public sealed class PlayerCombatStateData
    {
        public string playerId;
        public float hp = 50f;
        // 防御属性：用于伤害衰减，不会直接抵消伤害。
        public float shield;
        // 可消耗护盾：受伤时优先吸收伤害，不参与防御衰减公式。
        public float absorbShield;
        public float preparationDefenseBonus;
        public int stamina;
        public bool isDefeated;
        public bool wasHitThisTurn;
        public DeckData deck = new DeckData();
        public List<CombatBuffData> buffs = new List<CombatBuffData>();
    }

    [Serializable]
    public sealed class CombatBuffData
    {
        public CombatBuffKind kind = CombatBuffKind.None;
        public string sourceCardId;
        public float value;
        public float value2;
        public float value3;
        public float secondsLeft;
        public bool untilTurnEnd;
        public bool isDebuff;
        public float tick;
        public int turnsLeft;
        public bool used;
    }

    [Serializable]
    public sealed class CardEffectResultData
    {
        public string cardId;
        public CardEffectKind effectKind = CardEffectKind.None;
        public float value;
        public string note;
    }

    [Serializable]
    public sealed class ActionResultData
    {
        public string actorId;
        public string targetId;
        public string cardId;
        public string cardName;
        public BaseCardType? baseType;
        public string animationId;
        public float hpDelta;
        public float shieldDelta;
        public float absorbShieldDelta;
        public int drawCount;
        public List<string> skillIds = new List<string>();
        public List<CardEffectResultData> effects = new List<CardEffectResultData>();
    }

    [Serializable]
    public sealed class CombatSnapshotData
    {
        public GameState gameState = GameState.Init;
        public int turnIndex;
        public string activePlayerId;
        public float stateSecondsLeft;
        public int preparationSecondsLeft = 5;
        public int battleSecondsLeft = 15;
        public List<PlayerCombatStateData> players = new List<PlayerCombatStateData>();
        public List<ActionResultData> lastActions = new List<ActionResultData>();
    }
