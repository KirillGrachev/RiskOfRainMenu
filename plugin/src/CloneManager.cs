using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;
using UnityEngine.Networking;

namespace SandboxMenu
{
    /// <summary>
    /// Постоянные клоны игрока.
    ///
    /// Как игра сама делает копию персонажа - RoR2.MasterCopySpawnCard.
    /// CopyDataFromMaster, RID 16350, хвост метода:
    ///
    ///     IL_00f1  ldfld  RoR2.CharacterBody::bodyIndex
    ///     IL_00f7  call   RoR2.MasterCatalog::FindAiMasterIndexForBody
    ///     IL_00fc  call   RoR2.MasterCatalog::GetMasterPrefab
    ///     IL_0101  stfld  RoR2.SpawnCard::prefab
    ///
    /// то есть копия строится на AI-master'е того же тела. Это важно: обычный
    /// «игровой» master выжившего не имеет AI-контроллера, и такой клон просто
    /// стоял бы на месте. AI-master - та же модель и скиллы, но с BaseAI.
    ///
    /// Дальше клон собирается через MasterSummon:
    ///   loadout          - Loadout.Copy, поэтому скины и варианты скиллов те же;
    ///   inventoryToCopy  - клон дерётся набором предметов владельца;
    ///   ignoreTeamMemberLimit = true - ОБЯЗАТЕЛЬНО: в Perform (RID 32658) на
    ///       IL_0086 читается TeamDef.softCharacterLimit и при превышении метод
    ///       молча возвращает null (IL_009a), без всякой ошибки;
    ///   propogateMinionOwnership = true + OwnerBinding.Bind - клон принадлежит
    ///       тому игроку, для которого создан, а не всей группе.
    ///
    /// Эффекты копируются У ВЛАДЕЛЬЦА, а не у того, кто открыл меню: после спавна
    /// инвентарь клона досинхронизируется с инвентарём master'а владельца
    /// (CopyOwnerEffects), поэтому в кооперативе клон второго игрока дерётся
    /// предметами второго игрока, даже когда меню открыто у хоста.
    ///
    /// Учёт пары master+тело (Entry) принципиален: уничтожение только master'а
    /// оставляло в сцене осиротевшее тело - висящую модель без логики.
    ///
    /// Количество хранится отдельно на каждого игрока: в кооперативе у каждого
    /// своя группа клонов, и слайдер в меню правит число для выбранного игрока.
    /// </summary>
    internal static class CloneManager
    {
        /// <summary>Живой клон: master и его тело модели.</summary>
        private class Entry
        {
            public CharacterMaster Master;
            public CharacterBody Body;
        }

        /// <summary>Группа клонов одного игрока.</summary>
        private class Group
        {
            public NetworkUser Owner;
            public CharacterMaster OwnerMaster;
            public CharacterBody OwnerBody;
            public int Desired;
            public readonly List<Entry> Alive = new List<Entry>();
            public bool CopyInventory = true;
            public bool CopyLoadout = true;
            public bool Follow = true;
            public float NextSpawnAt;
            public float LastSyncAt;
        }

        private static readonly List<Group> _groups = new List<Group>();

        /// <summary>Поддерживать численность: погибшие клоны возвращаются.</summary>
        public static bool KeepAlive = true;

        /// <summary>Имя префаба последнего спавна - для строки состояния меню.</summary>
        public static string LastPrefabName = "";

        /// <summary>Последняя ошибка спавна - для строки состояния меню.</summary>
        public static string LastError = "";

        /// <summary>Потолок численности на одного игрока: настройка Clone.MaxClones.</summary>
        public static int MaxClones
        {
            get
            {
                var c = SandboxMenuPlugin.CloneMaxCount;
                int v = c != null ? c.Value : 20;
                return v < 1 ? 20 : v;
            }
        }

        // ------------------------------------------------------- per target --
        /// <summary>Группа владельца; create=false возвращает null, если её нет.</summary>
        private static Group GroupFor(NetworkUser owner, bool create)
        {
            if (owner == null) return null;
            foreach (var g in _groups)
            {
                if (ReferenceEquals(g.Owner, owner)) return g;
            }
            if (!create) return null;
            var ng = new Group { Owner = owner };
            var cfg = SandboxMenuPlugin.CloneFollowDefault;
            ng.Follow = cfg == null || cfg.Value;
            _groups.Add(ng);
            return ng;
        }

