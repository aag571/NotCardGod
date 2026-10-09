using System;
using System.Collections.Generic;
using UnityEngine;
    /// <summary>
    /// 驱动单局战斗状态机，管理玩家、发牌、弃牌、备战、行动提交和胜负结算。
    /// 持有 BalanceConfig、CardManager、CombatResolver 等核心依赖，串联整个战斗回合循环。
    /// 状态迁移：Init → GameStart → Draw → Discarding → Preparation → Battle → TurnEnd → (Draw 循环) → GameOver。
    /// </summary>
    public sealed class GameController
    {
        private readonly BalanceConfig balanceConfig;
        private readonly ICardManager cardManager;
        private readonly ICombatResolver combatResolver;

        // 按 playerId 索引的玩家战斗状态
        private readonly Dictionary<string, PlayerCombatStateData> playersById = new Dictionary<string, PlayerCombatStateData>();

        // 已完成弃牌的玩家 Id 集合（Discarding 状态下追踪）
        private readonly HashSet<string> discardFinishedPlayerIds = new HashSet<string>();

        // 已完成战斗阶段出牌的玩家 Id 集合（Battle 状态下追踪）
        private readonly HashSet<string> battleFinishedPlayerIds = new HashSet<string>();
        private readonly HashSet<string> preparationDefensePlayerIds = new HashSet<string>();

        // 当前状态剩余倒计时秒数
        private float stateTimer;

        public GameController(BalanceConfig balanceConfig, ICardManager cardManager, ICombatResolver combatResolver)
        {
            this.balanceConfig = balanceConfig ?? throw new ArgumentNullException(nameof(balanceConfig));
            this.cardManager = cardManager ?? throw new ArgumentNullException(nameof(cardManager));
            this.combatResolver = combatResolver ?? throw new ArgumentNullException(nameof(combatResolver));
        }

        // 战斗状态变更事件
        public event Action<GameState> StateChanged;

        // 行动被裁决后触发，携带本次行动的结果数据
        public event Action<ActionResultData> ActionResolved;

        // 玩家手牌变更事件，UI 订阅后刷新手牌显示
        public event Action<HandUpdateMessageData> HandUpdated;

        // 请求玩家弃牌事件，携带可弃牌数量等信息
        public event Action<DiscardRequestMessageData> DiscardRequested;

        // 游戏结束事件，携带胜负信息
        public event Action<GameOverMessageData> GameEnded;

        // 当前战斗阶段状态
        public GameState State { get; private set; } = GameState.Init;

        // 当前对局模式（PvE / PvP）
        public GameMode Mode { get; private set; } = GameMode.PvE;

        // 本地玩家的角色（Host / Guest / Spectator）
        public PlayerRole LocalRole { get; private set; } = PlayerRole.Host;

        // 当前回合索引，从 1 开始递增
        public int TurnIndex { get; private set; }

        // 本地玩家的 playerId
        public string LocalPlayerId { get; private set; }

        // 对手玩家的 playerId
        public string OpponentPlayerId { get; private set; }

        // 当前阶段剩余秒数
        public float StateSecondsLeft => Math.Max(0f, stateTimer);

        // 本地玩家是否拥有 Host 权限（PvE 模式或角色为 Host）
        public bool IsHostAuthority => LocalRole == PlayerRole.Host || Mode == GameMode.PvE;

        // 按 playerId 索引的玩家状态只读视图
        public IReadOnlyDictionary<string, PlayerCombatStateData> Players => playersById;

        /// <summary>
        /// 判断指定玩家是否已结束当前战斗阶段的出牌。
        /// </summary>
        /// <param name="playerId">玩家 Id。</param>
        public bool IsBattleActionEnded(string playerId)
        {
            return !string.IsNullOrEmpty(playerId) && battleFinishedPlayerIds.Contains(playerId);
        }

        /// <summary>
        /// 配置当前对局的模式、角色和玩家 Id。
        /// </summary>
        /// <param name="mode">对局模式（PvE / PvP）。</param>
        /// <param name="localRole">本地玩家的角色。</param>
        /// <param name="localPlayerId">本地玩家 Id。</param>
        /// <param name="opponentPlayerId">对手玩家 Id。</param>
        public void Configure(GameMode mode, PlayerRole localRole, string localPlayerId, string opponentPlayerId)
        {
            Mode = mode;
            LocalRole = localRole;
            LocalPlayerId = localPlayerId;
            OpponentPlayerId = opponentPlayerId;
        }

        /// <summary>
        /// 注册一名玩家到对局中，初始化其战斗属性。
        /// </summary>
        /// <param name="player">要注册的玩家战斗状态数据。</param>
        public void RegisterPlayer(PlayerCombatStateData player)
        {
            if (player == null || string.IsNullOrEmpty(player.playerId))
            {
                throw new ArgumentException("需要玩家和玩家ID", nameof(player));
            }

            // 从配置中读取初始生命值和防御值
            player.hp = balanceConfig.initialHp;
            player.shield = balanceConfig.initialDefense;
            player.absorbShield = 0f;
            player.preparationDefenseBonus = 0f;
            player.isDefeated = false;
            player.wasHitThisTurn = false;
            player.buffs.Clear();
            playersById[player.playerId] = player;
        }

        /// <summary>
        /// 开始对局，进入 GameStart 或 Connecting 状态（取决于对局模式）。
        /// </summary>
        public void StartGame()
        {
            if (playersById.Count == 0)
            {
                throw new InvalidOperationException("开始游戏前至少需要有一名玩家.");
            }

            TurnIndex = 0;
            ChangeState(GameState.GameStart);
        }

        /// <summary>
        /// 标记网络连接完成（PvP 模式下使用）。
        /// </summary>
        /// <summary>
        /// 标记对手已准备就绪。
        /// </summary>
        /// <summary>
        /// 结束备战阶段，进入战斗阶段。
        /// </summary>
        public void EndPreparation()
        {
            if (State == GameState.Preparation)
            {
                ChangeState(GameState.Battle);
            }
        }

        /// <summary>
        /// 标记指定玩家已完成战斗阶段的出牌。
        /// </summary>
        /// <param name="playerId">玩家 Id。</param>
        /// <param name="reason">失败时的原因文本。</param>
        /// <returns>操作是否成功。</returns>
        public bool EndBattleAction(string playerId, out string reason)
        {
            reason = string.Empty;
            if (State != GameState.Battle)
            {
                reason = "只有战斗阶段可以跳过出牌";
                return false;
            }

            var actorId = string.IsNullOrEmpty(playerId) ? LocalPlayerId : playerId;
            if (!playersById.ContainsKey(actorId))
            {
                reason = $"玩家 '{actorId}' 未注册";
                return false;
            }

            battleFinishedPlayerIds.Add(actorId);
            TryFinishBattleEarly();
            return true;
        }

        /// <summary>
        /// 每帧驱动战斗逻辑：Buff 计时、超时检测、状态自动迁移。
        /// </summary>
        /// <param name="deltaTime">本帧间隔秒数。</param>
        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                return;
            }

            combatResolver.TickBuffs(playersById.Values, deltaTime);

            // 每帧检查游戏是否结束
            if (CheckGameOver())
            {
                return;
            }

            // 只有 Preparation / Battle / Discarding 状态需要计时
            if (State != GameState.Preparation && State != GameState.Battle && State != GameState.Discarding)
            {
                return;
            }

            stateTimer -= deltaTime;

            // 计时未结束则等待
            if (stateTimer > 0f)
            {
                return;
            }

            // 超时后的自动状态迁移
            if (State == GameState.Discarding)
            {
                ForceDiscardOverflowHands();
                BeginPreparation();
                return;
            }

            if (State == GameState.Preparation)
            {
                ChangeState(GameState.Battle);
                return;
            }

            if (State == GameState.Battle)
            {
                ChangeState(GameState.TurnEnd);
            }
        }

        /// <summary>
        /// 提交一个玩家行动（出牌、弃牌、合成等），由 CombatResolver 裁决并广播结果。
        /// 仅 Host 权限方可执行裁决。
        /// </summary>
        /// <param name="action">玩家提交的行动消息。</param>
        /// <param name="result">裁决结果数据。</param>
        /// <param name="reason">失败原因。</param>
        /// <returns>行动是否被成功接受并裁决。</returns>
        public bool SubmitPlayerAction(PlayerActionMessageData action, out ActionResultData result, out string reason,
            bool allowGuestHandPrediction = false)
        {
            result = null;
            reason = string.Empty;

            if (action == null)
            {
                reason = "无效行动";
                return false;
            }

            // 非 Host 玩家无权提交裁决
            if (action.actionType == ActionType.EndPreparation && IsHostAuthority)
            {
                if (State != GameState.Preparation)
                {
                    reason = "当前不是备战阶段";
                    return false;
                }

                EndPreparation();
                return true;
            }

            var predictsLocalHandAction = allowGuestHandPrediction &&
                                          (action.actionType == ActionType.Discard ||
                                           action.actionType == ActionType.Synthesize);
            if (!IsHostAuthority && !predictsLocalHandAction)
            {
                reason = "只有房主可以裁决.";
                return false;
            }

            // 弃牌与合成有独立的处理路径
            if (action.actionType == ActionType.Discard)
            {
                return ResolveDiscard(action, out reason);
            }

            if (action.actionType == ActionType.Synthesize)
            {
                return ResolveSynthesis(action, out reason);
            }

            // 其他行动只能在 Battle 或 Preparation 状态提交
            if (State != GameState.Battle && State != GameState.Preparation)
            {
                reason = $"当前阶段无法行动";
                return false;
            }

            // 解析目标玩家
            if (!playersById.TryGetValue(action.targetPlayerId, out var target) && action.actionType != ActionType.PlayBase)
            {
                target = null;
            }

            var actorId = string.IsNullOrEmpty(action.playerId) ? LocalPlayerId : action.playerId;
            if (!playersById.TryGetValue(actorId, out var actor))
            {
                reason = $"玩家 '{actorId}' 未注册";
                return false;
            }

            var resolvedHandIndex = ResolveHandIndex(actor, action.handIndex,
                action.cardInstanceId, action.cardId);
            if (resolvedHandIndex < 0)
            {
                reason = "操作的卡牌已发生变化，请重新选择。";
                return false;
            }

            action.handIndex = resolvedHandIndex;

            // 战斗阶段已结束出牌的玩家不能再提交行动
            if (State == GameState.Battle && battleFinishedPlayerIds.Contains(actorId))
            {
                reason = "玩家已结束本轮战斗阶段的卡牌操作";
                return false;
            }

            // 被禁言的玩家不能出牌
            if (combatResolver.IsActionBlocked(actor))
            {
                reason = "你已被禁止出牌";
                return false;
            }

            if (State == GameState.Preparation && action.actionType == ActionType.PlayBase &&
                action.handIndex >= 0 && action.handIndex < actor.deck.hand.Count)
            {
                var instance = actor.deck.hand[action.handIndex];
                if (instance != null && cardManager.TryGetCard(instance.cardId, out var definition) &&
                    definition is HandCardBase handCard && handCard.HandCardType == BaseCardType.Defense &&
                    preparationDefensePlayerIds.Contains(actor.playerId))
                {
                    reason = "备战阶段每名玩家最多打出一张防御基础卡";
                    return false;
                }
            }

            // 构建卡牌释放上下文
            var context = new CardReleaseContext
            {
                ownerPlayerId = actor.playerId,
                targetPlayerId = action.targetPlayerId,
                targetCardId = action.cardId,
                turnIndex = TurnIndex,
                gameMode = Mode,
                gameState = State
            };

            if (!cardManager.TryPlayFromHand(actor, action.handIndex, context, out var releaseResult))
            {
                reason = releaseResult != null ? releaseResult.reason : "不存在卡牌实例.";
                return false;
            }

            // 裁决行动结果
            if (target == null && !string.IsNullOrEmpty(OpponentPlayerId))
            {
                playersById.TryGetValue(OpponentPlayerId, out target);
            }

            result = combatResolver.Resolve(releaseResult, actor, target);
            if (State == GameState.Preparation && releaseResult.baseType == BaseCardType.Defense)
                preparationDefensePlayerIds.Add(actor.playerId);
            ActionResolved?.Invoke(result);
            PublishHandUpdate(actor);

            // 裁决后检查是否需要结束战斗
            if (!CheckGameOver() && ShouldEndBattle(result))
            {
                ChangeState(GameState.TurnEnd);
            }

            return true;
        }

        /// <summary>
        /// 创建当前战斗状态的快照（用于同步或回放）。
        /// </summary>
        public CombatSnapshotData CreateSnapshot()
        {
            var snapshot = new CombatSnapshotData
            {
                gameState = State,
                turnIndex = TurnIndex,
                activePlayerId = LocalPlayerId,
                stateSecondsLeft = stateTimer,
                preparationSecondsLeft = State == GameState.Preparation ? Math.Max(0, (int)Math.Ceiling(stateTimer)) : 0,
                battleSecondsLeft = State == GameState.Battle ? Math.Max(0, (int)Math.Ceiling(stateTimer)) : 0
            };

            foreach (var pair in playersById)
            {
                snapshot.players.Add(pair.Value);
            }

            return snapshot;
        }

        public void ApplySnapshot(CombatSnapshotData snapshot)
        {
            if (snapshot == null)
                return;

            var previousState = State;
            playersById.Clear();
            if (snapshot.players != null)
            {
                for (var index = 0; index < snapshot.players.Count; index++)
                {
                    var player = snapshot.players[index];
                    if (player != null && !string.IsNullOrEmpty(player.playerId))
                        playersById[player.playerId] = player;
                }
            }

            State = snapshot.gameState;
            TurnIndex = snapshot.turnIndex;
            stateTimer = snapshot.stateSecondsLeft > 0f
                ? snapshot.stateSecondsLeft
                : State == GameState.Preparation ? snapshot.preparationSecondsLeft : snapshot.battleSecondsLeft;

            if (previousState != State)
                StateChanged?.Invoke(State);
        }

        /// <summary>
        /// 处理玩家的弃牌行动。
        /// 弃牌仅在 Discarding 状态下允许，弃完溢出手牌后自动进入备战阶段。
        /// </summary>
        /// <param name="action">弃牌行动消息。</param>
        /// <param name="reason">失败原因。</param>
        /// <returns>弃牌是否成功。</returns>
        private bool ResolveDiscard(PlayerActionMessageData action, out string reason)
        {
            reason = string.Empty;

            if (State != GameState.Discarding)
            {
                reason = "只能在弃牌阶段弃牌";
                return false;
            }

            var playerId = string.IsNullOrEmpty(action.playerId) ? LocalPlayerId : action.playerId;
            if (!playersById.TryGetValue(playerId, out var player))
            {
                reason = $"Player '{playerId}' is not registered.";
                return false;
            }

            var resolvedHandIndex = ResolveHandIndex(player, action.handIndex,
                action.cardInstanceId, action.cardId);
            if (resolvedHandIndex < 0)
            {
                reason = "要弃掉的卡牌已发生变化，请重新选择。";
                return false;
            }

            action.handIndex = resolvedHandIndex;

            if (!cardManager.TryDiscard(player, action.handIndex, out _))
            {
                reason = "弃牌失败";
                return false;
            }

            PublishHandUpdate(player);

            // 如果已无溢出手牌，标记该玩家弃牌完成
            if (State == GameState.Discarding && cardManager.CountOverflowCards(player) <= 0)
            {
                discardFinishedPlayerIds.Add(player.playerId);
                if (AreAllDiscardingPlayersFinished())
                {
                    BeginPreparation();
                }
            }

            return true;
        }

        /// <summary>
        /// 处理玩家的卡牌合成（附加技能）行动。
        /// </summary>
        /// <param name="action">合成行动消息。</param>
        /// <param name="reason">失败原因。</param>
        /// <returns>合成是否成功。</returns>
        private bool ResolveSynthesis(PlayerActionMessageData action, out string reason)
        {
            reason = string.Empty;

            if (State != GameState.Battle && State != GameState.Preparation)
            {
                reason = $"该阶段不允许合成.";
                return false;
            }

            var actorId = string.IsNullOrEmpty(action.playerId) ? LocalPlayerId : action.playerId;
            if (!playersById.TryGetValue(actorId, out var actor))
            {
                reason = $"玩家 '{actorId}' 未注册.";
                return false;
            }

            if (State == GameState.Battle && battleFinishedPlayerIds.Contains(actorId))
            {
                reason = "玩家已结束本次战斗阶段的卡牌操作.";
                return false;
            }

            if (combatResolver.IsActionBlocked(actor))
            {
                reason = "玩家暂时无法使用卡牌.";
                return false;
            }

            var skillIndex = ResolveHandIndex(actor, action.handIndex,
                action.cardInstanceId, action.cardId);
            if (skillIndex < 0)
            {
                reason = "选择的强化牌已发生变化，请重新选择。";
                return false;
            }

            action.handIndex = skillIndex;

            // 自动查找目标基础牌索引（如果未指定）
            var targetIndex = ResolveHandIndex(actor, action.targetIndex,
                action.targetInstanceId, action.targetCardId);
            if (targetIndex < 0 && string.IsNullOrEmpty(action.targetInstanceId) &&
                string.IsNullOrEmpty(action.targetCardId) && action.targetIndex >= 0)
            {
                targetIndex = action.targetIndex;
            }
            if (targetIndex < 0 && !string.IsNullOrEmpty(action.targetInstanceId))
            {
                reason = "目标基础牌已发生变化，请重新选择。";
                return false;
            }
            if (targetIndex < 0)
                targetIndex = FindTargetBaseIndex(actor, action.targetCardId, action.handIndex);

            action.targetIndex = targetIndex;
            if (targetIndex < 0)
            {
                reason = "目标基础牌已发生变化，请重新选择。";
                return false;
            }

            var context = new CardReleaseContext
            {
                ownerPlayerId = actor.playerId,
                targetPlayerId = action.targetPlayerId,
                targetCardId = action.cardId,
                turnIndex = TurnIndex,
                gameMode = Mode,
                gameState = State
            };

            if (!cardManager.TryAttachSkillToBase(
                    actor,
                    action.handIndex,
                    targetIndex,
                    context,
                    out _,
                    out _,
                    out reason))
            {
                return false;
            }

            PublishHandUpdate(actor);
            return true;
        }

        /// <summary>
        /// 自动出牌（AI 或自动流程使用）。
        /// </summary>
        /// <param name="actor">执行出牌的玩家。</param>
        /// <param name="handIndex">手牌索引。</param>
        /// <param name="reason">失败原因。</param>
        /// <returns>自动出牌是否成功。</returns>
        private bool TryAutoPlay(PlayerCombatStateData actor, int handIndex, out string reason)
        {
            reason = string.Empty;
            if (actor == null || handIndex < 0 || handIndex >= actor.deck.hand.Count)
            {
                reason = "Auto play target is unavailable.";
                return false;
            }

            var instance = actor.deck.hand[handIndex];
            if (instance == null || !cardManager.TryGetCard(instance.cardId, out var card))
            {
                reason = "Auto play card is not registered.";
                return false;
            }

            var targetId = TargetPlayer(card, actor.playerId);
            playersById.TryGetValue(targetId, out var target);
            if (target == null && !string.IsNullOrEmpty(OpponentPlayerId))
            {
                playersById.TryGetValue(OpponentPlayerId, out target);
            }

            var ctx = new CardReleaseContext
            {
                ownerPlayerId = actor.playerId,
                targetPlayerId = targetId,
                turnIndex = TurnIndex,
                gameMode = Mode,
                gameState = State,
                isAutoRelease = true
            };

            if (!cardManager.TryPlayFromHand(actor, handIndex, ctx, out var releaseResult))
            {
                reason = releaseResult != null ? releaseResult.reason : "Auto play failed.";
                PublishHandUpdate(actor);
                return false;
            }

            var result = combatResolver.Resolve(releaseResult, actor, target);
            ActionResolved?.Invoke(result);
            PublishHandUpdate(actor);

            if (!CheckGameOver() && ShouldEndBattle(result))
            {
                ChangeState(GameState.TurnEnd);
            }

            return true;
        }

        /// <summary>
        /// 根据卡牌的目标模式确定目标玩家 Id。
        /// </summary>
        /// <param name="card">卡牌数据。</param>
        /// <param name="actorId">出牌玩家 Id。</param>
        /// <returns>目标玩家 Id。</returns>
        private string TargetPlayer(CardBase card, string actorId)
        {
            if (card == null)
            {
                return string.Empty;
            }

            if (card.TargetMode == CardTargetMode.Self)
            {
                return actorId;
            }

            if (card.TargetMode == CardTargetMode.Enemy || card.TargetMode == CardTargetMode.AnyPlayer)
            {
                return actorId == LocalPlayerId ? OpponentPlayerId : LocalPlayerId;
            }

            return string.Empty;
        }

        /// <summary>
        /// 状态变更的核心入口。
        /// 根据目标状态执行对应的初始化逻辑并广播 StateChanged 事件。
        /// </summary>
        /// <param name="nextState">目标状态。</param>
        private void ChangeState(GameState nextState)
        {
            if (nextState == GameState.Preparation)
            {
                BeginPreparation();
                return;
            }

            if (nextState == GameState.Battle || nextState == GameState.TurnEnd)
            {
                battleFinishedPlayerIds.Clear();
            }

            if (nextState == GameState.TurnEnd)
            {
                foreach (var pair in playersById)
                {
                    combatResolver.EndTurn(pair.Value);
                }
            }

            State = nextState;
            StateChanged?.Invoke(State);

            switch (nextState)
            {
                case GameState.GameStart:
                    ChangeState(GameState.Draw);
                    break;
                case GameState.Draw:
                    BeginNewRound();
                    break;
                case GameState.Discarding:
                    stateTimer = balanceConfig.discardSeconds;
                    break;
                case GameState.Battle:
                    stateTimer = balanceConfig.battleSeconds;
                    break;
                case GameState.TurnEnd:
                    FinishTurn();
                    break;
            }
        }


        /// <summary>
        /// 尝试提前结束战斗阶段（所有玩家均已结束出牌时）。
        /// </summary>
        private void TryFinishBattleEarly()
        {
            if (State == GameState.Battle && AreAllBattlePlayersFinished())
            {
                ChangeState(GameState.TurnEnd);
            }
        }

        /// <summary>
        /// 检查是否所有存活玩家都已结束战斗阶段出牌。
        /// </summary>
        private bool AreAllBattlePlayersFinished()
        {
            if (playersById.Count == 0)
            {
                return false;
            }

            foreach (var pair in playersById)
            {
                var player = pair.Value;
                if (player != null && !player.isDefeated && !battleFinishedPlayerIds.Contains(player.playerId))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 开始一个新回合，为所有玩家发牌并检查是否需要弃牌。
        /// </summary>
        private void BeginNewRound()
        {
            TurnIndex++;
            foreach (var pair in playersById)
            {
                combatResolver.BeginTurn(pair.Value);
                var drawnCards = cardManager.DrawForRound(pair.Value, TurnIndex);
                if (drawnCards.Count > 0)
                {
                    PublishHandUpdate(pair.Value);
                }
            }

            if (HasAnyOverflowHand())
            {
                BeginDiscardPhase();
                return;
            }

            BeginPreparation();
        }

        /// <summary>
        /// 结束当前回合，检查游戏胜负后进入下一回合的发牌阶段。
        /// </summary>
        private void FinishTurn()
        {
            if (CheckGameOver())
            {
                return;
            }

            ChangeState(GameState.Draw);
        }

        /// <summary>
        /// 进入弃牌阶段，向有溢出手牌的玩家发送弃牌请求。
        /// </summary>
        private void BeginDiscardPhase()
        {
            discardFinishedPlayerIds.Clear();
            ChangeState(GameState.Discarding);
            foreach (var pair in playersById)
            {
                var overflow = cardManager.CountOverflowCards(pair.Value);
                if (overflow <= 0)
                {
                    discardFinishedPlayerIds.Add(pair.Value.playerId);
                    continue;
                }

                DiscardRequested?.Invoke(new DiscardRequestMessageData
                {
                    playerId = pair.Value.playerId,
                    maxCards = pair.Value.deck.hand.Count - overflow,
                    currentCards = pair.Value.deck.hand.Count
                });
            }
        }

        /// <summary>
        /// 进入备战阶段，重置防御加成并启动备战计时。
        /// </summary>
        private void BeginPreparation()
        {
            preparationDefensePlayerIds.Clear();
            State = GameState.Preparation;
            stateTimer = balanceConfig.preparationSeconds;
            foreach (var pair in playersById)
            {
                pair.Value.shield = balanceConfig.initialDefense;
                pair.Value.preparationDefenseBonus = 0f;
                pair.Value.absorbShield = 0f;
            }

            StateChanged?.Invoke(State);
        }

        /// <summary>
        /// 检查是否有玩家手牌溢出上限。
        /// </summary>
        private bool HasAnyOverflowHand()
        {
            foreach (var pair in playersById)
            {
                if (cardManager.CountOverflowCards(pair.Value) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查是否所有有溢出手牌的玩家均已完成弃牌。
        /// </summary>
        private bool AreAllDiscardingPlayersFinished()
        {
            foreach (var pair in playersById)
            {
                if (!discardFinishedPlayerIds.Contains(pair.Key) && cardManager.CountOverflowCards(pair.Value) > 0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 强制弃掉所有玩家的溢出手牌（超时保护）。
        /// </summary>
        private void ForceDiscardOverflowHands()
        {
            foreach (var pair in playersById)
            {
                var guard = 64;
                while (cardManager.CountOverflowCards(pair.Value) > 0 && guard-- > 0)
                {
                    if (!cardManager.ForceDiscardRandomOverflow(pair.Value))
                    {
                        break;
                    }
                }

                PublishHandUpdate(pair.Value);
            }
        }

        /// <summary>
        /// 在玩家的手牌中查找指定 cardId 的索引（排除传入的排除索引）。
        /// </summary>
        /// <param name="player">玩家状态。</param>
        /// <param name="targetCardId">要查找的卡牌 Id。</param>
        /// <param name="excludedHandIndex">要排除的手牌索引。</param>
        /// <returns>找到的索引，未找到返回 -1。</returns>
        private int ResolveHandIndex(PlayerCombatStateData player, int preferredIndex,
            string instanceId, string cardId)
        {
            if (player?.deck?.hand == null)
                return -1;

            if (!string.IsNullOrEmpty(instanceId))
            {
                for (var index = 0; index < player.deck.hand.Count; index++)
                {
                    var instance = player.deck.hand[index];
                    if (instance != null && string.Equals(instance.instanceId, instanceId, StringComparison.Ordinal))
                        return index;
                }

                return -1;
            }

            if (preferredIndex >= 0 && preferredIndex < player.deck.hand.Count)
            {
                var preferred = player.deck.hand[preferredIndex];
                if (preferred != null && (string.IsNullOrEmpty(cardId) || preferred.cardId == cardId))
                    return preferredIndex;
            }

            if (!string.IsNullOrEmpty(cardId))
            {
                for (var index = 0; index < player.deck.hand.Count; index++)
                {
                    var instance = player.deck.hand[index];
                    if (instance != null && instance.cardId == cardId)
                        return index;
                }
            }

            return -1;
        }

        private int FindTargetBaseIndex(PlayerCombatStateData player, string targetCardId, int excludedHandIndex)
        {
            if (player?.deck?.hand == null || string.IsNullOrEmpty(targetCardId))
            {
                return -1;
            }

            for (var i = 0; i < player.deck.hand.Count; i++)
            {
                if (i == excludedHandIndex)
                {
                    continue;
                }

                var instance = player.deck.hand[i];
                if (instance != null && instance.cardId == targetCardId)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 判断一个裁决结果是否应触发战斗阶段提前结束（如 ExtraTurn 效果）。
        /// </summary>
        private static bool ShouldEndBattle(ActionResultData result)
        {
            if (result == null || result.effects == null)
            {
                return false;
            }

            for (var i = 0; i < result.effects.Count; i++)
            {
                if (result.effects[i] != null && result.effects[i].effectKind == CardEffectKind.ExtraTurn)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查是否有玩家生命值归零，触发游戏结束逻辑。
        /// </summary>
        /// <returns>游戏是否已结束。</returns>
        private bool CheckGameOver()
        {
            if (State == GameState.GameOver)
            {
                return true;
            }

            PlayerCombatStateData defeated = null;
            PlayerCombatStateData winner = null;

            foreach (var pair in playersById)
            {
                var player = pair.Value;
                if (player.hp <= 0)
                {
                    defeated = player;
                    continue;
                }

                winner = player;
            }

            if (defeated == null || winner == null)
            {
                return false;
            }

            State = GameState.GameOver;
            StateChanged?.Invoke(State);
            GameEnded?.Invoke(new GameOverMessageData
            {
                winnerId = winner.playerId,
                loserId = defeated.playerId,
                winnerHp = Mathf.RoundToInt(winner.hp),
                reason = "hp_zero"
            });
            return true;
        }

        /// <summary>
        /// 广播玩家手牌更新事件。
        /// </summary>
        /// <param name="player">手牌发生变化的玩家。</param>
        private void PublishHandUpdate(PlayerCombatStateData player)
        {
            HandUpdated?.Invoke(new HandUpdateMessageData
            {
                playerId = player.playerId,
                operation = MessageOperation.Replace,
                cards = new List<CardInstanceData>(player.deck.hand)
            });
        }
    }
