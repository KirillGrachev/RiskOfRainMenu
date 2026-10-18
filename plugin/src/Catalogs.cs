using System;
using System.Collections.Generic;
using RoR2;
using UnityEngine;

namespace SandboxMenu
{
    /// <summary>
    /// Кэшированные списки для меню. Строятся один раз за сессию, а список
    /// интерактивов - на каждой новой стадии (он зависит отClassicStageInfo).
    ///
    /// Важно: ItemCatalog.allItemDefs возвращает HG.ReadOnlyArray из сборки
    /// HGCSharpUtils, которой нет среди доступных для компиляции, поэтому
    /// предметы перечисляются через itemCount + GetItemDef((ItemIndex)i).
    /// MasterCatalog.MasterIndex тоже не конструируется вручную - там, где
    /// нужен индекс, он передаётся сквозняком из FindAiMasterIndexForBody.
    /// </summary>
    internal static class Catalogs
    {
        /// <summary>Запись списка персонажей: имя префаба, сам префаб, признак «союзный».</summary>
        public class MasterInfo
        {
            /// <summary>Имя префаба как в MasterCatalog.</summary>
            public string Name;
            /// <summary>Префаб master'а для MasterSummon.</summary>
            public GameObject Prefab;
            /// <summary>Похож на союзника (дрон, турель, жук) по имени префаба.</summary>
            public bool AllyLike;
        }

        /// <summary>Запись списка предметов: имя, топон, def, индексы, скрытость.</summary>
        public class ItemInfo
        {
            /// <summary>Локализованное имя для меню.</summary>
            public string Name;
            /// <summary>Токен локализации (по нему тоже ищет поиск).</summary>
            public string Token;
            /// <summary>Определение предмета из ItemCatalog.</summary>
            public ItemDef Def;
            /// <summary>Индекс предмета для выдачи/снятия.</summary>
            public ItemIndex Index;
            /// <summary>Пикап предмета для дропа (может быть невалидным).</summary>
            public PickupIndex Pickup;
            /// <summary>Служебный/скрытый предмет.</summary>
            public bool Hidden;
        }

        /// <summary>Запись списка пикапов: имя и индекс.</summary>
        public class PickupInfo
        {
            /// <summary>Локализованное имя для меню.</summary>
            public string Name;
            /// <summary>Индекс пикапа для дропа.</summary>
            public PickupIndex Index;
        }

        /// <summary>Запись списка интерактивов: имя префаба и карта спавна.</summary>
        public class InteractableInfo
        {
            /// <summary>Имя префаба объекта.</summary>
            public string Name;
            /// <summary>Карта спавна текущей сцены.</summary>
            public SpawnCard Card;
        }

        /// <summary>Все мастер-префабы игры, отсортированы по имени.</summary>
        public static readonly List<MasterInfo> Masters = new List<MasterInfo>();
        /// <summary>Все предметы ItemCatalog, отсортированы по имени.</summary>
        public static readonly List<ItemInfo> Items = new List<ItemInfo>();
        /// <summary>Все пикапы PickupCatalog, отсортированы по имени.</summary>
        public static readonly List<PickupInfo> Pickups = new List<PickupInfo>();
        /// <summary>Интерактивы текущей сцены, отсортированы по имени.</summary>
        public static readonly List<InteractableInfo> Interactables = new List<InteractableInfo>();

        /// <summary>Каталоги построены хотя бы раз за сессию.</summary>
        public static bool Built;
        /// <summary>Сцена, для которой построен список интерактивов.</summary>
        public static Stage CachedStage;

        private static readonly string[] AllyKeywords =
        {
            "drone", "turret", "beetle", "ally", "squid", "roboball", "parent",
            "ward", "minion", "totem", "gummy", "mimic", "poltergeist", "wisp",
            "echo", "helper", "follower", "construct"
        };

        /// <summary>Полный сброс кэшей (вызов при старте плагина).</summary>
        public static void Reset()
        {
            Masters.Clear();
            Items.Clear();
            Pickups.Clear();
            Interactables.Clear();
            Built = false;
            CachedStage = null;
        }