        /// <summary>Желаемая численность для игрока.</summary>
        public static int DesiredFor(NetworkUser owner)
        {
            var g = GroupFor(owner, false);
            return g != null ? g.Desired : 0;
        }

        /// <summary>Сколько клонов игрока живы сейчас.</summary>
        public static int AliveFor(NetworkUser owner)
        {
            var g = GroupFor(owner, false);
            if (g == null) return 0;
            Prune(g);
            return g.Alive.Count;
        }

        /// <summary>Суммарная живая численность по всем игрокам.</summary>
        public static int AliveTotal
        {
            get
            {
                int n = 0;
                foreach (var g in _groups) { Prune(g); n += g.Alive.Count; }
                return n;
            }
        }

        /// <summary>Копировать предметы владельца в клонов.</summary>
        public static bool CopyInventoryFor(NetworkUser owner)
        {
            var g = GroupFor(owner, false);
            return g == null || g.CopyInventory;
        }

        /// <summary>Копировать loadout владельца (скины, варианты скиллов).</summary>
        public static bool CopyLoadoutFor(NetworkUser owner)
        {
            var g = GroupFor(owner, false);
            return g == null || g.CopyLoadout;
        }

        /// <summary>Следуют ли клоны игрока за ним.</summary>
        public static bool FollowFor(NetworkUser owner)
        {
            var g = GroupFor(owner, false);
            return g == null || g.Follow;
        }

        /// <summary>Применить флаги группы одним вызовом (для меню и сети).</summary>
        public static void SetFlagsFor(NetworkUser owner, bool? copyInventory, bool? copyLoadout, bool? follow)
        {
            var g = GroupFor(owner, true);
            if (g == null) return;
            if (copyInventory.HasValue) g.CopyInventory = copyInventory.Value;
            if (copyLoadout.HasValue) g.CopyLoadout = copyLoadout.Value;
            if (follow.HasValue) g.Follow = follow.Value;
        }

        /// <summary>Задать желаемую численность; 0 убирает группу.</summary>
        public static void SetDesiredFor(NetworkUser owner, int value)
        {
            value = Mathf.Clamp(value, 0, MaxClones);
            var g = GroupFor(owner, true);
            if (g == null) { LastError = Texts.T("не выбран игрок", "no target player selected"); return; }
            g.Desired = value;
            if (value == 0) ClearGroup(g, false);
        }

        /// <summary>Полный сброс состояния (вызов при старте плагина).</summary>
        public static void Reset()
        {
            foreach (var g in _groups) ClearGroup(g, false);
            _groups.Clear();
            LastPrefabName = "";
            LastError = "";
        }

        /// <summary>
        /// Убирает мёртвых и осиротевших: если master умер, а тело осталось -
        /// тело уничтожается здесь же, иначе модель висела бы в сцене навсегда.
        /// Попутно подтягивает ссылку на тело, пока оно ещё не родилось.
        /// </summary>
        private static void Prune(Group g)
        {
            for (int i = g.Alive.Count - 1; i >= 0; i--)
            {
                var e = g.Alive[i];
                bool masterGone = e.Master == null || e.Master.gameObject == null;
                bool bodyGone = e.Body == null || e.Body.gameObject == null;

                if (masterGone && bodyGone) { g.Alive.RemoveAt(i); continue; }

                if (masterGone)
                {
                    // тело без master'а - мертвец: доубираем модель
                    Spawning.NetDestroy(e.Body.gameObject);
                    g.Alive.RemoveAt(i);
                    continue;
                }

                if (bodyGone)
                {
                    try { e.Body = e.Master.GetBody(); }
                    catch (Exception ex) { GameApi.WarnOnce("CharacterMaster.GetBody", ex); }
                }
            }
        }

