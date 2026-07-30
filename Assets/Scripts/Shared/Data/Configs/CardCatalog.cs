using System.Collections.Generic;
using UnityEngine;

    /// <summary>
    /// 战斗可用卡牌目录。
    /// CardManager 会按 CardId 建索引，并把这里的卡牌拆分为基础牌池和强化牌池。
    /// </summary>
    [CreateAssetMenu(fileName = "CardCatalog", menuName = "NotCardGod/Data/Card Catalog")]
    public sealed class CardCatalog : ScriptableObject
    {
        // 本地运行目录中允许出现的全部卡牌定义。
        public List<CardBase> cards = new List<CardBase>();
    }
