using System;
using System.Collections.Generic;

    /// <summary>
    /// 战斗会话接口。
    /// 屏蔽本地对战与在线对战的实现差异，对外提供统一的战斗流程控制。
    /// </summary>
    public interface IBattleSession
    {
        event Action<GameState> StateChanged;
        event Action<ActionResultData> ActionResolved;
        event Action<HandUpdateMessageData> HandUpdated;
        event Action<DiscardRequestMessageData> DiscardRequested;
        event Action<GameOverMessageData> GameEnded;

        GameState State { get; }
        GameMode Mode { get; }
        int TurnIndex { get; }
        float StateSecondsLeft { get; }
        IReadOnlyDictionary<string, PlayerCombatStateData> Players { get; }

        void Configure(GameMode mode, PlayerRole localRole, string localPlayerId, string opponentPlayerId);
        void RegisterPlayer(PlayerCombatStateData player);
        void StartGame();
        void Tick(float deltaTime);
        bool SubmitPlayerAction(PlayerActionMessageData action, out ActionResultData result, out string reason);
        void EndPreparation();
        bool EndBattleAction(string playerId, out string reason);
        bool IsBattleActionEnded(string playerId);
        CombatSnapshotData CreateSnapshot();
    }
