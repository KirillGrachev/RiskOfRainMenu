using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RoR2;
using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Точка входа BepInEx.
    ///
    /// Меню открывается клавишей Delete (настраивается в BepInEx/config).
    /// Всё, что меняет игровой мир, выполняется только на сервере и обёрнуто
    /// в try/catch: плагин не должен уметь ронять игру, даже если какая-то
    /// цель в новой сборке ведёт себя иначе, чем ожидалось.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public class SandboxMenuPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<KeyCode> MenuKey;
        internal static ConfigEntry<bool> ShowChatMessages;
        internal static ConfigEntry<int> CloneRespawnDelay;
        internal static ConfigEntry<string> MenuLanguage;
        internal static ConfigEntry<bool> CloneFollowDefault;

        private static Harmony _harmony;
        private float _updateAccumulator;

        private void Awake()
        {
            GameApi.Log = Logger;

            MenuKey = Config.Bind("General", "MenuKey", KeyCode.Delete,
                "Key that opens/closes the sandbox menu.");
            ShowChatMessages = Config.Bind("General", "ChatMessages", true,
                "Print a short line into the in-game chat on every action.");
            CloneRespawnDelay = Config.Bind("Clone", "RespawnDelaySeconds", 5,
                "Pause before respawning a clone that died (0 = instantly).");
            MenuLanguage = Config.Bind("General", "Language", "ru",
                "Menu and chat language: ru or en.");
            CloneFollowDefault = Config.Bind("Clone", "FollowByDefault", true,
                "New clone groups follow their owner by default (off = hold position).");
            Texts.Russian = !string.Equals(MenuLanguage.Value, "en", System.StringComparison.OrdinalIgnoreCase);

            UnlockPatches.Apply();
            Catalogs.Reset();
            CloneManager.Reset();
            PlayerTargets.Refresh();

            // вне забега Language ещё не загружен: каталоги, снятые в главном
            // меню, получают имена-заглушки; пересобираем на старте забега
            Run.onRunStartGlobal += HandleRunStart;

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(UnlockPatches));
            _harmony.PatchAll(typeof(DropHooks));

            Logger.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded. Menu key: {MenuKey.Value}");
        }

        private void OnDestroy()
        {
            try { Run.onRunStartGlobal -= HandleRunStart; } catch { /* best effort */ }
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { /* best effort */ }
        }

        /// <summary>Старт забега: язык загружен, снимаем каталоги начисто.</summary>
        private static void HandleRunStart(Run run)
        {
            try { Catalogs.BuildAll(); }
            catch (Exception e) { GameApi.WarnOnce("Catalogs.BuildAll on run start", e); }
        }

        private void Update()
        {
            try
            {
                var key = MenuKey != null ? MenuKey.Value : KeyCode.Delete;
                if (key != KeyCode.None && Input.GetKeyDown(key))
                {
                    MenuUI.Toggle();
                    if (MenuUI.IsVisible) Catalogs.EnsureBuilt();
                }
            }
            catch (Exception e)
            {
                GameApi.WarnOnce("Update/key", e);
            }

            // поддержку клонов и привязку миньонов не обязательно считать каждый кадр
            _updateAccumulator += Time.unscaledDeltaTime;
            if (_updateAccumulator >= 0.5f)
            {
                _updateAccumulator = 0f;
                try { PlayerTargets.Refresh(); }
                catch (Exception e) { GameApi.WarnOnce("PlayerTargets.Refresh", e); }
                try { CloneManager.Maintain(); }
                catch (Exception e) { GameApi.WarnOnce("CloneManager.Maintain", e); }
                try { Spawning.SweepKillQueue(); }
                catch (Exception e) { GameApi.WarnOnce("Spawning.SweepKillQueue", e); }
                try { SpawnRegistry.SweepDrops(); }
                catch (Exception e) { GameApi.WarnOnce("SpawnRegistry.SweepDrops", e); }
                // тело клона рождается не в тот же кадр, что master, поэтому
                // привязка AI доделывается из очереди
                try { OwnerBinding.Tick(); }
                catch (Exception e) { GameApi.WarnOnce("OwnerBinding.Tick", e); }
                try { NetProtocol.Tick(); }
                catch (Exception e) { GameApi.WarnOnce("NetProtocol.Tick", e); }
            }
        }

        private void OnGUI()
        {
            try { MenuUI.Draw(); }
            catch (Exception e) { GameApi.WarnOnce("MenuUI.Draw", e); }
        }
    }

    internal static class PluginInfo
    {
        public const string Guid = "local.ror2.sandboxmenu";
        public const string Name = "RoR2 Sandbox Menu";
        public const string Version = "1.2.0";
    }
}
