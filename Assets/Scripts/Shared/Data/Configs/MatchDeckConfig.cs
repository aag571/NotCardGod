using System;
using System.Collections.Generic;
using UnityEngine;

    /// <summary>
    /// 保存匹配对局中强化卡库存、品级权重、基础牌权重和技能类型权重。
    /// 用于 Match 模式的对战匹配发牌。
    /// </summary>
    [CreateAssetMenu(fileName = "MatchDeckConfig", menuName = "NotCardGod/Data/Match Deck Config")]
    public sealed class MatchDeckConfig : ScriptableObject
    {
        [Header("Limited Match Stock")]
        // 银卡是否无限供应
        public bool silverUnlimited = true;
        // 金卡是否无限供应
        public bool goldUnlimited = true;
        // 每局铂金卡总数上限
        public int platinumTotalPerMatch = 50;
        // 每张彩卡的副本数
        public int prismaticCopiesPerCard = 1;

        [Header("Base Card Weight")]
        // 基础牌类型的发牌权重表
        public List<BaseCardDrawWeight> baseWeights = new List<BaseCardDrawWeight>
        {
            new BaseCardDrawWeight { type = BaseCardType.Attack, weight = 75 },
            new BaseCardDrawWeight { type = BaseCardType.Defense, weight = 15 },
            new BaseCardDrawWeight { type = BaseCardType.Heal, weight = 10 }
        };

        [Header("Skill Quality Weight")]
        // 技能强化卡品级发牌权重表
        public List<QualityDrawWeight> qualityWeights = new List<QualityDrawWeight>
        {
            new QualityDrawWeight { quality = CardQuality.Silver, weight = 68 },
            new QualityDrawWeight { quality = CardQuality.Gold, weight = 22 },
            new QualityDrawWeight { quality = CardQuality.Platinum, weight = 8 },
            new QualityDrawWeight { quality = CardQuality.Prismatic, weight = 2 }
        };

        [Header("Skill Type Weight")]
        // 技能类型发牌权重表
        public List<BaseCardDrawWeight> skillTypeWeights = new List<BaseCardDrawWeight>
        {
            new BaseCardDrawWeight { type = BaseCardType.Attack, weight = 60 },
            new BaseCardDrawWeight { type = BaseCardType.Defense, weight = 25 },
            new BaseCardDrawWeight { type = BaseCardType.Heal, weight = 15 }
        };

        /// <summary>
        /// 获取指定基础牌类型的发牌权重。
        /// </summary>
        /// <param name="type">基础牌类型。</param>
        /// <returns>权重值。</returns>
        public int BaseWeight(BaseCardType type)
        {
            return FindBaseWeight(baseWeights, type);
        }

        /// <summary>
        /// 获取指定技能类型的发牌权重。
        /// </summary>
        /// <param name="type">技能类型。</param>
        /// <returns>权重值。</returns>
        public int SkillTypeWeight(BaseCardType type)
        {
            return FindBaseWeight(skillTypeWeights, type);
        }

        /// <summary>
        /// 获取指定品级的发牌权重。
        /// </summary>
        /// <param name="quality">强化卡品级。</param>
        /// <returns>权重值。</returns>
        public int QualityWeight(CardQuality quality)
        {
            if (qualityWeights == null)
            {
                return 0;
            }

            for (var i = 0; i < qualityWeights.Count; i++)
            {
                var item = qualityWeights[i];
                if (item != null && item.quality == quality)
                {
                    return Mathf.Max(0, item.weight);
                }
            }

            return 0;
        }

        /// <summary>
        /// 在权重列表中查找指定类型的权重值。
        /// </summary>
        /// <param name="list">权重列表。</param>
        /// <param name="type">基础牌类型。</param>
        /// <returns>权重值，未找到返回 0。</returns>
        private static int FindBaseWeight(IList<BaseCardDrawWeight> list, BaseCardType type)
        {
            if (list == null)
            {
                return 0;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                if (item != null && item.type == type)
                {
                    return Mathf.Max(0, item.weight);
                }
            }

            return 0;
        }
    }

    /// <summary>
    /// 基础牌类型在发牌随机池中的权重。
    /// </summary>
    [Serializable]
    public sealed class BaseCardDrawWeight
    {
        // 基础牌类型
        public BaseCardType type;
        // 发牌权重
        public int weight;
    }

    /// <summary>
    /// 强化卡品级在发牌随机池中的权重。
    /// </summary>
    [Serializable]
    public sealed class QualityDrawWeight
    {
        // 强化卡品级
        public CardQuality quality;
        // 发牌权重
        public int weight;
    }
