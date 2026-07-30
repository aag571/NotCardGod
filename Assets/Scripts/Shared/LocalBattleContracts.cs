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
    public string targetPlayerId;
    public string targetCardId;
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

public enum MessageOperation
{
    Add,
    Remove,
    Replace
}
