using RoR2;
using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вкладка «Читы»: бессмертие, ваншот, множители, перезарядки, лечение,
    /// зачистка врагов, экономика (деньги, лунные монеты, уровень, опыт)
    /// и бафы (быстрые кнопки + каталог с поиском).
    ///
    /// Все действия идут через <see cref="CheatActions"/>, поэтому вкладка
    /// одинаково работает у хоста и у клиента (запросом на сервер, локальное
    /// состояние - оптимистичное зеркало). Значения строк экономики читаются
    /// из синхронизированных полей игры и кэшируются раз в секунду.
    /// </summary>
    internal static class CheatsTab {

        private static string _search = "";
        private static string _moneyText = "10000";
        private static string _coinsText = "1000";
        private static int _levelSlider = 1;
        private static bool _levelInit;
        private static int _radius = 0;
        private static float _killArmUntil;
        private static string _econCache = "";
        private static float _econAt;

        // индексы быстрых бафов: каталог бафов стабилен в пределах процесса,
        // поэтому резолвим по имени один раз (-2 = ещё не искали, -1 = нет в игре)
        private static int _mightIdx = -2;
        private static int _energizedIdx = -2;
        private static int _warbannerIdx = -2;
        private static int _clovenIdx = -2;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw() {

            var target = PlayerTargets.SelectedUser;
            uint id = PlayerTargets.NetIdOf(target);

            Widgets.Header(Texts.T("ЧИТЫ", "CHEATS"), Theme.HeaderRed,
                    Texts.T("бессмертие, урон, перезарядки, экономика, бафы - всё для выбранной цели",
                            "god mode, damage, cooldowns, economy, buffs - all for the selected target"));

            Widgets.Hint(Texts.T("цель: ", "target: ") + PlayerTargets.DisplayName(target));

            if (id == 0u) {

                Widgets.Hint(Texts.T(
                        "нет сетевой цели: читы игрока доступны в забеге",
                        "no network target: player cheats need an active run"));

            }

            GUILayout.Space(2f);
            DrawSurvivability(id);
            GUILayout.Space(2f);
            DrawMultipliers(id);
            GUILayout.Space(2f);
            DrawSkills(id);
            GUILayout.Space(2f);
            DrawEnemies();
            GUILayout.Space(2f);
            DrawEconomy(target);
            GUILayout.Space(2f);
            DrawBuffs();
            GUILayout.Space(2f);
            DrawFlight();

        }

        // --------------------------------------------------- survivability --
        private static void DrawSurvivability(uint id) {

            Widgets.Header(Texts.T("ЖИВУЧЕСТЬ", "SURVIVABILITY"), Theme.HeaderRed);

            bool god = CheatState.HasFlag(id, CheatFlag.GodMode);
            bool nextGod = Widgets.Toggle(god,
                    Texts.T("бессмертие цели", "target god mode"),
                    Texts.T("весь входящий урон цели отклоняется (включая падения)",
                            "all incoming damage to the target is rejected (falls included)"));

            if (nextGod != god) {
                CheatActions.SetCheatFlag(CheatFlag.GodMode, nextGod);
            }

            bool godAll = CheatState.GodModeAll;
            bool nextGodAll = Widgets.Toggle(godAll,
                    Texts.T("бессмертие всех игроков", "god mode for all players"),
                    Texts.T("глобальный флаг: действует на каждого игрока команды",
                            "global flag: applies to every player of the team"));

            if (nextGodAll != godAll) {
                CheatActions.SetCheatFlag(CheatFlag.GodModeAll, nextGodAll);
            }

            bool oneHit = CheatState.HasFlag(id, CheatFlag.OneHitKill);
            bool nextOneHit = Widgets.Toggle(oneHit,
                    Texts.T("ваншот", "one-hit kill"),
                    Texts.T("любой урон цели-атакующего убивает с одного попадания",
                            "any hit from the target kills instantly"));

            if (nextOneHit != oneHit) {
                CheatActions.SetCheatFlag(CheatFlag.OneHitKill, nextOneHit);
            }

            GUILayout.BeginHorizontal();

                if (Widgets.Button(Texts.T("Полное лечение", "Full heal"), 150f,
                        Texts.T("здоровье, щит и барьер цели - до максимума",
                                "health, shield and barrier of the target - to the maximum"))) {
                    CheatActions.HealTarget();
                }

                if (Widgets.Button(Texts.T("Снять все бафы", "Clear all buffs"), 160f,
                        Texts.T("убрать с цели все бафы и дебафы",
                                "remove every buff and debuff from the target"))) {
                    CheatActions.ClearBuffs();
                }

            GUILayout.EndHorizontal();

        }

        // ----------------------------------------------------- multipliers --
        private static void DrawMultipliers(uint id) {

            Widgets.Header(Texts.T("УРОН И СКОРОСТЬ", "DAMAGE AND SPEED"), Theme.HeaderRed,
                    Texts.T("множители в процентах; 100 = без изменений",
                            "multipliers in percent; 100 = unchanged"));

            DrawPctSlider(Texts.T("Урон, %", "Damage, %"), CheatValue.DamagePct, id);
            DrawPctSlider(Texts.T("Скор. атаки, %", "Attack speed, %"), CheatValue.AttackSpeedPct, id);
            DrawPctSlider(Texts.T("Скор. бега, %", "Move speed, %"), CheatValue.MoveSpeedPct, id);

        }

        /// <summary>
        /// Строка множителя: подпись, слайдер 10..1000, отправка при изменении.
        /// Локальный кэш слайдера инициализируется текущим значением один раз
        /// и дальше живёт своей жизнью - как слайдер клонов.
        /// </summary>
        private static void DrawPctSlider(string label, CheatValue value, uint id) {

            int current = CheatState.ValueOf(id, value);

            GUILayout.BeginHorizontal();

                GUILayout.Label(label, Theme.Name,
                        GUILayout.Width(110f), GUILayout.Height(Widgets.RowHeight));
                int next = Widgets.SliderInline(current, CheatState.MinPct, CheatState.MaxPct, 200f);

            GUILayout.EndHorizontal();

            if (next != current) {
                CheatActions.SetCheatValue(value, next);
            }

        }

        // ---------------------------------------------------------- skills --
        private static void DrawSkills(uint id) {

            Widgets.Header(Texts.T("УМЕНИЯ", "SKILLS"), Theme.HeaderRed);

            bool noCd = CheatState.HasFlag(id, CheatFlag.NoCooldown);
            bool nextNoCd = Widgets.Toggle(noCd,
                    Texts.T("без перезарядки (умения и оборудование)", "no cooldowns (skills and equipment)"),
                    Texts.T("заряды всегда полны, оборудование готово сразу",
                            "stocks stay full, equipment recharges instantly"));

            if (nextNoCd != noCd) {
                CheatActions.SetCheatFlag(CheatFlag.NoCooldown, nextNoCd);
            }

            GUILayout.BeginHorizontal();

                if (Widgets.Button(Texts.T("Сбросить перезарядки", "Reset cooldowns"), 190f,
                        Texts.T("разово заполнить заряды умений и сбросить оборудование цели",
                                "one-shot: refill skill stocks and reset the equipment of the target"))) {
                    CheatActions.ResetCooldowns();
                }

            GUILayout.EndHorizontal();

        }

        // --------------------------------------------------------- enemies --
        private static void DrawEnemies() {

            Widgets.Header(Texts.T("ВРАГИ", "ENEMIES"), Theme.HeaderRed);

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Радиус, м", "Radius, m"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
                _radius = Widgets.SliderInline(_radius, 0, 500, 200f);
                GUILayout.Label(_radius == 0
                        ? Texts.T("(вся карта)", "(whole map)")
                        : "", Theme.Small, GUILayout.Height(Widgets.RowHeight));

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

                bool armed = Time.unscaledTime < _killArmUntil;
                string label = armed
                        ? Texts.T("ТОЧНО убить?", "SURE to kill?")
                        : Texts.T("Убить врагов", "Kill enemies");

                if (Widgets.Button(label, 170f,
                        Texts.T("добить всех враждебных существ (нейтралы не трогаются); убийство засчитывается цели",
                                "finish off every hostile creature (neutrals untouched); kills are credited to the target"))) {

                    if (!armed) {

                        _killArmUntil = Time.unscaledTime + 4f;

                    } else {

                        _killArmUntil = 0f;
                        CheatActions.KillEnemies(_radius);

                    }

                }

            GUILayout.EndHorizontal();

        }

        // --------------------------------------------------------- economy --
        private static void DrawEconomy(NetworkUser target) {

            Widgets.Header(Texts.T("ЭКОНОМИКА", "ECONOMY"), Theme.HeaderRed);

            if (Time.unscaledTime - _econAt > 1f) {

                _econAt = Time.unscaledTime;
                _econCache = BuildEconomySummary(target);

            }

            GUILayout.Label(_econCache, Theme.SmallRich, GUILayout.Height(14f));

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Деньги", "Money"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
                _moneyText = Widgets.TextField(_moneyText, 90f);

                if (Widgets.Button(Texts.T("Задать", "Set"), 62f,
                        Texts.T("установить деньги цели", "set the money of the target"))) {
                    CheatActions.SetMoney(Widgets.ParseInt(_moneyText, 0));
                }

                if (Widgets.Button(Texts.T("Добавить", "Give"), 84f,
                        Texts.T("добавить деньги цели", "add money to the target"))) {
                    CheatActions.GiveMoney(Widgets.ParseInt(_moneyText, 0));
                }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Монеты", "Coins"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
                _coinsText = Widgets.TextField(_coinsText, 90f);

                if (Widgets.Button(Texts.T("Задать", "Set"), 62f,
                        Texts.T("установить лунные монеты игрока (валюта профиля)",
                                "set the lunar coins of the player (profile currency)"))) {
                    CheatActions.SetLunarCoins(Widgets.ParseInt(_coinsText, 0));
                }

                if (Widgets.Button(Texts.T("Добавить", "Give"), 84f,
                        Texts.T("добавить лунные монеты игроку", "add lunar coins to the player"))) {
                    CheatActions.GiveLunarCoins(Widgets.ParseInt(_coinsText, 0));
                }

            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Уровень", "Level"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
                _levelSlider = Widgets.SliderInline(_levelSlider, 1, CheatState.MaxLevel, 140f);

                if (Widgets.Button(Texts.T("Задать", "Set"), 62f,
                        Texts.T("установить уровень команды (в RoR2 уровень общий)",
                                "set the team level (levels are team-wide in RoR2)"))) {
                    CheatActions.SetTeamLevel(_levelSlider);
                }

                if (Widgets.Button("+10k XP", 76f,
                        Texts.T("добавить команде 10 000 опыта", "give the team 10,000 experience"))) {
                    CheatActions.GiveExperience(10000);
                }

            GUILayout.EndHorizontal();

            // инициализация слайдера текущим уровнем команды - один раз за сессию вкладки
            if (!_levelInit && TeamManager.instance != null) {

                try {

                    _levelSlider = Mathf.Clamp((int)TeamManager.instance.GetTeamLevel(TeamIndex.Player),
                            1, CheatState.MaxLevel);
                    _levelInit = true;

                } catch (System.Exception e) {
                    GameApi.WarnOnce("CheatsTab.levelInit", e);
                }

            }

        }

        /// <summary>Строка текущих значений цели (кэш раз в секунду).</summary>
        private static string BuildEconomySummary(NetworkUser target) {

            uint money = 0;
            uint coins = 0;
            uint level = 0;

            try {

                var master = PlayerTargets.MasterOf(target);

                if (master != null) {
                    money = master.money;
                }

                if (target != null) {
                    coins = target.lunarCoins;
                }

                if (TeamManager.instance != null) {
                    level = TeamManager.instance.GetTeamLevel(TeamIndex.Player);
                }

            } catch (System.Exception e) {
                GameApi.WarnOnce("BuildEconomySummary", e);
            }

            return Texts.T("деньги", "money") + $": {GameApi.Val(money)}   " +
                    Texts.T("монеты", "coins") + $": {GameApi.Val(coins)}   " +
                    Texts.T("уровень", "level") + $": {GameApi.Val(level)}";

        }

        // ----------------------------------------------------------- buffs --
        private static void DrawBuffs() {

            Widgets.Header(Texts.T("БАФЫ", "BUFFS"), Theme.HeaderRed,
                    Texts.T("быстрые кнопки ищут баф по внутреннему имени и не ломаются от обновления каталога",
                            "quick buttons resolve buffs by internal name and survive catalog updates"));

            GUILayout.BeginHorizontal();

                DrawQuickBuff(ref _mightIdx, "Might", Texts.T("Мощь", "Might"),
                        Texts.T("+урон на короткое время", "+damage for a short time"));
                DrawQuickBuff(ref _energizedIdx, "Energized", Texts.T("Заряд", "Energized"),
                        Texts.T("+скорость атаки на короткое время", "+attack speed for a short time"));
                DrawQuickBuff(ref _warbannerIdx, "Warbanner", Texts.T("Знамя", "Warbanner"),
                        Texts.T("+урон и скорость атаки от знамён войны", "+damage and attack speed from war banners"));
                DrawQuickBuff(ref _clovenIdx, "Cloven", Texts.T("Раскол", "Cloven"),
                        Texts.T("эффект расколотой сферы", "cloven hooves-like effect"));

            GUILayout.EndHorizontal();

            GUILayout.Space(2f);
            _search = Widgets.SearchBox(_search);

            var body = PlayerTargets.Body;
            int shown = 0;

            foreach (var buff in Catalogs.Buffs) {

                if (!Widgets.Matches(_search, buff.Name)) {
                    continue;
                }

                if (shown >= MenuUI.MaxRows) {

                    shown++;
                    continue;

                }

                shown++;

                GUILayout.BeginHorizontal();

                    if (Widgets.Button(Texts.T("Выдать", "Give"), 62f,
                            Texts.T("один стак бафа цели", "one stack of the buff to the target"))) {
                        CheatActions.GiveBuff((int)buff.Index, 1);
                    }

                    if (Widgets.Button("x5", 40f,
                            Texts.T("пять стаков бафа цели", "five stacks of the buff to the target"))) {
                        CheatActions.GiveBuff((int)buff.Index, 5);
                    }

                    string label = buff.Name;

                    if (body != null) {

                        try {

                            int count = body.GetBuffCount(buff.Index);

                            if (count > 0) {
                                label = $"{buff.Name}  <color=#FFD866>x{count}</color>";
                            }

                        } catch (System.Exception e) {
                            GameApi.WarnOnce("GetBuffCount", e);
                        }

                    }

                    GUILayout.Label(new GUIContent(label, buff.Name + "  [" + buff.Index + "]"),
                            Theme.SmallRich, GUILayout.Height(Widgets.RowHeight));

                GUILayout.EndHorizontal();

            }

            Widgets.Footer(System.Math.Min(shown, MenuUI.MaxRows), Catalogs.Buffs.Count);

        }

        /// <summary>Кнопка быстрого бафа: индекс резолвится по имени один раз.</summary>
        private static void DrawQuickBuff(ref int cache, string internalName, string label, string tooltip) {

            if (cache == -2) {
                cache = ResolveBuff(internalName);
            }

            bool enabled = cache >= 1;
            var color = GUI.color;

            if (!enabled) {
                GUI.color = new Color(color.r, color.g, color.b, 0.45f);
            }

            bool clicked = Widgets.Button(label, 74f,
                    enabled ? tooltip : Texts.T("бафа нет в этой сборке игры", "buff missing in this game build"));
            GUI.color = color;

            if (clicked && enabled) {
                CheatActions.GiveBuff(cache, 5);
            }

        }

        /// <summary>Индекс бафа по внутреннему имени; -1 если в игре его нет.</summary>
        private static int ResolveBuff(string internalName) {

            try {

                var index = BuffCatalog.FindBuffIndex(internalName);
                int value = (int)index;
                return value >= 1 ? value : -1;

            } catch (System.Exception e) {

                GameApi.WarnOnce($"FindBuffIndex({internalName})", e);
                return -1;

            }

        }

        // ----------------------------------------------------------- flight --
        private static void DrawFlight() {

            Widgets.Header(Texts.T("ПОЛЁТ (ЛОКАЛЬНО)", "FLIGHT (LOCAL)"), Theme.HeaderRed,
                    Texts.T("работает и на клиенте без мода у хоста: движение своего тела авторитетно у игрока",
                            "works on clients even without the mod on the host: own-body movement is player-authoritative"));

            bool next = Widgets.Toggle(FlyController.Enabled,
                    Texts.T("свободный полёт", "free flight"),
                    Texts.T("WASD - движение по камере, Space/Ctrl - вверх/вниз, Shift - ускорение; проходит сквозь стены",
                            "WASD - camera-relative, Space/Ctrl - up/down, Shift - boost; passes through walls"));

            if (next != FlyController.Enabled) {

                FlyController.Enabled = next;

                if (next) {

                    GameApi.SayOk(Texts.T(
                            "чит: полёт включён (WASD, Space/Ctrl, Shift)",
                            "cheat: flight enabled (WASD, Space/Ctrl, Shift)"));

                }

            }

            GUILayout.BeginHorizontal();

                GUILayout.Label(Texts.T("Скорость", "Speed"), Theme.Name,
                        GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
                float current = SandboxMenuPlugin.FlySpeed != null ? SandboxMenuPlugin.FlySpeed.Value : 20f;
                float nextSpeed = GUILayout.HorizontalSlider(current, 5f, 100f,
                        GUILayout.Width(200f), GUILayout.Height(Widgets.RowHeight));
                GUILayout.Label(Mathf.RoundToInt(nextSpeed).ToString(), Theme.Name,
                        GUILayout.Width(30f), GUILayout.Height(Widgets.RowHeight));

            GUILayout.EndHorizontal();

            if (Mathf.Abs(nextSpeed - current) > 0.5f && SandboxMenuPlugin.FlySpeed != null) {
                SandboxMenuPlugin.FlySpeed.Value = Mathf.RoundToInt(nextSpeed);
            }

        }

    }

}