        /// <summary>
        /// Имя пригодно для показа: не пустое, не «???» и не сырой токен языка
        /// (вида ITEM_... / EQUIPMENT_..._NAME). Такие записи уходят в конец
        /// списка, чтобы рабочие имена были сверху.
        /// </summary>
        private static int NameRank(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "???") return 1;
            bool tokenLike = name.IndexOf('_') >= 0 && name == name.ToUpperInvariant();
            return tokenLike ? 1 : 0;
        }

        /// <summary>Сравнение имён: пригодные первыми, остальные в конце.</summary>
        private static int CompareByName(string a, string b)
        {
            int ra = NameRank(a);
            int rb = NameRank(b);
            if (ra != rb) return ra - rb;
            return string.CompareOrdinal(a, b);
        }

        /// <summary>Построить каталоги, если ещё не построены, и освежить интерактивы.</summary>
        public static void EnsureBuilt()
        {
            if (!Built) BuildAll();
            RefreshInteractables();
        }

        /// <summary>Пересобрать все каталоги заново.</summary>
        public static void BuildAll()
        {
            Reset();
            BuildMasters();
            BuildItems();
            BuildPickups();
            RefreshInteractables();
            Built = true;
            GameApi.Info($"catalogs built: masters={Masters.Count} items={Items.Count} " +
                         $"pickups={Pickups.Count} interactables={Interactables.Count}");
        }

        private static void BuildMasters()
        {
            try
            {
                foreach (var m in MasterCatalog.allMasters)
                {
                    if (m == null) continue;
                    var go = m.gameObject;
                    if (go == null) continue;
                    var name = go.name;
                    bool ally = false;
                    foreach (var k in AllyKeywords)
                    {
                        if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { ally = true; break; }
                    }
                    Masters.Add(new MasterInfo { Name = name, Prefab = go, AllyLike = ally });
                }
                Masters.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildMasters", e); }
        }

        private static void BuildItems()
        {
            try
            {
                int count = ItemCatalog.itemCount;
                for (int i = 1; i <= count; i++)
                {
                    var idx = (ItemIndex)i;
                    ItemDef def = ItemCatalog.GetItemDef(idx);
                    if (def == null) continue;
                    string token = def.nameToken;
                    PickupIndex pidx = PickupIndex.none;
                    try { pidx = PickupCatalog.FindPickupIndex(idx); } catch { }
                    Items.Add(new ItemInfo
                    {
                        Name = GameApi.Localize(token),
                        Token = token,
                        Def = def,
                        Index = idx,
                        Pickup = pidx,
                        Hidden = def.hidden
                    });
                }
                Items.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildItems", e); }
        }

        private static void BuildPickups()
        {
            try
            {
                foreach (var def in PickupCatalog.allPickups)
                {
                    if (def == null) continue;
                    var pidx = def.pickupIndex;
                    if (!pidx.isValid) continue;
                    string label = GameApi.Localize(def.nameToken);
                    if (string.IsNullOrEmpty(label)) label = def.internalName;
                    Pickups.Add(new PickupInfo { Name = label, Index = pidx });
                }
                Pickups.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildPickups", e); }
        }

        /// Интерактивы берём из текущей стадии: их набор зависит от сцены.
        public static void RefreshInteractables()
        {
            try
            {
                var stage = Stage.instance;
                var csi = ClassicStageInfo.instance;
                if (csi == null)
                {
                    if (Interactables.Count != 0) Interactables.Clear();
                    return;
                }
                if (CachedStage == stage && Interactables.Count > 0) return;
                CachedStage = stage;
                Interactables.Clear();

                var cards = csi.interactableCards;
                if (cards == null) return;
                var seen = new HashSet<string>();
                foreach (var card in cards)
                {
                    SpawnCard sc = card.GetSpawnCard();
                    if (sc == null || sc.prefab == null) continue;
                    var name = sc.prefab.name;
                    if (!seen.Add(name)) continue;
                    Interactables.Add(new InteractableInfo { Name = name, Card = sc });
                }
                Interactables.Sort((x, y) => string.CompareOrdinal(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("RefreshInteractables", e); }
        }
    }
}