        // ------------------------------------------------------------ maintain
        /// <summary>Вызывается из Plugin.Update раз в 0.5 с.</summary>
        public static void Maintain()
        {
            if (!NetworkServer.active) return;

            for (int i = _groups.Count - 1; i >= 0; i--)
            {
                var g = _groups[i];
                if (g.Owner == null)
                {
                    // игрок вышел - его клонов убираем, чтобы не висели ничьи
                    ClearGroup(g, false);
                    _groups.RemoveAt(i);
                    continue;
                }
                Prune(g);
                RefreshOwnerRefs(g);

                // следование: лидер AI сверяется с текущим телом владельца.
                // После смены стадии или респауна тело владельца - новый объект,
                // и без перепривязки клоны оставались бы на старом месте.
                if (g.OwnerBody != null)
                {
                    foreach (var e in g.Alive)
                    {
                        OwnerBinding.EnsureLeader(e.Master, g.OwnerBody, g.Follow);
                    }
                }

                if (!KeepAlive) continue;
                if (g.Desired <= 0) continue;
                if (g.OwnerBody == null || g.OwnerMaster == null) continue;

                while (g.Alive.Count > g.Desired)
                {
                    var last = g.Alive[g.Alive.Count - 1];
                    g.Alive.RemoveAt(g.Alive.Count - 1);
                    Spawning.DestroyCharacter(last.Master);
                }

                if (g.Alive.Count >= g.Desired) continue;
                if (Time.unscaledTime < g.NextSpawnAt) continue;

                var clone = SpawnOne(g);
                if (clone != null) g.Alive.Add(clone);
                else g.NextSpawnAt = Time.unscaledTime + Mathf.Max(1f, RespawnDelay());

                // эффекты владельца досинхронизируются периодически: предметы,
                // взятые владельцем после спавна, доедают до клонов (минус 9)
                if (g.CopyInventory && g.OwnerMaster != null &&
                    Time.unscaledTime - g.LastSyncAt > 4f)
                {
                    g.LastSyncAt = Time.unscaledTime;
                    foreach (var e in g.Alive) SyncInventory(e.Master, g.OwnerMaster);
                }
            }
        }

        /// <summary>
        /// Освежает тело/master владельца: после смены стадии или респауна тело -
        /// новый объект, ссылки группы должны указывать на актуальный.
        /// </summary>
        private static void RefreshOwnerRefs(Group g)
        {
            try
            {
                if (g.Owner == null) return;
                var b = g.Owner.GetCurrentBody();
                if (b != null && !ReferenceEquals(b, g.OwnerBody))
                {
                    g.OwnerBody = b;
                }
                var m = g.Owner.master;
                if (m != null) g.OwnerMaster = m;
                else if (b != null) g.OwnerMaster = b.master;
            }
            catch (Exception e) { GameApi.WarnOnce("CloneManager.RefreshOwnerRefs", e); }
        }

        /// <summary>Пауза между попытками спавна после неудачи (секунды).</summary>
        private static float RespawnDelay()
        {
            var cfg = SandboxMenuPlugin.CloneRespawnDelay;
            return cfg != null ? Mathf.Max(0f, cfg.Value) : 5f;
        }

        // ------------------------------------------------------------- manual --
        /// <summary>
        /// Мгновенный спавн для игрока. Кнопки честно поднимают Desired, поэтому
        /// слайдер, автореспавн и ручной спавн не спорят друг с другом (раньше
        /// SpawnNow добавлял клона сверх Desired, и следующий тик его сносил).
        /// </summary>
        public static void SpawnNow(NetworkUser owner, int count)
        {
            var g = GroupFor(owner, true);
            if (g == null) { GameApi.SayErr(Texts.T("клоны: не выбран игрок", "clones: no target player")); return; }
            RefreshOwnerRefs(g);
            if (g.OwnerBody == null || g.OwnerMaster == null)
            {
                g.OwnerBody = PlayerTargets.BodyOf(owner);
                g.OwnerMaster = PlayerTargets.MasterOf(owner);
            }
            if (g.OwnerBody == null || g.OwnerMaster == null)
            {
                GameApi.SayErr(Texts.T("клоны: у цели нет тела (нужен забег)", "clones: target has no body (enter a run)"));
                return;
            }

            int ok = 0;
            for (int i = 0; i < count; i++)
            {
                var c = SpawnOne(g);
                if (c == null) break;
                g.Alive.Add(c);
                ok++;
            }
            g.Desired = Mathf.Clamp(g.Desired + ok, 0, MaxClones);

            if (ok > 0)
            {
                GameApi.SayOk(Texts.T(
                    $"клоны: +{ok} для {PlayerTargets.DisplayName(owner)}, в строю {g.Alive.Count}",
                    $"clones: +{ok} for {PlayerTargets.DisplayName(owner)}, alive {g.Alive.Count}"));
            }
            else
            {
                GameApi.SayErr(Texts.T("клоны: спавн не удался", "clones: spawn failed") +
                               (LastError.Length > 0 ? ": " + LastError : ""));
            }
        }

