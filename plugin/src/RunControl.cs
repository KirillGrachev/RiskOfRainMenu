using System;
using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Управление забегом: таймер, сложность, переход по стадиям, телепортер,
    /// артефакты и телепортация игроков.
    ///
    /// Контракт тот же, что у <see cref="CheatActions"/>: локальное нажатие
    /// исполняется на сервере, удалённое уезжает запросом в те же Run-тела.
    /// Все действия требуют активного забега; вне забега кнопки честно
    /// говорят об этом в чат, а не молча ничего не делают.
    /// </summary>
    internal static class RunControl {

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
        /// <summary>Заморозить/разморозить таймер забега (глобальный флаг).</summary>
        public static void FreezeTimer(bool on) {

            if (Local) {
                RunFreezeTimer(on);
                return;
            }

            CheatState.FreezeTimer = on;
            Send(new SandboxRequest { Action = ActionCode.FreezeTimer, P1 = on ? 1 : 0 });

        }

        /// <summary>Установить таймер забега в секундах (абсолютно).</summary>
        public static void SetTimer(int seconds) {

            if (Local) {
                RunSetTimer(seconds);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.SetTimer, P1 = seconds });

        }

        /// <summary>Установить сложность забега (DifficultyIndex числом).</summary>
        public static void SetDifficulty(int index) {

            if (Local) {
                RunSetDifficulty(index);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.SetDifficulty, P1 = index });

        }

        /// <summary>Перейти на стадию по внутреннему имени сцены.</summary>
        public static void TeleportStage(string sceneName) {

            if (Local) {
                RunTeleportStage(sceneName);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.TeleportStage, Name = sceneName });

        }

        /// <summary>Мгновенно зарядить телепортер текущей стадии.</summary>
        public static void ChargeTeleporter() {

            if (Local) {
                RunChargeTeleporter();
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.ChargeTeleporter });

        }

        /// <summary>Включить/выключить артефакт прямо во время забега.</summary>
        public static void SetArtifact(int artifactIndexValue, bool on) {

            if (Local) {
                RunSetArtifact(artifactIndexValue, on);
                return;
            }

            Send(new SandboxRequest { Action = ActionCode.SetArtifact, P1 = artifactIndexValue, P2 = on ? 1 : 0 });

        }

        /// <summary>
        /// Телепорт игроков: sendMeToTarget - отправителя к цели,
        /// иначе цель к отправителю. Отправитель передаётся своим netId:
        /// сервер резолвит его тем же FindByNetId, что и цель.
        /// </summary>
        public static void TeleportPlayer(bool sendMeToTarget) {

            var t = Target;

            if (Local) {
                RunTeleportPlayer(t, sendMeToTarget, SenderId());
                return;
            }

            Send(new SandboxRequest {
                Action = ActionCode.TeleportPlayer,
                TargetNetId = TargetId(t),
                P1 = sendMeToTarget ? 1 : 0,
                P2 = (int)SenderId()
            });

        }

        /// <summary>netId локального игрока (0 - вне забега/лобби без NetworkUser).</summary>
        internal static uint SenderId() {

            var lu = GameApi.GetLocalUser();
            var nu = lu != null ? lu.currentNetworkUser : null;
            return PlayerTargets.NetIdOf(nu);

        }

        // --------------------------------------------------------- dispatch --
        /// <summary>
        /// Серверное исполнение запросов забега. false - код не наш.
        /// </summary>
        internal static bool TryExecute(SandboxRequest req, NetworkUser target) {

            switch (req.Action) {

                case ActionCode.FreezeTimer:
                    RunFreezeTimer(req.P1 != 0);
                    return true;
                case ActionCode.SetTimer:
                    RunSetTimer(req.P1);
                    return true;
                case ActionCode.SetDifficulty:
                    RunSetDifficulty(req.P1);
                    return true;
                case ActionCode.TeleportStage:
                    RunTeleportStage(req.Name);
                    return true;
                case ActionCode.ChargeTeleporter:
                    RunChargeTeleporter();
                    return true;
                case ActionCode.SetArtifact:
                    RunSetArtifact(req.P1, req.P2 != 0);
                    return true;
                case ActionCode.TeleportPlayer:
                    RunTeleportPlayer(target, req.P1 != 0, (uint)req.P2);
                    return true;
                default:
                    return false;

            }

        }

        // ------------------------------------------------------ run bodies ---
        private static void RunFreezeTimer(bool on) {

            var run = Run.instance;

            if (run == null) {
                NoRun();
                return;
            }

            try {

                run.SetForcePauseRunStopwatch(on);
                CheatState.FreezeTimer = on;
                GameApi.SayOk(Texts.T(
                        $"забег: таймер {(on ? "заморожен" : "разморожен")}",
                        $"run: timer {(on ? "frozen" : "unfrozen")}"));

            } catch (Exception e) {
                GameApi.Warn("FreezeTimer", e);
            }

        }

        private static void RunSetTimer(int seconds) {

            var run = Run.instance;

            if (run == null) {
                NoRun();
                return;
            }

            int value = Math.Max(0, Math.Min(seconds, CheatState.MaxTimerSeconds));

            try {

                run.SetRunStopwatch(value);
                GameApi.SayOk(Texts.T(
                        $"забег: таймер = {GameApi.Val(FormatTime(value))}",
                        $"run: timer = {GameApi.Val(FormatTime(value))}"));

            } catch (Exception e) {
                GameApi.Warn("SetTimer", e);
            }

        }

        private static void RunSetDifficulty(int index) {

            var run = Run.instance;

            if (run == null) {
                NoRun();
                return;
            }

            if (!Enum.IsDefined(typeof(DifficultyIndex), index)) {

                GameApi.Warn("SetDifficulty", new NotSupportedException("difficulty index " + index));
                return;

            }

            var difficulty = (DifficultyIndex)index;

            if (difficulty == DifficultyIndex.Invalid || difficulty == DifficultyIndex.Count) {
                return;
            }

            try {

                run.selectedDifficulty = difficulty;
                run.RecalculateDifficultyCoefficent();
                GameApi.SayOk(Texts.T(
                        $"забег: сложность {GameApi.Val(difficulty)} (коэффициент {run.difficultyCoefficient:F2})",
                        $"run: difficulty {GameApi.Val(difficulty)} (coefficient {run.difficultyCoefficient:F2})"));

            } catch (Exception e) {
                GameApi.Warn("SetDifficulty", e);
            }

        }

        private static void RunTeleportStage(string sceneName) {

            var run = Run.instance;

            if (run == null) {
                NoRun();
                return;
            }

            if (string.IsNullOrEmpty(sceneName)) {

                GameApi.SayErr(Texts.T("забег: имя сцены пусто", "run: scene name is empty"));
                return;

            }

            SceneDef def = null;

            try {
                def = SceneCatalog.GetSceneDefFromSceneName(sceneName);
            } catch (Exception e) {
                GameApi.WarnOnce("GetSceneDefFromSceneName", e);
            }

            if (def == null) {

                GameApi.SayErr(Texts.T("забег: сцена не найдена", "run: scene not found") + $": {sceneName}");
                return;

            }

            try {

                run.AdvanceStage(def);
                GameApi.SayOk(Texts.T(
                        $"забег: переход на стадию {GameApi.Val(GameApi.Localize(def.nameToken))}",
                        $"run: advancing to stage {GameApi.Val(GameApi.Localize(def.nameToken))}"));

            } catch (Exception e) {
                GameApi.Warn("TeleportStage", e);
            }

        }

        private static void RunChargeTeleporter() {

            var teleporter = TeleporterInteraction.instance;

            if (teleporter == null) {

                GameApi.SayErr(Texts.T(
                        "забег: на стадии нет телепортера",
                        "run: this stage has no teleporter"));
                return;

            }

            var holdout = teleporter.holdoutZoneController;

            if (holdout == null) {

                GameApi.SayErr(Texts.T(
                        "забег: телепортер не в режиме зарядки",
                        "run: teleporter is not in charging mode"));
                return;

            }

            try {

                // charge - read-only свойство (сеттер приватный), поэтому пишем
                // в приватное поле _charge; обновление контроллера само увидит
                // заряд >= maxCharge и вызовет onCharged (поле проверяет GameRefsTests)
                ResolveHoldoutReflection();

                if (_holdoutChargeField == null) {

                    GameApi.SayErr(Texts.T(
                            "забег: в этой сборке игры заряд телепортера недоступен",
                            "run: teleporter charge is not writable in this game build"));
                    return;

                }

                _holdoutChargeField.SetValue(holdout, holdout.maxCharge);
                GameApi.SayOk(Texts.T(
                        "забег: телепортер заряжен мгновенно",
                        "run: teleporter charged instantly"));

            } catch (Exception e) {
                GameApi.Warn("ChargeTeleporter", e);
            }

        }

        private static System.Reflection.FieldInfo _holdoutChargeField;
        private static bool _holdoutResolved;

        /// <summary>Один раз находит приватное поле заряда холдаут-зоны.</summary>
        private static void ResolveHoldoutReflection() {

            if (_holdoutResolved) {
                return;
            }

            _holdoutResolved = true;
            _holdoutChargeField = typeof(HoldoutZoneController).GetField("_charge",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (_holdoutChargeField == null) {

                GameApi.WarnOnce("ChargeTeleporter",
                        new MissingFieldException("HoldoutZoneController._charge: instant charge degrades"));

            }

        }

        private static void RunSetArtifact(int artifactIndexValue, bool on) {

            var manager = RunArtifactManager.instance;

            if (manager == null) {
                NoRun();
                return;
            }

            ArtifactDef def = null;

            try {

                if (artifactIndexValue >= 1 && artifactIndexValue <= ArtifactCatalog.artifactCount) {
                    def = ArtifactCatalog.GetArtifactDef((ArtifactIndex)artifactIndexValue);
                }

            } catch (Exception e) {
                GameApi.WarnOnce("GetArtifactDef", e);
            }

            if (def == null) {

                GameApi.SayErr(Texts.T("забег: артефакт не найден", "run: artifact not found") +
                        $" ({artifactIndexValue})");
                return;

            }

            try {

                manager.SetArtifactEnabledServer(def, on);
                GameApi.SayOk(Texts.T(
                        $"забег: артефакт {GameApi.Val(GameApi.Localize(def.nameToken))} - {(on ? "вкл" : "выкл")}",
                        $"run: artifact {GameApi.Val(GameApi.Localize(def.nameToken))} - {(on ? "on" : "off")}"));

            } catch (Exception e) {
                GameApi.Warn("SetArtifact", e);
            }

        }

        private static void RunTeleportPlayer(NetworkUser target, bool sendRequesterToTarget, uint requesterNetId) {

            var requester = PlayerTargets.FindByNetId(requesterNetId);
            NetworkUser from = sendRequesterToTarget ? requester : target;
            NetworkUser to = sendRequesterToTarget ? target : requester;

            if (from == null || to == null) {

                GameApi.SayErr(Texts.T(
                        "телепорт: нет отправителя или цели",
                        "teleport: missing requester or target"));
                return;

            }

            var fromBody = BodyStrict(from);
            var toBody = BodyStrict(to);

            if (fromBody == null || toBody == null) {

                GameApi.SayErr(Texts.T(
                        "телепорт: у кого-то нет живого тела",
                        "teleport: someone has no living body"));
                return;

            }

            try {

                var position = toBody.footPosition + Vector3.up * 0.5f;
                TeleportHelper.TeleportBody(fromBody, position);
                GameApi.SayOk(Texts.T(
                        $"телепорт: {PlayerTargets.DisplayName(from)} → {PlayerTargets.DisplayName(to)}",
                        $"teleport: {PlayerTargets.DisplayName(from)} → {PlayerTargets.DisplayName(to)}"));

            } catch (Exception e) {
                GameApi.Warn("TeleportPlayer", e);
            }

        }

        // ------------------------------------------------------------ misc ---
        /// <summary>
        /// Тело игрока без фолбэка на локального (PlayerTargets.BodyOf
        /// подменяет null локальным телом - для телепорта чужих игроков
        /// это отправило бы не того).
        /// </summary>
        private static CharacterBody BodyStrict(NetworkUser nu) {

            if (nu == null) {
                return null;
            }

            try {

                var body = nu.GetCurrentBody();

                if (body != null) {
                    return body;
                }

            } catch (Exception e) {
                GameApi.WarnOnce("BodyStrict.GetCurrentBody", e);
            }

            try {

                var master = nu.master;

                if (master != null) {
                    return master.GetBody();
                }

            } catch (Exception e) {
                GameApi.WarnOnce("BodyStrict.master", e);
            }

            return null;

        }

        private static void NoRun() {

            GameApi.SayErr(Texts.T("нет забега - действие недоступно", "no run - action unavailable"));

        }

        /// <summary>Секунды таймера в mm:ss (для чата и вкладки).</summary>
        internal static string FormatTime(float seconds) {

            if (seconds < 0f) {
                seconds = 0f;
            }

            int total = Mathf.FloorToInt(seconds);
            return $"{total / 60:00}:{total % 60:00}";

        }

    }

}
