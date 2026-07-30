using System;
using UnityEngine;

    /// <summary>
    /// AOT 侧的战斗规则工厂。CombatResolver 的实现由 HotUpdate.dll 提供。
    /// </summary>
    public static class CombatHot
    {
        private const string ResolverTypeName = "CombatResolver, HotUpdate";

        public static ICombatResolver Create(BalanceConfig balanceConfig)
        {
            var type = Type.GetType(ResolverTypeName) ?? FindType("CombatResolver");
            if (type == null)
            {
                throw new InvalidOperationException(
                    "CombatResolver 未加载。请先加载 HotUpdate.dll.bytes，再创建战斗控制器。");
            }

            try
            {
                // 显式传入 object[]，避免 Unity/Mono 将 BalanceConfig 误绑定到
                // Activator.CreateInstance(Type, bool) 的重载，进而尝试无参构造。
                return Activator.CreateInstance(type, new object[] { balanceConfig }) as ICombatResolver
                    ?? throw new InvalidOperationException("HotUpdate CombatResolver 未实现 ICombatResolver。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建 HotUpdate CombatResolver 失败：{ex}");
                throw;
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
