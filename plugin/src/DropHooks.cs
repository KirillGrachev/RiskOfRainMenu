using HarmonyLib;
using RoR2;

namespace SandboxMenu
{
    /// <summary>
    /// Хуки учёта дропа, созданного меню.
    ///
    /// PickupDropletController.CreatePickupDroplet не возвращает созданный
    /// объект, поэтому «свой» дроп помечается иначе: на время вызова взводится
    /// счётчик ожидания (Spawning.ArmDrops), и postfix OnEnable капли, который
    /// Unity дёргает синхронно внутри Instantiate, забирает объект в реестр.
    /// Когда капля приземляется, она уничтожается и рождает GenericPickupController
    /// рядом с точкой приземления - второй postfix усыновляет такой пикап
    /// ближайшей недавно приземлившейся записью реестра.
    ///
    /// Обе цели - приватные OnEnable, объявленные в самих типах игры: Harmony
    /// патчит их по имени, без привязки к адресам.
    /// </summary>
    internal static class DropHooks
    {
        /// <summary>Капля дропа родилась: забираем её в реестр, если ждали.</summary>
        [HarmonyPatch(typeof(PickupDropletController), "OnEnable")]
        internal static class DropletOnEnable
        {
            private static void Postfix(PickupDropletController __instance)
            {
                try
                {
                    if (__instance != null && SpawnRegistry.ConsumeDropArm())
                    {
                        SpawnRegistry.TrackDroplet(__instance.gameObject);
                    }
                }
                catch (System.Exception e) { GameApi.WarnOnce("DropletOnEnable", e); }
            }
        }

        /// <summary>Пикап родился: усыновляем, если рядом недавно села капля меню.</summary>
        [HarmonyPatch(typeof(GenericPickupController), "OnEnable")]
        internal static class PickupOnEnable
        {
            private static void Postfix(GenericPickupController __instance)
            {
                try
                {
                    if (__instance != null) SpawnRegistry.AdoptPickup(__instance.gameObject);
                }
                catch (System.Exception e) { GameApi.WarnOnce("PickupOnEnable", e); }
            }
        }
    }
}
