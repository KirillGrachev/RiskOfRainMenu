using System;
using System.Collections.Generic;
using System.Reflection;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Примитивы изменения мира. Все методы принимают цель (NetworkUser) явно:
    /// и локальное нажатие кнопки, и запрос от клиента проходят через один и тот
    /// же код, где владелец действия известен однозначно, а не «кто открыл меню».
    ///
    /// Почему не DirectorCore.TrySpawnObject: ему нужен DirectorSpawnRequest, а
    /// тот конструируется с Xoroshiro128Plus из сборки HGCSharpUtils, которой
    /// нет среди доступных для компиляции. Поэтому:
    ///   персонажи   -> MasterSummon.Perform()
    ///   интерактивы -> Object.Instantiate + NetworkServer.Spawn
    ///                  (повторяет InteractableSpawnCard.Spawn, RID 16240,
    ///                   без косметического рандома ориентации)
    ///
    /// Выдача/снятие предметов идут цепочкой методов через reflection: сборки
    /// игры переименовывали GiveItem/GiveItemPermanent между версиями, жёсткая
    /// привязка ломала выдачу молча. Цепочка кэшируется один раз.
    /// </summary>
    internal static class Spawning
    {
        public static string LastError = "";

        public static bool ServerReady(string what)
        {
            if (NetworkServer.active) return true;
            LastError = Texts.T("нет сервера - хост или одиночная игра", "not a server - host or play single player");
            GameApi.SayErr($"{what}: {LastError}");
            return false;
        }

        /// <summary>Универсальный вызов MasterSummon. Возвращает master или null.</summary>
        public static CharacterMaster Summon(GameObject masterPrefab, Vector3 position, Quaternion rotation,
                                             TeamIndex? team, GameObject summonerBodyObject,
                                             bool ignoreTeamLimit, Loadout loadout,
                                             Inventory inventoryToCopy, bool propagateOwnership)
        {
            if (masterPrefab == null) return null;
            if (!NetworkServer.active) return null;

            var ms = new MasterSummon
            {
                masterPrefab = masterPrefab,
                position = position,
                rotation = rotation,
                summonerBodyObject = summonerBodyObject,
                ignoreTeamMemberLimit = ignoreTeamLimit,
                useAmbientLevel = true,
                propogateMinionOwnership = propagateOwnership,
                enablePrintController = false,
                swarmsHealth = false
            };
            if (team.HasValue) ms.teamIndexOverride = team;
            if (loadout != null) ms.loadout = loadout;
            if (inventoryToCopy != null) ms.inventoryToCopy = inventoryToCopy;

            try
            {
                var master = ms.Perform();
                if (master == null)
                {
                    // Perform() сам пишет LogError, если prefab null или вызов
                    // упёрся в TeamDef.softCharacterLimit (его снимает
                    // ignoreTeamMemberLimit = true); причину дублируем в LastError,
                    // чтобы UI и чат показывали её, а не общее «не удался»
                    LastError = Texts.T(
                        $"MasterSummon.Perform вернул null для {masterPrefab.name}",
                        $"MasterSummon.Perform returned null for {masterPrefab.name}");
                    GameApi.Info(LastError);
                }
                return master;
            }
            catch (Exception e)
            {
                GameApi.Warn($"Summon({masterPrefab.name})", e);
                return null;
            }
        }

        /// <summary>Спавн персонажа рядом с целью; союзники привязываются к ней.</summary>
        public static CharacterMaster SpawnCharacter(GameObject prefab, TeamIndex team, NetworkUser target,
                                                     int ringIndex, int ringTotal)
        {
            var ownerBody = PlayerTargets.BodyOf(target);
            var ownerMaster = PlayerTargets.MasterOf(target);
            var playerTeam = PlayerTargets.TeamOf(target);

            bool isAlly = team == playerTeam;
            float radius = isAlly ? 3.5f : 14f;
            var pos = PlayerTargets.PointOnRing(target, ringIndex, ringTotal, radius);
            if (pos == Vector3.zero) pos = GameApi.SpawnPointNearPlayer(radius, 0.5f);

            var master = Summon(prefab, pos, PlayerTargets.RotationOf(target), team,
                                ownerBody != null ? ownerBody.gameObject : null,
                                true, null, null, isAlly);
            if (master != null && isAlly)
            {
                OwnerBinding.Bind(master, ownerMaster, ownerBody);
            }
            return master;
        }

        private static MethodInfo[] _giveChain;
        private static MethodInfo[] _removeChain;
        private static MethodInfo _stackReader;
        private static bool _stackReaderResolved;

        private static MethodInfo[] ResolveChain(ref MethodInfo[] cache, string[] names)
        {
            if (cache != null) return cache;
            var list = new List<MethodInfo>();
            var t = typeof(Inventory);
            var int32 = typeof(int);
            foreach (var name in names)
            {
                foreach (var arg in new[] { typeof(ItemIndex), typeof(ItemDef) })
                {
                    var mi = t.GetMethod(name, new[] { arg, int32 });
                    if (mi != null && !list.Contains(mi)) list.Add(mi);
                }
            }
            cache = list.ToArray();
            return cache;
        }

        private static MethodInfo[] GiveChain()
        {
            return ResolveChain(ref _giveChain, new[] { "GiveItemPermanent", "GiveItem" });
        }

        private static MethodInfo[] RemoveChain()
        {
            return ResolveChain(ref _removeChain, new[] { "RemoveItem", "RemoveItemPermanent" });
        }

        /// Число стаков предмета у инвентаря (GetItemStack в новых сборках ушёл).
        private static MethodInfo StackReader()
        {
            if (_stackReaderResolved) return _stackReader;
            _stackReaderResolved = true;
            var t = typeof(Inventory);
            foreach (var name in new[] { "GetItemCountTotal", "GetItemStack", "CalculateEffectiveItemStacks" })
            {
                var mi = t.GetMethod(name, new[] { typeof(ItemIndex) });
                if (mi != null && mi.ReturnType == typeof(int)) { _stackReader = mi; break; }
            }
            return _stackReader;
        }

        public static int StackCount(Inventory inv, ItemIndex index)
        {
            var mi = StackReader();
            if (mi == null || inv == null) return 0;
            try { return (int)mi.Invoke(inv, new object[] { index }); }
            catch (Exception e) { GameApi.WarnOnce($"Inventory.{mi.Name}", e); return 0; }
        }

        private static bool InvokeChain(MethodInfo[] chain, Inventory inv, ItemIndex index, int count,
                                        ItemDef def, out Exception last)
        {
            last = null;
            foreach (var mi in chain)
            {
                var ps = mi.GetParameters();
                object arg = ps[0].ParameterType == typeof(ItemDef)
                    ? (object)(def ?? (def = ItemCatalog.GetItemDef(index)))
                    : (object)index;
                if (arg == null) continue;
                try
                {
                    mi.Invoke(inv, new[] { arg, (object)count });
                    return true;
                }
                catch (Exception e)
                {
                    last = e;
                    GameApi.Warn($"Inventory.{mi.Name}", e);
                }
            }
            return false;
        }

        /// Выдать stopку предметов в конкретный инвентарь. Причина отказа - в LastError.
        public static bool GiveItemTo(Inventory inv, ItemIndex index, int count)
        {
            LastError = "";
            if (inv == null) { LastError = Texts.T("у цели нет инвентаря", "target has no inventory"); return false; }
            var chain = GiveChain();
            if (chain.Length == 0)
            {
                LastError = Texts.T("в этой сборке игры нет метода выдачи", "this game build has no give method");
                return false;
            }
            Exception last;
            if (InvokeChain(chain, inv, index, count, null, out last)) return true;
            LastError = last != null ? last.GetType().Name + ": " + last.Message
                                     : Texts.T("метод выдачи отклонил аргументы", "give method rejected arguments");
            return false;
        }

        /// Снять stopку предметов с конкретного инвентаря (зеркало выдачи).
        public static bool RemoveItemFrom(Inventory inv, ItemIndex index, int count)
        {
            LastError = "";
            if (inv == null) { LastError = Texts.T("у цели нет инвентаря", "target has no inventory"); return false; }
            var chain = RemoveChain();
            if (chain.Length == 0)
            {
                LastError = Texts.T("в этой сборке игры нет метода снятия", "this game build has no remove method");
                return false;
            }
            Exception last;
            if (InvokeChain(chain, inv, index, count, null, out last)) return true;
            LastError = last != null ? last.GetType().Name + ": " + last.Message
                                     : Texts.T("метод снятия отклонил аргументы", "remove method rejected arguments");
            return false;
        }

        /// Инвентарь цели: сначала тело, затем master (в разных сборках по-разному).
        public static Inventory InventoryOf(NetworkUser target)
        {
            var body = PlayerTargets.BodyOf(target);
            if (body != null)
            {
                try { if (body.inventory != null) return body.inventory; }
                catch (Exception e) { GameApi.WarnOnce("body.inventory", e); }
            }
            var master = PlayerTargets.MasterOf(target);
            if (master != null)
            {
                try { if (master.inventory != null) return master.inventory; }
                catch (Exception e) { GameApi.WarnOnce("master.inventory", e); }
            }
            return null;
        }

        /// Полная очистка инвентаря цели (зеркало «выдать всё»).
        public static int ClearInventory(Inventory inv)
        {
            if (inv == null) return 0;
            int removed = 0;
            try
            {
                var order = new List<ItemIndex>(inv.itemAcquisitionOrder);
                foreach (var idx in order)
                {
                    int n = StackCount(inv, idx);
                    if (n <= 0) continue;
                    if (RemoveItemFrom(inv, idx, n)) removed++;
                }
            }
            catch (Exception e) { GameApi.Warn("ClearInventory", e); }
            return removed;
        }

        /// <summary>Дроп появляется у цели, а не у того, кто нажал кнопку.</summary>
        public static bool DropPickup(PickupIndex index, int count, NetworkUser target)
        {
            LastError = "";
            if (!index.isValid) { LastError = Texts.T("некорректный индекс пикапа", "invalid pickup index"); return false; }
            if (!NetworkServer.active) return false;

            int spawned = 0;
            SpawnRegistry.ArmDrops(count);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var p = PlayerTargets.PointOnRing(target, i, Math.Max(count, 2), 2.5f);
                        if (p == Vector3.zero) p = GameApi.SpawnPointNearPlayer(2.5f, 0.5f);
                        // 4-аргументный оверлод: isDuplicated=false - дроп создаётся с нуля
                        PickupDropletController.CreatePickupDroplet(new UniquePickup(index), p,
                                                                    Vector3.up * 8f, false);
                        spawned++;
                    }
                    catch (Exception e) { GameApi.Warn("DropPickup", e); break; }
                }
            }
            finally
            {
                SpawnRegistry.DisarmDrops();
            }
            if (spawned == 0) LastError = Texts.T("дроп не создан", "no droplet created");
            return spawned > 0;
        }

        /// <summary>
        /// Зеркало дропа: убрать все лежащие в мире пикапы и капли.
        /// PickupDropletController - капля в полёте, GenericPickupController -
        /// сам предмет на земле; оба - обычные сетевые объекты сцены.
        /// </summary>
        public static int ClearDrops()
        {
            if (!NetworkServer.active) return 0;
            int n = 0;
            try
            {
                foreach (var d in UnityEngine.Object.FindObjectsOfType<PickupDropletController>())
                {
                    if (d == null || d.gameObject == null) continue;
                    NetDestroy(d.gameObject);
                    n++;
                }
            }
            catch (Exception e) { GameApi.Warn("ClearDrops(droplets)", e); }
            try
            {
                foreach (var p in UnityEngine.Object.FindObjectsOfType<GenericPickupController>())
                {
                    if (p == null || p.gameObject == null) continue;
                    NetDestroy(p.gameObject);
                    n++;
                }
            }
            catch (Exception e) { GameApi.Warn("ClearDrops(pickups)", e); }
            return n;
        }

        /// <summary>Убрать дроп в радиусе вокруг цели (режим по умолчанию).</summary>
        public static int ClearDropsNear(NetworkUser target, float radius)
        {
            var origin = PlayerTargets.PositionOf(target);
            if (origin == Vector3.zero) origin = GameApi.SpawnPointNearPlayer(0f, 0f);
            return SpawnRegistry.RemoveDropsNear(origin, radius);
        }

        /// <summary>
        /// Повторяет InteractableSpawnCard.Spawn (RID 16240): Instantiate ->
        /// прижать к полу, если orientToFloor -> NetworkServer.Spawn. Случайный
        /// доворот 0..360 и покачивание +/-30 градусов пропущены: они там нужны
        /// только потому, что у карты есть RNG.
        /// </summary>
        public static GameObject SpawnInteractable(SpawnCard card, Vector3 position)
        {
            LastError = "";
            if (card == null || card.prefab == null) { LastError = Texts.T("у карты нет префаба", "card has no prefab"); return null; }
            if (!NetworkServer.active) return null;

            try
            {
                var go = UnityEngine.Object.Instantiate(card.prefab, position, Quaternion.identity);
                var tr = go.transform;

                var isc = card as InteractableSpawnCard;
                if (isc != null && isc.orientToFloor)
                {
                    RaycastHit hit;
                    if (Physics.Raycast(position + Vector3.up * 0.5f, Vector3.down, out hit, 40f,
                                        LayerIndex.world.mask))
                    {
                        tr.position = hit.point;
                    }
                    tr.up = Vector3.up;
                }

                NetworkServer.Spawn(go);
                return go;
            }
            catch (Exception e)
            {
                GameApi.Warn($"SpawnInteractable({card.name})", e);
                LastError = e.GetType().Name;
                return null;
            }
        }

        /// <summary>
        /// Полное удаление персонажа: тело (модель) и master вместе. Уничтожение
        /// одного master'а оставляло осиротевшее тело - висящую модель без логики.
        /// На сервере - NetworkServer.Destroy, чтобы удаление доехало до клиентов.
        /// </summary>
        public static void DestroyCharacter(CharacterMaster master)
        {
            if (master == null) return;
            CharacterBody body = null;
            try { body = master.GetBody(); }
            catch (Exception e) { GameApi.WarnOnce("GetBody on remove", e); }
            if (body == null)
            {
                // запасной путь: тело без компонента, но с GameObject
                try
                {
                    var go = master.GetBodyObject();
                    if (go != null) body = go.GetComponent<CharacterBody>();
                }
                catch (Exception e) { GameApi.WarnOnce("GetBodyObject on remove", e); }
            }

            if (body != null && body.gameObject != null) NetDestroy(body.gameObject);
            if (master.gameObject != null) NetDestroy(master.gameObject);
        }

        /// <summary>
        /// Очередь добивания: объект помечается на удаление и, если пережил
        /// первую попытку (NetworkServer.Destroy молча отказывается от объектов
        /// без NetworkIdentity, отложенный Destroy мог не дойти), доубивается
        /// на следующем тике уже безусловно. Каждая такая retry-ситуация
        /// оставляет строку в предупреждениях вкладки «Инфо» и в логе.
        /// </summary>
        private static readonly List<GameObject> _killQueue = new List<GameObject>();

        public static void NetDestroy(GameObject go)
        {
            if (go == null) return;
            _killQueue.Add(go);
            DestroyNow(go);
        }

        private static void DestroyNow(GameObject go)
        {
            try
            {
                // сетевой объект убираем штатно (клиенты получат удаление),
                // несетевой - просто уничтожаем, иначе HLAPI молча пропустит
                if (NetworkServer.active && go.GetComponent<NetworkIdentity>() != null)
                {
                    NetworkServer.Destroy(go);
                }
                else
                {
                    UnityEngine.Object.Destroy(go);
                }
            }
            catch (Exception e) { GameApi.Warn("NetDestroy", e); }
        }

        /// <summary>
        /// Вызывается из Plugin.Update раз в 0.5 с: добить выживших. За один
        /// проход в лог уходит не больше пяти персональных предупреждений,
        /// остальное сводится в одну строку - массовый отказ не спамит.
        /// </summary>
        public static void SweepKillQueue()
        {
            if (_killQueue.Count == 0) return;
            int warned = 0, total = 0;
            for (int i = _killQueue.Count - 1; i >= 0; i--)
            {
                var go = _killQueue[i];
                _killQueue.RemoveAt(i);
                if (go == null) continue;
                total++;
                if (warned < 5)
                {
                    warned++;
                    GameApi.Warn("KillQueue",
                        new InvalidOperationException("object survived destroy: " + go.name));
                }
                DestroyNow(go);
                UnityEngine.Object.Destroy(go);
            }
            if (total > 5)
            {
                GameApi.Warn("KillQueue",
                    new InvalidOperationException($"and {total - 5} more objects survived destroy"));
            }
        }

        /// <summary>
        /// Чистка осиротевших моделей: тела без живого master'а (наследие старых
        /// версий плагина и любых иных путей, где master умер первым). Игрок не
        /// трогается: его тело пересоздаётся игрой и master'а может не быть миг.
        /// Вызывается после массовых удалений и кнопкой во вкладке «Инфо».
        /// </summary>
        public static int CleanupOrphanBodies()
        {
            if (!NetworkServer.active) return 0;
            int n = 0;
            try
            {
                foreach (var body in UnityEngine.Object.FindObjectsOfType<CharacterBody>())
                {
                    if (body == null || body.gameObject == null) continue;
                    if (body.isPlayerControlled) continue;
                    var m = body.master;
                    if (m != null && m.gameObject != null) continue;
                    NetDestroy(body.gameObject);
                    n++;
                }
            }
            catch (Exception e) { GameApi.Warn("CleanupOrphanBodies", e); }
            return n;
        }
    }
}

