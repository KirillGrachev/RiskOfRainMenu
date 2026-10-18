using System.Diagnostics;
using RoR2;

namespace SandboxMenu
{
    /// <summary>
    /// Compile-time страховка reflection-цепочек выдачи/снятия предметов.
    ///
    /// Цепочки в Spawning.GiveItemTo/RemoveItemFrom сознательно идут через
    /// reflection, чтобы переживать переименования между сборками игры, но
    /// из-за этого CI не видел бы исчезновение всех имён цепочки. Методы ниже
    /// вызываются никогда (Conditional с несуществующим символом вырезает
    /// вызовы), однако их ТЕЛА компилируются и держат прямые ссылки на имена:
    /// уберёт игра GiveItemPermanent или CalculateEffectiveItemStacks -
    /// сборка упадёт здесь красным чеком, и цепочку обновят осознанно.
    /// </summary>
    internal static class ApiGuard
    {
        // obsolete-ворнинги здесь ожидаемы:.guard держит в том числе имена,
        // которые игра помечает устаревшими, но которые цепочки ещё используют
        [Conditional("SANDBOX_API_GUARD_NEVER")]
#pragma warning disable 618
        public static void ItemChainGuard(Inventory inv, ItemIndex index, int count)
        {
            inv.GiveItemPermanent(index, count);
            inv.GiveItem(index, count);
            inv.RemoveItem(index, count);
            inv.RemoveItemPermanent(index, count);
            inv.CalculateEffectiveItemStacks(index);
        }
#pragma warning restore 618
    }
}
