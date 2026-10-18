using System;
using System.Collections.Generic;
using System.IO;
using IOPath = System.IO.Path;
using System.Xml.Linq;
using BepInEx;
using HarmonyLib;
using RoR2;

namespace SandboxMenu
{
    /// <summary>
    /// Разблокировки через Harmony-postfix, а не правкой байтов.
    ///
    /// Тот же набор гейтов, что и в байтовом патче (sigs.json), но переключаемый
    /// на лету: сняли галочку - postfix перестал подменять результат, и игра
    /// снова спрашивает профиль. Метод находится по имени и сигнатуре, поэтому
    /// обновление игры не требует пересборки.
    ///
    /// Скины в RoR2 закрыты тем же HasUnlockable(UnlockableDef): у SkinDef есть
    /// поле unlockableDef, и меню выбора скина спрашивает именно профиль. Так что
    /// один переключатель открывает и скины, и варианты способностей, и логбук.
    ///
    /// Проверки прав на платные дополнения (CheckUserHasRequiredEntitlement и
    /// resolvers Steam/EGS) здесь НЕ патчатся намеренно - см. docs/02-signature-patching.md.
    /// </summary>
    internal static class UnlockPatches
    {
        public static bool AllUnlockables = true;
        public static bool AllSurvivors = true;
        public static bool AllViewables = true;

        public static int UnlockableCount;
        public static int SkinCount;

        /// Применяется один раз при старте плагина.
        public static void Apply()
        {
            try { UnlockableCount = UnlockableCatalog.unlockableCount; }
            catch (Exception e) { GameApi.WarnOnce("UnlockableCatalog.unlockableCount", e); }
            try { SkinCount = SkinCatalog.skinCount; }
            catch (Exception e) { GameApi.WarnOnce("SkinCatalog.skinCount", e); }
        }

        public static void RefreshCounts()
        {
            try { UnlockableCount = UnlockableCatalog.unlockableCount; } catch { }
            try { SkinCount = SkinCatalog.skinCount; } catch { }
        }

        /// <summary>
        /// Реально выдать все unlockable в профиль (сохраняется в файл сейва).
        /// Без резервной копии это необратимо, поэтому перед выдачей профиль
        /// автоматически снимается в XML-бэкап (см. BackupCurrent), а кнопка
        /// «Откат» в меню возвращает сохранённое состояние через FromXml.
        /// </summary>
        public static int GrantAllToProfile()
        {
            var profile = GameApi.GetProfile();
            if (profile == null)
            {
                GameApi.SayErr(Texts.T("профиль: нет локального профиля - зайди в игру", "profile: no local profile - enter the game first"));
                return -1;
            }

            string backup = BackupCurrent();
            if (backup == null)
            {
                GameApi.SayErr(Texts.T("профиль: бэкап не создан, выдача отменена", "profile: backup failed, grant cancelled"));
                return -1;
            }

            int granted = 0, failed = 0;
            int count;
            try { count = UnlockableCatalog.unlockableCount; }
            catch (Exception e) { GameApi.Warn("UnlockableCatalog.unlockableCount", e); return -1; }

            for (int i = 1; i <= count; i++)
            {
                try
                {
                    var def = UnlockableCatalog.GetUnlockableDef((UnlockableIndex)i);
                    if (def == null) continue;
                    if (profile.HasUnlockable(def)) continue;
                    profile.GrantUnlockable(def);
                    granted++;
                }
                catch (Exception e)
                {
                    failed++;
                    if (failed < 5) GameApi.Warn($"GrantUnlockable[{i}]", e);
                }
            }
            try { profile.RequestEventualSave(); }
            catch (Exception e) { GameApi.WarnOnce("UserProfile.RequestEventualSave", e); }

            GameApi.Info($"granted {granted} unlockables to profile (failed {failed}), backup {backup}");
            if (failed == 0)
            {
                GameApi.SayOk(Texts.T(
                    $"профиль: открыто {granted} unlockable, бэкап {IOPath.GetFileName(backup)}",
                    $"profile: granted {granted} unlockables, backup {IOPath.GetFileName(backup)}"));
            }
            else
            {
                GameApi.SayErr(Texts.T(
                    $"профиль: открыто {granted}, ошибок {failed}, бэкап {IOPath.GetFileName(backup)}",
                    $"profile: granted {granted}, failed {failed}, backup {IOPath.GetFileName(backup)}"));
            }
            return granted;
        }

        // ------------------------------------------------- profile backups --
        /// <summary>
        /// Бэкапы профиля лежат рядом с плагином: BepInEx/plugins/SandboxBackups.
        /// Формат - штатный XML игры (UserProfile.ToXml), поэтому откат не зависит
        /// от версий таблиц и не трогает файлы игры.
        /// </summary>
        public const int MaxBackups = 5;

