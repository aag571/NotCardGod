using System;
using System.Collections.Generic;
using UnityEngine;

    /// <summary>
    /// 根据行动结果、卡牌效果、攻防治疗公式和状态修正计算战斗结算。
    /// </summary>
    public sealed class CombatResolver : ICombatResolver
    {
        private readonly BalanceConfig cfg;

        /// <summary>
        /// 构造战斗裁决器实例。
        /// </summary>
        public CombatResolver(BalanceConfig balanceConfig)
        {
            cfg = balanceConfig ?? throw new ArgumentNullException(nameof(balanceConfig));
        }

        /// <summary>
        /// 裁决一次卡牌释放的结果。
        /// </summary>
        public ActionResultData Resolve(CardReleaseResult release, PlayerCombatStateData actor, PlayerCombatStateData target)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            if (actor == null)
            {
                throw new ArgumentNullException(nameof(actor));
            }

            var result = new ActionResultData
            {
                actorId = actor.playerId,
                targetId = target != null ? target.playerId : release.targetPlayerId,
                cardId = release.cardId,
                cardName = release.cardName,
                baseType = release.baseType,
                animationId = ResolveAnimationId(release)
            };

            if (release.skillIds != null)
            {
                result.skillIds.AddRange(release.skillIds);
            }

            switch (release.baseType)
            {
                case BaseCardType.Attack:
                    ApplyAttack(release, actor, target, result);
                    break;
                case BaseCardType.Defense:
                    ApplyDefense(release, actor, result);
                    break;
                case BaseCardType.Heal:
                    ApplyHeal(release, actor, target, result);
                    break;
                default:
                    ApplyEffects(release.effects, release.cardId, actor, target, result);
                    break;
            }

            actor.isDefeated = actor.hp <= 0;
            if (target != null)
            {
                target.isDefeated = target.hp <= 0;
            }

            return result;
        }

        /// <summary>
        /// 更新所有玩家身上的 Buff 计时。
        /// </summary>
        public void TickBuffs(IEnumerable<PlayerCombatStateData> players, float dt)
        {
            if (players == null || dt <= 0f)
            {
                return;
            }

            foreach (var player in players)
            {
                TickBuffs(player, dt);
            }
        }

        /// <summary>
        /// 开始新回合，重置玩家状态。
        /// </summary>
        public void BeginTurn(PlayerCombatStateData player)
        {
            if (player == null)
            {
                return;
            }

            player.wasHitThisTurn = false;
            for (var i = player.buffs.Count - 1; i >= 0; i--)
            {
                var buff = player.buffs[i];
                if (buff == null)
                {
                    player.buffs.RemoveAt(i);
                    continue;
                }

                if (buff.kind == CombatBuffKind.RegenTurnStart)
                {
                    Heal(player, Math.Max(0f, buff.value));
                }

                if (buff.turnsLeft > 0 && buff.secondsLeft <= 0f && !buff.untilTurnEnd)
                {
                    buff.turnsLeft--;
                    if (buff.turnsLeft <= 0)
                    {
                        player.buffs.RemoveAt(i);
                    }
                }
            }
        }

        public void EndTurn(PlayerCombatStateData player)
        {
            if (player?.buffs == null)
            {
                return;
            }

            for (var i = player.buffs.Count - 1; i >= 0; i--)
            {
                var buff = player.buffs[i];
                if (buff == null || buff.untilTurnEnd)
                {
                    player.buffs.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 清空玩家的本回合护盾。
        /// </summary>
        public void ClearTurnShield(PlayerCombatStateData player)
        {
            if (player != null)
            {
                player.preparationDefenseBonus = 0f;
                player.absorbShield = 0f;
            }
        }

        /// <summary>
        /// 检查玩家是否被禁用出牌。
        /// </summary>
        public bool IsActionBlocked(PlayerCombatStateData player)
        {
            /// <summary>
            /// 检查玩家是否拥有指定类型的 Buff。
            /// </summary>
            return HasBuff(player, CombatBuffKind.DisableCards);
        }

        /// <summary>
        /// 执行一次攻击效果的计算与数值应用。
        /// </summary>
        private void ApplyAttack(CardReleaseResult release, PlayerCombatStateData actor, PlayerCombatStateData target, ActionResultData result)
        {
            if (target == null)
            {
                result.effects.Add(EffectResult(release.cardId, CardEffectKind.None, 0, "没有可攻击目标."));
                return;
            }

            var basePower = Math.Max(0f, release.power);
            float? replacedPower = null;
            float? multiStrikePower = null;
            var strikeCount = 1;
            var pierceAll = false;
            var pierceValue = 0f;
            var forceTrue = false;
            var lifeRate = 0f;
            var damageBonus = 0f;
            var damageMultiplier = 1f;

            for (var i = 0; i < release.effects.Count; i++)
            {
                var fx = release.effects[i];
                if (fx == null)
                {
                    continue;
                }

                switch (fx.effectKind)
                {
                    case CardEffectKind.SetPower:
                        replacedPower = Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.DamageBoost:
                        damageBonus += Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.CopyDamage:
                        damageMultiplier *= 2f;
                        break;
                    case CardEffectKind.DamageMultiplier:
                        damageMultiplier *= Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.DefensePierce:
                    case CardEffectKind.IgnoreShield:
                        pierceAll = true;
                        break;
                    case CardEffectKind.DefensePierceValue:
                        pierceValue += Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.LifeStealRatio:
                        lifeRate += Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.ExecuteLowHp:
                        if (target.hp <= fx.value)
                        {
                            damageMultiplier *= Math.Max(1f, fx.value2);
                        }
                        break;
                    case CardEffectKind.DestroyShield:
                        var destroyedAbsorbShield = target.absorbShield;
                        target.absorbShield = 0f;
                        result.absorbShieldDelta -= destroyedAbsorbShield;
                        result.effects.Add(EffectResult(release.cardId, fx.effectKind, 0, "护盾被击毁."));
                        if (destroyedAbsorbShield > 0f)
                        {
                            RunDamageReactions(actor, target, 0f, 0f, 0f, destroyedAbsorbShield, release.cardId, result);
                        }
                        break;
                    case CardEffectKind.ShatterDefense:
                        if (target.shield > 0f)
                        {
                            var cut = Mathf.Ceil(target.shield * 0.5f);
                            target.shield = Math.Max(0f, target.shield - cut);
                            result.shieldDelta -= cut;
                        }
                        else
                        {
                            damageBonus += Math.Max(0f, fx.value);
                        }
                        break;
                    case CardEffectKind.TrueDamageMultiplier:
                        forceTrue = true;
                        damageMultiplier *= Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.TrueDamage:
                        forceTrue = true;
                        damageBonus += Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.MultiStrikeBurn:
                        multiStrikePower = (multiStrikePower ?? 0f) + Math.Max(0f, fx.value);
                        strikeCount *= Math.Max(1, Mathf.RoundToInt(fx.value2));
                        break;
                }
            }

            if (replacedPower.HasValue)
                basePower = replacedPower.Value;
            if (multiStrikePower.HasValue)
                basePower = multiStrikePower.Value;

            var damagePerStrike = Math.Max(0f, (basePower + damageBonus) * damageMultiplier);
            ApplyWeakAttack(actor, ref damagePerStrike);

            var totalActual = 0f;
            var didMainHit = false;
            if (damagePerStrike > 0f)
            {
                for (var strike = 0; strike < strikeCount; strike++)
                {
                    var holyShieldCount = CountEffects(result, CardEffectKind.HolyShield);
                    totalActual += DealDamage(actor, target, damagePerStrike, forceTrue, pierceAll,
                        pierceValue, release.cardId, result);
                    didMainHit |= CountEffects(result, CardEffectKind.HolyShield) == holyShieldCount;
                }
            }

            if (lifeRate > 0f && totalActual > 0f)
            {
                var healed = Heal(actor, totalActual * lifeRate);
                result.effects.Add(EffectResult(release.cardId, CardEffectKind.LifeStealRatio, healed, "Life steal resolved."));
            }

            ApplyNextLifeSteal(actor, totalActual, release.cardId, result);

            var hitCount = strikeCount * Math.Max(1, Mathf.RoundToInt(damageMultiplier));
            for (var i = 0; i < release.effects.Count; i++)
            {
                ApplyAttackSideEffect(release.effects[i], release.cardId, actor, target, result, didMainHit, hitCount);
            }
        }

        private static int CountEffects(ActionResultData result, CardEffectKind kind)
        {
            var count = 0;
            if (result?.effects == null)
                return count;

            for (var i = 0; i < result.effects.Count; i++)
            {
                if (result.effects[i] != null && result.effects[i].effectKind == kind)
                    count++;
            }

            return count;
        }

        /// <summary>
        /// 应用防御效果。
        /// </summary>
        private void ApplyDefense(CardReleaseResult release, PlayerCombatStateData actor, ActionResultData result)
        {
            var baseShield = Math.Max(0f, release.power);
            float? replacedShield = null;
            var shieldBonus = 0f;
            for (var i = 0; i < release.effects.Count; i++)
            {
                var fx = release.effects[i];
                if (fx == null)
                {
                    continue;
                }

                switch (fx.effectKind)
                {
                    case CardEffectKind.SetPower:
                        replacedShield = Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.ShieldBoost:
                        shieldBonus += Math.Max(0f, fx.value);
                        break;
                }
            }

            var shield = (replacedShield ?? baseShield) + shieldBonus;

            if (release.gameState == GameState.Preparation)
            {
                actor.shield = Math.Max(0f, actor.shield - actor.preparationDefenseBonus + shield);
                actor.preparationDefenseBonus = shield;
                result.shieldDelta = shield;
            }
            else
            {
                actor.shield += shield;
                result.shieldDelta += shield;
            }

            ApplyEffects(release.effects, release.cardId, actor, actor, result);
        }

        /// <summary>
        /// 应用治疗效果。
        /// </summary>
        private void ApplyHeal(CardReleaseResult release, PlayerCombatStateData actor, PlayerCombatStateData target, ActionResultData result)
        {
            var baseHeal = Math.Max(0f, release.power);
            float? replacedHeal = null;
            var healBonus = 0f;
            for (var i = 0; i < release.effects.Count; i++)
            {
                var fx = release.effects[i];
                if (fx == null)
                {
                    continue;
                }

                switch (fx.effectKind)
                {
                    case CardEffectKind.SetPower:
                        replacedHeal = Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.HealBoost:
                        healBonus += Math.Max(0f, fx.value);
                        break;
                }
            }

            var heal = (replacedHeal ?? baseHeal) + healBonus;

            result.hpDelta += Heal(actor, heal);
            ApplyEffects(release.effects, release.cardId, actor, target, result);
        }

        /// <summary>
        /// 应用卡牌效果列表。
        /// </summary>
        private void ApplyEffects(IReadOnlyList<CardEffectData> effects, string cardId, PlayerCombatStateData actor, PlayerCombatStateData target, ActionResultData result)
        {
            if (effects == null)
            {
                return;
            }

            for (var i = 0; i < effects.Count; i++)
            {
                var fx = effects[i];
                if (fx == null)
                {
                    continue;
                }

                switch (fx.effectKind)
                {
                    case CardEffectKind.AbsorbShield:
                        actor.absorbShield += Math.Max(0f, fx.value);
                        result.absorbShieldDelta += Math.Max(0f, fx.value);
                        break;
                    case CardEffectKind.DrawCards:
                        result.drawCount += Math.Max(0, (int)Math.Floor(fx.value));
                        break;
                    case CardEffectKind.HealEnemy:
                        if (target != null)
                        {
                            Heal(target, Math.Max(0f, fx.value));
                        }
                        break;
                    case CardEffectKind.HealOverTime:
                        AddBuff(actor, CombatBuffKind.HealOverTime, fx, cardId);
                        break;
                    case CardEffectKind.CleanseDebuff:
                        Cleanse(actor);
                        break;
                    case CardEffectKind.HolyShield:
                        AddBuff(actor, CombatBuffKind.HolyShield, fx, cardId);
                        break;
                    case CardEffectKind.Thorn:
                        AddBuff(actor, CombatBuffKind.Thorn, fx, cardId);
                        break;
                    case CardEffectKind.ReflectBlockedDamage:
                        AddBuff(actor, CombatBuffKind.ReflectBlockedDamage, fx, cardId);
                        break;
                    case CardEffectKind.ReduceNextDamage:
                        AddBuff(actor, CombatBuffKind.ReduceNextDamage, fx, cardId);
                        break;
                    case CardEffectKind.DebuffImmune:
                        AddBuff(actor, CombatBuffKind.DebuffImmune, fx, cardId);
                        break;
                    case CardEffectKind.HalfNextAttack:
                        AddBuff(actor, CombatBuffKind.HalfNextAttack, fx, cardId);
                        break;
                    case CardEffectKind.AbsorbBlockedHeal:
                        AddBuff(actor, CombatBuffKind.AbsorbBlockedHeal, fx, cardId);
                        break;
                    case CardEffectKind.DamageToHeal:
                        AddBuff(actor, CombatBuffKind.DamageToHeal, fx, cardId);
                        break;
                    case CardEffectKind.RegenTurnStart:
                        AddBuff(actor, CombatBuffKind.RegenTurnStart, fx, cardId);
                        break;
                    case CardEffectKind.SacrificeHeal:
                        DealDamage(actor, actor, fx.value, true, false, 0f, cardId, result, true);
                        result.hpDelta += Heal(actor, UnityEngine.Random.Range(fx.value2, fx.value3));
                        break;
                    case CardEffectKind.ConditionalHealIfHit:
                        if (actor != null && actor.wasHitThisTurn)
                        {
                            result.hpDelta += Heal(actor, Math.Max(0f, fx.value));
                        }
                        break;
                    case CardEffectKind.DoubleDefense:
                        AddBuff(actor, CombatBuffKind.DoubleDefense, fx, cardId);
                        break;
                    case CardEffectKind.RevengeShield:
                        AddBuff(actor, CombatBuffKind.RevengeShield, fx, cardId);
                        break;
                    case CardEffectKind.FatalGuard:
                        AddBuff(actor, CombatBuffKind.FatalGuard, fx, cardId);
                        break;
                    case CardEffectKind.MirrorDamage:
                        AddBuff(actor, CombatBuffKind.MirrorDamage, fx, cardId);
                        break;
                    case CardEffectKind.SetHpMin:
                        if (actor != null && actor.hp < fx.value)
                        {
                            result.hpDelta += Heal(actor, fx.value - actor.hp);
                        }
                        break;
                    case CardEffectKind.SwapHp:
                        ApplySwapOrDiffHeal(actor, target, fx, result);
                        break;
                    case CardEffectKind.LifeSteal:
                        AddBuff(actor, CombatBuffKind.LifeStealNextAttack, fx, cardId);
                        break;
                    case CardEffectKind.ExtraTurn:
                    case CardEffectKind.TimeBoost:
                    case CardEffectKind.Stun:
                        result.effects.Add(EffectResult(cardId, fx.effectKind, fx.value, "State effect requested."));
                        break;
                }
            }
        }

        /// <summary>
        /// 应用攻击的附加效果。
        /// </summary>
        private void ApplyAttackSideEffect(CardEffectData fx, string cardId, PlayerCombatStateData actor, PlayerCombatStateData target, ActionResultData result, bool hit, int hitCount)
        {
            if (fx == null)
            {
                return;
            }

            switch (fx.effectKind)
            {
                case CardEffectKind.Burn:
                    if (hit)
                    {
                        for (var i = 0; i < Math.Max(1, hitCount); i++)
                        {
                            AddBadBuff(target, CombatBuffKind.Burn, fx, cardId);
                        }
                    }
                    break;
                case CardEffectKind.DisableCards:
                    if (hit)
                    {
                        AddBadBuff(target, CombatBuffKind.DisableCards, fx, cardId);
                    }
                    break;
                case CardEffectKind.WeakenNextAttack:
                    if (hit)
                    {
                        AddBadBuff(target, CombatBuffKind.WeakenNextAttack, fx, cardId);
                    }
                    break;
                case CardEffectKind.ReduceDefense:
                    if (hit && target != null)
                    {
                        var cut = Math.Min(target.shield, Math.Max(0f, fx.value));
                        target.shield -= cut;
                        result.shieldDelta -= cut;
                    }
                    break;
                case CardEffectKind.SelfDamage:
                    DealDamage(actor, actor, Math.Max(0f, fx.value), true, false, 0f, cardId, result, true);
                    break;
                case CardEffectKind.MultiStrikeBurn:
                    if (hit && fx.value3 > 0f)
                    {
                        AddBadBuff(target, CombatBuffKind.Burn, fx.value3, 1f, 0f, 0f, 0, cardId, true);
                    }
                    break;
            }
        }

        /// <summary>
        /// 计算并造成伤害。
        /// </summary>
        private float DealDamage(
            PlayerCombatStateData source,
            PlayerCombatStateData target,
            float raw,
            bool real,
            bool pierceAll,
            float pierceValue,
            string cardId,
            ActionResultData result,
            bool noDeath = false,
            bool reaction = false)
        {
            if (target == null || raw <= 0f)
            {
                return 0f;
            }

            target.wasHitThisTurn = true;

            if (!real && !pierceAll && ConsumeBuff(target, CombatBuffKind.HolyShield, out _))
            {
                result.effects.Add(EffectResult(cardId, CardEffectKind.HolyShield, 0, "Holy shield blocked damage."));
                return 0f;
            }

            if (!real && !pierceAll && ConsumeBuff(target, CombatBuffKind.HalfNextAttack, out _))
            {
                raw *= 0.5f;
            }

            // 可消耗护盾与防御属性完全分离：护盾先直接吸收伤害，
            // 只有剩余伤害才进入防御衰减公式。
            var shieldBeforeHit = target.absorbShield;
            var absorbed = 0f;
            if (!real && !pierceAll && target.absorbShield > 0f)
            {
                absorbed = Math.Min(target.absorbShield, raw);
                target.absorbShield -= absorbed;
                result.absorbShieldDelta -= absorbed;
                raw -= absorbed;
                if (raw <= 0f)
                {
                    result.effects.Add(EffectResult(cardId, CardEffectKind.AbsorbShield, absorbed, "Absorb shield blocked damage."));
                    if (!reaction && source != null && source != target)
                    {
                        RunDamageReactions(source, target, 0f, 0f, 0f, shieldBeforeHit, cardId, result);
                    }
                    return 0f;
                }

                result.effects.Add(EffectResult(cardId, CardEffectKind.AbsorbShield, absorbed, "Absorb shield partially blocked damage."));
            }

            var shield = pierceAll ? 0f : Math.Max(0f, target.shield - pierceValue);
            if (HasBuff(target, CombatBuffKind.DoubleDefense))
            {
                shield *= 2f;
            }

            var actual = real ? raw : Reduced(raw, shield);
            var blocked = Math.Max(0f, raw - actual);

            if (!real && !pierceAll && ConsumeBuff(target, CombatBuffKind.ReduceNextDamage, out var reduce))
            {
                actual = Math.Max(0f, actual - Math.Max(0f, reduce.value));
            }

            if (!real && !pierceAll && HasBuff(target, CombatBuffKind.DoubleDefense))
            {
                actual = Math.Max(0f, actual - 1f);
            }

            if (noDeath)
            {
                actual = Math.Min(actual, Math.Max(0f, target.hp - 1f));
            }
            else if (target.hp - actual <= 0f && ConsumeBuff(target, CombatBuffKind.FatalGuard, out _))
            {
                actual = Math.Min(actual, Math.Max(0f, target.hp - 1f));
            }

            var before = target.hp;
            target.hp = Math.Max(0f, target.hp - actual);
            actual = before - target.hp;
            if (target == source)
            {
                result.hpDelta -= actual;
            }
            else
            {
                result.hpDelta -= actual;
            }

            if (!reaction && source != null && source != target)
            {
                RunDamageReactions(source, target, raw, actual, blocked, shieldBeforeHit, cardId, result);
            }

            return actual;
        }

        /// <summary>
        /// 处理伤害后的反应效果。
        /// </summary>
        private void RunDamageReactions(PlayerCombatStateData source, PlayerCombatStateData target, float raw, float actual, float blocked, float shieldBeforeHit, string cardId, ActionResultData result)
        {
            for (var i = target.buffs.Count - 1; i >= 0; i--)
            {
                var buff = target.buffs[i];
                if (buff == null)
                {
                    continue;
                }

                switch (buff.kind)
                {
                    case CombatBuffKind.AbsorbBlockedHeal:
                        if (blocked > 0f)
                        {
                            Heal(target, blocked);
                        }
                        break;
                    case CombatBuffKind.DamageToHeal:
                        if (actual > 0f)
                        {
                            Heal(target, actual * Math.Max(0f, buff.value));
                        }
                        break;
                    case CombatBuffKind.ReflectBlockedDamage:
                        if (blocked > 0f)
                        {
                            DealDamage(target, source, blocked * Math.Max(0f, buff.value), true, false, 0f, cardId, result, false, true);
                            target.buffs.RemoveAt(i);
                        }
                        break;
                    case CombatBuffKind.Thorn:
                        if (actual > 0f || blocked > 0f)
                        {
                            var reflect = buff.value < 1f ? actual * buff.value : buff.value;
                            DealDamage(target, source, reflect, true, false, 0f, cardId, result, false, true);
                            if (buff.value < 1f || (buff.secondsLeft <= 0f && buff.turnsLeft <= 0))
                            {
                                target.buffs.RemoveAt(i);
                            }
                        }
                        break;
                    case CombatBuffKind.RevengeShield:
                        if (shieldBeforeHit > 0f && (buff.value2 > 0f || target.absorbShield <= 0f))
                        {
                            DealDamage(target, source, Math.Max(0f, buff.value), true, false, 0f, cardId, result, false, true);
                            if (buff.value2 > 0f)
                            {
                                AddBadBuff(source, CombatBuffKind.DisableCards, buff.value2, 0f, 0f, buff.value2, 0, cardId);
                            }
                            else
                            {
                                target.buffs.RemoveAt(i);
                            }
                        }
                        break;
                    case CombatBuffKind.MirrorDamage:
                        if (raw > 0f)
                        {
                            DealDamage(target, source, raw, false, false, 0f, cardId, result, false, true);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 更新所有玩家身上的 Buff 计时。
        /// </summary>
        private void TickBuffs(PlayerCombatStateData player, float dt)
        {
            if (player == null || player.buffs == null)
            {
                return;
            }

            for (var i = player.buffs.Count - 1; i >= 0; i--)
            {
                var buff = player.buffs[i];
                if (buff == null)
                {
                    player.buffs.RemoveAt(i);
                    continue;
                }

                if ((buff.secondsLeft > 0f || buff.untilTurnEnd) && (buff.kind == CombatBuffKind.Burn || buff.kind == CombatBuffKind.HealOverTime))
                {
                    var interval = Math.Max(0.1f, buff.value2 <= 0f ? 1f : buff.value2);
                    buff.tick += buff.untilTurnEnd ? dt : Math.Min(dt, buff.secondsLeft);
                    while (buff.tick >= interval)
                    {
                        buff.tick -= interval;
                        if (buff.kind == CombatBuffKind.Burn)
                        {
                            var tmp = new ActionResultData();
                            DealDamage(null, player, Math.Max(0f, buff.value), true, false, 0f, buff.sourceCardId, tmp, false, true);
                        }
                        else
                        {
                            Heal(player, Math.Max(0f, buff.value));
                        }
                    }
                }

                if (buff.secondsLeft > 0f)
                {
                    buff.secondsLeft -= dt;
                    if (buff.secondsLeft <= 0f)
                    {
                        player.buffs.RemoveAt(i);
                        continue;
                    }
                }

                if (!buff.untilTurnEnd && buff.secondsLeft <= 0f && buff.turnsLeft <= 0 && IsTimedBuff(buff.kind))
                {
                    player.buffs.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 应用削弱后的攻击效果。
        /// </summary>
        private void ApplyWeakAttack(PlayerCombatStateData actor, ref float damage)
        {
            if (ConsumeBuff(actor, CombatBuffKind.WeakenNextAttack, out var buff))
            {
                damage = Math.Max(0f, damage - Math.Max(0f, buff.value));
            }
        }

        /// <summary>
        /// 应用下次攻击的吸血效果。
        /// </summary>
        private void ApplyNextLifeSteal(PlayerCombatStateData actor, float actualDamage, string cardId, ActionResultData result)
        {
            if (actor == null || actualDamage <= 0f || !ConsumeBuff(actor, CombatBuffKind.LifeStealNextAttack, out var buff))
            {
                return;
            }

            var healed = Heal(actor, actualDamage * Math.Max(0f, buff.value));
            result.effects.Add(EffectResult(cardId, CardEffectKind.LifeSteal, healed, "Next attack life steal resolved."));
        }

        /// <summary>
        /// 应用生命值交换或差值治疗。
        /// </summary>
        private void ApplySwapOrDiffHeal(PlayerCombatStateData actor, PlayerCombatStateData target, CardEffectData fx, ActionResultData result)
        {
            if (actor == null || target == null)
            {
                return;
            }

            if (fx.value < 0f)
            {
                var actorHp = actor.hp;
                actor.hp = target.hp;
                target.hp = actorHp;
                if (fx.value2 > 0f)
                {
                    result.hpDelta += Heal(actor, fx.value2);
                }

                return;
            }

            if (actor.hp < target.hp)
            {
                var diff = Math.Min(target.hp - actor.hp, Math.Max(0f, fx.value));
                result.hpDelta += Heal(actor, diff);
            }
        }

        /// <summary>
        /// 给玩家添加一个 Buff。
        /// </summary>
        private void AddBuff(PlayerCombatStateData player, CombatBuffKind kind, CardEffectData fx, string cardId)
        {
            if (fx == null)
            {
                AddBuff(player, kind, 0f, 0f, 0f, 0f, 1, cardId);
                return;
            }

            AddBuff(player, kind, fx.value, fx.value2, fx.value3, fx.seconds, fx.durationTurns, cardId, fx.untilTurnEnd);
        }

        /// <summary>
        /// 给玩家添加一个负面 Buff。
        /// </summary>
        private void AddBadBuff(PlayerCombatStateData player, CombatBuffKind kind, CardEffectData fx, string cardId)
        {
            if (fx == null)
            {
                return;
            }

            AddBadBuff(player, kind, fx.value, fx.value2, fx.value3, fx.seconds, fx.durationTurns, cardId, fx.untilTurnEnd);
        }

        /// <summary>
        /// 给玩家添加一个负面 Buff。
        /// </summary>
        private void AddBadBuff(PlayerCombatStateData player, CombatBuffKind kind, float v, float v2, float v3, float seconds, int turns, string cardId, bool untilTurnEnd = false)
        {
            if (HasBuff(player, CombatBuffKind.DebuffImmune))
            {
                return;
            }

            AddBuff(player, kind, v, v2, v3, seconds, turns, cardId, untilTurnEnd, true);
        }

        /// <summary>
        /// 给玩家添加一个 Buff。
        /// </summary>
        private void AddBuff(PlayerCombatStateData player, CombatBuffKind kind, float v, float v2, float v3, float seconds, int turns, string cardId, bool untilTurnEnd = false, bool isDebuff = false)
        {
            if (player == null)
            {
                return;
            }

            if (player.buffs == null)
            {
                player.buffs = new List<CombatBuffData>();
            }

            player.buffs.Add(new CombatBuffData
            {
                kind = kind,
                value = v,
                value2 = v2,
                value3 = v3,
                secondsLeft = seconds,
                untilTurnEnd = untilTurnEnd,
                isDebuff = isDebuff,
                turnsLeft = seconds > 0f ? 0 : turns,
                sourceCardId = cardId
            });
        }

        /// <summary>
        /// 消耗一个已存在的 Buff。
        /// </summary>
        private static bool ConsumeBuff(PlayerCombatStateData player, CombatBuffKind kind, out CombatBuffData buff)
        {
            buff = null;
            if (player?.buffs == null)
            {
                return false;
            }

            for (var i = 0; i < player.buffs.Count; i++)
            {
                if (player.buffs[i] != null && player.buffs[i].kind == kind)
                {
                    buff = player.buffs[i];
                    player.buffs.RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 检查玩家是否拥有指定类型的 Buff。
        /// </summary>
        private static bool HasBuff(PlayerCombatStateData player, CombatBuffKind kind)
        {
            if (player?.buffs == null)
            {
                return false;
            }

            for (var i = 0; i < player.buffs.Count; i++)
            {
                if (player.buffs[i] != null && player.buffs[i].kind == kind)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 净化玩家身上的可驱散 Buff。
        /// </summary>
        private static void Cleanse(PlayerCombatStateData player)
        {
            if (player?.buffs == null)
            {
                return;
            }

            for (var i = player.buffs.Count - 1; i >= 0; i--)
            {
                if (player.buffs[i].isDebuff)
                {
                    player.buffs.RemoveAt(i);
                    return;
                }
            }
        }

        /// <summary>
        /// 计算数值的减免结果。
        /// </summary>
        private float Reduced(float raw, float shield)
        {
            var k = Math.Max(0.01f, cfg.damageReductionK);
            return Mathf.Floor((k * raw / (Math.Max(0f, shield) + k)) * 10f) / 10f;
        }

        /// <summary>
        /// 计算治疗数值。
        /// </summary>
        private static float Heal(PlayerCombatStateData target, float heal)
        {
            if (target == null || heal <= 0f)
            {
                return 0f;
            }

            var before = target.hp;
            target.hp += heal;
            return target.hp - before;
        }

        /// <summary>
        /// 判断 Buff 是否为计时类型。
        /// </summary>
        private static bool IsTimedBuff(CombatBuffKind kind)
        {
            return kind == CombatBuffKind.Burn ||
                   kind == CombatBuffKind.HealOverTime ||
                   kind == CombatBuffKind.DisableCards ||
                   kind == CombatBuffKind.Thorn ||
                   kind == CombatBuffKind.AbsorbBlockedHeal ||
                   kind == CombatBuffKind.DamageToHeal ||
                   kind == CombatBuffKind.DoubleDefense ||
                   kind == CombatBuffKind.MirrorDamage ||
                   kind == CombatBuffKind.FatalGuard ||
                   kind == CombatBuffKind.DebuffImmune;
        }

        /// <summary>
        /// 解析动画 Id。
        /// </summary>
        private static string ResolveAnimationId(CardReleaseResult release)
        {
            if (!string.IsNullOrEmpty(release.cardId))
            {
                return release.cardId;
            }

            return release.baseType.HasValue ? release.baseType.Value.ToString() : "unknown_action";
        }

        /// <summary>
        /// 创建单个效果的结果数据。
        /// </summary>
        private static CardEffectResultData EffectResult(string cardId, CardEffectKind kind, float value, string note)
        {
            return new CardEffectResultData
            {
                cardId = cardId,
                effectKind = kind,
                value = value,
                note = note
            };
        }
    }
