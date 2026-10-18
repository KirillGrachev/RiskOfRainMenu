using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Вкладка «Дроп»: всё, что может лежать на земле (PickupCatalog), и
    /// зеркальная кнопка очистки мира от лежащего дропа.
    /// </summary>
    internal static class DropsTab
    {
        private static string _search = "";
        private static int _count = 1;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw()
        {
            Widgets.Header(Texts.T("ДРОП", "DROPS"), Theme.HeaderPurple,
                           Texts.T("PickupCatalog: предметы, оборудование, артефакты, дроны, монеты",
                                   "PickupCatalog: items, equipment, artifacts, drones, coins"));

            _count = Widgets.Slider(Texts.T("Кол-во", "How many"), _count, 1, 20);

            // три ветви зеркала: по умолчанию - лишний дроп рядом с целью,
            // затем только выброшенный меню, затем весь дроп мира
            Widgets.Row(() =>
            {
                if (Widgets.Button(Texts.T("Убрать дроп рядом", "Clear drops nearby"), 170f,
                                   Texts.T("по умолчанию: капли и пикапы в радиусе 15 м от цели",
                                           "default: droplets and pickups within 15 m of the target")))
                {
                    SandboxActions.ClearDropsNear();
                }
                if (Widgets.Button(Texts.T("дроп меню", "menu drops"), 100f,
                                   Texts.T($"только выброшенный меню: {SpawnRegistry.MenuDropCount} шт.",
                                           $"only what the menu dropped: {SpawnRegistry.MenuDropCount}")))
                {
                    SandboxActions.ClearMenuDrops();
                }
                if (Widgets.Button(Texts.T("весь дроп", "all drops"), 100f,
                                   Texts.T("все капли и лежащие пикапы в мире",
                                           "every droplet and lying pickup in the world")))
                {
                    SandboxActions.ClearDrops();
                }
            });

            if (!GameApi.InRun)
            {
                Widgets.Hint(Texts.T("имена подтянутся после старта забега",
                                     "names refresh once a run starts"));
            }
            _search = Widgets.SearchBox(_search);

            int shown = 0;
            foreach (var p in Catalogs.Pickups)
            {
                if (!Widgets.Matches(_search, p.Name)) continue;
                if (shown >= MenuUI.MaxRows) { shown++; continue; }
                shown++;

                Widgets.Row(() =>
                {
                    if (Widgets.Button(Texts.T("Дроп", "Drop"), 62f,
                                       Texts.T("выбросить у цели", "throw at the target")))
                    {
                        SandboxActions.DropPickup(p.Index, _count);
                    }
                    GUILayout.Label(new GUIContent(p.Name, p.Name), Theme.Name);
                });
            }
            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Pickups.Count);
        }
    }
}
