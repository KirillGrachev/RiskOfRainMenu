using System;
using System.Reflection;
using HarmonyLib;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu {

    /// <summary>
    /// Боевые читы через Harmony и тики.
    ///
    /// Бессмертие, ваншот и множитель урона стоят prefix'ом на
    /// HealthComponent.TakeDamage - единственной точке входа урона в игре
    /// (снаряды, DoT'ы, падения, взрывы идут через неё). Множители скорости
    /// атаки и движения - postfix'ы на геттерах CharacterBody.attackSpeed и
    /// moveSpeed (в этой сборке статы read-only, сеттеры приватные): патч
    /// геттера ловит все чтения игры - перезарядки, мотор, анимации. Тем же
    /// приёмом поднимается лимит прыжков (get_maxJumpCount).
    ///
    /// Отсутствие перезарядки поддержано тиком (0.5 с из Plugin.Update),
    /// а не патчем: stock=maxStock и заполненный baseRechargeStopwatch -
    /// те же рычаги, что использует сама игра, и они переживают обновления
    /// лучше, чем патч внутренней тикалки перезарядки. Перезарядка
    /// оборудования сбрасывается в приватное поле _rechargeTime
    /// (reflection, наличие поля проверяет GameRefsTests).
    ///
    /// Сигнатуры сверены с метаданными RoR2.dll (сборка игры 2026-10-18):
    ///   HealthComponent.TakeDamage(DamageInfo)      [instance] -> void
    ///   CharacterBody.get_attackSpeed / get_moveSpeed  -> float
    ///   GenericSkill.stock / maxStock               public, settable
    ///   GenericSkill.baseRechargeStopwatch          public field
    ///   GenericSkill.GetFinalRechargeInterval()     public
    ///   EquipmentSlot._rechargeTime                 private Run.FixedTimeStamp
    ///   EquipmentSlot.subcooldownTimer              private float
    /// </summary>
    internal static class CheatPatches {

        private static FieldInfo _rechargeTimeField;
        private static FieldInfo _subcooldownField;
        private static bool _equipmentReflectionResolved;

        /// <summary>
        /// Один раз находит приватные поля перезарядки оборудования.
        /// Отсутствие поля - не молчаливый отказ, а предупреждение в лог
        /// и на вкладку «Инфо» (обновление игры переименовало поле).
        /// </summary>
        private static void ResolveEquipmentReflection() {

            if (_equipmentReflectionResolved) {
                return;
            }

            _equipmentReflectionResolved = true;
            _rechargeTimeField = typeof(EquipmentSlot).GetField("_rechargeTime",
                    BindingFlags.NonPublic | BindingFlags.Instance);
            _subcooldownField = typeof(EquipmentSlot).GetField("subcooldownTimer",
                    BindingFlags.NonPublic | BindingFlags.Instance);

            if (_rechargeTimeField == null) {

                GameApi.WarnOnce("CheatPatches",
                        new MissingFieldException("EquipmentSlot._rechargeTime: equipment cooldown reset degrades"));

            }

        }

        /// <summary>
        /// netId игрока-владельца тела (0 - тело не принадлежит игрокам).
        /// Сравнение идёт по master'у и по телу: в момент респавна master
        /// может ещё не указывать на новое тело.
        /// </summary>
        internal static uint NetIdForBody(CharacterBody body) {

            if (body == null) {
                return 0u;
            }

            CharacterMaster master = null;

            try {
                master = body.master;
            } catch (Exception e) {
                GameApi.WarnOnce("NetIdForBody.master", e);
            }

            foreach (var nu in PlayerTargets.Users) {

                if (nu == null) {
                    continue;
                }

                try {

                    if (master != null && ReferenceEquals(nu.master, master)) {
                        return PlayerTargets.NetIdOf(nu);
                    }

                    var userBody = nu.GetCurrentBody();

                    if (userBody != null && ReferenceEquals(userBody, body)) {
                        return PlayerTargets.NetIdOf(nu);
                    }

                } catch (Exception e) {
                    GameApi.WarnOnce("NetIdForBody.scan", e);
                }

            }

            return 0u;

        }

        /// <summary>
        /// Prefix TakeDamage: бессмертие жертвы, ваншот и множитель урона
        /// атакующего. false - урон не применяется вовсе.
        /// </summary>
        internal static bool OnTakeDamage(HealthComponent health, DamageInfo damageInfo) {

            if (health == null || damageInfo == null) {
                return true;
            }

            if (!NetworkServer.active) {
                return true;
            }

            var victimBody = health.GetComponent<CharacterBody>();
            uint victimId = NetIdForBody(victimBody);

            if (victimId != 0u && CheatState.HasFlag(victimId, CheatFlag.GodMode)) {

                damageInfo.rejected = true;
                damageInfo.damage = 0f;
                return false;

            }

            if (damageInfo.attacker == null) {
                return true;
            }

            var attackerBody = damageInfo.attacker.GetComponent<CharacterBody>();
            uint attackerId = NetIdForBody(attackerBody);

            if (attackerId == 0u || attackerId == victimId) {
                return true;
            }

            if (CheatState.HasFlag(attackerId, CheatFlag.OneHitKill)) {

                // запас на броню (процентное снижение) и барьер: x3 + 100k бьёт
                // наверняка даже по боссу позднего забега максимальной сложности
                damageInfo.damage = (health.combinedHealth + health.barrier) * 3f + 100000f;

            }

            int pct = CheatState.ValueOf(attackerId, CheatValue.DamagePct);

            if (pct != CheatState.DefaultPct) {
                damageInfo.damage *= pct / 100f;
            }

            return true;

        }

        /// <summary>
        /// Множитель скорости (атаки или движения) для postfix'а геттера:
        /// быстрый выход, пока ни у кого нет множителей, иначе резолв
        /// владельца тела и домножение результата.
        /// </summary>
        internal static void MultiplySpeed(CharacterBody body, CheatValue which, ref float result) {

            if (!CheatState.AnySpeedTuning || body == null) {
                return;
            }

            uint id = NetIdForBody(body);

            if (id == 0u) {
                return;
            }

            int pct = CheatState.ValueOf(id, which);

            if (pct != CheatState.DefaultPct) {
                result *= pct / 100f;
            }

        }

        /// <summary>Потолок «бесконечных» прыжков: хватит на полёт, не хватит на лаг.</summary>
        internal const int MaxJumps = 10;

        /// <summary>
        /// Поднятие лимита прыжков для postfix'а геттера maxJumpCount:
        /// движок прыжков читает лимит через свойство, поэтому патч геттера
        /// покрывает и обычные прыжки, и все предметы на доп-прыжки.
        /// </summary>
        internal static void RaiseJumpLimit(CharacterBody body, ref int result) {

            if (!CheatState.AnyJumpTuning || body == null || result >= MaxJumps) {
                return;
            }

            uint id = NetIdForBody(body);

            if (id == 0u) {
                return;
            }

            if (CheatState.HasFlag(id, CheatFlag.InfiniteJumps)) {
                result = MaxJumps;
            }

        }

        /// <summary>
        /// Тик из Plugin.Update (0.5 с): держит заполненными заряды умений и
        /// сбрасывает оборудование у всех игроков с флагом NoCooldown.
        /// </summary>
        internal static void MaintainContinuous() {

            if (!NetworkServer.active) {
                return;
            }

            foreach (var nu in PlayerTargets.Users) {

                if (nu == null) {
                    continue;
                }

                uint id = PlayerTargets.NetIdOf(nu);

                if (id == 0u || !CheatState.HasFlag(id, CheatFlag.NoCooldown)) {
                    continue;
                }

                var body = PlayerTargets.BodyOf(nu);

                if (body != null) {
                    RefillSkills(body);
                    ResetEquipmentCooldown(body.equipmentSlot);
                }

            }

        }

        /// <summary>Заполнить заряды всех умений тела (однократно или тиком).</summary>
        private static void RefillSkills(CharacterBody body) {

            var locator = body.skillLocator;

            if (locator == null) {
                return;
            }

            RefillSkill(locator.primary);
            RefillSkill(locator.secondary);
            RefillSkill(locator.utility);
            RefillSkill(locator.special);

        }

        private static void RefillSkill(GenericSkill skill) {

            if (skill == null) {
                return;
            }

            try {

                if (skill.stock < skill.maxStock) {
                    skill.stock = skill.maxStock;
                }

                float interval = skill.GetFinalRechargeInterval();

                if (skill.baseRechargeStopwatch < interval) {
                    skill.baseRechargeStopwatch = interval;
                }

            } catch (Exception e) {
                GameApi.WarnOnce("RefillSkill", e);
            }

        }

        /// <summary>
        /// Сброс перезарядки оборудования: приватное _rechargeTime уходит
        /// в negativeInfinity (заряд истёк «давным-давно»), субкулдаун в 0.
        /// </summary>
        internal static void ResetEquipmentCooldown(EquipmentSlot slot) {

            if (slot == null) {
                return;
            }

            try {

                ResolveEquipmentReflection();

                if (_rechargeTimeField != null) {
                    _rechargeTimeField.SetValue(slot, Run.FixedTimeStamp.negativeInfinity);
                }

                if (_subcooldownField != null) {
                    _subcooldownField.SetValue(slot, 0f);
                }

            } catch (Exception e) {
                GameApi.WarnOnce("ResetEquipmentCooldown", e);
            }

        }

        /// <summary>Разовый сброс перезарядок цели (кнопка «Сбросить кулдауны»).</summary>
        internal static void ResetCooldownsFor(CharacterBody body) {

            if (body == null) {
                return;
            }

            RefillSkills(body);
            ResetEquipmentCooldown(body.equipmentSlot);

        }

    }

    /// <summary>
    /// Патч входа урона: бессмертие, ваншот, множитель урона.
    /// Ошибка в логике чита не должна ломать урон - при исключении
    /// prefix возвращает true (урон проходит как обычно).
    /// </summary>
    [HarmonyPatch(typeof(HealthComponent), nameof(HealthComponent.TakeDamage), new[] { typeof(DamageInfo) })]
    internal static class TakeDamageCheatPatch {

        private static bool Prefix(HealthComponent __instance, DamageInfo damageInfo) {

            try {
                return CheatPatches.OnTakeDamage(__instance, damageInfo);
            } catch (Exception e) {
                GameApi.WarnOnce("TakeDamageCheatPatch", e);
                return true;
            }

        }

    }

    /// <summary>
    /// Патч геттера скорости атаки: множитель применяется там, где игру
    /// интересует стата (перезарядки умений, анимации), а не в пересчёте -
    /// attackSpeed в этой сборке read-only, сеттер приватный.
    /// </summary>
    [HarmonyPatch(typeof(CharacterBody), "get_attackSpeed")]
    internal static class AttackSpeedCheatPatch {

        private static void Postfix(CharacterBody __instance, ref float __result) {

            try {
                CheatPatches.MultiplySpeed(__instance, CheatValue.AttackSpeedPct, ref __result);
            } catch (Exception e) {
                GameApi.WarnOnce("AttackSpeedCheatPatch", e);
            }

        }

    }

    /// <summary>
    /// Патч геттера скорости движения: то же, что для атаки. Геттер дёргается
    /// мотором каждый кадр, поэтому MultiplySpeed выходит по флагу
    /// CheatState.AnySpeedTuning до всякого резолва.
    /// </summary>
    [HarmonyPatch(typeof(CharacterBody), "get_moveSpeed")]
    internal static class MoveSpeedCheatPatch {

        private static void Postfix(CharacterBody __instance, ref float __result) {

            try {
                CheatPatches.MultiplySpeed(__instance, CheatValue.MoveSpeedPct, ref __result);
            } catch (Exception e) {
                GameApi.WarnOnce("MoveSpeedCheatPatch", e);
            }

        }

    }

    /// <summary>
    /// Патч геттера лимита прыжков: с флагом InfiniteJumps поднимает лимит
    /// до <see cref="CheatPatches.MaxJumps"/>. Как и с скоростями, быстрый
    /// выход по глобальному счётчику - патч дёргается на каждый прыжок.
    /// </summary>
    [HarmonyPatch(typeof(CharacterBody), "get_maxJumpCount")]
    internal static class MaxJumpCountCheatPatch {

        private static void Postfix(CharacterBody __instance, ref int __result) {

            try {
                CheatPatches.RaiseJumpLimit(__instance, ref __result);
            } catch (Exception e) {
                GameApi.WarnOnce("MaxJumpCountCheatPatch", e);
            }

        }

    }

}
