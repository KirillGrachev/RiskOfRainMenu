using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Оболочка окна меню: заголовок со сворачиванием, вкладки, строка состояния
    /// и выбор цели. Содержимое вкладок живут в отдельных классах
    /// (UnlocksTab, CloneTab, CharactersTab, ItemsTab, DropsTab, PropsTab,
    /// InfoTab), каждое действие которых идёт через <see cref="SandboxActions"/>.
    ///
    /// Стабильность раскладки: ширина окна фиксирована, скролл-зона имеет
    /// фиксированную высоту, строки списков - фиксированную высоту со обрезкой
    /// текста, поэтому окно не «плывёт» между вкладками и кадрами и не уходит
    /// за экран. Свёрнутое окно - одна строка-заголовок: можно играть, не
    /// закрывая меню.
    ///
    /// Почему IMGUI, а не игровой UI: у RoR2 свои префабы панелей, они лежат в
    /// ассетах и подгружаются через Addressables; встраивание в HUD потребовало
    /// бы ассет-бандл и R2API. IMGUI рисуется силами Unity без единого префаба.
    ///
    /// Клавиша открытия - Delete (настраивается в BepInEx/config/... .cfg).
    /// </summary>
    internal static class MenuUI
    {
        private const int WindowId = 0x5A11B0;
        private const float BaseWidth = 520f;

        /// <summary>Потолок строк в списках: защита от просадок FPS на каталогах.</summary>
        public const int MaxRows = 250;

        private static Rect _rect = new Rect(90f, 60f, BaseWidth, 610f);
        private static bool _visible;
        private static bool _collapsed;
        private static int _tab;
        private static Vector2 _scroll;
        private static bool _stateLoaded;
        private static string _statusCache = "";
        private static float _statusAt;

        // состояние указателя до открытия меню, чтобы при закрытии вернуть ровно
        // то, что было (в забеге игра держит курсор скрытым и залоченным,
        // в главных меню - видимым): безусловный restore сломал бы главное меню
        private static bool _cursorWasVisible;
        private static CursorLockMode _cursorWasLock;

        private static readonly string[] TabsRu =
        {
            "Разблокировки", "Клоны", "Персонажи", "Предметы", "Дроп", "Объекты", "Инфо", "Читы", "Забег"
        };

        private static readonly string[] TabsEn =
        {
            "Unlocks", "Clones", "Chars", "Items", "Drops", "Props", "Info", "Cheats", "Run"
        };

        /// <summary>Открыто ли меню сейчас.</summary>
        public static bool IsVisible { get { return _visible; } }

        /// <summary>Открыть/закрыть меню; при открытии освежает каталоги и язык.</summary>
        public static void Toggle()
        {
            if (_visible) PersistState();
            _visible = !_visible;
            if (!_visible)
            {
                // возвращаем указатель игре: ровно то состояние, что было до открытия
                Cursor.visible = _cursorWasVisible;
                Cursor.lockState = _cursorWasLock;
                return;
            }
            // меню открыто: без видимого разблокированного курсора кликать нечем -
            // игра в забеге держит указатель скрытым/залоченным под прицел
            _cursorWasVisible = Cursor.visible;
            _cursorWasLock = Cursor.lockState;
            ApplyCursor();
            LoadState();

            var cfg = SandboxMenuPlugin.MenuLanguage;
            Texts.Russian = cfg == null ||
                            !string.Equals(cfg.Value, "en", System.StringComparison.OrdinalIgnoreCase);
            Catalogs.EnsureBuilt();
            UnlockPatches.RefreshCounts();
            PlayerTargets.Refresh();
            if (!NetProtocol.IsServer) NetProtocol.PingHost();
        }

        /// <summary>Отрисовка окна; вызывается из OnGUI плагина.</summary>
        public static void Draw()
        {
            if (!_visible) return;
            Theme.Ensure();
            ApplyCursor();
            LoadState();

            // игра красит GUI.color под свой HUD - без сброса наше окно
            // рисуется полупрозрачным даже с непрозрачной текстурой фона
            var color = GUI.color;
            var bgColor = GUI.backgroundColor;
            var contentColor = GUI.contentColor;
            var matrix = GUI.matrix;
            GUI.color = Color.white;
            GUI.backgroundColor = Color.white;
            GUI.contentColor = Color.white;

            // HiDPI/4K: всё меню рисуется в виртуальных координатах под масштабом
            float scale = Mathf.Clamp(SandboxMenuPlugin.MenuScale != null ? SandboxMenuPlugin.MenuScale.Value : 1f, 1f, 2f);
            if (scale > 1.01f)
            {
                GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            }
            float vw = Screen.width / scale;
            float vh = Screen.height / scale;

            // окно не шире экрана и не расширяется под минимальную ширину вкладок
            float width = Mathf.Min(BaseWidth, Mathf.Max(320f, vw - 24f));
            _rect.width = width;
            _rect.x = Mathf.Clamp(_rect.x, 8f, Mathf.Max(8f, vw - width - 8f));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, vh - 28f));

            _rect = GUILayout.Window(WindowId, _rect, DrawWindow, "", Theme.Window);

            GUI.color = color;
            GUI.backgroundColor = bgColor;
            GUI.contentColor = contentColor;
            GUI.matrix = matrix;
        }

        /// <summary>Держит курсор видимым и разблокированным, пока меню открыто.
        /// Вызывается каждый кадр: игра в забеге переставляет указатель под прицел
        /// (скрытый/залоченный, а местами программный), из-за чего над IMGUI-окном
        /// его не видно или он «уходит под окно». Аппаратный курсор ОС
        /// (SetCursor(null, Vector2.zero, CursorMode.Auto)) рисуется поверх любого IMGUI.</summary>
        private static void ApplyCursor()
        {
            if (!Cursor.visible || Cursor.lockState != CursorLockMode.None)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>Восстановить позицию/вкладку/свёрнутость из конфига (один раз).</summary>
        private static void LoadState()
        {
            if (_stateLoaded) return;
            _stateLoaded = true;
            _tab = Mathf.Clamp(SandboxMenuPlugin.MenuTab != null ? SandboxMenuPlugin.MenuTab.Value : 0, 0, 8);
            _collapsed = SandboxMenuPlugin.MenuCollapsed != null && SandboxMenuPlugin.MenuCollapsed.Value;
            _rect.x = SandboxMenuPlugin.MenuX != null ? SandboxMenuPlugin.MenuX.Value : 90;
            _rect.y = SandboxMenuPlugin.MenuY != null ? SandboxMenuPlugin.MenuY.Value : 60;
        }

        /// <summary>Сохранить состояние окна в конфиг: переживает перезапуск игры.</summary>
        public static void PersistState()
        {
            if (SandboxMenuPlugin.MenuTab != null) SandboxMenuPlugin.MenuTab.Value = _tab;
            if (SandboxMenuPlugin.MenuCollapsed != null) SandboxMenuPlugin.MenuCollapsed.Value = _collapsed;
            if (SandboxMenuPlugin.MenuX != null) SandboxMenuPlugin.MenuX.Value = Mathf.RoundToInt(_rect.x);
            if (SandboxMenuPlugin.MenuY != null) SandboxMenuPlugin.MenuY.Value = Mathf.RoundToInt(_rect.y);
            try { SandboxMenuPlugin.Self?.Config.Save(); } catch { /* best effort */ }
        }

        /// <summary>Содержимое окна: заголовок, вкладки, состояние, активная вкладка.</summary>
        private static void DrawWindow(int id)
        {
            DrawTitleBar();
            if (_collapsed)
            {
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
                return;
            }

            GUILayout.Space(2f);
            // SelectionGrid в два ряда: подписи вкладок не обрезаются в ноль,
            // как в одном ряду на 9 кнопок; полное имя - в тултипе
            var tabs = Texts.Russian ? TabsRu : TabsEn;
            int perRow = (tabs.Length + 1) / 2;
            int rows = 2;
            int tab = GUILayout.SelectionGrid(_tab, tabs, perRow, Theme.TabButton,
                                              GUILayout.Width(_rect.width - 20f), GUILayout.Height(24f * rows));
            if (tab != _tab) _tab = tab;
            GUILayout.Space(4f);
            DrawStatusBar();
            DrawTargetSelector();

            float scrollH = Mathf.Max(180f, Mathf.Min(500f, Screen.height - 300f));
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(scrollH));
            switch (_tab)
            {
                case 0: UnlocksTab.Draw(); break;
                case 1: CloneTab.Draw(); break;
                case 2: CharactersTab.Draw(); break;
                case 3: ItemsTab.Draw(); break;
                case 4: DropsTab.Draw(); break;
                case 5: PropsTab.Draw(); break;
                case 7: CheatsTab.Draw(); break;
                case 8: RunTab.Draw(); break;
                default: InfoTab.Draw(); break;
            }
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        /// <summary>Заголовок окна: имя плагина, свернуть/развернуть, закрыть.</summary>
        private static void DrawTitleBar()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=#6EC1FF><b>" + Texts.T("МЕНЮ", "MENU") + "</b></color>",
                            Theme.Title, GUILayout.Height(20f));
            GUILayout.FlexibleSpace();
            if (Widgets.Button(_collapsed ? "+" : "-", 26f,
                               Texts.T("свернуть/развернуть", "collapse/expand")))
            {
                _collapsed = !_collapsed;
            }
            if (Widgets.Button("X", 26f, Texts.T("закрыть меню", "close menu")))
            {
                Toggle();
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>Одна строка состояния: забег, роль (сервер/клиент), цель.
        /// Строка интерполируется не чаще раза в 0.5 с - не аллоцируем в кадре.</summary>
        private static void DrawStatusBar()
        {
            if (Time.unscaledTime - _statusAt > 0.5f)
            {
                _statusAt = Time.unscaledTime;
                _statusCache = BuildStatus();
            }
            GUILayout.Label(_statusCache, Theme.SmallRich, GUILayout.Height(16f));

            if (!NetProtocol.IsServer && !NetProtocol.HostHasMod)
            {
                Widgets.Hint(Texts.T(
                    "действия мира недоступны: мод нужен и хосту",
                    "world actions unavailable: the host needs the mod too"));
            }
        }

        private static string BuildStatus()
        {
            string run = GameApi.InRun
                ? "<color=#7EE787>" + Texts.T("забег", "run") + "</color>"
                : "<color=#FF7B72>" + Texts.T("нет забега", "no run") + "</color>";

            string role;
            if (NetProtocol.IsServer)
            {
                role = "<color=#7EE787>" + Texts.T("сервер", "server") + "</color>";
            }
            else if (NetProtocol.HostHasMod)
            {
                role = "<color=#FFD866>" + Texts.T("клиент, хост с модом", "client, host has mod") + "</color>";
            }
            else
            {
                role = "<color=#FF7B72>" + Texts.T("клиент, хост без мода", "client, host without mod") + "</color>";
            }

            var body = PlayerTargets.Body;
            string who = body != null ? body.name : "-";
            return Texts.T(
                $"[ {run} ]  [ {role} ]  <color=#9FB2CC>цель:</color> <color=#E8EEF7>{who}</color>",
                $"[ {run} ]  [ {role} ]  <color=#9FB2CC>target:</color> <color=#E8EEF7>{who}</color>");
        }

        /// <summary>Выбор игрока-цели в кооперативе; в соло цель одна и строка тихая.</summary>
        private static void DrawTargetSelector()
        {
            var names = PlayerTargets.DisplayNames();
            if (names.Length <= 1) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(Texts.T("Цель", "Target"), Theme.Name,
                            GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
            int sel = GUILayout.SelectionGrid(PlayerTargets.Selected, names,
                                              Mathf.Min(names.Length, 4), Theme.Button,
                                              GUILayout.Height(Widgets.RowHeight));
            if (sel != PlayerTargets.Selected) PlayerTargets.Selected = sel;
            GUILayout.EndHorizontal();
        }
    }
}


