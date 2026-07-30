using System;
using System.Collections.Generic;

    /// <summary>
    /// 保存一个参与权重随机的值和对应权重。
    /// </summary>
    public sealed class WeightedItem<T>
    {
        // 实际会被随机返回的值。
        public T value;

        // 随机时使用的相对权重，必须大于 0 才会参与抽取。
        public int weight;

        /// <summary>
        /// 创建一个权重项。
        /// </summary>
        public WeightedItem(T value, int weight)
        {
            this.value = value;
            this.weight = weight;
        }
    }

    /// <summary>
    /// 提供按整数权重随机选取元素的工具方法。
    /// </summary>
    public static class WeightedRandomPicker
    {
        /// <summary>
        /// 从权重项列表中随机选出一个值。
        /// </summary>
        public static T Pick<T>(IList<WeightedItem<T>> items, Random random)
        {
            if (items == null || items.Count == 0)
            {
                throw new ArgumentException("权重列表不能为空。", nameof(items));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var totalWeight = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].weight > 0)
                {
                    totalWeight += items[i].weight;
                }
            }

            if (totalWeight <= 0)
            {
                throw new InvalidOperationException("至少需要一个权重大于 0 的选项。");
            }

            var roll = random.Next(0, totalWeight);
            var cursor = 0;
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null || item.weight <= 0)
                {
                    continue;
                }

                cursor += item.weight;
                if (roll < cursor)
                {
                    return item.value;
                }
            }

            return items[items.Count - 1].value;
        }
    }
