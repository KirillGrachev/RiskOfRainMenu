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

        /// <summary>Запись списка бафов: имя, индекс, определение.</summary>
        public class BuffInfo
        {
            /// <summary>Внутреннее имя бафа (BuffDef.name - токена локализации у бафов нет).</summary>
            public string Name;
            /// <summary>Индекс бафа для выдачи/снятия.</summary>
            public BuffIndex Index;
            /// <summary>Определение бафа (canStack и пр.).</summary>
            public BuffDef Def;
        }

        /// <summary>Запись списка стадий: показываемое имя и внутреннее имя сцены.</summary>
        public class StageInfo
        {
            /// <summary>Локализованное имя для меню.</summary>
            public string Name;
            /// <summary>Внутреннее имя сцены (SceneDef.cachedName) - стабильный ключ телепорта.</summary>
            public string SceneName;
        }

        /// <summary>Запись списка артефактов: имя и индекс.</summary>
        public class ArtifactInfo
        {
            /// <summary>Локализованное имя для меню.</summary>
            public string Name;
            /// <summary>Индекс артефакта для включения/выключения.</summary>
            public ArtifactIndex Index;
        }

        /// <summary>Запись списка выживших: имя и имя префаба тела.</summary>
        public class SurvivorInfo
        {
            /// <summary>Локализованное имя для меню.</summary>
            public string Name;
            /// <summary>Имя префаба тела - ключ CharacterMaster.Respawn.</summary>
            public string BodyName;
        }

        /// <summary>Все мастер-префабы игры, отсортированы по имени.</summary>
        public static readonly List<MasterInfo> Masters = new List<MasterInfo>();
        /// <summary>Все предметы ItemCatalog, отсортированы по имени.</summary>
        public static readonly List<ItemInfo> Items = new List<ItemInfo>();
        /// <summary>Все пикапы PickupCatalog, отсортированы по имени.</summary>
        public static readonly List<PickupInfo> Pickups = new List<PickupInfo>();
        /// <summary>Интерактивы текущей сцены, отсортированы по имени.</summary>
        public static readonly List<InteractableInfo> Interactables = new List<InteractableInfo>();
        /// <summary>Видимые бафы BuffCatalog, отсортированы по имени.</summary>
        public static readonly List<BuffInfo> Buffs = new List<BuffInfo>();
        /// <summary>Стадийные сцены SceneCatalog, отсортированы по имени.</summary>
        public static readonly List<StageInfo> Stages = new List<StageInfo>();
        /// <summary>Все артефакты ArtifactCatalog, отсортированы по имени.</summary>
        public static readonly List<ArtifactInfo> Artifacts = new List<ArtifactInfo>();
        /// <summary>Все выжившие SurvivorCatalog, отсортированы по имени.</summary>
        public static readonly List<SurvivorInfo> Survivors = new List<SurvivorInfo>();

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
            Buffs.Clear();
            Stages.Clear();
            Artifacts.Clear();
            Survivors.Clear();
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
            // сырое имя перечисления (ItemIndex.Karma, EquipmentIndex.X) - это
            // нерезолвленный internalName: такой «мусор» уходит в конец списка
            if (name.IndexOf('.') >= 0) return 1;
            bool tokenLike = name.IndexOf('_') >= 0 && name == name.ToUpperInvariant();
            return tokenLike ? 1 : 0;
        }

        /// <summary>Сравнение имён: пригодные первыми, остальные в конце.</summary>
        private static int CompareByName(string leftName, string rightName)
        {
            int leftRank = NameRank(leftName);
            int rightRank = NameRank(rightName);
            if (leftRank != rightRank) return leftRank - rightRank;
            return string.CompareOrdinal(leftName, rightName);
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
            BuildBuffs();
            BuildStages();
            BuildArtifacts();
            BuildSurvivors();
            RefreshInteractables();
            Built = true;
            GameApi.Info($"catalogs built: masters={Masters.Count} items={Items.Count} " +
                         $"pickups={Pickups.Count} interactables={Interactables.Count} buffs={Buffs.Count} " +
                         $"stages={Stages.Count} artifacts={Artifacts.Count} survivors={Survivors.Count}");
        }

        private static void BuildMasters()
        {
            try
            {
                foreach (var masterEntry in MasterCatalog.allMasters)
                {
                    if (masterEntry == null) continue;
                    var masterPrefab = masterEntry.gameObject;
                    if (masterPrefab == null) continue;
                    var name = masterPrefab.name;
                    bool ally = false;
                    foreach (var keyword in AllyKeywords)
                    {
                        if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) { ally = true; break; }
                    }
                    Masters.Add(new MasterInfo { Name = name, Prefab = masterPrefab, AllyLike = ally });
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
                    PickupIndex pickupIndex = PickupIndex.none;
                    try { pickupIndex = PickupCatalog.FindPickupIndex(idx); } catch { }
                    Items.Add(new ItemInfo
                    {
                        Name = GameApi.Localize(token),
                        Token = token,
                        Def = def,
                        Index = idx,
                        Pickup = pickupIndex,
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
                    var pickupIndex = def.pickupIndex;
                    if (!pickupIndex.isValid) continue;
                    string label = GameApi.Localize(def.nameToken);
                    if (string.IsNullOrEmpty(label)) label = def.internalName;
                    Pickups.Add(new PickupInfo { Name = label, Index = pickupIndex });
                }
                Pickups.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildPickups", e); }
        }

        private static void BuildBuffs()
        {
            try
            {
                // nonHiddenBuffIndices - готовый отбор игры: служебные бафы
                // (маркеры состояний, технические флаги) в меню не нужны
                foreach (var idx in BuffCatalog.nonHiddenBuffIndices)
                {
                    var def = BuffCatalog.GetBuffDef(idx);
                    if (def == null) continue;
                    string name = def.name;
                    if (string.IsNullOrEmpty(name)) name = idx.ToString();
                    Buffs.Add(new BuffInfo { Name = name, Index = idx, Def = def });
                }
                Buffs.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildBuffs", e); }
        }

        private static void BuildStages()
        {
            try
            {
                int count = SceneCatalog.sceneDefCount;
                for (int i = 0; i < count; i++)
                {
                    var def = SceneCatalog.GetSceneDef((SceneIndex)i);
                    if (def == null) continue;
                    // только игровые стадии: меню, катсцены и intermission'ы телепортом не нужны
                    if (def.sceneType != SceneType.Stage && def.sceneType != SceneType.UntimedStage) continue;
                    string sceneName = def.cachedName;
                    if (string.IsNullOrEmpty(sceneName)) continue;
                    string display = GameApi.Localize(def.nameToken);
                    if (string.IsNullOrEmpty(display) || display == def.nameToken) display = sceneName;
                    Stages.Add(new StageInfo { Name = display, SceneName = sceneName });
                }
                Stages.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildStages", e); }
        }

        private static void BuildArtifacts()
        {
            try
            {
                int count = ArtifactCatalog.artifactCount;
                for (int i = 1; i <= count; i++)
                {
                    var idx = (ArtifactIndex)i;
                    var def = ArtifactCatalog.GetArtifactDef(idx);
                    if (def == null) continue;
                    string name = GameApi.Localize(def.nameToken);
                    if (string.IsNullOrEmpty(name) || name == def.nameToken) name = def.cachedName;
                    Artifacts.Add(new ArtifactInfo { Name = name, Index = idx });
                }
                Artifacts.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildArtifacts", e); }
        }

        /// <summary>
        /// Выжившие для мгновенной смены персонажа: ключ - имя префаба тела
        /// (его принимает CharacterMaster.Respawn), подпись - из токена имени.
        /// </summary>
        private static void BuildSurvivors()
        {
            try
            {
                foreach (var def in SurvivorCatalog.allSurvivorDefs)
                {
                    if (def == null || def.bodyPrefab == null) continue;
                    string bodyName = def.bodyPrefab.name;
                    if (string.IsNullOrEmpty(bodyName)) continue;
                    string name = GameApi.Localize(def.displayNameToken);
                    if (string.IsNullOrEmpty(name) || name == def.displayNameToken) name = bodyName;
                    Survivors.Add(new SurvivorInfo { Name = name, BodyName = bodyName });
                }
                Survivors.Sort((x, y) => CompareByName(x.Name, y.Name));
            }
            catch (Exception e) { GameApi.Warn("BuildSurvivors", e); }
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


