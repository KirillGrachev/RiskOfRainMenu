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

        // ------------------------------------------------------- landmarks --
        /// <summary>Виды ориентиров для TeleportLandmark (значения P1 запроса).</summary>
        public const int KindTeleporter = 1;
        public const int KindChest = 2;
        public const int KindShrine = 3;
        public const int KindBoss = 4;
        public const int KindSpawn = 5;

        /// <summary>Последний допустимый вид ориентира (для клампа протокола).</summary>
        public const int LastLandmarkKind = KindSpawn;

        /// <summary>Число слотов сохранённых точек.</summary>
        public const int WaypointCount = 3;

        /// <summary>
        /// Точка помнит свою стадию: координаты чужой карты бессмысленны
        /// (игрок упал бы в пустоту), поэтому перенос возможен только там,
        /// где точка сохранена.
        /// </summary>
        private class Waypoint {

            public string Scene;
            public Vector3 Position;

        }

        private static readonly Waypoint[] Points = new Waypoint[WaypointCount];

        /// <summary>Занят ли слот точки (для строки состояния вкладки).</summary>
        public static bool HasWaypoint(int slot) {

            return slot >= 1 && slot <= WaypointCount && Points[slot - 1] != null;

        }

        /// <summary>Имя стадии сохранённой точки или null, если слот пуст.</summary>
        public static string WaypointScene(int slot) {

            return HasWaypoint(slot) ? Points[slot - 1].Scene : null;

        }

        /// <summary>Перенести цель к ориентиру карты (телепорт, сундук, алтарь, босс, старт).</summary>
        public static void TeleportLandmark(int kind) {

            var t = Target;

            if (Local) {
                RunTeleportLandmark(t, kind);
                return;
            }

            Send(new SandboxRequest {
                Action = ActionCode.TeleportLandmark,
                TargetNetId = TargetId(t),
                P1 = kind
            });

        }

        /// <summary>Запомнить позицию цели в слоте точек.</summary>
        public static void WaypointSave(int slot) {

            var t = Target;

            if (Local) {
                RunWaypointSave(t, slot);
                return;
            }

            // позиции синхронизированы - зеркало сохраняется и на клиенте,
            // чтобы строка состояния слота не врала до ответа сервера
            TrySavePoint(t, slot);
            Send(new SandboxRequest {
                Action = ActionCode.WaypointSave,
                TargetNetId = TargetId(t),
                P1 = slot
            });

        }

        /// <summary>Вернуть цель к сохранённой точке (только на её стадии).</summary>
        public static void WaypointLoad(int slot) {

            var t = Target;

            if (Local) {
                RunWaypointLoad(t, slot);
                return;
            }

            Send(new SandboxRequest {
                Action = ActionCode.WaypointLoad,
                TargetNetId = TargetId(t),
                P1 = slot
            });

        }

        // --------------------------------------------------------- timescale --
        /// <summary>Скорость игры в процентах (глобально, живёт на сервере).</summary>
        public static void SetTimeScale(int pct) {

            int clamped = CheatState.ClampPct(pct);

            if (Local) {
                RunSetTimeScale(clamped);
                return;
            }

            CheatState.TimeScalePct = clamped;
            Send(new SandboxRequest { Action = ActionCode.SetTimeScale, P1 = clamped });

        }

        private static bool _timeScaleForced;

        /// <summary>
        /// Каждый кадр из Plugin.Update: держим Time.timeScale, только пока
        /// процент не равен 100 - иначе игра не могла бы сама управлять
        /// замедлениями (катсцены, эффекты времени).
        /// </summary>
        internal static void ApplyTimeScaleFrame() {

            if (!NetProtocol.IsServer || CheatState.TimeScalePct == CheatState.DefaultPct) {

                if (_timeScaleForced) {

                    Time.timeScale = 1f;
                    _timeScaleForced = false;

                }

                return;

            }

            float target = CheatState.TimeScalePct / 100f;

            if (Mathf.Abs(Time.timeScale - target) > 0.001f) {
                Time.timeScale = target;
            }

            _timeScaleForced = true;

        }

        /// <summary>Вызов при выгрузке плагина: вернуть игре штатное время.</summary>
        internal static void ShutdownTimeScale() {

            try {

                if (_timeScaleForced) {
                    Time.timeScale = 1f;
                }

            } catch { /* best effort */ }

            _timeScaleForced = false;

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
                case ActionCode.TeleportLandmark:
                    RunTeleportLandmark(target, req.P1);
                    return true;
                case ActionCode.WaypointSave:
                    RunWaypointSave(target, req.P1);
                    return true;
                case ActionCode.WaypointLoad:
                    RunWaypointLoad(target, req.P1);
                    return true;
                case ActionCode.SetTimeScale:
                    RunSetTimeScale(req.P1);
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

            if (MoveBody(fromBody, toBody.footPosition + Vector3.up * 0.5f)) {

                GameApi.SayOk(Texts.T(
                        $"телепорт: {PlayerTargets.DisplayName(from)} → {PlayerTargets.DisplayName(to)}",
                        $"teleport: {PlayerTargets.DisplayName(from)} → {PlayerTargets.DisplayName(to)}"));

            }

        }

        // -------------------------------------------------------- landmarks --
        private static void RunTeleportLandmark(NetworkUser target, int kind) {

            var body = BodyStrict(target);

            if (body == null) {

                GameApi.SayErr(Texts.T(
                        "телепорт: у цели нет живого тела",
                        "teleport: the target has no living body"));
                return;

            }

            Vector3 origin = body.footPosition;
            Vector3 destination;
            string label;

            if (!TryFindLandmark(kind, origin, out destination, out label)) {

                GameApi.SayErr(Texts.T(
                        "телепорт: ориентир не найден",
                        "teleport: the landmark was not found"));
                return;

            }

            if (MoveBody(body, destination)) {

                GameApi.SayOk(Texts.T(
                        $"телепорт: {PlayerTargets.DisplayName(target)} → {label}",
                        $"teleport: {PlayerTargets.DisplayName(target)} → {label}"));

            }

        }

        /// <summary>Поиск ориентира: точка в мире и короткая подпись для чата.</summary>
        private static bool TryFindLandmark(int kind, Vector3 origin, out Vector3 destination, out string label) {

            destination = Vector3.zero;
            label = "";

            try {

                switch (kind) {

                    case KindTeleporter: {

                        var teleporter = TeleporterInteraction.instance;

                        if (teleporter == null || teleporter.transform == null) {
                            return false;
                        }

                        destination = teleporter.transform.position + Vector3.up * 2.5f;
                        label = Texts.T("телепорт", "teleporter");
                        return true;

                    }

                    case KindChest:
                    case KindShrine:
                        return NearestPurchase(kind == KindChest, origin, out destination, out label);

                    case KindBoss:
                        return NearestBoss(origin, out destination, out label);

                    case KindSpawn: {

                        var stage = Stage.instance;

                        if (stage == null) {
                            return false;
                        }

                        var spawn = stage.GetPlayerSpawnTransform();

                        if (spawn == null) {
                            return false;
                        }

                        destination = spawn.position;
                        label = Texts.T("старт стадии", "stage spawn");
                        return true;

                    }

                    default:
                        return false;

                }

            } catch (Exception e) {

                GameApi.WarnOnce("TryFindLandmark", e);
                return false;

            }

        }

        /// <summary>
        /// Ближайший сундук (chests=true) или алтарь: ищем среди живых
        /// PurchaseInteraction по компоненту-поведению. Прочие интерактивы
        /// (дроны, нюта) не трогаем: единого признака «сюда стоит переносить»
        /// у них нет.
        /// </summary>
        private static bool NearestPurchase(bool chests, Vector3 origin, out Vector3 destination, out string label) {

            destination = Vector3.zero;
            label = "";
            float best = float.MaxValue;

            foreach (var purchase in InstanceTracker.GetInstancesList<PurchaseInteraction>()) {

                if (purchase == null || purchase.transform == null) {
                    continue;
                }

                bool chest = purchase.GetComponent<ChestBehavior>() != null;
                bool shrine = !chest && purchase.GetComponent<ShrineBehavior>() != null;

                if (chests ? !chest : !shrine) {
                    continue;
                }

                Vector3 position = purchase.transform.position;
                float distance = (position - origin).sqrMagnitude;

                if (distance >= best) {
                    continue;
                }

                best = distance;
                destination = position + Vector3.up * 1.5f;
                label = chest
                        ? Texts.T("сундук", "chest")
                        : Texts.T("алтарь", "shrine");

            }

            return best < float.MaxValue;

        }

        private static bool NearestBoss(Vector3 origin, out Vector3 destination, out string label) {

            destination = Vector3.zero;
            label = "";
            float best = float.MaxValue;

            foreach (var body in CharacterBody.readOnlyInstancesList) {

                if (body == null || !body.isBoss) {
                    continue;
                }

                var health = body.healthComponent;

                if (health == null || !health.alive) {
                    continue;
                }

                var team = body.teamComponent;

                if (team == null || team.teamIndex == TeamIndex.Player) {
                    continue;
                }

                float distance = (body.footPosition - origin).sqrMagnitude;

                if (distance >= best) {
                    continue;
                }

                best = distance;
                // не в самого босса, а в сторону от него: телепорт внутрь модели неприятен
                destination = body.footPosition + new Vector3(5f, 1.5f, 0f);

                try {
                    label = body.GetDisplayName();
                } catch {
                    label = Texts.T("босс", "boss");
                }

            }

            return best < float.MaxValue;

        }

        // --------------------------------------------------------- waypoints --
        private static void RunWaypointSave(NetworkUser target, int slot) {

            if (TrySavePoint(target, slot)) {

                GameApi.SayOk(Texts.T($"точка {slot}: сохранена", $"waypoint {slot}: saved"));

            } else {

                GameApi.SayErr(Texts.T(
                        $"точка {slot}: у цели нет позиции",
                        $"waypoint {slot}: the target has no position"));

            }

        }

        private static void RunWaypointLoad(NetworkUser target, int slot) {

            if (!HasWaypoint(slot)) {

                GameApi.SayErr(Texts.T($"точка {slot}: пуста", $"waypoint {slot}: empty"));
                return;

            }

            var body = BodyStrict(target);

            if (body == null) {

                GameApi.SayErr(Texts.T(
                        "телепорт: у цели нет живого тела",
                        "teleport: the target has no living body"));
                return;

            }

            var point = Points[slot - 1];

            if (!string.Equals(point.Scene, CurrentSceneName(), StringComparison.Ordinal)) {

                GameApi.SayErr(Texts.T(
                        $"точка {slot}: сохранена на другой стадии",
                        $"waypoint {slot}: saved on another stage"));
                return;

            }

            if (MoveBody(body, point.Position + Vector3.up * 0.5f)) {

                GameApi.SayOk(Texts.T($"точка {slot}: перенос выполнен", $"waypoint {slot}: warped"));

            }

        }

        /// <summary>Запись точки без чата: используется и сервером, и зеркалом клиента.</summary>
        private static bool TrySavePoint(NetworkUser target, int slot) {

            if (slot < 1 || slot > WaypointCount) {
                return false;
            }

            var body = BodyStrict(target);

            if (body == null) {
                return false;
            }

            Points[slot - 1] = new Waypoint {
                Scene = CurrentSceneName(),
                Position = body.footPosition
            };

            return true;

        }

        private static string CurrentSceneName() {

            try {

                var stage = Stage.instance;
                var def = stage != null ? stage.sceneDef : null;

                if (def != null && !string.IsNullOrEmpty(def.cachedName)) {
                    return def.cachedName;
                }

            } catch (Exception e) {
                GameApi.WarnOnce("CurrentSceneName", e);
            }

            return UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        }

        // ------------------------------------------------------------- shared --
        /// <summary>
        /// Перенос тела штатным TeleportBodyArgs (тот же набор флагов, что у
        /// телепорта игроков): миньонов берём с собой, транспорт покидаем,
        /// конечные автоматы не сбрасываем. 2-аргументный TeleportBody
        /// помечен obsolete, а обещанного его сообщением 4-аргументного
        /// оверлода в dll нет - полный набор живёт в структурных аргументах.
        /// </summary>
        private static bool MoveBody(CharacterBody body, Vector3 position) {

            try {

                TeleportHelper.TeleportBody(new TeleportHelper.TeleportBodyArgs {
                    body = body,
                    targetPosition = position,
                    forceOutOfVehicle = true,
                    teleportMinions = true,
                    resetStateMachines = false
                });

                return true;

            } catch (Exception e) {

                GameApi.Warn("MoveBody", e);
                return false;

            }

        }

        // --------------------------------------------------------- timescale --
        private static void RunSetTimeScale(int pct) {

            int clamped = CheatState.ClampPct(pct);
            CheatState.TimeScalePct = clamped;

            GameApi.SayOk(Texts.T(
                    $"забег: скорость игры = {GameApi.Val(clamped + "%")}",
                    $"run: game speed = {GameApi.Val(clamped + "%")}"));

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
