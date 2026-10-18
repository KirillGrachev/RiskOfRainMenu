using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Вкладка «Клоны»: численность, флаги группы (копирование предметов и
    /// loadout'а, следование), мгновенный спавн и зеркальное удаление.
    ///
    /// Все кнопки идут через <see cref="SandboxActions"/>, поэтому вкладка
    /// одинаково работает у хоста и у клиента (запросом на сервер).
    /// </summary>
    internal static class CloneTab
    {
        private static float _slider = -1f;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw()
        {
            var target = PlayerTargets.SelectedUser;

            Widgets.Header(Texts.T("КЛОНЫ", "CLONES"), Theme.HeaderBlue,
                           Texts.T("копия персонажа на его AI-master'е: модель, скиллы, предметы владельца",
                                   "a copy on the character's AI master: model, skills, owner's items"));

            Widgets.Hint(Texts.T("владелец: ", "owner: ") + PlayerTargets.DisplayName(target));

            int desired = CloneManager.DesiredFor(target);
            if (_slider < 0f) _slider = desired;

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(Texts.T("Авто-число", "Auto count"),
                            Texts.T("сколько клонов игра держит сама: погибшие возвращаются, пока включено «держать число»; кнопки +1/+5 добавляют к этому числу",
                                    "how many clones the game keeps: dead ones respawn while 'keep the count up' is on; +1/+5 add to this number")),
                            Theme.Name, GUILayout.Width(Widgets.LabelColumn), GUILayout.Height(Widgets.RowHeight));
            float next = GUILayout.HorizontalSlider(_slider, 0f, CloneManager.MaxClones,
                                                    GUILayout.Height(Widgets.RowHeight));
            GUILayout.Label(Mathf.RoundToInt(next).ToString(), Theme.Name,
                            GUILayout.Width(26f), GUILayout.Height(Widgets.RowHeight));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(next - _slider) > 0.01f)
            {
                _slider = next;
                SandboxActions.CloneSetDesired(Mathf.RoundToInt(next));
            }

            bool keep = CloneManager.KeepAlive;
            bool nk = Widgets.Toggle(keep, Texts.T("держать число (респавн)", "keep the count up"),
                                     Texts.T("погибшие клоны возвращаются", "dead clones respawn"));
            if (nk != keep) SandboxActions.CloneSetKeepAlive(nk);

            bool follow = CloneManager.FollowFor(target);
            bool inv = CloneManager.CopyInventoryFor(target);
            bool load = CloneManager.CopyLoadoutFor(target);

            bool nf = Widgets.Toggle(follow, Texts.T("следовать за игроком", "follow the player"),
                                     Texts.T("выкл - клоны стоят охраной на месте",
                                             "off - clones hold position"));
            bool ni = Widgets.Toggle(inv, Texts.T("копировать предметы", "copy items"),
                                     Texts.T("клон дерётся инвентарём владельца, а не хоста",
                                             "the clone fights with the owner's items, not the host's"));
            bool nl = Widgets.Toggle(load, Texts.T("копировать loadout", "copy loadout"),
                                     Texts.T("скины и варианты скиллов владельца",
                                             "owner's skins and skill variants"));
            if (nf != follow || ni != inv || nl != load)
            {
                SandboxActions.CloneSetFlags(ni, nl, nf);
            }

            GUILayout.Space(4f);
            Widgets.Row(() =>
            {
                if (Widgets.Button("+1", 44f, Texts.T("клон сразу", "spawn one now")))
                {
                    SandboxActions.CloneSpawnNow(1); Sync(target);
                }
                if (Widgets.Button("+5", 44f, Texts.T("пять клонов сразу", "spawn five now")))
                {
                    SandboxActions.CloneSpawnNow(5); Sync(target);
                }
                if (Widgets.Button(Texts.T("Убрать моих", "Remove mine"), 130f,
                                   Texts.T("удалить клонов цели: тело и master",
                                           "remove the target's clones: body and master")))
                {
                    SandboxActions.CloneClearTarget(); _slider = 0f;
                }
                if (Widgets.Button(Texts.T("Убрать всех", "Remove all"), 120f,
                                   Texts.T("удалить клонов всех игроков",
                                           "remove every player's clones")))
                {
                    SandboxActions.CloneClearAll(); _slider = 0f;
                }
            });

            GUILayout.Space(2f);
            Widgets.Hint(Texts.T(
                $"в строю {CloneManager.AliveFor(target)} / нужно {CloneManager.DesiredFor(target)}   всего {CloneManager.AliveTotal}",
                $"alive {CloneManager.AliveFor(target)} / desired {CloneManager.DesiredFor(target)}   total {CloneManager.AliveTotal}"));

            if (CloneManager.LastError.Length > 0) Widgets.Hint(CloneManager.LastError);
        }

        /// <summary>Синхронизировать слайдер с фактическим desired после ручного спавна.</summary>
        private static void Sync(RoR2.NetworkUser target)
        {
            _slider = CloneManager.DesiredFor(target);
        }
    }
}
