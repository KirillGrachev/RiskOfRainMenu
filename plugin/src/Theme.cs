using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вся визуальная тема меню в одном месте: цвета, шины-заголовки, строки.
    ///
    /// IMGUI-стили создаются один раз на главном потоке при первой отрисовке
    /// (Texture2D 1x1 как фон шин). Держим их статикой: OnGUI вызывается каждый
    /// кадр, аллокации стилей/текстур в кадре - прямой путь к GC-пикам.
    /// </summary>
    internal static class Theme {

        public static GUIStyle Window;
        public static GUIStyle Title;
        public static GUIStyle HeaderBlue;
        public static GUIStyle HeaderRed;
        public static GUIStyle HeaderPurple;
        public static GUIStyle Name;
        public static GUIStyle Small;
        public static GUIStyle SmallRich;

        /// <summary>Тот же Small, но с переносом по словам: длинные диагностические
        /// строки (блок ПРЕДУПРЕЖДЕНИЯ) не обрезаются краем окна, а высоту строки
        /// GUILayout считает по факту переносов - скролл учитывает её честно.</summary>
        public static GUIStyle SmallWrap;
        public static GUIStyle Button;
        public static GUIStyle TabButton;
        public static GUIStyle Field;

        /// <summary>Квадрат-маркер ESP-оверлея: белая плашка, цвет задаёт GUI.color.</summary>
        public static GUIStyle EspBox;

        public static bool Ready { get; private set; }

        /// <summary>Создаёт все стили темы один раз; повторные вызовы бесплатны.</summary>
        public static void Ensure() {

            if (Ready) {

                // фоновые текстуры могут погибнуть снаружи (игра грузит/выгружает
                // ресурсы): «фон то есть, то нет». Уничтоженный Texture2D сравнивается
                // с null, поэтому валидация дешёвая - несколько проверок за кадр
                if (Window != null && Window.normal.background != null
                        && HeaderBlue.normal.background != null
                        && HeaderRed.normal.background != null
                        && HeaderPurple.normal.background != null
                        && EspBox.normal.background != null) {
                    return;
                }

                Ready = false;

            }

            Ready = true;

            Window = new GUIStyle(GUI.skin.window);
            Window.normal.background = CreateTexture(0.09f, 0.10f, 0.13f, 1f);
            Window.padding = new RectOffset(8, 8, 6, 8);

            Title = new GUIStyle(GUI.skin.label);
            Title.fontStyle = FontStyle.Bold;
            Title.richText = true;
            Title.alignment = TextAnchor.MiddleLeft;

            HeaderBlue = CreateHeaderStyle(0.13f, 0.42f, 0.62f);
            HeaderRed = CreateHeaderStyle(0.62f, 0.24f, 0.27f);
            HeaderPurple = CreateHeaderStyle(0.40f, 0.29f, 0.60f);

            Name = new GUIStyle(GUI.skin.label);
            Name.wordWrap = false;
            Name.clipping = TextClipping.Clip;
            Name.alignment = TextAnchor.MiddleLeft;

            Small = new GUIStyle(GUI.skin.label);
            Small.wordWrap = false;
            Small.clipping = TextClipping.Clip;
            Small.fontSize = 11;

            SmallRich = new GUIStyle(Small);
            SmallRich.richText = true;

            SmallWrap = new GUIStyle(Small);
            SmallWrap.wordWrap = true;
            SmallWrap.clipping = TextClipping.Clip;

            Button = new GUIStyle(GUI.skin.button);
            Button.fontSize = 12;

            TabButton = new GUIStyle(Button);
            TabButton.wordWrap = false;
            TabButton.clipping = TextClipping.Clip;
            TabButton.alignment = TextAnchor.MiddleCenter;

            Field = new GUIStyle(GUI.skin.textField);
            Field.fontSize = 12;

            EspBox = new GUIStyle();
            EspBox.normal.background = CreateTexture(1f, 1f, 1f, 1f);
            EspBox.padding = new RectOffset(0, 0, 0, 0);
            EspBox.margin = new RectOffset(0, 0, 0, 0);
            EspBox.border = new RectOffset(0, 0, 0, 0);

        }

        /// <summary>Цветная шина-заголовок раздела из цвета в float-каналах.</summary>
        private static GUIStyle CreateHeaderStyle(float red, float green, float blue) {

            var headerStyle = new GUIStyle(GUI.skin.box);
            headerStyle.normal.background = CreateTexture(red, green, blue, 1f);
            headerStyle.normal.textColor = Color.white;
            headerStyle.fontStyle = FontStyle.Bold;
            headerStyle.fontSize = 12;
            headerStyle.alignment = TextAnchor.MiddleLeft;
            headerStyle.padding = new RectOffset(6, 4, 2, 2);
            headerStyle.margin = new RectOffset(0, 0, 6, 4);

            return headerStyle;

        }

        /// <summary>Однопиксельная текстура цвета - фон для стилей.</summary>
        private static Texture2D CreateTexture(float red, float green, float blue, float alpha) {

            var texture = new Texture2D(1, 1);
            // без этого флажка текстуры могут уезжать в выгрузку ресурсов игры -
            // фон окна пропадал «на ровном месте»
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, new Color(red, green, blue, alpha));
            texture.Apply();

            return texture;

        }
    }
}

