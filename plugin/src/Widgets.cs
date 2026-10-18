using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// IMGUI-примитивы меню: строки, кнопки, тумблеры, слайдеры, поиск, футер.
    ///
    /// Все элементы фиксированной высоты (RowHeight) - окно не меняет геометрию
    /// от кадра к кадру. Подписи действий короткие, подробности - в тултипах
    /// (GUIContent.tooltip): наведение курсора показывает справку, интерфейс
    /// остаётся чистым.
    /// </summary>
    internal static class Widgets
    {
        public const float RowHeight = 20f;
        public const float HeaderHeight = 22f;
        public const float LabelColumn = 62f;

        /// <summary>Имя фокусируемого контрола поиска: пока он в фокусе,
        /// клавиша меню не сворачивает окно (Plugin.Update проверяет фокус).</summary>
        public const string SearchControlName = "sbx_search";

        /// Цветная шина-заголовок раздела (как в штатных панелях игры).
        public static void Header(string text, GUIStyle style, string tooltip = null)
        {
            GUILayout.Label(new GUIContent(text, tooltip), style, GUILayout.Height(HeaderHeight));
        }

        public static bool Button(string label, float width, string tooltip = null)
        {
            return GUILayout.Button(new GUIContent(label, tooltip), Theme.Button,
                                    GUILayout.Width(width), GUILayout.Height(RowHeight));
        }

        public static bool Toggle(bool value, string label, string tooltip = null)
        {
            return GUILayout.Toggle(value, new GUIContent(label, tooltip), GUILayout.Height(RowHeight));
        }

        /// Строка «подпись + слайдер + значение» фиксированной высоты.
        public static int Slider(string label, int value, int min, int max, float sliderWidth = 200f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Theme.Name, GUILayout.Width(LabelColumn), GUILayout.Height(RowHeight));
            int v = SliderInline(value, min, max, sliderWidth);
            GUILayout.EndHorizontal();
            return v;
        }

        /// Слайдер со значением без подписи - для строк, где подпись уже есть.
        public static int SliderInline(int value, int min, int max, float sliderWidth = 200f)
        {
            float v = GUILayout.HorizontalSlider(value, min, max,
                                                 GUILayout.Width(sliderWidth), GUILayout.Height(RowHeight));
            GUILayout.Label(Mathf.RoundToInt(v).ToString(), Theme.Name,
                            GUILayout.Width(30f), GUILayout.Height(RowHeight));
            return Mathf.Clamp(Mathf.RoundToInt(v), min, max);
        }

        public static void Row(System.Action body)
        {
            GUILayout.BeginHorizontal();
            body();
            GUILayout.EndHorizontal();
        }

        /// Поле поиска с кнопкой сброса; значение хранит вызывающая вкладка.
        public static string SearchBox(string current)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Texts.T("Поиск", "Search"), Theme.Name,
                            GUILayout.Width(LabelColumn), GUILayout.Height(RowHeight));
            GUI.SetNextControlName(SearchControlName);
            string next = GUILayout.TextField(current ?? "", Theme.Field, GUILayout.Height(RowHeight));
            if (Button("x", 24f, Texts.T("сбросить поиск", "clear search"))) next = "";
            GUILayout.EndHorizontal();
            GUILayout.Space(2f);
            return next;
        }

        public static void Footer(int shown, int total)
        {
            GUILayout.Space(2f);
            GUILayout.Label(Texts.T(
                $"показано {shown} из {total}" + (shown >= MenuUI.MaxRows ? " (лимит, используй поиск)" : ""),
                $"shown {shown} of {total}" + (shown >= MenuUI.MaxRows ? " (capped, use search)" : "")),
                Theme.Small, GUILayout.Height(14f));
        }

        /// Мелкая строка-пояснение (одна, не простыня).
        public static void Hint(string text)
        {
            GUILayout.Label(text, Theme.Small, GUILayout.Height(14f));
        }

        public static bool Matches(string search, string value)
        {
            if (string.IsNullOrEmpty(search)) return true;
            return value != null && value.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}

