using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;

    /// <summary>
    /// YooAsset 包更新进度快照。
    /// 加载界面通过它显示当前包名、下载字节数和状态说明。
    /// </summary>
    [Serializable]
    public sealed class HotUpdateProgressData
    {
        // 当前正在初始化、检查或下载的资源包名称。
        public string packageName;

        // 当前操作已经下载的字节数。
        public long downloadedBytes;

        // 当前操作预计需要下载的总字节数。
        public long totalBytes;

        // 归一化进度，范围为 0 到 1；总字节数未知时返回 0。
        public float Progress => totalBytes <= 0 ? 0f : (float)downloadedBytes / totalBytes;

        // 给加载界面或日志显示的状态文本。
        public string message;
    }

    /// <summary>
    /// 资源热更和代码热更共用的结果对象。
    /// 它记录操作是否成功，以及本次加载了哪些程序集或资源包。
    /// </summary>
    [Serializable]
    public sealed class HotUpdateResultData
    {
        // 当前操作是否成功完成。
        public bool success;

        // 失败时记录错误信息。
        public string error;

        // 本次成功加载的热更程序集名称。
        public List<string> loadedAssemblies = new List<string>();

        // 本次初始化或更新过的 YooAsset 包名称。
        public List<string> updatedPackages = new List<string>();
    }

    /// <summary>
    /// 定义 IResourceUpdateService 类型，承载对应模块的运行时职责。
    /// </summary>
    public interface IResourceUpdateService
    {
        event Action<HotUpdateProgressData> ProgressChanged;

        Task<HotUpdateResultData> InitializeAsync(CancellationToken cancellationToken);
        Task<HotUpdateResultData> CheckAndDownloadAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// 定义 ICodeHotUpdateService 类型，承载对应模块的运行时职责。
    /// </summary>
    public interface ICodeHotUpdateService
    {
        Task<HotUpdateResultData> LoadHotfixAssembliesAsync(IList<byte[]> assemblyBytes, CancellationToken cancellationToken);
    }

    /// <summary>
    /// 定义 IGameAssetService 类型，承载对应模块的运行时职责。
    /// </summary>
    public interface IGameAssetService
    {
        bool Ready { get; }
        string PackageName { get; }

        Task<T> LoadAssetAsync<T>(string location, CancellationToken cancellationToken) where T : UnityEngine.Object;
        Task LoadSceneAsync(string location, LoadSceneMode mode, CancellationToken cancellationToken);
        void ReleaseAll();
    }

    /// <summary>
    /// 战斗规则的 AOT/HotUpdate 边界。具体效果结算实现位于 HotUpdate 程序集。
    /// </summary>
    public interface ICombatResolver
    {
        ActionResultData Resolve(CardReleaseResult release, PlayerCombatStateData actor, PlayerCombatStateData target);
        void TickBuffs(IEnumerable<PlayerCombatStateData> players, float dt);
        void BeginTurn(PlayerCombatStateData player);
        void ClearTurnShield(PlayerCombatStateData player);
        bool IsActionBlocked(PlayerCombatStateData player);
    }

    /// <summary>
    /// 发牌、合成、出牌和弃牌规则的 AOT/HotUpdate 边界。
    /// </summary>
    public interface ICardManager
    {
        int EnhSlotMax { get; }
        void RegisterCatalog(CardCatalog cardCatalog);
        bool TryGetCard(string cardId, out CardBase card);
        void InitializeDeck(PlayerCombatStateData player, IList<string> unlockedStrengthenedCardIds);
        List<CardInstanceData> DrawForRound(PlayerCombatStateData player, int turnIndex);
        bool TryAttachSkillToBase(PlayerCombatStateData player, int skillHandIndex, int targetBaseHandIndex,
            CardReleaseContext context, out int baseHandIdx, out bool slotFull, out string reason);
        bool TryPlayFromHand(PlayerCombatStateData player, int handIndex, CardReleaseContext context, out CardReleaseResult result);
        bool TryDiscard(PlayerCombatStateData player, int handIndex, out CardInstanceData discardedCard);
        int CountOverflowCards(PlayerCombatStateData player);
        bool IsHandWithinLimits(PlayerCombatStateData player);
        bool ForceDiscardRandomOverflow(PlayerCombatStateData player);
    }

    /// <summary>
    /// AI 决策规则的 AOT/HotUpdate 边界。
    /// </summary>
    public interface IAIController
    {
        AiDecisionData Decide(PlayerCombatStateData aiPlayer, PlayerCombatStateData opponent, GameState gameState);
    }

    [Serializable]
    public sealed class AiDecisionData
    {
        public bool hasAction;
        public float delaySeconds;
        public PlayerActionMessageData action = new PlayerActionMessageData();
        public string reason;
    }