        public static string BackupDir
        {
            get { return IOPath.Combine(Paths.PluginPath, "SandboxBackups"); }
        }

        /// Снимок текущего профиля. Возвращает путь или null при ошибке.
        public static string BackupCurrent()
        {
            var profile = GameApi.GetProfile();
            if (profile == null) return null;
            try
            {
                var doc = UserProfile.ToXml(profile);
                Directory.CreateDirectory(BackupDir);
                string name = "profile-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".xml";
                string path = IOPath.Combine(BackupDir, name);
                doc.Save(path);
                PruneBackups();
                GameApi.Info($"profile backup written: {path}");
                return path;
            }
            catch (Exception e)
            {
                GameApi.Warn("ProfileBackup", e);
                return null;
            }
        }

        public static List<string> Backups()
        {
            var list = new List<string>();
            try
            {
                if (!Directory.Exists(BackupDir)) return list;
                list.AddRange(Directory.GetFiles(BackupDir, "profile-*.xml"));
                list.Sort(StringComparer.Ordinal);
            }
            catch (Exception e) { GameApi.Warn("ProfileBackups list", e); }
            return list;
        }

        public static string LatestBackup()
        {
            var list = Backups();
            return list.Count == 0 ? null : list[list.Count - 1];
        }

        private static void PruneBackups()
        {
            var list = Backups();
            for (int i = 0; i < list.Count - MaxBackups; i++)
            {
                try { File.Delete(list[i]); }
                catch (Exception e) { GameApi.Warn("PruneBackups", e); }
            }
        }

        /// <summary>
        /// Откат профиля к последнему бэкапу: текущее состояние само снимается в
        /// бэкап (можно вернуть и его), затем профиль подменяется восстановленным
        /// из XML и сохраняется штатным RequestEventualSave. Перезапуск игры не
        /// нужен: LocalUser.userProfile - публичный сеттер.
        /// </summary>
        public static bool RestoreLatest()
        {
            string path = LatestBackup();
            if (path == null)
            {
                GameApi.SayErr(Texts.T("профиль: бэкапов нет - откатывать некуда", "profile: no backups to restore"));
                return false;
            }

            var lu = GameApi.GetLocalUser();
            if (lu == null)
            {
                GameApi.SayErr(Texts.T("профиль: нет локального пользователя", "profile: no local user"));
                return false;
            }

            try
            {
                var doc = XDocument.Load(path);
                var restored = UserProfile.FromXml(doc);
                if (restored == null)
                {
                    GameApi.SayErr(Texts.T("профиль: бэкап пустой", "profile: backup is empty"));
                    return false;
                }

                // страховка: состояние «до отката» тоже снимаем
                BackupCurrent();

                lu.userProfile = restored;
                try { lu.ApplyUserProfileBindingsToRewiredPlayer(); }
                catch (Exception e) { GameApi.WarnOnce("ApplyUserProfileBindings", e); }
                try { restored.RequestEventualSave(); }
                catch (Exception e) { GameApi.WarnOnce("RequestEventualSave", e); }

                GameApi.SayOk(Texts.T(
                    $"профиль: откат к {IOPath.GetFileName(path)}",
                    $"profile: restored {IOPath.GetFileName(path)}"));
                return true;
            }
            catch (Exception e)
            {
                GameApi.Warn("ProfileRestore", e);
                GameApi.SayErr(Texts.T("профиль: откат не удался", "profile: restore failed") +
                               $": {e.GetType().Name}");
                return false;
            }
        }

        // -------------------------------------------------- Harmony patches --
        // Сигнатуры сверены с метаданными RoR2.dll:
        //   UserProfile.HasUnlockable(UnlockableDef)   [instance] -> bool
        //   UserProfile.HasUnlockable(string)          [instance] -> bool
        //   UserProfile.HasSurvivorUnlocked(SurvivorIndex) -> bool
        //   UserProfile.HasViewedViewable(string)      -> bool

        [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.HasUnlockable), new[] { typeof(UnlockableDef) })]
        internal static class HasUnlockableDefPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (AllUnlockables) __result = true;
            }
        }

        [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.HasUnlockable), new[] { typeof(string) })]
        internal static class HasUnlockableNamePatch
        {
            private static void Postfix(ref bool __result)
            {
                if (AllUnlockables) __result = true;
            }
        }

        [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.HasSurvivorUnlocked))]
        internal static class HasSurvivorUnlockedPatch
        {
            private static void Postfix(ref bool __result)
            {
                if (AllSurvivors) __result = true;
            }
        }

        [HarmonyPatch(typeof(UserProfile), nameof(UserProfile.HasViewedViewable))]
        internal static class HasViewedViewablePatch
        {
            private static void Postfix(ref bool __result)
            {
                if (AllViewables) __result = true;
            }
        }
    }
}

