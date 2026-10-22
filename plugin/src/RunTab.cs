using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вкладка «Забег»: таймер, сложность, переход на любую стадию, мгновенная
    /// зарядка телепортера, артефакты прямо во время забега и телепорт игроков.
    ///
    /// Все действия идут через <see cref="RunControl"/> (серверная авторитетность,
    /// клиент - запросом). Состояние таймера и артефактов читается из
    /// синхронизированных объектов игры, поэтому вкладка показывает правду
    /// и на клиенте; кэш строк - раз в 0.5 с, как в остальных вкладках.
    /// </summary>
    internal static class RunTab {

        private static string _search = "";
        private static string _stateCache = "";
        private static float _stateAt;

        /// <summary>Рабочий список сложностей: значения enum без служебных.</summary>
        private static DifficultyIndex[] _difficulties;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw() {

            Widgets.Header(Texts.T("ЗАБЕГ", "RUN"), Theme.HeaderBlue,
                    Texts.T("таймер, сложность, стадии, телепортер, артефакты, телепорт игроков",
                            "timer, difficulty, stages, teleporter, artifacts, player teleport"));

            if (!GameApi.InRun) {

                Widgets.Hint(Texts.T(
                        "нет забега: управление доступно после старта",
                        "no run: controls unlock once a run starts"));

            }

            GUILayout.Space(2f);
            DrawTimer();
            GUILayout.Space(2f);
            DrawDifficulty();
            GUILayout.Space(2f);
            DrawTeleporter();
            GUILayout.Space(2f);
            DrawPlayers();
            GUILayout.Space(2f);
            DrawLandmarks();
            GUILayout.Space(2f);
            DrawStages();
            GUILayout.Space(2f);
            DrawArtifacts();

        }

        private static void DrawTimer() {

            Widgets.Header(Texts.T("ТАЙМЕР И СЛОЖНОСТЬ", "TIMER AND DIFFICULTY"), Theme.HeaderBlue);

            if (Time.unscaledTime - _stateAt > 0.5f) {

                _stateAt = Time.unscaledTime;
                _stateCache = BuildRunState();

            }

            GUILayout.Label(_stateCache, Theme.SmallRich, GUILayout.Height(14f));

            bool frozen = CheatState.FreezeTimer;
            bool nextFrozen = Widgets.Toggle(frozen,
                    Texts.T("заморозить таймер забега", "freeze the run timer"),
                    Texts.T("сложность перестаёт расти со временем; существа на карте не трогаются",
                            "difficulty stops growing with time; world spawns untouched"));

            if (nextFrozen != frozen) {
                RunControl.FreezeTimer(nextFrozen);
            }

            GUILayout.BeginHorizontal();

                if (Widgets.Button("-60 " + Texts.T("с", "s"), 66f,
                        Texts.T("отмотать таймер на минуту", "rewind the timer by a minute"))) {

                    NudgeTimer(-60f);

                }

                if (Widgets.Button("+60 " + Texts.T("с", "s"), 66f,
                        Texts.T("добавить минуту к таймеру", "add a minute to the timer"))) {

                    NudgeTimer(60f);

                }

                if (Widgets.Button(Texts.T("Сброс в 0", "Reset to 0"), 100f,
                        Texts.T("обнулить таймер забега", "zero the run timer"))) {

                    RunControl.SetTimer(0);

                }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Скорость игры, %", "Game speed, %"), Theme.Name,
                        GUILayout.Width(110f), GUILayout.Height(Widgets.RowHeight));
                int nextScale = Widgets.SliderInline(CheatState.TimeScalePct,
                        CheatState.MinPct, 400, 200f);

            GUILayout.EndHorizontal();

            if (nextScale != CheatState.TimeScalePct) {

                RunControl.SetTimeScale(nextScale);

            }

            Widgets.Hint(Texts.T(
                    "скорость держится на сервере: симуляция всего мира идёт быстрее или медленнее",
                    "the scale lives on the server: the whole world simulation runs faster or slower"));

        }

        /// <summary>Сдвинуть таймер относительно текущего (читается на сервере и локально).</summary>
        private static void NudgeTimer(float delta) {

            float current = 0f;

            try {

                var run = Run.instance;

                if (run != null) {
                    current = run.GetRunStopwatch();
                }

            } catch (System.Exception e) {
                GameApi.WarnOnce("NudgeTimer", e);
            }

            RunControl.SetTimer(Mathf.Max(0, Mathf.RoundToInt(current + delta)));

        }

        /// <summary>Строка состояния забега: таймер, сложность, стадия (кэш 0.5 с).</summary>
        private static string BuildRunState() {

            var run = Run.instance;

            if (run == null) {

                return Texts.T("забега нет", "no active run");

            }

            string time = "?";
            string difficulty = "?";

            try {
                time = RunControl.FormatTime(run.GetRunStopwatch());
            } catch (System.Exception e) {
                GameApi.WarnOnce("BuildRunState.time", e);
            }

            try {
                difficulty = run.selectedDifficulty.ToString();
            } catch (System.Exception e) {
                GameApi.WarnOnce("BuildRunState.difficulty", e);
            }

            string stage = "?";

            try {

                var sceneDef = SceneCatalog.GetSceneDefForCurrentScene();

                if (sceneDef != null) {
                    stage = GameApi.Localize(sceneDef.nameToken);
                }

            } catch (System.Exception e) {
                GameApi.WarnOnce("BuildRunState.stage", e);
            }

            return Texts.T("таймер", "timer") + $": {GameApi.Val(time)}   " +
                    Texts.T("сложность", "difficulty") + $": {GameApi.Val(difficulty)}   " +
                    Texts.T("стадия", "stage") + $": {GameApi.Val(stage)}";

        }

        private static void DrawDifficulty() {

            var difficulties = Difficulties();

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Сложность", "Difficulty"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));

                if (Widgets.Button("<", 30f, Texts.T("на шаг проще", "one step easier"))) {
                    StepDifficulty(difficulties, -1);
                }

                if (Widgets.Button(">", 30f, Texts.T("на шаг сложнее", "one step harder"))) {
                    StepDifficulty(difficulties, 1);
                }

                GUILayout.Label(Texts.T("(включая Eclipse)", "(Eclipse included)"), Theme.Small,
                        GUILayout.Height(Widgets.RowHeight));

            GUILayout.EndHorizontal();

        }

        /// <summary>Значения DifficultyIndex без Invalid/Count, по возрастанию.</summary>
        private static DifficultyIndex[] Difficulties() {

            if (_difficulties != null) {
                return _difficulties;
            }

            var list = new List<DifficultyIndex>();

            foreach (DifficultyIndex value in System.Enum.GetValues(typeof(DifficultyIndex))) {

                if (value == DifficultyIndex.Invalid || value == DifficultyIndex.Count) {
                    continue;
                }

                list.Add(value);

            }

            list.Sort((a, b) => ((int)a).CompareTo((int)b));
            _difficulties = list.ToArray();
            return _difficulties;

        }

        private static void StepDifficulty(DifficultyIndex[] difficulties, int step) {

            var run = Run.instance;

            if (run == null || difficulties.Length == 0) {
                return;
            }

            int current = 0;

            try {

                var selected = run.selectedDifficulty;

                for (int i = 0; i < difficulties.Length; i++) {

                    if (difficulties[i] == selected) {
                        current = i;
                        break;
                    }

                }

            } catch (System.Exception e) {
                GameApi.WarnOnce("StepDifficulty", e);
            }

            int next = Mathf.Clamp(current + step, 0, difficulties.Length - 1);

            if (next != current) {
                RunControl.SetDifficulty((int)difficulties[next]);
            }

        }

        private static void DrawTeleporter() {

            Widgets.Header(Texts.T("ТЕЛЕПОРТЕР", "TELEPORTER"), Theme.HeaderBlue);

            GUILayout.BeginHorizontal();

                GUILayout.Label(TeleporterState(), Theme.SmallRich, GUILayout.Height(Widgets.RowHeight));
                GUILayout.FlexibleSpace();

                if (Widgets.Button(Texts.T("Зарядить мгновенно", "Charge instantly"), 170f,
                        Texts.T("довести заряд телепортера текущей стадии до конца",
                                "fill the charge of the current stage's teleporter"))) {
                    RunControl.ChargeTeleporter();
                }

            GUILayout.EndHorizontal();

        }

        /// <summary>Состояние телепортера одной строкой.</summary>
        private static string TeleporterState() {

            try {

                var teleporter = TeleporterInteraction.instance;

                if (teleporter == null) {

                    return "<color=#9FB2CC>" + Texts.T("на стадии нет телепортера", "no teleporter on this stage") + "</color>";

                }

                var holdout = teleporter.holdoutZoneController;

                if (holdout != null && holdout.maxCharge > 0f) {

                    int pct = Mathf.Clamp(Mathf.RoundToInt(holdout.charge / holdout.maxCharge * 100f), 0, 100);

                    return Texts.T("заряд", "charge") + $": {GameApi.Val(pct + "%")}";

                }

                return teleporter.isCharged
                        ? Texts.T("заряжен", "charged")
                        : Texts.T("ожидает активации", "idle");

            } catch (System.Exception e) {

                GameApi.WarnOnce("TeleporterState", e);
                return "?";

            }

        }

        private static void DrawPlayers() {

            Widgets.Header(Texts.T("ТЕЛЕПОРТ ИГРОКОВ", "PLAYER TELEPORT"), Theme.HeaderBlue);

            GUILayout.BeginHorizontal();

                if (Widgets.Button(Texts.T("Цель ко мне", "Target to me"), 130f,
                        Texts.T("переместить выбранную цель к отправителю",
                                "move the selected target to the requester"))) {
                    RunControl.TeleportPlayer(false);
                }

                if (Widgets.Button(Texts.T("Меня к цели", "Me to target"), 130f,
                        Texts.T("переместить отправителя к выбранной цели",
                                "move the requester to the selected target"))) {
                    RunControl.TeleportPlayer(true);
                }

            GUILayout.EndHorizontal();

        }

        private static void DrawLandmarks() {

            Widgets.Header(Texts.T("ТЕЛЕПОРТ ПО КАРТЕ", "MAP TELEPORT"), Theme.HeaderBlue,
                    Texts.T("переносит выбранную цель; ориентир ищется на сервере среди живых объектов стадии",
                            "moves the selected target; the landmark is looked up on the server among the stage's live objects"));

            GUILayout.BeginHorizontal();

                if (Widgets.Button(Texts.T("К телепорту", "To teleporter"), 128f,
                        Texts.T("телепорт текущей стадии", "the stage teleporter"))) {
                    RunControl.TeleportLandmark(RunControl.KindTeleporter);
                }

                if (Widgets.Button(Texts.T("К сундуку", "To chest"), 118f,
                        Texts.T("ближайший к цели сундук", "the chest nearest to the target"))) {
                    RunControl.TeleportLandmark(RunControl.KindChest);
                }

                if (Widgets.Button(Texts.T("К алтарю", "To shrine"), 118f,
                        Texts.T("ближайший к цели алтарь", "the shrine nearest to the target"))) {
                    RunControl.TeleportLandmark(RunControl.KindShrine);
                }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

                if (Widgets.Button(Texts.T("К боссу", "To boss"), 128f,
                        Texts.T("ближайший живой босс (в сторону от него)", "the nearest living boss (offset to the side)"))) {
                    RunControl.TeleportLandmark(RunControl.KindBoss);
                }

                if (Widgets.Button(Texts.T("На старт стадии", "To stage spawn"), 158f,
                        Texts.T("точка появления игроков", "the player spawn point"))) {
                    RunControl.TeleportLandmark(RunControl.KindSpawn);
                }

            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            Widgets.Header(Texts.T("ТОЧКИ", "WAYPOINTS"), Theme.HeaderBlue,
                    Texts.T("три слота; точка помнит свою стадию и применяется только на ней",
                            "three slots; a point remembers its stage and only applies there"));

            for (int slot = 1; slot <= RunControl.WaypointCount; slot++) {
                DrawWaypointRow(slot);
            }

        }

        private static void DrawWaypointRow(int slot) {

            string scene = RunControl.WaypointScene(slot);
            string state = scene != null
                    ? Texts.T("стадия", "stage") + ": " + scene
                    : Texts.T("пусто", "empty");

            GUILayout.BeginHorizontal();

                if (Widgets.Button(Texts.T($"Сохр. {slot}", $"Save {slot}"), 90f,
                        Texts.T("запомнить позицию цели", "remember the target's position"))) {
                    RunControl.WaypointSave(slot);
                }

                if (Widgets.Button(Texts.T($"Перенести {slot}", $"Go to {slot}"), 110f,
                        Texts.T("вернуть цель в точку (только на её стадии)",
                                "return the target to the point (only on its stage"))) {
                    RunControl.WaypointLoad(slot);
                }

                GUILayout.Label(new GUIContent(state, state), Theme.Small,
                        GUILayout.Height(Widgets.RowHeight));

            GUILayout.EndHorizontal();

        }

        private static void DrawStages() {

            Widgets.Header(Texts.T("СТАДИИ", "STAGES"), Theme.HeaderBlue,
                    Texts.T("переход на любую стадию напрямую, минуя телепортер",
                            "jump to any stage directly, skipping the teleporter"));

            _search = Widgets.SearchBox(_search);

            int shown = 0;

            foreach (var stage in Catalogs.Stages) {

                if (!Widgets.Matches(_search, stage.Name) && !Widgets.Matches(_search, stage.SceneName)) {
                    continue;
                }

                if (shown >= MenuUI.MaxRows) {

                    shown++;
                    continue;

                }

                shown++;

                GUILayout.BeginHorizontal();

                    if (Widgets.Button(Texts.T("Перейти", "Go"), 76f,
                            Texts.T("немедленно перейти на стадию (вся команда)",
                                    "advance to the stage immediately (whole team)"))) {
                        RunControl.TeleportStage(stage.SceneName);
                    }

                    GUILayout.Label(new GUIContent(stage.Name, stage.SceneName), Theme.Name,
                            GUILayout.Height(Widgets.RowHeight));

                GUILayout.EndHorizontal();

            }

            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Stages.Count);

        }

        private static void DrawArtifacts() {

            Widgets.Header(Texts.T("АРТЕФАКТЫ", "ARTIFACTS"), Theme.HeaderBlue,
                    Texts.T("включаются и выключаются прямо во время забега",
                            "toggled live during the run"));

            var manager = RunArtifactManager.instance;

            if (manager == null) {

                Widgets.Hint(Texts.T("нет забега - артефакты недоступны", "no run - artifacts unavailable"));
                return;

            }

            int shown = 0;

            foreach (var artifact in Catalogs.Artifacts) {

                if (shown >= MenuUI.MaxRows) {

                    shown++;
                    continue;

                }

                shown++;

                bool enabled = false;

                try {
                    enabled = manager.IsArtifactEnabled(artifact.Index);
                } catch (System.Exception e) {
                    GameApi.WarnOnce("IsArtifactEnabled", e);
                }

                GUILayout.BeginHorizontal();

                    bool next = GUILayout.Toggle(enabled,
                            new GUIContent(artifact.Name, artifact.Name + "  [" + artifact.Index + "]"),
                            GUILayout.Height(Widgets.RowHeight));

                    if (next != enabled) {
                        RunControl.SetArtifact((int)artifact.Index, next);
                    }

                GUILayout.EndHorizontal();

            }

            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Artifacts.Count);

        }

    }

}
