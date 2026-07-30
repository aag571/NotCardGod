using System;
using System.Collections.Generic;
using UnityEngine;

    /// <summary>
    /// 统一持有运行期配置、卡牌目录、Boss 配置、玩家资料、解锁进度和体力状态。
    /// 所有运行时数据驱动的来源，全局唯一，通过 GameApp 获取。
    /// </summary>
    public sealed class GameDataRepository
    {
        public BalanceConfig BalanceConfig { get; private set; }
        public CardCatalog CardCatalog { get; private set; }
        public BossConfig BossConfig { get; private set; }
        public AiBrainConfigData AiBrainConfig { get; private set; }
        public PlayerProfileData PlayerProfile { get; private set; }
        public UnlockProgressData UnlockProgress { get; private set; }
        public StaminaStateData StaminaState { get; private set; }

        // 任一数据字段变更时触发
        public event Action DataChanged;

        public void NotifyDataChanged() => DataChanged?.Invoke();

        /// <summary>
        /// 设置静态配置（运行时不变的数据表）。
        /// </summary>
        /// <param name="balanceConfig">数值平衡配置。</param>
        /// <param name="cardCatalog">卡牌目录。</param>
        /// <param name="bossConfig">Boss 配置。</param>
        /// <param name="aiBrainConfig">AI 行为配置。</param>
        public void SetStaticConfigs(
            BalanceConfig balanceConfig,
            CardCatalog cardCatalog,
            BossConfig bossConfig,
            AiBrainConfigData aiBrainConfig)
        {
            BalanceConfig = balanceConfig;
            CardCatalog = cardCatalog;
            BossConfig = bossConfig;
            AiBrainConfig = aiBrainConfig;
            DataChanged?.Invoke();
        }

        /// <summary>
        /// 设置玩家存档数据（可变数据）。
        /// </summary>
        /// <param name="playerProfile">玩家资料。</param>
        /// <param name="unlockProgress">解锁进度。</param>
        /// <param name="staminaState">体力状态。</param>
        public void SetAccountData(
            PlayerProfileData playerProfile,
            UnlockProgressData unlockProgress,
            StaminaStateData staminaState)
        {
            PlayerProfile = playerProfile;
            UnlockProgress = unlockProgress;
            StaminaState = staminaState;
            DataChanged?.Invoke();
        }

        /// <summary>
        /// 按 CardId 从 CardCatalog 中查找卡牌定义。
        /// </summary>
        /// <param name="cardId">卡牌唯一标识。</param>
        /// <param name="card">查到的卡牌定义，未找到时为 null。</param>
        /// <returns>是否找到。</returns>
        public bool TryGetCard(string cardId, out CardBase card)
        {
            card = null;
            if (CardCatalog == null || CardCatalog.cards == null)
            {
                return false;
            }

            for (var i = 0; i < CardCatalog.cards.Count; i++)
            {
                var candidate = CardCatalog.cards[i];
                if (candidate != null && candidate.CardId == cardId)
                {
                    card = candidate;
                    return true;
                }
            }

            return false;
        }
    }
