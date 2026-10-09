using System;
using System.Collections.Generic;

public sealed class BattleSnapshotSanitizer
{
    private readonly ICardManager cardManager;
    private readonly string baseCardId;
    private readonly Dictionary<CardQuality, string> skillCardIds = new Dictionary<CardQuality, string>();

    public BattleSnapshotSanitizer(ICardManager cardManager, CardCatalog catalog)
    {
        this.cardManager = cardManager;
        if (catalog?.cards == null)
            return;

        foreach (var card in catalog.cards)
        {
            if (card is HandCardBase && string.IsNullOrEmpty(baseCardId))
                baseCardId = card.CardId;
            else if (card is SkillCardBase skill && !skillCardIds.ContainsKey(skill.Quality))
                skillCardIds.Add(skill.Quality, skill.CardId);
        }
    }

    public CombatSnapshotData CreateForOpponent(CombatSnapshotData source, string hiddenPlayerId)
    {
        if (source == null)
            return null;

        var snapshot = new CombatSnapshotData
        {
            gameState = source.gameState,
            turnIndex = source.turnIndex,
            activePlayerId = source.activePlayerId,
            stateSecondsLeft = source.stateSecondsLeft,
            preparationSecondsLeft = source.preparationSecondsLeft,
            battleSecondsLeft = source.battleSecondsLeft,
            lastActions = source.lastActions
        };

        foreach (var player in source.players)
        {
            snapshot.players.Add(player != null &&
                string.Equals(player.playerId, hiddenPlayerId, StringComparison.Ordinal)
                ? HidePrivateCards(player) : player);
        }

        return snapshot;
    }

    private PlayerCombatStateData HidePrivateCards(PlayerCombatStateData source)
    {
        var deck = source.deck;
        var visibleDeck = new DeckData
        {
            discardPile = deck.discardPile,
            board = deck.board
        };

        foreach (var card in deck.hand)
        {
            if (card == null)
                continue;

            var hiddenCardId = baseCardId;
            if (cardManager.TryGetCard(card.cardId, out var definition) &&
                definition is SkillCardBase && skillCardIds.TryGetValue(card.quality, out var skillCardId))
                hiddenCardId = skillCardId;

            visibleDeck.hand.Add(new CardInstanceData
            {
                instanceId = card.instanceId,
                cardId = hiddenCardId,
                quality = card.quality
            });
        }

        foreach (var card in deck.drawPile)
            visibleDeck.drawPile.Add(new CardInstanceData());

        return new PlayerCombatStateData
        {
            playerId = source.playerId,
            hp = source.hp,
            shield = source.shield,
            absorbShield = source.absorbShield,
            preparationDefenseBonus = source.preparationDefenseBonus,
            stamina = source.stamina,
            isDefeated = source.isDefeated,
            wasHitThisTurn = source.wasHitThisTurn,
            buffs = source.buffs,
            deck = visibleDeck
        };
    }
}
