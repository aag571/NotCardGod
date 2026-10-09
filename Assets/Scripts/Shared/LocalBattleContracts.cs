using System;
using System.Collections.Generic;

[Serializable]
public sealed class PlayerActionMessageData
{
    public string playerId;
    public ActionType actionType;
    public int handIndex;
    public int targetIndex = -1;
    public string cardId;
    public string cardInstanceId;
    public string targetPlayerId;
    public string targetCardId;
    public string targetInstanceId;
    public int turnIndex = -1;
}

[Serializable]
public sealed class HandUpdateMessageData
{
    public string playerId;
    public MessageOperation operation;
    public List<CardInstanceData> cards;
}

[Serializable]
public sealed class DiscardRequestMessageData
{
    public string playerId;
    public int maxCards;
    public int currentCards;
}

[Serializable]
public sealed class GameOverMessageData
{
    public string winnerId;
    public string loserId;
    public int winnerHp;
    public string reason;
}

[Serializable]
public sealed class BattleNetworkMessageData
{
    public const int CurrentProtocolVersion = 1;
    public int protocolVersion;
    public string type;
    public string senderDisplayName;
    public long startSequence;
    public long snapshotRevision;
    public long actionSequence;
    public PlayerActionMessageData action;
    public CombatSnapshotData snapshot;
    public ActionResultData actionResult;
    public GameOverMessageData gameOver;
    public bool actionResponse;
    public string actionReason;
}

public enum MessageOperation
{
    Add,
    Remove,
    Replace
}
