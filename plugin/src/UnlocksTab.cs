using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Вкладка «Разблокировки»: живые переключатели Harmony-postfix'ов и блок
    /// профиля с обратимой выдачей (автобэкап + откат).
    ///
    /// Действия профиля исполняются всегда локально: профиль хранится на машине
    /// игрока, сервер для него не нужен, поэтому кнопки работают и у клиента.
    /// </summary>
    internal static class UnlocksTab
    {
        private static float _grantArmUntil;

        /// <summary>Отрисовка вкладки; вызывается оболочкой окна.</summary>
        public static void Draw()
        {
            Widgets.Header(Texts.T("РАЗБЛОКИРОВКИ", "UNLOCKS"), Theme.HeaderBlue,
                           Texts.T("галочки не трогают файл сейва", "toggles do not touch the save file"));

            UnlockPatches.AllUnlockables = Widgets.Toggle(UnlockPatches.AllUnlockables,
                Texts.T("все разблокировки и скины", "all unlockables and skins"),
                Texts.T("UserProfile.HasUnlockable возвращает true, пока включено",
                        "UserProfile.HasUnlockable returns true while on"));

            UnlockPatches.AllSurvivors = Widgets.Toggle(UnlockPatches.AllSurvivors,
                Texts.T("все выжившие", "all survivors"),
                Texts.T("UserProfile.HasSurvivorUnlocked возвращает true",
                        "UserProfile.HasSurvivorUnlocked returns true"));

            UnlockPatches.AllViewables = Widgets.Toggle(UnlockPatches.AllViewables,
                Texts.T("всё просмотрено", "all viewables seen"),
                Texts.T("убирает метки «new» в меню игры", "removes 'new' markers in game menus"));

            Widgets.Hint(Texts.T(
                $"unlockable: {UnlockPatches.UnlockableCount}   скинов: {UnlockPatches.SkinCount}",
                $"unlockables: {UnlockPatches.UnlockableCount}   skins: {UnlockPatches.SkinCount}"));

            Widgets.Header(Texts.T("ПРОФИЛЬ", "PROFILE"), Theme.HeaderRed,
                           Texts.T("запись в файл сейва; обратима через откат",
                                   "writes the save file; reversible via restore"));

            bool armed = Time.unscaledTime < _grantArmUntil;
            string grantLabel = armed
                ? Texts.T("ТОЧНО? запись в сейв!", "SURE? writes to the save!")
                : Texts.T("Открыть всё (в файл профиля)", "Grant all (write to profile)");
            if (Widgets.Button(grantLabel, 240f,
                               Texts.T("перед выдачей профиль автоматически снимается в бэкап",
                                       "a backup is taken automatically before granting")))
            {
                if (!armed) _grantArmUntil = Time.unscaledTime + 4f;
                else { _grantArmUntil = 0f; UnlockPatches.GrantAllToProfile(); }
            }

            Widgets.Row(() =>
            {
                if (Widgets.Button(Texts.T("Откат к бэкапу", "Restore backup"), 160f,
                                   Texts.T("вернуть профиль из последнего бэкапа, без перезапуска",
                                           "restore the profile from the latest backup, live")))
                {
                    UnlockPatches.RestoreLatest();
                }
                GUILayout.Label(Texts.T($"бэкапов: {UnlockPatches.Backups().Count}",
                                        $"backups: {UnlockPatches.Backups().Count}"),
                                Theme.Small, GUILayout.Height(Widgets.RowHeight));
            });
        }
    }
}