        /// <summary>Убрать всех клонов игрока (тело и master уничтожаются).</summary>
        public static void ClearFor(NetworkUser owner)
        {
            var g = GroupFor(owner, false);
            if (g == null) return;
            g.Desired = 0;
            ClearGroup(g, true);
        }

        /// <summary>Убрать всех клонов всех игроков.</summary>
        public static void ClearAll()
        {
            foreach (var g in _groups)
            {
                g.Desired = 0;
                ClearGroup(g, false);
            }
            _groups.Clear();
            OwnerBinding.Clear();
            Spawning.CleanupOrphanBodies();
            int debuffs = SpawnRegistry.CleanseDebuffs();
            GameApi.SayOk(Texts.T(
                $"клоны: убраны все клоны всех игроков, дебафов снято: {debuffs}",
                $"clones: removed every player's clones, debuffs cleansed: {debuffs}"));
        }

        /// <summary>Уничтожить группу целиком: каждая пара тело+master.</summary>
        private static void ClearGroup(Group g, bool announce)
        {
            foreach (var e in g.Alive) Spawning.DestroyCharacter(e.Master);
            g.Alive.Clear();
            if (announce) GameApi.SayOk(Texts.T("клоны: убраны клоны игрока", "clones: removed for this player"));
        }

        // ------------------------------------------------------------- spawn --
        /// <summary>
        /// Префаб клона: игровой путь (AI-master тела), запасной - master игрока
        /// по имени (без AI: клон может не двигаться, о чём меню скажет отдельно).
        /// </summary>
        public static GameObject FindClonePrefab(CharacterMaster sourceMaster, CharacterBody body)
        {
            LastError = "";

            try
            {
                if (body != null)
                {
                    var aiIndex = MasterCatalog.FindAiMasterIndexForBody(body.bodyIndex);
                    if (aiIndex.isValid)
                    {
                        var prefab = MasterCatalog.GetMasterPrefab(aiIndex);
                        if (prefab != null)
                        {
                            LastPrefabName = prefab.name;
                            return prefab;
                        }
                    }
                }
            }
            catch (Exception e) { GameApi.Warn("FindAiMasterIndexForBody", e); }

            try
            {
                if (sourceMaster != null && sourceMaster.gameObject != null)
                {
                    var name = sourceMaster.gameObject.name;
                    int cut = name.IndexOf("(Clone)", StringComparison.Ordinal);
                    if (cut > 0) name = name.Substring(0, cut).Trim();
                    var prefab = MasterCatalog.FindMasterPrefab(name);
                    if (prefab != null)
                    {
                        LastPrefabName = prefab.name;
                        LastError = Texts.T(
                            "для этого тела нет AI-master'а: клон без AI может не двигаться",
                            "no AI master for this body: the clone may not move");
                        return prefab;
                    }
                }
            }
            catch (Exception e) { GameApi.Warn("FindMasterPrefab", e); }

            LastError = Texts.T("не найден master-префаб персонажа", "could not resolve a master prefab");
            return null;
        }

        /// <summary>Создать одного клона группы; null при ошибке (см. LastError).</summary>
        private static Entry SpawnOne(Group g)
        {
            var src = g.OwnerMaster;
            var body = g.OwnerBody;
            if (src == null || body == null)
            {
                LastError = Texts.T("у цели нет тела/master'а (нужен забег)", "target has no body/master (enter a run)");
                return null;
            }

            var prefab = FindClonePrefab(src, body);
            if (prefab == null) return null;

            Loadout loadout = null;
            try
            {
                if (g.CopyLoadout && src.loadout != null)
                {
                    loadout = new Loadout();
                    src.loadout.Copy(loadout);
                }
            }
            catch (Exception e) { GameApi.Warn("Loadout.Copy", e); loadout = null; }

            Inventory inv = null;
            try { if (g.CopyInventory) inv = src.inventory; }
            catch (Exception e) { GameApi.Warn("CharacterMaster.inventory", e); }

            TeamIndex team = TeamIndex.Player;
            try
            {
                var tc = body.teamComponent;
                if (tc != null) team = tc.teamIndex;
            }
            catch (Exception e) { GameApi.WarnOnce("teamComponent", e); }

            var pos = PlayerTargets.PointOnRing(g.Owner, g.Alive.Count + 1, g.Desired + 2, 3.5f);
            if (pos == Vector3.zero)
            {
                var t = body.coreTransform;
                pos = (t != null ? t.position : body.footPosition) + Vector3.up;
            }

            var master = Spawning.Summon(prefab, pos, PlayerTargets.RotationOf(g.Owner), team,
                                         body.gameObject, true, loadout, inv, true);
            if (master == null) return null;

            // эффекты владельца доедают после спавна: inventoryToCopy может не
            // покрыть разницу, а клон обязан драться предметами своего хозяина
            if (g.CopyInventory) CopyOwnerEffects(master, src);

            CharacterBody cloneBody = null;
            try { cloneBody = master.GetBody(); }
            catch (Exception e) { GameApi.WarnOnce("GetBody on spawn", e); }
            var entry = new Entry { Master = master, Body = cloneBody };
            OwnerBinding.Bind(master, src, body);
            OwnerBinding.EnsureLeader(master, body, g.Follow);
            return entry;
        }

