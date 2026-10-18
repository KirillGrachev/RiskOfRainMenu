using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вкладка «Персонажи»: спавн любого master-префаба на выбранной команде
    /// рядом с целью и зеркальное удаление всего призванного меню.
    /// </summary>
    internal static class CharactersTab {

        private static string _search = "";
        private static int _count = 1;
        private static int _teamIndex;
        private static bool _allyOnly;

        private static readonly string[] TeamsRu = { "Игрок", "Монстр", "Нейтрал", "Луна", "Войд" };
        private static readonly string[] TeamsEn = { "Player", "Monster", "Neutral", "Lunar", "Void" };

        private static readonly TeamIndex[] TeamValues = {
            TeamIndex.Player, TeamIndex.Monster, TeamIndex.Neutral, TeamIndex.Lunar, TeamIndex.Void
        };

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw() {

            Widgets.Header(Texts.T("ПЕРСОНАЖИ", "CHARACTERS"), Theme.HeaderRed,
                    Texts.T("MasterCatalog: выжившие, монстры, дроны, боссы",
                            "MasterCatalog: survivors, monsters, drones, bosses"));

            GUILayout.BeginHorizontal();
                GUILayout.Label(Texts.T("Команда", "Team"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(22f));

                var teamNames = Texts.Russian ? TeamsRu : TeamsEn;
                int selectedTeam = GUILayout.SelectionGrid(_teamIndex, teamNames, 5, Theme.Button, GUILayout.Height(22f));

                if (selectedTeam != _teamIndex) {
                    _teamIndex = selectedTeam;
                }
            GUILayout.EndHorizontal();

            _count = Widgets.Slider(Texts.T("Кол-во", "How many"), _count, 1, 20);
            _allyOnly = Widgets.Toggle(_allyOnly, Texts.T("только союзные", "ally-like only"),
                    Texts.T("дроны, турели, жуки - фильтр по имени префаба",
                            "drones, turrets, beetles - prefab name filter"));

            GUILayout.BeginHorizontal();
                if (Widgets.Button(Texts.T("Убрать призванных", "Remove spawned"), 170f,
                        Texts.T("зеркало спавна: уничтожить призванных меню",
                                "mirror of spawn: kill everything the menu summoned"))) {
                    SandboxActions.RemoveSpawnedMasters();
                }

                GUILayout.Label(Texts.T($"призванных: {SpawnRegistry.MasterCount}",
                        $"spawned: {SpawnRegistry.MasterCount}"),
                        Theme.Small, GUILayout.Height(Widgets.RowHeight));
            GUILayout.EndHorizontal();

            _search = Widgets.SearchBox(_search);

            var team = TeamValues[_teamIndex];
            int shown = 0;

            foreach (var master in Catalogs.Masters) {

                if (!Widgets.Matches(_search, master.Name)) {
                    continue;
                }

                if (_allyOnly && !master.AllyLike) {
                    continue;
                }

                if (shown >= MenuUI.MaxRows) {

                    shown++;
                    continue;

                }

                shown++;

                GUILayout.BeginHorizontal();
                    if (Widgets.Button(Texts.T("Спавн", "Spawn"), 62f,
                            Texts.T("призвать рядом с целью", "summon near the target"))) {
                        SandboxActions.SpawnMaster(master.Prefab, master.Name, team, _count);
                    }

                    GUILayout.Label(new GUIContent(master.Name, master.Name), Theme.Name);
                GUILayout.EndHorizontal();

            }

            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Masters.Count);

        }
    }
}