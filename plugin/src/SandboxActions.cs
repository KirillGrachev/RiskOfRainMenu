using RoR2;
using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Единая точка исполнения действий меню.
    ///
    /// Каждая кнопка меню вызывает метод отсюда; метод сам решает, где
    /// исполниться: на сервере - сразу, на клиенте - уехать запросом
    /// <see cref="NetProtocol"/> и исполниться на сервере тем же кодом.
    /// Поэтому локальное нажатие и запрос от клиента не могут разойтись:
    /// исполняющий код один, владелец действия передаётся явно.
    ///
    /// Правило пары: у каждой функции выдачи есть зеркальная функция удаления
    /// (предметы, дроп, персонажи, объекты, клоны, разблокировки профиля).
    /// </summary>
    internal static class SandboxActions
    {
        /// <summary>Действия исполняются локально: мы сервер или одиночная игра.</summary>
        private static bool Local { get { return NetProtocol.IsServer; } }

        /// <summary>Цель текущего нажатия кнопки в меню.</summary>
        private static NetworkUser Target { get { return PlayerTargets.SelectedUser; } }

        /// <summary>netId цели для запроса (0 - отправитель).</summary>
        private static uint TargetId(NetworkUser target)
        {
            return PlayerTargets.NetIdOf(target);
        }

        // -------------------------------------------------------------- items --
        /// <summary>Выдать N стаков предмета цели.</summary>
        public static void GiveItem(ItemIndex index, int count)
        {
            var t = Target;
            if (Local) { RunGiveItem(t, index, count); return; }
            Send(new SandboxRequest { Action = ActionCode.GiveItem, TargetNetId = TargetId(t), P1 = (int)index, P2 = count });
        }

        /// <summary>Снять N стаков предмета с цели (зеркало выдачи).</summary>
        public static void RemoveItem(ItemIndex index, int count)
        {
            var t = Target;
            if (Local) { RunRemoveItem(t, index, count); return; }
            Send(new SandboxRequest { Action = ActionCode.RemoveItem, TargetNetId = TargetId(t), P1 = (int)index, P2 = count });
        }

        /// <summary>Очистить инвентарь цели целиком.</summary>
        public static void ClearItems()
        {
            var t = Target;
            if (Local) { RunClearItems(t); return; }
            Send(new SandboxRequest { Action = ActionCode.ClearItems, TargetNetId = TargetId(t) });
        }

        /// <summary>Выдать цели по N штук каждого нескрытого предмета каталога.</summary>
        public static void GiveAllItems(int count)
        {
            var t = Target;
            if (Local) { RunGiveAllItems(t, count); return; }
            Send(new SandboxRequest { Action = ActionCode.GiveAllItems, TargetNetId = TargetId(t), P1 = count });
        }

        // -------------------------------------------------------------- drops --
        /// <summary>Выбросить N пикапов у цели.</summary>
        public static void DropPickup(PickupIndex index, int count)
        {
            var t = Target;
            if (Local) { RunDropPickup(t, index, count); return; }
            Send(new SandboxRequest { Action = ActionCode.DropPickup, TargetNetId = TargetId(t), P1 = index.value, P2 = count });
        }

        /// <summary>Убрать весь лежащий в мире дроп (зеркало дропа).</summary>
        public static void ClearDrops()
        {
            if (Local) { RunClearDrops(); return; }
            Send(new SandboxRequest { Action = ActionCode.ClearDrops });
        }

        /// <summary>Убрать лишний дроп рядом с целью (радиус 15, режим по умолчанию).</summary>
        public static void ClearDropsNear()
        {
            var t = Target;
            if (Local) { RunClearDropsNear(t); return; }
            Send(new SandboxRequest { Action = ActionCode.ClearDropsNear, TargetNetId = TargetId(t) });
        }

        /// <summary>Убрать только тот дроп, который выбросило меню.</summary>
        public static void ClearMenuDrops()
        {
            if (Local) { RunClearMenuDrops(); return; }
            Send(new SandboxRequest { Action = ActionCode.ClearMenuDrops });
        }

        // --------------------------------------------------------- characters --
        /// <summary>Призвать N персонажей префаба на команде team рядом с целью.</summary>
        public static void SpawnMaster(GameObject prefab, string name, TeamIndex team, int count)
        {
            var t = Target;
            if (Local) { RunSpawnMaster(t, prefab, team, count); return; }
            Send(new SandboxRequest { Action = ActionCode.SpawnMaster, TargetNetId = TargetId(t), P1 = count, Team = (byte)team, Name = name });
        }

        /// <summary>Убрать всех призванных меню персонажей (зеркало спавна).</summary>
        public static void RemoveSpawnedMasters()
        {
            if (Local) { RunRemoveSpawnedMasters(); return; }
            Send(new SandboxRequest { Action = ActionCode.RemoveSpawnedMasters });
        }

        /// <summary>Убрать осиротевшие модели (тела без master'а) по всей сцене.</summary>
        public static void CleanupOrphans()
        {
            if (Local) { RunCleanupOrphans(); return; }
            Send(new SandboxRequest { Action = ActionCode.CleanupOrphans });
        }

        // -------------------------------------------------------------- props --
        /// <summary>Заспавнить N объектов сцены (сундуки, автоматы, алтари) у цели.</summary>
        public static void SpawnProp(SpawnCard card, string name, int count)
        {
            var t = Target;
            if (Local) { RunSpawnProp(t, card, count); return; }
            Send(new SandboxRequest { Action = ActionCode.SpawnProp, TargetNetId = TargetId(t), P1 = count, Name = name });
        }

        /// <summary>Убрать все заспавненные меню объекты (зеркало спавна).</summary>
        public static void RemoveSpawnedProps()
        {
            if (Local) { RunRemoveSpawnedProps(); return; }
            Send(new SandboxRequest { Action = ActionCode.RemoveSpawnedProps });
        }

        // -------------------------------------------------------------- clones --
        /// <summary>Задать желаемую численность клонов цели.</summary>
        public static void CloneSetDesired(int value)
        {
            var t = Target;
            if (Local) { CloneManager.SetDesiredFor(t, value); return; }
            Send(new SandboxRequest { Action = ActionCode.CloneSetDesired, TargetNetId = TargetId(t), P1 = value });
        }

        /// <summary>Мгновенно добавить N клонов цели.</summary>
        public static void CloneSpawnNow(int count)
        {
            var t = Target;
            if (Local) { CloneManager.SpawnNow(t, count); return; }
            Send(new SandboxRequest { Action = ActionCode.CloneSpawnNow, TargetNetId = TargetId(t), P1 = count });
        }

        /// <summary>Убрать клонов цели.</summary>
        public static void CloneClearTarget()
        {
            var t = Target;
            if (Local) { CloneManager.ClearFor(t); return; }
            Send(new SandboxRequest { Action = ActionCode.CloneClearTarget, TargetNetId = TargetId(t) });
        }

        /// <summary>Убрать клонов всех игроков.</summary>
        public static void CloneClearAll()
        {
            if (Local) { CloneManager.ClearAll(); return; }
            Send(new SandboxRequest { Action = ActionCode.CloneClearAll });
        }

        /// <summary>Флаги группы клонов цели: копирование предметов/loadout, следование.</summary>
        public static void CloneSetFlags(bool copyInventory, bool copyLoadout, bool follow)
        {
            var t = Target;
            if (Local) { CloneManager.SetFlagsFor(t, copyInventory, copyLoadout, follow); return; }
            int mask = (copyInventory ? 1 : 0) | (copyLoadout ? 2 : 0) | (follow ? 4 : 0);
            Send(new SandboxRequest { Action = ActionCode.CloneSetFlags, TargetNetId = TargetId(t), P1 = mask });
        }

        /// <summary>Глобальный флаг поддержания численности клонов.</summary>
        public static void CloneSetKeepAlive(bool value)
        {
            if (Local) { CloneManager.KeepAlive = value; return; }
            Send(new SandboxRequest { Action = ActionCode.CloneSetKeepAlive, P1 = value ? 1 : 0 });
        }

        // ---------------------------------------------------- server execution --
        /// <summary>
        /// Диспетчер запросов на сервере: исполняет действие от имени target.
        /// Вызывается только из <see cref="NetProtocol"/>.
        /// </summary>
        internal static void ExecuteRequest(SandboxRequest req, NetworkUser target)
        {
            switch (req.Action)
            {
                case ActionCode.GiveItem:
                    RunGiveItem(target, (ItemIndex)req.P1, req.P2);
                    break;
                case ActionCode.RemoveItem:
                    RunRemoveItem(target, (ItemIndex)req.P1, req.P2);
                    break;
                case ActionCode.ClearItems:
                    RunClearItems(target);
                    break;
                case ActionCode.GiveAllItems:
                    RunGiveAllItems(target, req.P1);
                    break;
                case ActionCode.DropPickup:
                {
                    RunDropPickup(target, new PickupIndex(req.P1), req.P2);
                    break;
                }
                case ActionCode.ClearDrops:
                    RunClearDrops();
                    break;
                case ActionCode.ClearDropsNear:
                    RunClearDropsNear(target);
                    break;
                case ActionCode.ClearMenuDrops:
                    RunClearMenuDrops();
                    break;
                case ActionCode.SpawnMaster:
                {
                    var prefab = MasterCatalog.FindMasterPrefab(req.Name ?? "");
                    RunSpawnMaster(target, prefab, (TeamIndex)req.Team, req.P1);
                    break;
                }
                case ActionCode.RemoveSpawnedMasters:
                    RunRemoveSpawnedMasters();
                    break;
                case ActionCode.CleanupOrphans:
                    RunCleanupOrphans();
                    break;
                case ActionCode.SpawnProp:
                    RunSpawnPropByName(target, req.Name, req.P1);
                    break;
                case ActionCode.RemoveSpawnedProps:
                    RunRemoveSpawnedProps();
                    break;
                case ActionCode.CloneSetDesired:
                    CloneManager.SetDesiredFor(target, req.P1);
                    break;
                case ActionCode.CloneSpawnNow:
                    CloneManager.SpawnNow(target, req.P1);
                    break;
                case ActionCode.CloneClearTarget:
                    CloneManager.ClearFor(target);
                    break;
                case ActionCode.CloneClearAll:
                    CloneManager.ClearAll();
                    break;
                case ActionCode.CloneSetFlags:
                    CloneManager.SetFlagsFor(target,
                        (req.P1 & 1) != 0, (req.P1 & 2) != 0, (req.P1 & 4) != 0);
                    break;
                case ActionCode.CloneSetKeepAlive:
                    CloneManager.KeepAlive = req.P1 != 0;
                    break;
                default:
                    // коды трейнера (читы, экономика) и управления забегом
                    // исполняют специализированные диспетчеры той же цепочкой
                    if (CheatActions.TryExecute(req, target)) break;
                    if (RunControl.TryExecute(req, target)) break;
                    GameApi.Warn("ExecuteRequest", new System.NotSupportedException("action " + req.Action));
                    break;
            }
        }

        // --------------------------------------------------------- run bodies --
        private static void RunGiveItem(NetworkUser target, ItemIndex index, int count)
        {
            var inv = Spawning.InventoryOf(target);
            string label = ItemName(index);
            if (Spawning.GiveItemTo(inv, index, count))
            {
                GameApi.SayOk(Texts.T($"предметы: выдано {count}× {label}", $"items: gave {count}x {label}"));
            }
            else
            {
                GameApi.SayErr(Texts.T($"предметы: {label} не выдался", $"items: {label} failed") +
                               ": " + Spawning.LastError);
            }
        }

        private static void RunRemoveItem(NetworkUser target, ItemIndex index, int count)
        {
            var inv = Spawning.InventoryOf(target);
            string label = ItemName(index);
            if (Spawning.RemoveItemFrom(inv, index, count))
            {
                GameApi.SayOk(Texts.T($"предметы: снято {count}× {label}", $"items: removed {count}x {label}"));
            }
            else
            {
                GameApi.SayErr(Texts.T($"предметы: {label} не снялся", $"items: {label} remove failed") +
                               ": " + Spawning.LastError);
            }
        }

        private static void RunClearItems(NetworkUser target)
        {
            var inv = Spawning.InventoryOf(target);
            int n = Spawning.ClearInventory(inv);
            GameApi.SayOk(Texts.T(
                $"предметы: инвентарь {PlayerTargets.DisplayName(target)} очищен ({n} поз.)",
                $"items: inventory of {PlayerTargets.DisplayName(target)} cleared ({n} entries)"));
        }

        private static void RunGiveAllItems(NetworkUser target, int count)
        {
            var inv = Spawning.InventoryOf(target);
            if (inv == null)
            {
                GameApi.SayErr(Texts.T("предметы: у цели нет инвентаря", "items: target has no inventory"));
                return;
            }
            int given = 0, failed = 0;
            foreach (var item in Catalogs.Items)
            {
                if (item.Hidden) continue;
                if (Spawning.GiveItemTo(inv, item.Index, count)) given++;
                else failed++;
            }
            if (failed == 0)
            {
                GameApi.SayOk(Texts.T(
                    $"предметы: выдано {given} видов по {count}×",
                    $"items: gave {given} kinds x{count}"));
            }
            else
            {
                GameApi.SayErr(Texts.T(
                    $"предметы: выдано {given} видов по {count}×, ошибок {failed}",
                    $"items: gave {given} kinds x{count}, failed {failed}"));
            }
        }

        private static void RunDropPickup(NetworkUser target, PickupIndex index, int count)
        {
            if (Spawning.DropPickup(index, count, target))
            {
                GameApi.SayOk(Texts.T($"дроп: {count}× у {PlayerTargets.DisplayName(target)}",
                                      $"dropped {count}x at {PlayerTargets.DisplayName(target)}"));
            }
            else
            {
                GameApi.SayErr(Texts.T("дроп не создан", "drop failed") + ": " + Spawning.LastError);
            }
        }

        private static void RunClearDrops()
        {
            int n = Spawning.ClearDrops();
            GameApi.SayOk(Texts.T($"дроп: убрано {n} шт. из мира", $"drops: removed {n} from the world"));
        }

        private static void RunClearDropsNear(NetworkUser target)
        {
            int n = Spawning.ClearDropsNear(target, 15f);
            GameApi.SayOk(Texts.T(
                $"дроп: убрано {n} шт. рядом с {PlayerTargets.DisplayName(target)}",
                $"drops: removed {n} near {PlayerTargets.DisplayName(target)}"));
        }

        private static void RunClearMenuDrops()
        {
            int n = SpawnRegistry.RemoveMenuDrops();
            GameApi.SayOk(Texts.T($"дроп: убрано {n} шт. выброшенных меню", $"drops: removed {n} menu drops"));
        }

        private static void RunSpawnMaster(NetworkUser target, GameObject prefab, TeamIndex team, int count)
        {
            if (prefab == null)
            {
                GameApi.SayErr(Texts.T("персонаж: префаб не найден", "character: prefab not found"));
                return;
            }
            int ok = 0;
            for (int i = 0; i < count; i++)
            {
                var m = Spawning.SpawnCharacter(prefab, team, target, i, count);
                if (m != null) { SpawnRegistry.Track(m); ok++; }
            }
            if (ok > 0)
            {
                GameApi.SayOk(Texts.T($"спавн: {ok}× {prefab.name} ({GameApi.TeamName(team)})",
                                      $"spawned {ok}x {prefab.name} ({GameApi.TeamName(team)})"));
            }
            else
            {
                GameApi.SayErr(Texts.T($"спавн не удался: {prefab.name}", $"spawn failed: {prefab.name}"));
            }
        }

        private static void RunRemoveSpawnedMasters()
        {
            int n = SpawnRegistry.RemoveMasters();
            int debuffs = SpawnRegistry.CleanseDebuffs();
            GameApi.SayOk(Texts.T(
                $"спавн: убрано {n} призванных персонажей вместе со свитой, дебафов снято: {debuffs}",
                $"removed {n} spawned characters with their minions, debuffs cleansed: {debuffs}"));
        }

        private static void RunCleanupOrphans()
        {
            int n = Spawning.CleanupOrphanBodies();
            int debuffs = SpawnRegistry.CleanseDebuffs();
            GameApi.SayOk(Texts.T(
                $"модели: убрано {n} осиротевших тел, дебафов снято: {debuffs}",
                $"models: removed {n} orphan bodies, debuffs cleansed: {debuffs}"));
        }

        private static void RunSpawnProp(NetworkUser target, SpawnCard card, int count)
        {
            if (card == null)
            {
                GameApi.SayErr(Texts.T("объект: карта не найдена", "prop: card not found"));
                return;
            }
            int ok = 0;
            for (int i = 0; i < count; i++)
            {
                var pos = PlayerTargets.PointOnRing(target, i, count, 3f);
                if (pos == Vector3.zero) pos = PlayerTargets.PointNear(target, 3f, 0.2f);
                var go = Spawning.SpawnInteractable(card, pos);
                if (go != null) { SpawnRegistry.Track(go); ok++; }
            }
            string name = card.prefab != null ? card.prefab.name : "?";
            if (ok > 0) GameApi.SayOk(Texts.T($"объект: {ok}× {name}", $"spawned {ok}x {name}"));
            else GameApi.SayErr(Texts.T($"объект не заспавнился: {name}", $"spawn failed: {name}"));
        }

        /// <summary>Спавн объекта по имени префаба (путь сетевого запроса).</summary>
        private static void RunSpawnPropByName(NetworkUser target, string name, int count)
        {
            SpawnCard card = null;
            foreach (var it in Catalogs.Interactables)
            {
                if (it.Name == name) { card = it.Card; break; }
            }
            RunSpawnProp(target, card, count);
        }

        private static void RunRemoveSpawnedProps()
        {
            int n = SpawnRegistry.RemoveProps();
            GameApi.SayOk(Texts.T($"объекты: убрано {n} заспавненных", $"removed {n} spawned props"));
        }

        // -------------------------------------------------------------- misc --
        /// <summary>Отправить запрос или пожаловаться, что канал недоступен.</summary>
        private static void Send(SandboxRequest req)
        {
            if (!NetProtocol.Send(req))
            {
                GameApi.SayErr(Texts.T(
                    "нет соединения с сервером: действие не отправлено",
                    "no server connection: action not sent"));
            }
        }

        /// <summary>Локализованное имя предмета для чат-сообщений.</summary>
        private static string ItemName(ItemIndex index)
        {
            try
            {
                var def = ItemCatalog.GetItemDef(index);
                if (def != null) return GameApi.Localize(def.nameToken);
            }
            catch (System.Exception e) { GameApi.WarnOnce("ItemCatalog.GetItemDef", e); }
            return index.ToString();
        }
    }
}

