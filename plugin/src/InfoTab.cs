using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вкладка «Инфо»: сводка состояния (кэш раз в секунду, чтобы не дёргать
    /// каталоги каждый кадр), список игроков, последние предупреждения и
    /// пересборка каталогов. Сюда же доедают ошибки спавна и сети.
    /// </summary>
    internal static class InfoTab {

        private static string _cache = "";
        private static float _cacheAt;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw() {

            Widgets.Header(Texts.T("СОСТОЯНИЕ", "STATE"), Theme.HeaderBlue,
                    Texts.T("сводка раз в секунду", "refreshed once per second"));

            if (Time.unscaledTime - _cacheAt > 1f) {

                _cacheAt = Time.unscaledTime;
                _cache = BuildSummary();

            }

            GUILayout.Label(_cache, Theme.Small);

            GUILayout.Space(4f);

            var playerNames = PlayerTargets.DisplayNames();

            for (int index = 0; index < playerNames.Length; index++) {
                GUILayout.Label((index == PlayerTargets.Selected ? "> " : "   ") + playerNames[index],
                        Theme.Small, GUILayout.Height(14f));
            }

            GUILayout.Space(4f);
            Widgets.Header(Texts.T("ПРЕДУПРЕЖДЕНИЯ", "WARNINGS"), Theme.HeaderRed,
                    Texts.T("то же самое лежит в BepInEx/LogOutput.log",
                            "the same lines live in BepInEx/LogOutput.log"));

            var warnings = GameApi.RecentWarnings();

            if (warnings.Count == 0) {
                GUILayout.Label("none", Theme.Small, GUILayout.Height(14f));
            }

            foreach (var warning in warnings) {
                GUILayout.Label("  " + warning, Theme.Small, GUILayout.Height(14f));
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
                if (Widgets.Button(Texts.T("Убрать модели-призраки", "Remove ghost models"), 210f,
                        Texts.T("доубить тела без master'а: наследие старых удалений",
                                "finish off bodies without a master: leftovers of old removes"))) {
                    SandboxActions.CleanupOrphans();
                }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);

            if (Widgets.Button(Texts.T("Пересобрать каталоги", "Rebuild catalogs"), 190f,
                    Texts.T("перечитать каталоги игры заново",
                            "re-read the game catalogs"))) {
                Catalogs.BuildAll();
            }

        }

        /// <summary>Текст сводки: роль, сцена, каталоги, клоны, реестр, бэкапы.</summary>
        private static string BuildSummary() {

            var body = GameApi.GetBody();
            string role = NetProtocol.IsServer
                    ? Texts.T("сервер", "server")
                    : (NetProtocol.HostHasMod
                            ? Texts.T("клиент, хост с модом", "client, host has mod")
                            : Texts.T("клиент, хост без мода", "client, host without mod"));

            return Texts.T("роль", "role") + $": {role}\n" +
                    Texts.T("забег", "run") + $": {(GameApi.InRun ? Texts.T("да", "yes") : Texts.T("нет", "no"))}   " +
                    Texts.T("сцена", "stage") + $": {(Stage.instance != null ? Stage.instance.sceneDef?.nameToken ?? "?" : Texts.T("нет", "none"))}\n" +
                    Texts.T("тело", "body") + $": {(body != null ? body.name : Texts.T("нет", "none"))}   " +
                    Texts.T("команда", "team") + $": {(body != null && body.teamComponent != null ? GameApi.TeamName(body.teamComponent.teamIndex) : "-")}\n" +
                    Texts.T("персонажи", "masters") + $": {Catalogs.Masters.Count}   " +
                    Texts.T("предметы", "items") + $": {Catalogs.Items.Count}\n" +
                    Texts.T("пикапы", "pickups") + $": {Catalogs.Pickups.Count}   " +
                    Texts.T("объекты", "props") + $": {Catalogs.Interactables.Count}\n" +
                    Texts.T("бафы", "buffs") + $": {Catalogs.Buffs.Count}   " +
                    Texts.T("стадии", "stages") + $": {Catalogs.Stages.Count}   " +
                    Texts.T("артефакты", "artifacts") + $": {Catalogs.Artifacts.Count}\n" +
                    Texts.T("разблокировок", "unlockables") + $": {UnlockPatches.UnlockableCount}   " +
                    Texts.T("скинов", "skins") + $": {UnlockPatches.SkinCount}\n" +
                    Texts.T("клоны", "clones") + $": {CloneManager.AliveTotal}   " +
                    Texts.T("заспавнено", "spawned") + $": {SpawnRegistry.MasterCount}/{SpawnRegistry.PropCount}\n" +
                    Texts.T("игроки", "players") + $": {PlayerTargets.Count}   " +
                    Texts.T("бэкапы", "backups") + $": {UnlockPatches.Backups().Count}\n" +
                    BuildRunLine() + "\n" +
                    BuildCheatLine();

        }

        /// <summary>Строка забега: деньги цели, уровень команды, таймер.</summary>
        private static string BuildRunLine() {

            string money = "-", level = "-", timer = "-";

            try {

                var master = PlayerTargets.Master;

                if (master != null) {
                    money = master.money.ToString();
                }

                if (TeamManager.instance != null) {
                    level = TeamManager.instance.GetTeamLevel(TeamIndex.Player).ToString();
                }

                var run = Run.instance;

                if (run != null) {
                    timer = RunControl.FormatTime(run.GetRunStopwatch());
                }

            } catch (System.Exception e) {
                GameApi.WarnOnce("InfoTab.BuildRunLine", e);
            }

            return Texts.T("деньги", "money") + $": {money}   " +
                    Texts.T("уровень", "level") + $": {level}   " +
                    Texts.T("таймер", "timer") + $": {timer}";

        }

        /// <summary>Строка активных читов: сколько игроков с флагами и множителями.</summary>
        private static string BuildCheatLine() {

            string line = Texts.T("читы", "cheats") + $": {CheatState.FlaggedPlayerCount} " +
                    Texts.T("с флагами", "flagged") + $", {CheatState.TunedPlayerCount} " +
                    Texts.T("с множителями", "tuned");

            if (CheatState.GodModeAll) {
                line += "   [god-all]";
            }

            if (CheatState.FreezeTimer) {
                line += "   [freeze]";
            }

            if (FlyController.Enabled) {
                line += "   [fly]";
            }

            return line;

        }
    }
}