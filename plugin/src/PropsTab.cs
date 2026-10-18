using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вкладка «Объекты»: интерактивы текущей сцены (сунуны, автоматы, алтари)
    /// из ClassicStageInfo.interactableCards и зеркальное удаление заспавненного.
    /// </summary>
    internal static class PropsTab {

        private static string _search = "";

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw() {

            Widgets.Header(Texts.T("ОБЪЕКТЫ СЦЕНЫ", "STAGE PROPS"), Theme.HeaderRed,
                    Texts.T("то, что может заспавнить текущая сцена",
                            "whatever the current stage can spawn"));

            GUILayout.BeginHorizontal();
                if (Widgets.Button(Texts.T("Обновить список", "Refresh list"), 170f,
                        Texts.T("перечитать карты спавна сцены",
                                "re-read the stage spawn cards"))) {

                    Catalogs.CachedStage = null;
                    Catalogs.RefreshInteractables();

                }

                if (Widgets.Button(Texts.T("Убрать заспавненные", "Remove spawned"), 190f,
                        Texts.T("зеркало спавна: удалить объекты меню",
                                "mirror of spawn: remove the menu's props"))) {
                    SandboxActions.RemoveSpawnedProps();
                }

                GUILayout.Label(Texts.T($"заспавнено: {SpawnRegistry.PropCount}",
                        $"spawned: {SpawnRegistry.PropCount}"),
                        Theme.Small, GUILayout.Height(Widgets.RowHeight));
            GUILayout.EndHorizontal();

            _search = Widgets.SearchBox(_search);

            int shown = 0;

            foreach (var interactable in Catalogs.Interactables) {

                if (!Widgets.Matches(_search, interactable.Name)) {
                    continue;
                }

                if (shown >= MenuUI.MaxRows) {

                    shown++;
                    continue;

                }

                shown++;

                GUILayout.BeginHorizontal();
                    if (Widgets.Button(Texts.T("Спавн", "Spawn"), 62f,
                            Texts.T("один объект у цели", "one prop at the target"))) {
                        SandboxActions.SpawnProp(interactable.Card, interactable.Name, 1);
                    }

                    if (Widgets.Button("x3", 38f, Texts.T("три по кругу", "three in a ring"))) {
                        SandboxActions.SpawnProp(interactable.Card, interactable.Name, 3);
                    }

                    GUILayout.Label(new GUIContent(interactable.Name, interactable.Name), Theme.Name);
                GUILayout.EndHorizontal();

            }

            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Interactables.Count);

        }
    }
}