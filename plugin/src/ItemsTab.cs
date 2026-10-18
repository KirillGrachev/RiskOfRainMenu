using RoR2;
using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Вкладка «Предметы»: выдать / снять / сбросить стопку и полная очистка
    /// инвентаря цели. У каждой кнопки выдачи есть зеркальная кнопка удаления.
    /// </summary>
    internal static class ItemsTab
    {
        private static string _search = "";
        private static int _count = 1;
        private static bool _hideHidden = true;
        private static float _clearArmUntil;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw()
        {
            Widgets.Header(Texts.T("ПРЕДМЕТЫ", "ITEMS"), Theme.HeaderPurple,
                           Texts.T("ItemCatalog: выдать, снять или сбросить стопку цели",
                                   "ItemCatalog: give, remove or drop stacks to the target"));

            GUILayout.BeginHorizontal();
                _hideHidden = GUILayout.Toggle(_hideHidden,
                    Texts.T("скрыть служебные", "hide hidden"), GUILayout.Height(Widgets.RowHeight));
                GUILayout.FlexibleSpace();
                GUILayout.Label(Texts.T("стопка", "stack"), Theme.Name,
                                GUILayout.Width(44f), GUILayout.Height(Widgets.RowHeight));
                _count = Widgets.SliderInline(_count, 1, 50, 130f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
                bool armed = Time.unscaledTime < _clearArmUntil;
                string label = armed
                    ? Texts.T("ТОЧНО очистить?", "SURE to clear?")
                    : Texts.T("Очистить инвентарь", "Clear inventory");
                if (Widgets.Button(label, 170f,
                                   Texts.T("снять все предметы цели", "remove every item of the target")))
                {
                    if (!armed) _clearArmUntil = Time.unscaledTime + 4f;
                    else { _clearArmUntil = 0f; SandboxActions.ClearItems(); }
                }
            GUILayout.EndHorizontal();

            if (!GameApi.InRun)
            {
                Widgets.Hint(Texts.T("имена подтянутся после старта забега",
                                     "names refresh once a run starts"));
            }
            _search = Widgets.SearchBox(_search);

            int shown = 0;
            foreach (var it in Catalogs.Items)
            {
                if (_hideHidden && it.Hidden) continue;
                if (!Widgets.Matches(_search, it.Name) && !Widgets.Matches(_search, it.Token)) continue;
                if (shown >= MenuUI.MaxRows) { shown++; continue; }
                shown++;

                GUILayout.BeginHorizontal();
                    if (Widgets.Button(Texts.T("Выдать", "Give"), 62f,
                                       Texts.T("добавить стопку цели", "add stacks to the target")))
                    {
                        SandboxActions.GiveItem(it.Index, _count);
                    }
                    if (Widgets.Button(Texts.T("Снять", "Take"), 56f,
                                       Texts.T("снять стопку с цели", "take stacks from the target")))
                    {
                        SandboxActions.RemoveItem(it.Index, _count);
                    }
                    if (Widgets.Button(Texts.T("Дроп", "Drop"), 56f,
                                       Texts.T("выбросить пикапом в мир", "throw into the world as a pickup")))
                    {
                        SandboxActions.DropPickup(it.Pickup, _count);
                    }
                    GUILayout.Label(new GUIContent(it.Name, it.Name + "  [" + it.Index + "]"), Theme.Name);
                GUILayout.EndHorizontal();
            }
            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Items.Count);
        }
    }
}

