using System;
using System.Collections.Generic;
using UnityEngine;

    /// <summary>
    /// 保存卡牌正反面、品级和基础牌类型对应的美术资源，供战斗卡牌视图查询显示。
    /// </summary>
    [CreateAssetMenu(fileName = "CardArtConfig", menuName = "NotCardGod/Data/Card Art Config")]
    public sealed class CardArtConfig : ScriptableObject
    {
        [SerializeField] private Sprite defaultFront;
        [SerializeField] private Sprite defaultBack;
        [SerializeField] private Sprite commonBack;
        [SerializeField] private List<CardArt> arts = new List<CardArt>();
        [SerializeField] private List<BaseCardArt> baseArts = new List<BaseCardArt>();

        // 强化卡牌美术资源只读视图
        public IReadOnlyList<CardArt> Arts => arts;

        // 基础手牌美术资源只读视图
        public IReadOnlyList<BaseCardArt> BaseArts => baseArts;

        /// <summary>
        /// 获取指定品质的强化卡正面图。
        /// </summary>
        /// <param name="quality">卡牌品质。</param>
        /// <returns>正面 Sprite，未找到时返回默认正面。</returns>
        public Sprite Front(CardQuality quality)
        {
            var art = Find(quality);
            return art != null && art.front != null ? art.front : defaultFront;
        }

        /// <summary>
        /// 获取指定品质的强化卡背面图。
        /// </summary>
        /// <param name="quality">卡牌品质。</param>
        /// <returns>背面 Sprite，未找到时返回默认背面。</returns>
        public Sprite Back(CardQuality quality)
        {
            var art = Find(quality);
            return art != null && art.back != null ? art.back : defaultBack;
        }

        /// <summary>
        /// 获取指定类型的基础牌正面图。
        /// </summary>
        /// <param name="type">基础牌类型。</param>
        /// <returns>正面 Sprite。</returns>
        public Sprite BaseFront(BaseCardType type)
        {
            var art = FindBase(type);
            return art != null ? art.front : null;
        }

        /// <summary>
        /// 获取基础牌默认背面图。
        /// </summary>
        /// <returns>背面 Sprite。</returns>
        public Sprite BaseBack()
        {
            return commonBack != null ? commonBack : defaultBack;
        }

        /// <summary>
        /// 设置默认的正反面对。
        /// </summary>
        /// <param name="front">默认正面。</param>
        /// <param name="back">默认背面。</param>
        public void SetDefault(Sprite front, Sprite back)
        {
            defaultFront = front;
            defaultBack = back;
        }

        /// <summary>
        /// 设置指定品质的强化卡正反面。
        /// </summary>
        /// <param name="quality">卡牌品质。</param>
        /// <param name="front">正面 Sprite。</param>
        /// <param name="back">背面 Sprite。</param>
        public void Set(CardQuality quality, Sprite front, Sprite back)
        {
            var art = Find(quality);
            if (art == null)
            {
                art = new CardArt { quality = quality };
                arts.Add(art);
            }

            art.front = front;
            art.back = back;
        }

        /// <summary>
        /// 设置基础牌默认背面。
        /// </summary>
        /// <param name="back">背面 Sprite。</param>
        public void SetBaseBack(Sprite back)
        {
            commonBack = back;
        }

        /// <summary>
        /// 设置指定类型的基础牌正面。
        /// </summary>
        /// <param name="type">基础牌类型。</param>
        /// <param name="front">正面 Sprite。</param>
        public void SetBase(BaseCardType type, Sprite front)
        {
            var art = FindBase(type);
            if (art == null)
            {
                art = new BaseCardArt { type = type };
                baseArts.Add(art);
            }

            art.front = front;
        }

        /// <summary>
        /// 按品质查找强化卡美术资源。
        /// </summary>
        /// <param name="quality">卡牌品质。</param>
        /// <returns>找到的 CardArt，未找到返回 null。</returns>
        private CardArt Find(CardQuality quality)
        {
            for (var i = 0; i < arts.Count; i++)
            {
                if (arts[i] != null && arts[i].quality == quality)
                {
                    return arts[i];
                }
            }
            
            return null;
        }

        /// <summary>
        /// 按类型查找基础牌美术资源。
        /// </summary>
        /// <param name="type">基础牌类型。</param>
        /// <returns>找到的 BaseCardArt，未找到返回 null。</returns>
        private BaseCardArt FindBase(BaseCardType type)
        {
            for (var i = 0; i < baseArts.Count; i++)
            {
                if (baseArts[i] != null && baseArts[i].type == type)
                {
                    return baseArts[i];
                }
            }
            
            return null;
        }
    }

    /// <summary>
    /// 强化卡牌在指定品级下的美术资源。
    /// </summary>
    [Serializable]
    public sealed class CardArt
    {
        // 卡牌品质
        public CardQuality quality;
        // 卡牌正面 Sprite
        public Sprite front;
        // 卡牌背面 Sprite
        public Sprite back;
    }

    /// <summary>
    /// 基础手牌类型对应的美术资源。
    /// </summary>
    [Serializable]
    public sealed class BaseCardArt
    {
        // 基础牌类型
        public BaseCardType type;
        // 卡牌正面 Sprite
        public Sprite front;
    }
