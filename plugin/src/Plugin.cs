using System;
using System.Collections.Generic;
using System.Reflection;
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
    /// Меню открывается клавишей Delete (настраивается в BepInEx/config),
    /// кроме случая, когда фокус стоит в поле поиска меню - иначе набор
    /// запроса сворачивал бы окно. Всё, что меняет игровой мир, выполняется
    /// только на сервере и обёрнуто в try/catch: плагин не должен уметь
    /// ронять игру, даже если какая-то цель в новой сборке ведёт себя иначе,
    /// чем ожидалось.
    ///
    /// Регистрация сетевых обработчиков - по событиям игры
    /// (NetworkManagerSystem.onStartServerGlobal/onStartClientGlobal),
    /// интерактивы перечитываются по Stage.onStageStartGlobal, каталоги -
    /// по Run.onRunStartGlobal: поллинг оставлен только для дешёвых флагов.
    /// </summary>
    [BepInPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public class SandboxMenuPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<KeyCode> MenuKey;
        internal static ConfigEntry<bool> ShowChatMessages;
        internal static ConfigEntry<int> CloneRespawnDelay;
        internal static ConfigEntry<string> MenuLanguage;
        internal static ConfigEntry<bool> CloneFollowDefault;
        internal static ConfigEntry<int> CloneMaxCount;
        internal static ConfigEntry<string> RemoteTargets;
        internal static ConfigEntry<int> MessageBaseId;
        internal static ConfigEntry<float> MenuScale;
        internal static ConfigEntry<int> MenuTab;
        internal static ConfigEntry<bool> MenuCollapsed;
        internal static ConfigEntry<int> MenuX;
        internal static ConfigEntry<int> MenuY;
        internal static ConfigEntry<float> FlySpeed;

        /// <summary>Инстанс для сохранения конфига из статических классов меню.</summary>
        internal static SandboxMenuPlugin Self;

        private static Harmony _harmony;
        private float _updateAccumulator;

        private void Awake()
        {
            GameApi.Log = Logger;
            Self = this;

            MenuKey = Config.Bind("General", "MenuKey", KeyCode.Delete,
                "Key that opens/closes the sandbox menu.");
            ShowChatMessages = Config.Bind("General", "ChatMessages", true,
                "Print a short line into the in-game chat on every action.");
            MenuLanguage = Config.Bind("General", "Language", "ru",
                "Menu and chat language: ru or en.");
            MessageBaseId = Config.Bind("General", "MessageBaseId", 31337,
                "Base HLAPI message id (base..base+3). Change if another mod occupies the range.");
            MenuScale = Config.Bind("General", "MenuScale", 1f,
                "UI scale for HiDPI/4K displays (1.0 - 2.0).");
            MenuTab = Config.Bind("General", "LastTab", 0,
                "Remembered menu tab index.");
            MenuCollapsed = Config.Bind("General", "LastCollapsed", false,
                "Remembered collapsed state of the menu window.");
            MenuX = Config.Bind("General", "LastX", 90,
                "Remembered window position X.");
            MenuY = Config.Bind("General", "LastY", 60,
                "Remembered window position Y.");
            FlySpeed = Config.Bind("Cheats", "FlySpeed", 20f,
                "Free-flight speed in meters per second (Shift gives x3). Range 5..100 in the menu.");
            CloneRespawnDelay = Config.Bind("Clone", "RespawnDelaySeconds", 5,
                "Pause before respawning a clone that died (0 = instantly).");
            CloneFollowDefault = Config.Bind("Clone", "FollowByDefault", true,
                "New clone groups follow their owner by default (off = hold position).");
            CloneMaxCount = Config.Bind("Clone", "MaxClones", 20,
                "Hard cap of clones per player (slider and requests are clamped to it).");
            RemoteTargets = Config.Bind("Network", "RemoteTargets", "any",
                "any - clients may act on any player; self - clients may act only on themselves.");

            Texts.Russian = !string.Equals(MenuLanguage.Value, "en", System.StringComparison.OrdinalIgnoreCase);

            UnlockPatches.Apply();
            Catalogs.Reset();
            CloneManager.Reset();
            PlayerTargets.Refresh();

            // вне забега Language ещё не загружен: каталоги, снятые в главном
            // меню, получают имена-заглушки; пересобираем на старте забега
            Run.onRunStartGlobal += HandleRunStart;
            // интерактивы зависят от сцены: перечитываем на входе в стадию
            Stage.onStageStartGlobal += HandleStageStart;
            NetProtocol.SubscribeEvents();

            _harmony = new Harmony(PluginInfo.Guid);
            _harmony.PatchAll(typeof(UnlockPatches));
            _harmony.PatchAll(typeof(DropHooks));
            // ВАЖНО: PatchAll(Type) обходит только сам тип и его ВЛОЖЕННЫЕ классы,
            // а патчи читов - отдельные top-level классы одного файла, поэтому
            // регистрируются каждый своим вызовом. Без этого бессмертие, ваншот
            // и множители молча не работали бы (VerifyHarmonyTargets ругался).
            _harmony.PatchAll(typeof(CheatPatches));
            _harmony.PatchAll(typeof(TakeDamageCheatPatch));
            _harmony.PatchAll(typeof(AttackSpeedCheatPatch));
            _harmony.PatchAll(typeof(MoveSpeedCheatPatch));
            VerifyHarmonyTargets();

            Logger.LogInfo($"{PluginInfo.Name} {PluginInfo.Version} loaded. Menu key: {MenuKey.Value}");
        }

        private void OnDestroy()
        {
            try { Run.onRunStartGlobal -= HandleRunStart; } catch { /* best effort */ }
            try { Stage.onStageStartGlobal -= HandleStageStart; } catch { /* best effort */ }
            try { FlyController.Shutdown(); } catch { /* best effort */ }
            try { MenuUI.PersistState(); } catch { /* best effort */ }
            try { if (_harmony != null) _harmony.UnpatchSelf(); } catch { /* best effort */ }
        }

        /// <summary>
        /// Самопроверка Harmony: если цель не встала (игру обновили, приватный
        /// метод переименован), это видно сразу в чате и логе, а не проявляется
        /// молча через час игры в «дроп не учитывается».
        /// </summary>
        private void VerifyHarmonyTargets()
        {
            var expected = new List<MethodBase>
            {
                AccessTools.Method(typeof(UserProfile), "HasUnlockable", new[] { typeof(UnlockableDef) }),
                AccessTools.Method(typeof(UserProfile), "HasUnlockable", new[] { typeof(string) }),
                AccessTools.Method(typeof(UserProfile), "HasSurvivorUnlocked"),
                AccessTools.Method(typeof(UserProfile), "HasViewedViewable"),
                AccessTools.Method(typeof(PickupDropletController), "OnEnable"),
                AccessTools.Method(typeof(GenericPickupController), "OnEnable"),
                AccessTools.Method(typeof(HealthComponent), "TakeDamage", new[] { typeof(DamageInfo) }),
                AccessTools.PropertyGetter(typeof(CharacterBody), "attackSpeed"),
                AccessTools.PropertyGetter(typeof(CharacterBody), "moveSpeed"),
            };

            var patched = new HashSet<MethodBase>();
            try
            {
                foreach (var m in _harmony.GetPatchedMethods()) patched.Add(m);
            }
            catch (Exception e) { GameApi.Warn("GetPatchedMethods", e); return; }

            foreach (var m in expected)
            {
                if (m == null || patched.Contains(m)) continue;
                GameApi.SayErr(Texts.T(
                    $"Harmony-цель не встала: {m.DeclaringType.Name}.{m.Name} - часть функций деградирует",
                    $"Harmony target missed: {m.DeclaringType.Name}.{m.Name} - some features degrade"));
                GameApi.Warn("VerifyHarmonyTargets",
                    new MissingMethodException($"{m.DeclaringType.FullName}.{m.Name}"));
            }
        }

        /// <summary>Старт забега: язык загружен, снимаем каталоги начисто.</summary>
        private static void HandleRunStart(Run run)
        {
            // чит-флаги прошлых забегов привязаны к мертвым netId - сбрасываем
            try { CheatState.ResetRun(); }
            catch (Exception e) { GameApi.WarnOnce("CheatState.ResetRun on run start", e); }
            try { Catalogs.BuildAll(); }
            catch (Exception e) { GameApi.WarnOnce("Catalogs.BuildAll on run start", e); }
        }

        /// <summary>Вход в новую стадию: список интерактивов сцены устарел.</summary>
        private static void HandleStageStart(Stage stage)
        {
            try { Catalogs.RefreshInteractables(); }
            catch (Exception e) { GameApi.WarnOnce("Catalogs.RefreshInteractables on stage start", e); }
        }

        private void Update()
        {
            try
            {
                var key = MenuKey != null ? MenuKey.Value : KeyCode.Delete;
                bool typing = GUI.GetNameOfFocusedControl() == Widgets.SearchControlName;
                if (!typing && key != KeyCode.None && Input.GetKeyDown(key))
                {
                    MenuUI.Toggle();
                    if (MenuUI.IsVisible) Catalogs.EnsureBuilt();
                }
            }
            catch (Exception e)
            {
                GameApi.WarnOnce("Update/key", e);
            }

            // полёт читает ввод и двигает тело каждый кадр - вне аккумулятора
            try { FlyController.Tick(); }
            catch (Exception e) { GameApi.WarnOnce("FlyController.Tick", e); }

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
                try { CheatPatches.MaintainContinuous(); }
                catch (Exception e) { GameApi.WarnOnce("CheatPatches.MaintainContinuous", e); }
            }
        }

        private void OnGUI()
        {
            try { MenuUI.Draw(); }
            catch (Exception e) { GameApi.WarnOnce("MenuUI.Draw", e); }
        }
    }

    /// <summary>
    /// Константы точки входа. Version приходит из сгенерированного файла
    /// PluginInfo.g.cs (msbuild-свойство PluginVersion), чтобы номер версии
    /// dll не мог разъехаться с тегом релиза: CI передаёт -p:PluginVersion=vX.Y.Z.
    /// </summary>
    internal static partial class PluginInfo
    {
        public const string Guid = "local.ror2.sandboxmenu";
        public const string Name = "RoR2 Sandbox Menu";
    }
}

