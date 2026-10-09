using UnityEngine;

    /// <summary>
    /// 运行时创建的强化卡定义，用于把表格或热更新数据临时注入卡牌目录。
    /// </summary>
    public sealed class SkillCardRt : SkillCardBase
    {
    }

    /// <summary>
    /// 本地内置强化卡注册入口，把技能表中的卡牌加入卡牌目录。
    /// </summary>
    public sealed class SkillCatalogHot : ISkillCatalogProvider
    {
        /// <summary>
        /// 将技能卡注册到运行时的热更卡牌目录中。
        /// </summary>
        public void AddTo(CardCatalog catalog)
        {
            SkillTable.AddTo(catalog);
        }
    }

    /// <summary>
    /// 保存内置强化卡表，并负责把卡牌标识、名称、数值和效果转成运行时卡牌定义。
    /// </summary>
    public static class SkillTable
    {
        /// <summary>
        /// 将技能卡注册到运行时的热更卡牌目录中。
        /// </summary>
        public static void AddTo(CardCatalog catalog)
        {
            if (catalog == null || catalog.cards == null)
            {
                return;
            }

            Add(catalog, "sk_s_atk_charge", "蓄力", "本次攻击力 +2。", BaseCardType.Attack, 2, CardQuality.Silver, false);
            Add(catalog, "sk_s_atk_pierce", "穿透", "本次攻击无视目标 3 点防御。", BaseCardType.Attack, 0, CardQuality.Silver, false, Fx(CardEffectKind.DefensePierceValue, 3));
            Add(catalog, "sk_s_atk_burn", "灼烧", "命中后造成每秒 0.5 灼烧伤害，持续 10 秒。", BaseCardType.Attack, 0, CardQuality.Silver, false, Fx(CardEffectKind.Burn, 0.5f, seconds: 10));
            Add(catalog, "sk_s_atk_blood", "吸血", "本次攻击造成伤害后回复等额生命。", BaseCardType.Attack, 0, CardQuality.Silver, false, Fx(CardEffectKind.LifeStealRatio, 1));
            Add(catalog, "sk_s_atk_smash", "猛击", "本次攻击力 +4，自身受到 1 点反噬伤害，不会致死。", BaseCardType.Attack, 4, CardQuality.Silver, false, Fx(CardEffectKind.SelfDamage, 1));
            Add(catalog, "sk_s_atk_aim", "精准", "本次攻击直接无视目标护盾。", BaseCardType.Attack, 0, CardQuality.Silver, false, Fx(CardEffectKind.IgnoreShield));

            Add(catalog, "sk_s_def_wall", "铁壁", "防御 +2，总计提供 3 点防御。", BaseCardType.Defense, 2, CardQuality.Silver, true);
            Add(catalog, "sk_s_def_shield", "护盾", "本回合生成可吸收 3 点伤害的护盾。", BaseCardType.Defense, 0, CardQuality.Silver, true, Fx(CardEffectKind.AbsorbShield, 3));
            Add(catalog, "sk_s_def_counter", "反震", "下一次受到攻击时，将被防御抵消的伤害反弹给敌人。", BaseCardType.Defense, 0, CardQuality.Silver, true, Fx(CardEffectKind.ReflectBlockedDamage, 1));
            Add(catalog, "sk_s_def_tough", "坚韧", "下一次受到攻击时，最终伤害额外减免 1。", BaseCardType.Defense, 0, CardQuality.Silver, true, Fx(CardEffectKind.ReduceNextDamage, 1));
            Add(catalog, "sk_s_def_firm", "坚定", "本回合免疫负面状态。", BaseCardType.Defense, 0, CardQuality.Silver, true, Fx(CardEffectKind.DebuffImmune, untilTurnEnd: true));
            Add(catalog, "sk_s_def_block", "格挡", "下一次受到攻击时，攻击力先减半再结算防御。", BaseCardType.Defense, 0, CardQuality.Silver, true, Fx(CardEffectKind.HalfNextAttack));

            Add(catalog, "sk_s_heal_bandage", "包扎", "本次治疗 +1，总共恢复 2 点生命。", BaseCardType.Heal, 1, CardQuality.Silver, true);
            Add(catalog, "sk_s_heal_firstaid", "急救", "立刻恢复 5 点生命。", BaseCardType.Heal, 4, CardQuality.Silver, true);
            Add(catalog, "sk_s_heal_cleanse", "净化", "清除自身一个负面状态。", BaseCardType.Heal, -1, CardQuality.Silver, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.CleanseDebuff));
            Add(catalog, "sk_s_heal_share", "共享", "自身恢复 3 点生命，敌方同步恢复 1 点生命。", BaseCardType.Heal, 2, CardQuality.Silver, true, Fx(CardEffectKind.HealEnemy, 1));
            Add(catalog, "sk_s_heal_sustain", "持续", "6 秒内每 2 秒恢复 1 点生命。", BaseCardType.Heal, -1, CardQuality.Silver, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.HealOverTime, 1, 2, seconds: 6));
            Add(catalog, "sk_s_heal_regen", "再生", "后续 3 秒每秒恢复 1 点生命。", BaseCardType.Heal, -1, CardQuality.Silver, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.HealOverTime, 1, 1, seconds: 3));

            Add(catalog, "sk_g_atk_double", "双重打击", "本次攻击造成双倍伤害。", BaseCardType.Attack, 0, CardQuality.Gold, false, Fx(CardEffectKind.DamageMultiplier, 2));
            Add(catalog, "sk_g_atk_break", "破甲", "本次攻击无视目标 10 点防御。", BaseCardType.Attack, 0, CardQuality.Gold, false, Fx(CardEffectKind.DefensePierceValue, 10));
            Add(catalog, "sk_g_atk_deadly", "致命", "本次攻击力 +5。", BaseCardType.Attack, 5, CardQuality.Gold, false);
            Add(catalog, "sk_g_atk_weak", "虚弱打击", "命中后敌人下次攻击力 -2，并降低 5 点防御。", BaseCardType.Attack, 0, CardQuality.Gold, false, Fx(CardEffectKind.WeakenNextAttack, 2), Fx(CardEffectKind.ReduceDefense, 5));
            Add(catalog, "sk_g_atk_execute", "处决", "目标生命值小于等于 5 时，本次攻击 *3。", BaseCardType.Attack, 0, CardQuality.Gold, false, Fx(CardEffectKind.ExecuteLowHp, 5, 3));

            Add(catalog, "sk_g_def_holy", "圣盾", "无条件抵挡一次任意数值的伤害。", BaseCardType.Defense, 0, CardQuality.Gold, true, Fx(CardEffectKind.HolyShield));
            Add(catalog, "sk_g_def_thorn", "荆棘", "本回合每次受到攻击，自动反击 1 点伤害。", BaseCardType.Defense, 0, CardQuality.Gold, true, Fx(CardEffectKind.Thorn, 1, untilTurnEnd: true));
            Add(catalog, "sk_g_def_absorb", "吸收", "本回合防御抵消的伤害会转化为生命。", BaseCardType.Defense, 0, CardQuality.Gold, true, Fx(CardEffectKind.AbsorbBlockedHeal, untilTurnEnd: true));
            Add(catalog, "sk_g_def_guard", "守护", "防御 +10。", BaseCardType.Defense, 10, CardQuality.Gold, true);
            Add(catalog, "sk_g_def_reflect", "反射", "下一次受到伤害时，反射自身实际承伤的 50%。", BaseCardType.Defense, 0, CardQuality.Gold, true, Fx(CardEffectKind.Thorn, 0.5f));

            Add(catalog, "sk_g_heal_spring", "妙手回春", "立刻恢复 10 点生命。", BaseCardType.Heal, 9, CardQuality.Gold, true);
            Add(catalog, "sk_g_heal_convert", "伤害转换", "本回合受到伤害的 1/3 转换为治疗。", BaseCardType.Heal, -1, CardQuality.Gold, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.DamageToHeal, 0.333f, untilTurnEnd: true));
            Add(catalog, "sk_g_heal_revive", "复苏", "接下来 3 个回合，每回合开局恢复 5。", BaseCardType.Heal, -1, CardQuality.Gold, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.RegenTurnStart, 5, durationTurns: 3));
            Add(catalog, "sk_g_heal_sacrifice", "牺牲", "消耗 3 点生命，随机恢复 8~15 点生命。", BaseCardType.Heal, -1, CardQuality.Gold, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.SacrificeHeal, 3, 8, 15));
            Add(catalog, "sk_g_heal_link", "生命链接", "恢复 3 点生命；本回合受过攻击则额外恢复 2 点。", BaseCardType.Heal, 2, CardQuality.Gold, true, Fx(CardEffectKind.ConditionalHealIfHit, 2));

            Add(catalog, "sk_p_atk_void", "虚空打击", "真实伤害，完全无视防御与护盾，攻击力 *2。", BaseCardType.Attack, 0, CardQuality.Platinum, false, Fx(CardEffectKind.TrueDamageMultiplier, 2));
            Add(catalog, "sk_p_atk_ruin", "毁灭", "摧毁敌方所有护盾，造成 7 点真实伤害，自身承受 2 点反噬伤害。", BaseCardType.Attack, -1, CardQuality.Platinum, false, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.DestroyShield), Fx(CardEffectKind.TrueDamage, 7), Fx(CardEffectKind.SelfDamage, 2));
            Add(catalog, "sk_p_atk_suppress", "压制", "命中后敌人 2 秒内无法打出任何手牌。", BaseCardType.Attack, 0, CardQuality.Platinum, false, Fx(CardEffectKind.DisableCards, seconds: 2));
            Add(catalog, "sk_p_atk_shatter", "碎甲", "击碎敌方一半防御；若目标无防御则伤害 +5。", BaseCardType.Attack, 0, CardQuality.Platinum, false, Fx(CardEffectKind.ShatterDefense, 5));

            Add(catalog, "sk_p_def_abs", "绝对防御", "持续 5 秒，防御值翻倍，受到攻击最终伤害 -1。", BaseCardType.Defense, 0, CardQuality.Platinum, true, Fx(CardEffectKind.DoubleDefense, 1, seconds: 5));
            Add(catalog, "sk_p_def_revenge", "复仇", "获得 5 点吸收护盾；护盾被击破时反击 3 点真实伤害。", BaseCardType.Defense, 0, CardQuality.Platinum, true, Fx(CardEffectKind.AbsorbShield, 5), Fx(CardEffectKind.RevengeShield, 3));
            Add(catalog, "sk_p_def_unyield", "不屈", "即将承受致死伤害时自动触发，保留 1 点生命。", BaseCardType.Defense, 0, CardQuality.Platinum, true, Fx(CardEffectKind.FatalGuard));
            Add(catalog, "sk_p_def_mirror", "镜像", "本回合每次受到攻击，敌方受到同等伤害。", BaseCardType.Defense, 0, CardQuality.Platinum, true, Fx(CardEffectKind.MirrorDamage, untilTurnEnd: true));

            Add(catalog, "sk_p_heal_holy", "圣疗", "立刻恢复 25 点生命。", BaseCardType.Heal, 24, CardQuality.Platinum, true);
            Add(catalog, "sk_p_heal_redeem", "救赎", "恢复 10 点生命。", BaseCardType.Heal, 9, CardQuality.Platinum, true);
            Add(catalog, "sk_p_heal_leech", "生命窃取", "本回合下次攻击每造成 1 点伤害，自身恢复 3 点生命。", BaseCardType.Heal, -1, CardQuality.Platinum, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.LifeSteal, 3));
            Add(catalog, "sk_p_heal_reverse", "逆转", "自身血量低于敌方时，回复双方血量差值，单次上限 30。", BaseCardType.Heal, -1, CardQuality.Platinum, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.HealBoost, 0), Fx(CardEffectKind.SwapHp, 30));

            Add(catalog, "sk_r_atk_god", "弑神一击", "直接造成 15 点真实伤害。", BaseCardType.Attack, -1, CardQuality.Prismatic, false, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.TrueDamage, 15));
            Add(catalog, "sk_r_atk_flame", "狂风烈焰斩", "连续 3 次 6 攻击力的灼烧攻击；任意一击未被完全格挡，敌人每秒受到 1 点真实伤害，持续到本回合结束。", BaseCardType.Attack, -1, CardQuality.Prismatic, false, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.MultiStrikeBurn, 6, 3, 1));
            Add(catalog, "sk_r_def_immortal", "不朽", "本回合不会被击败，生命最低保留 1。", BaseCardType.Defense, 0, CardQuality.Prismatic, true, Fx(CardEffectKind.FatalGuard, untilTurnEnd: true));
            Add(catalog, "sk_r_def_bless", "天佑", "本回合获得 50 点吸收护盾；护盾存续期间受击会反击 2 点伤害并禁牌 1 秒。", BaseCardType.Defense, 0, CardQuality.Prismatic, true, Fx(CardEffectKind.AbsorbShield, 50), Fx(CardEffectKind.RevengeShield, 2, 1));
            Add(catalog, "sk_r_heal_grace", "神之恩赐", "生命值直接补满至 50。", BaseCardType.Heal, -1, CardQuality.Prismatic, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.SetHpMin, 50));
            Add(catalog, "sk_r_heal_karma", "因果逆转", "互换双方当前生命，自身额外恢复 10；直接结束当前回合。", BaseCardType.Heal, -1, CardQuality.Prismatic, true, Fx(CardEffectKind.SetPower, 0), Fx(CardEffectKind.SwapHp, -1, 10), Fx(CardEffectKind.ExtraTurn));
        }

        /// <summary>
        /// 添加一项技能卡关系到运行时目录。
        /// </summary>
        private static void Add(CardCatalog catalog, string id, string name, string desc, BaseCardType type, int power, CardQuality quality, bool pre, params CardEffectData[] effects)
        {
            for (var i = 0; i < catalog.cards.Count; i++)
            {
                if (catalog.cards[i] != null && catalog.cards[i].CardId == id)
                {
                    return;
                }
            }

            var card = ScriptableObject.CreateInstance<SkillCardRt>();
            card.ConfigureRuntime(id, name, desc, type, power, quality, pre, effects);
            catalog.cards.Add(card);
        }

        /// <summary>
        /// 应用聚合运算出最终数值。
        /// </summary>
        private static CardEffectData Fx(CardEffectKind kind, float v = 0f, float v2 = 0f, float v3 = 0f, float seconds = 0f, int durationTurns = 1, bool untilTurnEnd = false)
        {
            return new CardEffectData
            {
                effectKind = kind,
                value = v,
                value2 = v2,
                value3 = v3,
                seconds = seconds,
                durationTurns = durationTurns,
                untilTurnEnd = untilTurnEnd
            };
        }
    }
