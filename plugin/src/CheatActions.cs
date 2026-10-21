using System;
using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Действия трейнера: читы игрока, бой и экономика.
    ///
    /// Тот же контракт, что у <see cref="SandboxActions"/>: публичный метод
    /// исполняется локально на сервере или уезжает запросом, а серверный
    /// диспетчер <see cref="TryExecute"/> вызывает те же Run-тела. Владелец
    /// действия - всегда цель запроса (target), глобальные флаги
    /// (GodModeAll) не привязаны к игроку.
    ///
    /// Значения из сети проходят двойную защиту: ClampCount в NetProtocol
    /// и здешние клампы (CheatState.ClampPct/ClampStacks, проверки
    /// IsKnownFlag/IsKnownValue, границы каталогов) - диспетчер доверяет
    /// только себе.
    /// </summary>
    internal static class CheatActions {

        /// <summary>Действия исполняются локально: мы сервер или одиночная игра.</summary>
        private static bool Local { get { return NetProtocol.IsServer; } }

        /// <summary>Цель текущего нажатия кнопки в меню.</summary>
        private static NetworkUser Target { get { return PlayerTargets.SelectedUser; } }

        private static uint TargetId(NetworkUser target) {

            return PlayerTargets.NetIdOf(target);

        }

        private static void Send(SandboxRequest req) {

            if (!NetProtocol.Send(req)) {

                GameApi.SayErr(Texts.T(
                        "нет соединения с сервером: действие не отправлено",
                        "no server connection: action not sent"));

            }

        }

        // ------------------------------------------------------------ entry --
        /// <summary>Включить/выключить флаг чита у цели (GodModeAll - глобальный).</summary>
        public static void SetCheatFlag(CheatFlag flag, bool on) {

            var t = Target;

            if (Local) {
                RunSetCheatFlag(t, (int)flag, on);
                return;
            }

            // оптимистичное зеркало: вкладка читает локальное состояние,
            // правда остаётся на сервере (тот же приём, что CloneManager)
            CheatState.SetFlag(flag == CheatFlag.GodModeAll ? 0u : TargetId(t), flag, on);

            Send(new SandboxRequest {
                Action = ActionCode.SetCheatFlag,
                TargetNetId = TargetId(t),
                P1 = (int)flag,
                P2 = on ? 1 : 0
            });

        }

        /// <summary>Установить множитель (проценты) у цели.</summary>
        public static void SetCheatValue(CheatValue value, int pct) {

            var t = Target;

            if (Local) {
                RunSetCheatValue(t, (int)value, pct);
                return;
            }

            CheatState.SetValue(TargetId(t), value, pct);

            Send(new SandboxRequest {
                Action = ActionCode.SetCheatValue,
                TargetNetId = TargetId(t),
                P1 = (int)value,
                P2 = pct
            });

        }

        /// <summary>Полное лечение цели (здоровье, щит, барьер).</summary>
        public static void HealTarget() {

            var t = Target;

            if (Local) {
                RunHealTarget(t);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.HealTarget, TargetNetId = TargetId(t) });

        }

        /// <summary>Убить врагов: radius 0 - вся карта, иначе вокруг цели.</summary>
        public static void KillEnemies(int radius) {

            var t = Target;

            if (Local) {
                RunKillEnemies(t, radius);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.KillEnemies, TargetNetId = TargetId(t), P1 = radius });

        }

        /// <summary>Разово сбросить перезарядки умений и оборудования цели.</summary>
        public static void ResetCooldowns() {

            var t = Target;

            if (Local) {
                RunResetCooldowns(t);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.ResetCooldowns, TargetNetId = TargetId(t) });

        }

        /// <summary>Выдать стаки бафа цели (BuffIndex числом).</summary>
        public static void GiveBuff(int buffIndexValue, int stacks) {

            var t = Target;

            if (Local) {
                RunGiveBuff(t, buffIndexValue, stacks);
                return;
            }

            Send(new SandboxRequest {
                Action = ActionCode.GiveBuff,
                TargetNetId = TargetId(t),
                P1 = buffIndexValue,
                P2 = stacks
            });

        }

        /// <summary>Снять с цели все бафы и дебафы.</summary>
        public static void ClearBuffs() {

            var t = Target;

            if (Local) {
                RunClearBuffs(t);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.ClearBuffs, TargetNetId = TargetId(t) });

        }

        /// <summary>Установить деньги цели (мастер хранит кошелёк игрока).</summary>
        public static void SetMoney(int amount) {

            var t = Target;

            if (Local) {
                RunSetMoney(t, amount);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.SetMoney, TargetNetId = TargetId(t), P1 = amount });

        }

        /// <summary>Добавить деньги цели.</summary>
        public static void GiveMoney(int amount) {

            var t = Target;

            if (Local) {
                RunGiveMoney(t, amount);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.GiveMoney, TargetNetId = TargetId(t), P1 = amount });

        }

        /// <summary>Установить лунные монеты игрока (валюта профиля).</summary>
        public static void SetLunarCoins(int amount) {

            var t = Target;

            if (Local) {
                RunSetLunarCoins(t, amount);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.SetLunarCoins, TargetNetId = TargetId(t), P1 = amount });

        }

        /// <summary>Добавить лунные монеты игроку.</summary>
        public static void GiveLunarCoins(int amount) {

            var t = Target;

            if (Local) {
                RunGiveLunarCoins(t, amount);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.GiveLunarCoins, TargetNetId = TargetId(t), P1 = amount });

        }

        /// <summary>Установить уровень команды (в RoR2 уровень общий на команду).</summary>
        public static void SetTeamLevel(int level) {

            var t = Target;

            if (Local) {
                RunSetTeamLevel(level);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.SetTeamLevel, TargetNetId = TargetId(t), P1 = level });

        }

        /// <summary>Добавить опыт команде.</summary>
        public static void GiveExperience(int amount) {

            var t = Target;

            if (Local) {
                RunGiveExperience(amount);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.GiveExperience, TargetNetId = TargetId(t), P1 = amount });

        }

        /// <summary>
        /// Мгновенно сменить персонажа цели на префаб тела bodyName.
        /// Заодно работает как воскрешение: Respawn поднимает и мёртвого мастера.
        /// </summary>
        public static void RespawnAs(string bodyName) {

            var t = Target;

            if (Local) {
                RunRespawnAs(t, bodyName);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.RespawnAs, TargetNetId = TargetId(t), Name = bodyName });

        }

        // --------------------------------------------------------- dispatch --
        /// <summary>
        /// Серверное исполнение запросов трейнера. Возвращает false, если код
        /// действия не наш (вызывающий пробует следующий диспетчер).
        /// </summary>
        internal static bool TryExecute(SandboxRequest req, NetworkUser target) {

            switch (req.Action) {

                case ActionCode.SetCheatFlag:
                    RunSetCheatFlag(target, req.P1, req.P2 != 0);
                    return true;
                case ActionCode.SetCheatValue:
                    RunSetCheatValue(target, req.P1, req.P2);
                    return true;
                case ActionCode.HealTarget:
                    RunHealTarget(target);
                    return true;
                case ActionCode.KillEnemies:
                    RunKillEnemies(target, req.P1);
                    return true;
                case ActionCode.ResetCooldowns:
                    RunResetCooldowns(target);
                    return true;
                case ActionCode.GiveBuff:
                    RunGiveBuff(target, req.P1, req.P2);
                    return true;
                case ActionCode.ClearBuffs:
                    RunClearBuffs(target);
                    return true;
                case ActionCode.SetMoney:
                    RunSetMoney(target, req.P1);
                    return true;
                case ActionCode.GiveMoney:
                    RunGiveMoney(target, req.P1);
                    return true;
                case ActionCode.SetLunarCoins:
                    RunSetLunarCoins(target, req.P1);
                    return true;
                case ActionCode.GiveLunarCoins:
                    RunGiveLunarCoins(target, req.P1);
                    return true;
                case ActionCode.SetTeamLevel:
                    RunSetTeamLevel(req.P1);
                    return true;
                case ActionCode.GiveExperience:
                    RunGiveExperience(req.P1);
                    return true;
                case ActionCode.RespawnAs:
                    RunRespawnAs(target, req.Name);
                    return true;
                default:
                    return false;

            }

        }

        // ------------------------------------------------------ run bodies ---
        private static void RunSetCheatFlag(NetworkUser target, int flagValue, bool on) {

            if (!CheatState.IsKnownFlag(flagValue)) {

                GameApi.Warn("SetCheatFlag", new NotSupportedException("flag id " + flagValue));
                return;

            }

            var flag = (CheatFlag)flagValue;

            if (flag == CheatFlag.GodModeAll) {

                CheatState.SetFlag(0u, flag, on);
                GameApi.SayOk(Texts.T(
                        $"чит: бессмертие всех игроков - {(on ? "вкл" : "выкл")}",
                        $"cheat: god mode for all players - {(on ? "on" : "off")}"));
                return;

            }

            uint id = TargetId(target);

            if (id == 0u) {

                GameApi.SayErr(Texts.T("чит: нет цели", "cheat: no target"));
                return;

            }

            CheatState.SetFlag(id, flag, on);

            // «без перезарядки» применяем сразу, не дожидаясь тика
            if (flag == CheatFlag.NoCooldown && on) {

                var body = PlayerTargets.BodyOf(target);

                if (body != null) {
                    CheatPatches.ResetCooldownsFor(body);
                }

            }

            GameApi.SayOk(Texts.T(
                    $"чит: {FlagName(flag)} [{PlayerTargets.DisplayName(target)}] - {(on ? "вкл" : "выкл")}",
                    $"cheat: {FlagName(flag)} [{PlayerTargets.DisplayName(target)}] - {(on ? "on" : "off")}"));

        }

        /// <summary>Короткое имя флага для чата.</summary>
        internal static string FlagName(CheatFlag flag) {

            switch (flag) {

                case CheatFlag.GodMode:
                    return Texts.T("бессмертие", "god mode");
                case CheatFlag.OneHitKill:
                    return Texts.T("ваншот", "one-hit kill");
                case CheatFlag.NoCooldown:
                    return Texts.T("без перезарядки", "no cooldowns");
                case CheatFlag.GodModeAll:
                    return Texts.T("бессмертие всех", "god mode for all");
                case CheatFlag.InfiniteJumps:
                    return Texts.T("бесконечные прыжки", "infinite jumps");
                default:
                    return flag.ToString();

            }

        }

        /// <summary>Короткое имя множителя для чата.</summary>
        internal static string ValueName(CheatValue value) {

            switch (value) {

                case CheatValue.DamagePct:
                    return Texts.T("урон", "damage");
                case CheatValue.AttackSpeedPct:
                    return Texts.T("скорость атаки", "attack speed");
                case CheatValue.MoveSpeedPct:
                    return Texts.T("скорость движения", "move speed");
                default:
                    return value.ToString();

            }

        }

        private static void RunSetCheatValue(NetworkUser target, int valueId, int pct) {

            if (!CheatState.IsKnownValue(valueId)) {

                GameApi.Warn("SetCheatValue", new NotSupportedException("value id " + valueId));
                return;

            }

            uint id = TargetId(target);

            if (id == 0u) {

                GameApi.SayErr(Texts.T("чит: нет цели", "cheat: no target"));
                return;

            }

            var value = (CheatValue)valueId;
            int clamped = CheatState.ClampPct(pct);
            CheatState.SetValue(id, value, clamped);

            // скорости применяются в RecalculateStats - инициируем пересчёт,
            // чтобы изменение подействовало сразу, а не при следующем бафе
            var body = PlayerTargets.BodyOf(target);

            if (body != null) {

                try {
                    body.RecalculateStats();
                } catch (Exception e) {
                    GameApi.WarnOnce("SetCheatValue.RecalculateStats", e);
                }

            }

            GameApi.SayOk(Texts.T(
                    $"чит: {ValueName(value)} [{PlayerTargets.DisplayName(target)}] = {clamped}%",
                    $"cheat: {ValueName(value)} [{PlayerTargets.DisplayName(target)}] = {clamped}%"));

        }

        private static void RunHealTarget(NetworkUser target) {

            var body = PlayerTargets.BodyOf(target);
            var health = body != null ? body.healthComponent : null;

            if (health == null || !health.alive) {

                GameApi.SayErr(Texts.T(
                        "чит: у цели нет живого тела",
                        "cheat: target has no living body"));
                return;

            }

            try {

                // SyncVar-аксессоры: здоровье и щит доезжают до клиентов
                health.Networkhealth = health.fullHealth;
                health.Networkshield = health.fullShield;
                health.barrier = 0f;

                GameApi.SayOk(Texts.T(
                        $"чит: {PlayerTargets.DisplayName(target)} полностью вылечен",
                        $"cheat: {PlayerTargets.DisplayName(target)} fully healed"));

            } catch (Exception e) {
                GameApi.Warn("HealTarget", e);
            }

        }

        private static void RunKillEnemies(NetworkUser target, int radius) {

            if (!Spawning.ServerReady(Texts.T("убийство врагов", "kill enemies"))) {
                return;
            }

            var killerBody = PlayerTargets.BodyOf(target);
            var origin = PlayerTargets.PositionOf(target);
            int killed = 0;

            try {

                foreach (var body in InstanceTracker.GetInstancesList<CharacterBody>()) {

                    if (body == null || body.gameObject == null) {
                        continue;
                    }

                    if (body.isPlayerControlled) {
                        continue;
                    }

                    var health = body.healthComponent;

                    if (health == null || !health.alive) {
                        continue;
                    }

                    TeamIndex team;

                    try {
                        team = body.teamComponent != null ? body.teamComponent.teamIndex : TeamIndex.Neutral;
                    } catch (Exception e) {
                        GameApi.WarnOnce("KillEnemies.team", e);
                        continue;
                    }

                    // враги команды игроков: монстры, лунные, войд; нейтралы
                    // (торговец-мусорщик и т.п.) не трогаются
                    if (!TeamManager.IsTeamEnemy(TeamIndex.Player, team)) {
                        continue;
                    }

                    if (radius > 0) {

                        if (origin == Vector3.zero) {
                            continue;
                        }

                        if (Vector3.Distance(origin, body.footPosition) > radius) {
                            continue;
                        }

                    }

                    try {

                        health.TakeDamage(new DamageInfo {
                            damage = (health.combinedHealth + health.barrier) * 3f + 100000f,
                            attacker = killerBody != null ? killerBody.gameObject : null,
                            inflictor = killerBody != null ? killerBody.gameObject : null,
                            damageType = DamageType.Generic,
                            position = body.footPosition,
                            procCoefficient = 0f
                        });
                        killed++;

                    } catch (Exception e) {
                        GameApi.WarnOnce("KillEnemies.damage", e);
                    }

                }

            } catch (Exception e) {
                GameApi.Warn("KillEnemies", e);
            }

            GameApi.SayOk(Texts.T(
                    $"чит: убито врагов: {GameApi.Val(killed)}" + (radius > 0 ? $" (радиус {radius} м)" : " (вся карта)"),
                    $"cheat: enemies killed: {GameApi.Val(killed)}" + (radius > 0 ? $" (radius {radius} m)" : " (whole map)")));

        }

        private static void RunResetCooldowns(NetworkUser target) {

            var body = PlayerTargets.BodyOf(target);

            if (body == null) {

                GameApi.SayErr(Texts.T("чит: у цели нет тела", "cheat: target has no body"));
                return;

            }

            CheatPatches.ResetCooldownsFor(body);
            GameApi.SayOk(Texts.T(
                    $"чит: перезарядки [{PlayerTargets.DisplayName(target)}] сброшены",
                    $"cheat: cooldowns of [{PlayerTargets.DisplayName(target)}] reset"));

        }

        private static void RunGiveBuff(NetworkUser target, int buffIndexValue, int stacks) {

            var body = PlayerTargets.BodyOf(target);

            if (body == null) {

                GameApi.SayErr(Texts.T("чит: у цели нет тела", "cheat: target has no body"));
                return;

            }

            BuffDef def = null;
            var index = (BuffIndex)buffIndexValue;

            try {

                if (buffIndexValue >= 1 && buffIndexValue <= BuffCatalog.buffCount) {
                    def = BuffCatalog.GetBuffDef(index);
                }

            } catch (Exception e) {
                GameApi.WarnOnce("GiveBuff.GetBuffDef", e);
            }

            if (def == null) {

                GameApi.SayErr(Texts.T("чит: баф не найден", "cheat: buff not found") + $" ({buffIndexValue})");
                return;

            }

            int count = CheatState.ClampStacks(stacks);

            try {

                for (int i = 0; i < count; i++) {
                    body.AddBuff(index);
                }

                GameApi.SayOk(Texts.T(
                        $"чит: баф {GameApi.Val(def.name)} x{count}",
                        $"cheat: buff {GameApi.Val(def.name)} x{count}"));

            } catch (Exception e) {
                GameApi.Warn("GiveBuff", e);
            }

        }

        private static void RunClearBuffs(NetworkUser target) {

            var body = PlayerTargets.BodyOf(target);

            if (body == null) {

                GameApi.SayErr(Texts.T("чит: у цели нет тела", "cheat: target has no body"));
                return;

            }

            int removed = 0;

            try {

                int total = BuffCatalog.buffCount;

                for (int i = 1; i <= total && removed < 2000; i++) {

                    var index = (BuffIndex)i;
                    int count = body.GetBuffCount(index);

                    for (int k = 0; k < count && removed < 2000; k++) {

                        body.RemoveBuff(index);
                        removed++;

                    }

                }

            } catch (Exception e) {
                GameApi.Warn("ClearBuffs", e);
            }

            GameApi.SayOk(Texts.T(
                    $"чит: снято бафов/дебафов: {GameApi.Val(removed)}",
                    $"cheat: buffs/debuffs removed: {GameApi.Val(removed)}"));

        }

        private static void RunSetMoney(NetworkUser target, int amount) {

            var master = PlayerTargets.MasterOf(target);

            if (master == null) {

                GameApi.SayErr(Texts.T("чит: у цели нет мастера", "cheat: target has no master"));
                return;

            }

            uint value = (uint)Math.Max(0, Math.Min(amount, CheatState.MaxMoney));
            master.money = value;
            GameApi.SayOk(Texts.T(
                    $"чит: деньги [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(value)}",
                    $"cheat: money of [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(value)}"));

        }

        private static void RunGiveMoney(NetworkUser target, int amount) {

            var master = PlayerTargets.MasterOf(target);

            if (master == null) {

                GameApi.SayErr(Texts.T("чит: у цели нет мастера", "cheat: target has no master"));
                return;
            }

            long total = (long)master.money + Math.Max(0, Math.Min(amount, CheatState.MaxMoney));
            master.money = (uint)Math.Min(total, 100000000L);
            GameApi.SayOk(Texts.T(
                    $"чит: деньги [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(master.money)}",
                    $"cheat: money of [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(master.money)}"));

        }

        private static void RunSetLunarCoins(NetworkUser target, int amount) {

            if (target == null) {

                GameApi.SayErr(Texts.T("чит: нет цели", "cheat: no target"));
                return;

            }

            try {

                uint wanted = (uint)Math.Max(0, Math.Min(amount, CheatState.MaxMoney));
                uint current = target.lunarCoins;

                if (wanted > current) {
                    target.AwardLunarCoins(wanted - current);
                } else if (wanted < current) {
                    target.DeductLunarCoins(current - wanted);
                }

                GameApi.SayOk(Texts.T(
                        $"чит: лунные монеты [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(wanted)}",
                        $"cheat: lunar coins of [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(wanted)}"));

            } catch (Exception e) {
                GameApi.Warn("SetLunarCoins", e);
            }

        }

        private static void RunGiveLunarCoins(NetworkUser target, int amount) {

            if (target == null) {

                GameApi.SayErr(Texts.T("чит: нет цели", "cheat: no target"));
                return;

            }

            try {

                uint add = (uint)Math.Max(0, Math.Min(amount, CheatState.MaxMoney));
                target.AwardLunarCoins(add);
                GameApi.SayOk(Texts.T(
                        $"чит: лунные монеты [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(target.lunarCoins)}",
                        $"cheat: lunar coins of [{PlayerTargets.DisplayName(target)}] = {GameApi.Val(target.lunarCoins)}"));

            } catch (Exception e) {
                GameApi.Warn("GiveLunarCoins", e);
            }

        }

        private static void RunSetTeamLevel(int level) {

            var manager = TeamManager.instance;

            if (manager == null) {

                GameApi.SayErr(Texts.T("чит: нет забега - TeamManager не найден", "cheat: no run - TeamManager missing"));
                return;

            }

            uint value = (uint)Math.Max(1, Math.Min(level, CheatState.MaxLevel));

            try {

                manager.SetTeamLevel(TeamIndex.Player, value);
                GameApi.SayOk(Texts.T(
                        $"чит: уровень команды = {GameApi.Val(value)}",
                        $"cheat: team level = {GameApi.Val(value)}"));

            } catch (Exception e) {
                GameApi.Warn("SetTeamLevel", e);
            }

        }

        private static void RunGiveExperience(int amount) {

            var manager = TeamManager.instance;

            if (manager == null) {

                GameApi.SayErr(Texts.T("чит: нет забега - TeamManager не найден", "cheat: no run - TeamManager missing"));
                return;

            }

            ulong value = (ulong)Math.Max(0, Math.Min(amount, CheatState.MaxExperience));

            try {

                manager.GiveTeamExperience(TeamIndex.Player, value);
                GameApi.SayOk(Texts.T(
                        $"чит: выдано опыта: {GameApi.Val(value)} (уровень {manager.GetTeamLevel(TeamIndex.Player)})",
                        $"cheat: experience given: {GameApi.Val(value)} (level {manager.GetTeamLevel(TeamIndex.Player)})"));

            } catch (Exception e) {
                GameApi.Warn("GiveExperience", e);
            }

        }

        private static void RunRespawnAs(NetworkUser target, string bodyName) {

            if (!NetProtocol.IsServer) {
                return;
            }

            if (string.IsNullOrEmpty(bodyName)) {

                GameApi.Warn("RespawnAs", new NotSupportedException("empty body name"));
                return;

            }

            // имя приходит из сети - проверяем по каталогу тел, а не на глаз
            GameObject prefab = null;

            try {
                prefab = BodyCatalog.FindBodyPrefab(bodyName);
            } catch (Exception e) {
                GameApi.WarnOnce("BodyCatalog.FindBodyPrefab", e);
            }

            if (prefab == null) {

                GameApi.SayErr(Texts.T(
                        "персонаж: тело не найдено в каталоге",
                        "character: the body is not in the catalog") + $": {bodyName}");
                return;

            }

            var master = MasterStrict(target);

            if (master == null) {

                GameApi.SayErr(Texts.T(
                        "персонаж: у цели нет мастера",
                        "character: the target has no master"));
                return;

            }

            try {

                master.Respawn(bodyName);
                GameApi.SayOk(Texts.T(
                        $"персонаж: {PlayerTargets.DisplayName(target)} → {GameApi.Val(bodyName)}",
                        $"character: {PlayerTargets.DisplayName(target)} → {GameApi.Val(bodyName)}"));

            } catch (Exception e) {
                GameApi.Warn("RespawnAs", e);
            }

        }

        /// <summary>
        /// Мастер цели без фолбэка на локального игрока: смена персонажа
        /// «ничьего» клиента не должна пересаживать хоста.
        /// </summary>
        private static CharacterMaster MasterStrict(NetworkUser nu) {

            if (nu == null) {
                return null;
            }

            try {

                if (nu.master != null) {
                    return nu.master;
                }

            } catch (Exception e) {
                GameApi.WarnOnce("MasterStrict", e);
            }

            return null;

        }

    }

}
