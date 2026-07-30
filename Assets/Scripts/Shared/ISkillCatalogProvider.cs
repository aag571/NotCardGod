
    /// <summary>
    /// 定义热更新程序集向主工程提供卡牌目录注册能力的接口。
    /// </summary>
    public interface ISkillCatalogProvider
    {
        /// <summary>
        /// 处理 AddTo 方法的业务逻辑。
        /// </summary>
        void AddTo(CardCatalog catalog);
    }
