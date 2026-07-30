using System;
using System.Collections.Generic;

    /// <summary>
    /// 保存运行时服务实例，并提供按类型注册、查询和清理的能力。
    /// </summary>
    public sealed class GameServiceRegistry
    {
        // 已注册服务实例，键为服务接口或服务类型。
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        /// <summary>
        /// 注册或移除指定类型的服务实例。
        /// </summary>
        public void Register<TService>(TService service) where TService : class
        {
            if (service == null)
            {
                services.Remove(typeof(TService));
                return;
            }

            services[typeof(TService)] = service;
        }

        /// <summary>
        /// 尝试获取指定类型的服务实例。
        /// </summary>
        public bool TryResolve<TService>(out TService service) where TService : class
        {
            if (services.TryGetValue(typeof(TService), out var value))
            {
                service = value as TService;
                return service != null;
            }

            service = null;
            return false;
        }

        /// <summary>
        /// 获取指定类型的服务实例；未注册时抛出异常。
        /// </summary>
        public TService Resolve<TService>() where TService : class
        {
            if (TryResolve<TService>(out var service))
            {
                return service;
            }

            throw new InvalidOperationException($"服务 {typeof(TService).Name} 尚未注册。");
        }

        /// <summary>
        /// 处理 Clear 对应的运行时流程。
        /// </summary>
        public void Clear()
        {
            services.Clear();
        }
    }
