using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Вся визуальная тема меню в одном месте: цвета, шины-заголовки, строки.
    ///
    /// IMGUI-стили создаются один раз на главном потоке при первой отрисовке
    /// (Texture2D 1x1 как фон шин). Держим их статикой: OnGUI вызывается каждый
    /// кадр, аллокации стилей/текстур в кадре - прямой путь к GC-пикам.
    /// </summary>
    internal static class Theme
    {
        public static GUIStyle Window;
        public static GUIStyle Title;
        public static GUIStyle HeaderBlue;
        public static GUIStyle HeaderRed;
        public static GUIStyle HeaderPurple;
        public static GUIStyle Name;
        public static GUIStyle Small;
        public static GUIStyle SmallRich;
        public static GUIStyle Button;
        public static GUIStyle TabButton;
        public static GUIStyle Field;

        public static bool Ready { get; private set; }

        public static void Ensure()
        {
            if (Ready) return;
            Ready = true;

            Window = new GUIStyle(GUI.skin.window);
            Window.normal.background = Tex(0.09f, 0.10f, 0.13f, 1f);
            Window.padding = new RectOffset(8, 8, 6, 8);

            Title = new GUIStyle(GUI.skin.label);
            Title.fontStyle = FontStyle.Bold;
            Title.richText = true;
            Title.alignment = TextAnchor.MiddleLeft;

            HeaderBlue = Header(0.13f, 0.42f, 0.62f);
            HeaderRed = Header(0.62f, 0.24f, 0.27f);
            HeaderPurple = Header(0.40f, 0.29f, 0.60f);

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

            Button = new GUIStyle(GUI.skin.button);
            Button.fontSize = 12;

            TabButton = new GUIStyle(Button);
            TabButton.wordWrap = false;
            TabButton.clipping = TextClipping.Clip;
            TabButton.alignment = TextAnchor.MiddleCenter;

            Field = new GUIStyle(GUI.skin.textField);
            Field.fontSize = 12;
        }

        private static GUIStyle Header(float r, float g, float b)
        {
            var s = new GUIStyle(GUI.skin.box);
            s.normal.background = Tex(r, g, b, 1f);
            s.normal.textColor = Color.white;
            s.fontStyle = FontStyle.Bold;
            s.fontSize = 12;
            s.alignment = TextAnchor.MiddleLeft;
            s.padding = new RectOffset(6, 4, 2, 2);
            s.margin = new RectOffset(0, 0, 6, 4);
            return s;
        }

        private static Texture2D Tex(float r, float g, float b, float a)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, new Color(r, g, b, a));
            t.Apply();
            return t;
        }
    }
}
