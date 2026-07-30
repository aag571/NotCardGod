using System;
using UnityEngine;

    /// <summary>
    /// 创建 HotUpdate 中的战斗业务实现。
    /// </summary>
    public static class GameplayHot
    {
        public static ICardManager CreateCardManager(BalanceConfig balance, MatchDeckConfig deck, CardCatalog catalog, int seed)
        {
            var type = FindType("CardManager");
            if (type == null)
            {
                throw Missing("CardManager");
            }

            return Create<ICardManager>(type, balance, deck, catalog, seed);
        }

        public static IAIController CreateAi(AiBrainConfigData config, ICardManager cardManager, int seed)
        {
            var type = FindType("AIController");
            if (type == null)
            {
                throw Missing("AIController");
            }

            return Create<IAIController>(type, config, cardManager, seed);
        }

        private static T Create<T>(Type type, params object[] args) where T : class
        {
            try
            {
                return Activator.CreateInstance(type, args) as T
                    ?? throw new InvalidOperationException($"{type.FullName} 未实现 {typeof(T).Name}。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建 HotUpdate {type.Name} 失败：{ex}");
                throw;
            }
        }

        private static Type FindType(string name)
        {
            var qualifiedName = name + ", HotUpdate";
            var type = Type.GetType(qualifiedName);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(name);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static InvalidOperationException Missing(string name)
        {
            return new InvalidOperationException($"{name} 未加载。请先加载 HotUpdate.dll.bytes，再创建战斗控制器。");
        }
    }
