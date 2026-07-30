using System;
using UnityEngine;

    /// <summary>
    /// 热更层技能入口，负责创建强化卡牌并把数值调整暴露给 HybridCLR。
    /// </summary>
    public static class SkillHot
    {
        // 热更 DLL 中技能目录提供者的完整类型名，包含程序集名，供 Type.GetType 直接查找。
        private const string ProviderTypeName = "SkillCatalogHot, HotUpdate";

        // 已创建的热更技能目录提供者；缓存后可以避免每次进入战斗都重复反射。
        private static ISkillCatalogProvider provider;

        // 标记是否已经尝试查找过 provider；查找失败也会记录，避免每次 AddTo 都扫描程序集。
        private static bool searched;

        /// <summary>
        /// 向卡牌目录写入强化卡牌定义。
        /// 如果热更 DLL 已经加载，就使用热更层数据；否则使用主工程内置技能表兜底。
        /// </summary>
        /// <param name="catalog">需要追加强化卡定义的卡牌目录。</param>
        public static void AddTo(CardCatalog catalog)
        {
            if (catalog == null)
            {
                return;
            }

            if (TryGetProvider(out var hot))
            {
                // 热更层存在时，技能数值和效果都由热更代码决定。
                hot.AddTo(catalog);
                return;
            }

            Debug.LogWarning("热更新技能程序集未加载，当前仅使用基础卡牌目录。");
        }

        /// <summary>
        /// 获取热更技能目录提供者。
        /// 第一次调用会触发反射查找，之后直接返回缓存结果。
        /// </summary>
        /// <param name="hot">成功时返回热更技能目录提供者；失败时返回 null。</param>
        private static bool TryGetProvider(out ISkillCatalogProvider hot)
        {
            if (!searched)
            {
                searched = true;
                provider = CreateProvider();
            }

            hot = provider;
            return hot != null;
        }

        /// <summary>
        /// 处理 CreateProvider 对应的运行时流程。
        /// </summary>
        private static ISkillCatalogProvider CreateProvider()
        {
            var type = Type.GetType(ProviderTypeName) ?? FindType("SkillCatalogHot");
            if (type == null)
            {
                Debug.LogWarning("热更技能目录未加载，使用主工程内置技能表。");
                return null;
            }

            try
            {
                return Activator.CreateInstance(type) as ISkillCatalogProvider;
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建热更技能目录失败：{ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 在当前 AppDomain 已加载的程序集中查找指定类型。
        /// </summary>
        /// <param name="fullName">要查找的完整类型名，不包含程序集名。</param>
        private static Type FindType(string fullName)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var i = 0; i < assemblies.Length; i++)
            {
                var type = assemblies[i].GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
