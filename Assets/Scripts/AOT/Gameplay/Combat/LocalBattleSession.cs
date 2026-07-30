using System;
using System.Collections.Generic;

    /// <summary>
    /// 本地对战实现。
    /// 直接包装 GameController，所有逻辑在同一进程内完成。
    /// </summary>
    public sealed class LocalBattleSession : IBattleSession
    {
        private readonly GameController controller;

        /// <summary>
        /// 包装一个 GameController 实例。
        /// </summary>
        /// <param name="controller">GameController 实例。</param>
        public LocalBattleSession(GameController controller)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
        }

        public event Action<GameState> StateChanged
        {
            add => controller.StateChanged += value;
            remove => controller.StateChanged -= value;
        }

        public event Action<ActionResultData> ActionResolved
        {
            add => controller.ActionResolved += value;
            remove => controller.ActionResolved -= value;
        }

        public event Action<HandUpdateMessageData> HandUpdated
        {
            add => controller.HandUpdated += value;
            remove => controller.HandUpdated -= value;
        }

        public event Action<DiscardRequestMessageData> DiscardRequested
        {
            add => controller.DiscardRequested += value;
            remove => controller.DiscardRequested -= value;
        }

        public event Action<GameOverMessageData> GameEnded
        {
            add => controller.GameEnded += value;
            remove => controller.GameEnded -= value;
        }

        public GameState State => controller.State;
        public GameMode Mode => controller.Mode;
        public int TurnIndex => controller.TurnIndex;
        public float StateSecondsLeft => controller.StateSecondsLeft;
        public IReadOnlyDictionary<string, PlayerCombatStateData> Players => controller.Players;

        public void Configure(GameMode mode, PlayerRole localRole, string localPlayerId, string opponentPlayerId) =>
            controller.Configure(mode, localRole, localPlayerId, opponentPlayerId);

        public void RegisterPlayer(PlayerCombatStateData player) => controller.RegisterPlayer(player);

        public void StartGame() => controller.StartGame();

        public void Tick(float deltaTime) => controller.Tick(deltaTime);

        public bool SubmitPlayerAction(PlayerActionMessageData action, out ActionResultData result, out string reason) =>
            controller.SubmitPlayerAction(action, out result, out reason);

        public void EndPreparation() => controller.EndPreparation();

        public bool EndBattleAction(string playerId, out string reason) =>
            controller.EndBattleAction(playerId, out reason);

        public bool IsBattleActionEnded(string playerId) => controller.IsBattleActionEnded(playerId);

        public CombatSnapshotData CreateSnapshot() => controller.CreateSnapshot();
    }
