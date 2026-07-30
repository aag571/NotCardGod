
    /// <summary>
    /// 统一的账号服务接口，在线/离线模式共用同一契约。
    /// </summary>
    public interface IAccountService
    {
        
        int Stamina { get; }

        
        int MaxStamina { get; }

        
        int SkipCards { get; }

        
        int PendingPveReward { get; }

        
        
        bool TrySpendStamina(int amount);

        
        int AddStamina(int amount);

        
        void MarkShared();

        
        bool ClaimShareStamina(int amount, out int added);

        
        bool ClaimThreeBattleStamina(int amount, out int added);

        
        bool ClaimSkipCardByAd(int dailyLimit);

        
        bool ClaimPveReward(out string msg);

        
        bool TryUseSkipCard();

        
        void RecordPveResult(bool win, bool skipped);
    }
