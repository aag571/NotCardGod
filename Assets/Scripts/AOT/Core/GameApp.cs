
    /// <summary>
    /// 项目运行时服务入口，负责初始化并持有全局服务。
    /// </summary>
    public static class GameApp
    {
        // 全局服务注册表，保存当前运行期已经创建的服务实例。
        private static readonly GameServiceRegistry Services = new GameServiceRegistry();

        /// <summary>
        /// 注册或替换一个服务实例。
        /// </summary>
        /// <param name="service">要保存到注册表中的服务实例。</param>
        public static void Register<TService>(TService service) where TService : class
        {
            Services.Register(service);
        }

        /// <summary>
        /// 尝试获取服务，缺失时不会抛异常。
        /// </summary>
        /// <param name="service">成功时返回解析到的服务实例。</param>
        public static bool TryGet<TService>(out TService service) where TService : class
        {
            return Services.TryResolve(out service);
        }

        /// <summary>
        /// 获取必需服务，服务不存在时抛异常。
        /// </summary>
        public static TService Get<TService>() where TService : class
        {
            return Services.Resolve<TService>();
        }

        // 当前资源服务是否已经初始化完成。
        public static bool AssetsReady =>
            TryGet<IGameAssetService>(out var assets) && assets.Ready;

        /// <summary>
        /// 处理 Clear 对应的运行时流程。
        /// </summary>
        public static void Clear()
        {
            Services.Clear();
        }
    }
