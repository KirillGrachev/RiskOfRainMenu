using UnityEngine;

namespace SandboxMenu {

    /// <summary>
    /// Вкладка «Клоны»: численность, флаги группы (копирование предметов и
    /// loadout'а, следование), мгновенный спавн и зеркальное удаление.
    ///
    /// Все кнопки идут через <see cref="SandboxActions"/>, поэтому вкладка
    /// одинаково работает у хоста и у клиента (запросом на сервер).
    /// </summary>
    internal static class CloneTab {

        private static float _slider = -1f;
        private static string _statusCache = "";
        private static float _statusAt;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw() {

            var target = PlayerTargets.SelectedUser;

            Widgets.Header(Texts.T("КЛОНЫ", "CLONES"), Theme.HeaderBlue,
                    Texts.T("копия персонажа на его AI-master'е: модель, скиллы, предметы владельца",
                            "a copy on the character's AI master: model, skills, owner's items"));

            Widgets.Hint(Texts.T("владелец: ", "owner: ") + PlayerTargets.DisplayName(target));

            int desired = CloneManager.DesiredFor(target);

            if (_slider < 0f) {
                _slider = desired;
            }

            GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent(Texts.T("Авто-число", "Auto count"),
                        Texts.T("сколько клонов игра держит сама: погибшие возвращаются, пока включено «держать число»; кнопки +1/+5 добавляют к этому числу",
                                "how many clones the game keeps: dead ones respawn while 'keep the count up' is on; +1/+5 add to this number")),
                        Theme.Name, GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
                float sliderNext = GUILayout.HorizontalSlider(_slider, 0f, CloneManager.MaxClones,
                        GUILayout.Height(Widgets.RowHeight));
                GUILayout.Label(Mathf.RoundToInt(sliderNext).ToString(), Theme.Name,
                        GUILayout.Width(26f), GUILayout.Height(Widgets.RowHeight));
            GUILayout.EndHorizontal();

            if (Mathf.Abs(sliderNext - _slider) > 0.01f) {

                _slider = sliderNext;
                SandboxActions.CloneSetDesired(Mathf.RoundToInt(sliderNext));

            }

            bool keepAlive = CloneManager.KeepAlive;
            bool nextKeep = Widgets.Toggle(keepAlive, Texts.T("держать число (респавн)", "keep the count up"),
                    Texts.T("погибшие клоны возвращаются", "dead clones respawn"));

            if (nextKeep != keepAlive) {
                SandboxActions.CloneSetKeepAlive(nextKeep);
            }

            bool followOwner = CloneManager.FollowFor(target);
            bool copyInventory = CloneManager.CopyInventoryFor(target);
            bool copyLoadout = CloneManager.CopyLoadoutFor(target);

            bool nextFollow = Widgets.Toggle(followOwner, Texts.T("следовать за игроком", "follow the player"),
                    Texts.T("выкл - клоны стоят охраной на месте",
                            "off - clones hold position"));
            bool nextInventory = Widgets.Toggle(copyInventory, Texts.T("копировать предметы", "copy items"),
                    Texts.T("клон дерётся инвентарём владельца, а не хоста",
                            "the clone fights with the owner's items, not the host's"));
            bool nextLoadout = Widgets.Toggle(copyLoadout, Texts.T("копировать loadout", "copy loadout"),
                    Texts.T("скины и варианты скиллов владельца",
                            "owner's skins and skill variants"));

            if (nextFollow != followOwner || nextInventory != copyInventory || nextLoadout != copyLoadout) {
                SandboxActions.CloneSetFlags(nextInventory, nextLoadout, nextFollow);
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
                if (Widgets.Button("+1", 44f, Texts.T("клон сразу", "spawn one now"))) {

                    SandboxActions.CloneSpawnNow(1);
                    Sync(target);

                }

                if (Widgets.Button("+5", 44f, Texts.T("пять клонов сразу", "spawn five now"))) {

                    SandboxActions.CloneSpawnNow(5);
                    Sync(target);

                }

                if (Widgets.Button(Texts.T("Убрать моих", "Remove mine"), 130f,
                        Texts.T("удалить клонов цели: тело и master",
                                "remove the target's clones: body and master"))) {

                    SandboxActions.CloneClearTarget();
                    _slider = 0f;

                }

                if (Widgets.Button(Texts.T("Убрать всех", "Remove all"), 120f,
                        Texts.T("удалить клонов всех игроков",
                                "remove every player's clones"))) {

                    SandboxActions.CloneClearAll();
                    _slider = 0f;

                }
            GUILayout.EndHorizontal();

            GUILayout.Space(2f);

            if (Time.unscaledTime - _statusAt > 0.5f) {

                _statusAt = Time.unscaledTime;
                _statusCache = Texts.T(
                        $"в строю {CloneManager.AliveFor(target)} / нужно {CloneManager.DesiredFor(target)}   всего {CloneManager.AliveTotal}",
                        $"alive {CloneManager.AliveFor(target)} / desired {CloneManager.DesiredFor(target)}   total {CloneManager.AliveTotal}");

            }

            Widgets.Hint(_statusCache);

            if (CloneManager.LastError.Length > 0) {
                Widgets.Hint(CloneManager.LastError);
            }

        }

        /// <summary>Синхронизировать слайдер с фактическим desired после ручного спавна.</summary>
        private static void Sync(RoR2.NetworkUser target) {
            _slider = CloneManager.DesiredFor(target);
        }
    }
}