        /// <summary>
        /// Точная синхронизация инвентаря и слотов оборудования клона с master'ом
        /// владельца: недостающие стаки додаются, лишние снимаются, оборудование
        /// копируется по слотам (ранее оборудование доверялось одному
        /// inventoryToCopy без двойной страховки).
        /// </summary>
        private static void SyncInventory(CharacterMaster cloneMaster, CharacterMaster ownerMaster)
        {
            try
            {
                var srcInv = ownerMaster != null ? ownerMaster.inventory : null;
                var dstInv = cloneMaster != null ? cloneMaster.inventory : null;
                if (srcInv == null || dstInv == null) return;

                var srcOrder = new List<ItemIndex>(srcInv.itemAcquisitionOrder);
                var dstOrder = new List<ItemIndex>(dstInv.itemAcquisitionOrder);

                foreach (var idx in srcOrder)
                {
                    int have = Spawning.StackCount(dstInv, idx);
                    int want = Spawning.StackCount(srcInv, idx);
                    if (want > have) Spawning.GiveItemTo(dstInv, idx, want - have);
                    else if (have > want) Spawning.RemoveItemFrom(dstInv, idx, have - want);
                }
                foreach (var idx in dstOrder)
                {
                    if (srcOrder.Contains(idx)) continue;
                    int have = Spawning.StackCount(dstInv, idx);
                    if (have > 0) Spawning.RemoveItemFrom(dstInv, idx, have);
                }

                // слоты оборудования копируем_compat-API: игра сама помечает её
                // "For Mod Compat Only", то есть это штатный путь для модов;
                // pragma гасит obsolete-ворнинг локально и осознанно
                int slots = srcInv.GetEquipmentSlotCount();
#pragma warning disable 618
                for (int i = 0; i < slots; i++)
                {
                    var es = srcInv.GetEquipment((uint)i);
                    dstInv.SetEquipment(es, (uint)i);
                }
#pragma warning restore 618
            }
            catch (Exception e) { GameApi.WarnOnce("SyncInventory", e); }
        }

        /// <summary>
        /// Досинхронизировать инвентарь клона с инвентарём master'а владельца:
        /// недостающие стаки додаются цепочкой выдачи. Благодаря этому клон
        /// конкретного игрока всегда носит ЕГО предметы, а не предметы хоста,
        /// независимо от того, кто открыл меню и кто нажал кнопку.
        /// </summary>
        private static void CopyOwnerEffects(CharacterMaster cloneMaster, CharacterMaster ownerMaster)
        {
            try
            {
                var srcInv = ownerMaster != null ? ownerMaster.inventory : null;
                var dstInv = cloneMaster != null ? cloneMaster.inventory : null;
                if (srcInv == null || dstInv == null) return;

                var order = new List<ItemIndex>(srcInv.itemAcquisitionOrder);
                foreach (var idx in order)
                {
                    int have = Spawning.StackCount(dstInv, idx);
                    int want = Spawning.StackCount(srcInv, idx);
                    int diff = want - have;
                    if (diff > 0) Spawning.GiveItemTo(dstInv, idx, diff);
                }
            }
            catch (Exception e) { GameApi.WarnOnce("CopyOwnerEffects", e); }
        }
    }
}