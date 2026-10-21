using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace SandboxMenu.Tests {

    /// <summary>
    /// Интеграционные проверки против настоящей игровой сборки: цели
    /// Harmony-патчей и имена reflection-цепочек обязаны существовать в той
    /// RoR2.dll, против которой собран плагин.
    ///
    /// Сборка игры не лежит в репозитории: путь к папке с ней передаётся
    /// переменной окружения GAME_REFS_DIR (CI выставляет её в game-managed
    /// из кэша Actions). Без переменной фикстур пропускается (Assert.Ignore),
    /// поэтому локальный dotnet test без игры не краснеет.
    ///
    /// Метаданные читаются через MetadataLoadContext: сборка загружается
    /// только для отражения, её зависимости не резолвятся в рантайм теста.
    /// </summary>
    [TestFixture]
    public class GameRefsTests {

        private MetadataLoadContext _context;
        private Assembly _game;

        /// <summary>Открывает RoR2.dll из GAME_REFS_DIR для чтения метаданных.</summary>
        [OneTimeSetUp]
        public void LoadGameAssembly() {

            string directory = Environment.GetEnvironmentVariable("GAME_REFS_DIR");

            if (string.IsNullOrEmpty(directory) || !File.Exists(Path.Combine(directory, "RoR2.dll"))) {
                Assert.Ignore("GAME_REFS_DIR не задан или не содержит RoR2.dll: интеграционные проверки против игровой сборки пропущены");
            }

            string[] gamePaths = Directory.GetFiles(directory, "*.dll", SearchOption.AllDirectories);
            string runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
            string[] corePaths = Directory.GetFiles(runtimeDir, "*.dll");
            string[] testBinPaths = Directory.GetFiles(AppContext.BaseDirectory, "*.dll", SearchOption.AllDirectories);

            var unique = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (string path in gamePaths) {
                unique[Path.GetFileName(path)] = path;
            }

            foreach (string path in testBinPaths) {

                if (!unique.ContainsKey(Path.GetFileName(path))) {
                    unique[Path.GetFileName(path)] = path;
                }

            }

            foreach (string path in corePaths) {

                if (!unique.ContainsKey(Path.GetFileName(path))) {
                    unique[Path.GetFileName(path)] = path;
                }

            }

            var paths = new List<string>(unique.Values);
            _context = new MetadataLoadContext(new PathAssemblyResolver(paths));
            _game = _context.LoadFromAssemblyPath(Path.Combine(directory, "RoR2.dll"));

        }

        /// <summary>Освобождает контекст отражения после фикстура.</summary>
        [OneTimeTearDown]
        public void DisposeContext() {
            _context?.Dispose();
        }

        /// <summary>Цели UnlockPatches: четыре гейта профиля на месте.</summary>
        [Test]
        public void UnlockPatchTargets_ExistInGameAssembly() {

            var profile = _game.GetType("RoR2.UserProfile", true);
            var unlockableDef = _game.GetType("RoR2.UnlockableDef", true);

            Assert.That(Method(profile, "HasUnlockable", unlockableDef.Name), Is.Not.Null,
                    "UserProfile.HasUnlockable(UnlockableDef) исчез из игры");
            Assert.That(Method(profile, "HasUnlockable", "String"), Is.Not.Null,
                    "UserProfile.HasUnlockable(string) исчез из игры");
            Assert.That(AnyMethod(profile, "HasSurvivorUnlocked"), Is.Not.Null,
                    "UserProfile.HasSurvivorUnlocked исчез из игры");
            Assert.That(AnyMethod(profile, "HasViewedViewable"), Is.Not.Null,
                    "UserProfile.HasViewedViewable исчез из игры");

        }

        /// <summary>Цели DropHooks: приватные OnEnable обоих контроллеров на месте.</summary>
        [Test]
        public void DropHookTargets_ArePrivateOnEnable() {

            var droplet = _game.GetType("RoR2.PickupDropletController", true);
            var pickup = _game.GetType("RoR2.GenericPickupController", true);

            var dropletOnEnable = Method(droplet, "OnEnable");
            var pickupOnEnable = Method(pickup, "OnEnable");

            Assert.That(dropletOnEnable, Is.Not.Null, "PickupDropletController.OnEnable исчез из игры");
            Assert.That(pickupOnEnable, Is.Not.Null, "GenericPickupController.OnEnable исчез из игры");
            Assert.That(dropletOnEnable.IsPrivate, Is.True, "PickupDropletController.OnEnable перестал быть приватным");
            Assert.That(pickupOnEnable.IsPrivate, Is.True, "GenericPickupController.OnEnable перестал быть приватным");

        }

        /// <summary>Reflection-цепочка выдачи/снятия предметов: все имена в Inventory.</summary>
        [Test]
        public void ItemChainNames_ExistInInventory() {

            var inventory = _game.GetType("RoR2.Inventory", true);

            string[] chainNames = {
                "GiveItemPermanent",
                "GiveItem",
                "RemoveItem",
                "RemoveItemPermanent",
                "CalculateEffectiveItemStacks"
            };

            foreach (string chainName in chainNames) {
                Assert.That(AnyMethod(inventory, chainName), Is.Not.Null,
                        $"Inventory.{chainName} исчез из игры: reflection-цепочку пора обновлять");
            }

        }

        /// <summary>Счётчики каталогов для вкладки разблокировок доступны.</summary>
        [Test]
        public void CatalogCounters_Exist() {

            var unlockables = _game.GetType("RoR2.UnlockableCatalog", true);
            var skins = _game.GetType("RoR2.SkinCatalog", true);

            Assert.That(Member(unlockables, "unlockableCount"), Is.Not.Null,
                    "UnlockableCatalog.unlockableCount исчез из игры");
            Assert.That(Member(skins, "skinCount"), Is.Not.Null,
                    "SkinCatalog.skinCount исчез из игры");

        }

        /// <summary>Цели чит-хуков: вход урона, поля DamageInfo, пересчёт статов.</summary>
        [Test]
        public void CheatHookTargets_ExistInGameAssembly() {

            var health = _game.GetType("RoR2.HealthComponent", true);
            var damageInfo = _game.GetType("RoR2.DamageInfo", true);
            var body = _game.GetType("RoR2.CharacterBody", true);

            Assert.That(Method(health, "TakeDamage", "DamageInfo"), Is.Not.Null,
                    "HealthComponent.TakeDamage(DamageInfo) исчез из игры: хук бессмертия/ваншота встанет мимо");
            Assert.That(Method(body, "get_attackSpeed"), Is.Not.Null,
                    "CharacterBody.get_attackSpeed исчез из игры: множитель атаки встанет мимо");
            Assert.That(Method(body, "get_moveSpeed"), Is.Not.Null,
                    "CharacterBody.get_moveSpeed исчез из игры: множитель движения встанет мимо");

            foreach (string fieldName in new[] { "damage", "attacker", "inflictor", "rejected", "position", "procCoefficient", "damageType" }) {
                Assert.That(InstanceField(damageInfo, fieldName), Is.Not.Null,
                        $"DamageInfo.{fieldName} исчез из игры");
            }

            Assert.That(InstanceField(health, "barrier"), Is.Not.Null, "HealthComponent.barrier исчез из игры");
            Assert.That(InstanceProp(health, "combinedHealth"), Is.Not.Null, "HealthComponent.combinedHealth исчез из игры");
            Assert.That(InstanceProp(health, "alive"), Is.Not.Null, "HealthComponent.alive исчез из игры");
            Assert.That(InstanceProp(health, "fullHealth"), Is.Not.Null, "HealthComponent.fullHealth исчез из игры");
            Assert.That(InstanceProp(health, "fullShield"), Is.Not.Null, "HealthComponent.fullShield исчез из игры");

            Assert.That(WritableProp(health, "Networkhealth"), Is.Not.Null,
                    "HealthComponent.Networkhealth (settable) исчез из игры: лечение не синхронизируется");
            Assert.That(WritableProp(health, "Networkshield"), Is.Not.Null,
                    "HealthComponent.Networkshield (settable) исчез из игры");

            Assert.That(InstanceProp(body, "attackSpeed"), Is.Not.Null, "CharacterBody.attackSpeed исчез из игры");
            Assert.That(InstanceProp(body, "moveSpeed"), Is.Not.Null, "CharacterBody.moveSpeed исчез из игры");

        }

        /// <summary>Сброс перезарядок: рычаги GenericSkill, приватные поля EquipmentSlot, Run.FixedTimeStamp.</summary>
        [Test]
        public void CooldownResetTargets_ExistInGameAssembly() {

            var skill = _game.GetType("RoR2.GenericSkill", true);
            var locator = _game.GetType("RoR2.SkillLocator", true);
            var slot = _game.GetType("RoR2.EquipmentSlot", true);
            var run = _game.GetType("RoR2.Run", true);

            Assert.That(WritableProp(skill, "stock"), Is.Not.Null, "GenericSkill.stock (settable) исчез из игры");
            Assert.That(WritableProp(skill, "maxStock"), Is.Not.Null, "GenericSkill.maxStock (settable) исчез из игры");
            Assert.That(InstanceField(skill, "baseRechargeStopwatch"), Is.Not.Null,
                    "GenericSkill.baseRechargeStopwatch исчез из игры");
            Assert.That(AnyMethod(skill, "GetFinalRechargeInterval"), Is.Not.Null,
                    "GenericSkill.GetFinalRechargeInterval исчез из игры");

            foreach (string fieldName in new[] { "primary", "secondary", "utility", "special" }) {
                Assert.That(InstanceField(locator, fieldName), Is.Not.Null,
                        $"SkillLocator.{fieldName} исчез из игры");
            }

            Assert.That(InstanceField(slot, "_rechargeTime"), Is.Not.Null,
                    "EquipmentSlot._rechargeTime исчез из игры: reflection-сброс оборудования пора обновлять");
            Assert.That(InstanceField(slot, "subcooldownTimer"), Is.Not.Null,
                    "EquipmentSlot.subcooldownTimer исчез из игры");

            var timeStamp = run.GetNestedType("FixedTimeStamp", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(timeStamp, Is.Not.Null, "Run.FixedTimeStamp исчез из игры");
            Assert.That(timeStamp.GetField("negativeInfinity", BindingFlags.Public | BindingFlags.Static), Is.Not.Null,
                    "Run.FixedTimeStamp.negativeInfinity исчез из игры");

        }

        /// <summary>Экономика и бой: деньги, лунные монеты, уровень команды, бафы, перечисление врагов.</summary>
        [Test]
        public void EconomyAndCombatTargets_ExistInGameAssembly() {

            var master = _game.GetType("RoR2.CharacterMaster", true);
            var user = _game.GetType("RoR2.NetworkUser", true);
            var team = _game.GetType("RoR2.TeamManager", true);
            var body = _game.GetType("RoR2.CharacterBody", true);
            var buffCatalog = _game.GetType("RoR2.BuffCatalog", true);
            var tracker = _game.GetType("RoR2.InstanceTracker", true);

            Assert.That(WritableProp(master, "money"), Is.Not.Null,
                    "CharacterMaster.money (settable) исчез из игры: кошелёк игрока переехал");
            Assert.That(AnyMethod(user, "AwardLunarCoins"), Is.Not.Null, "NetworkUser.AwardLunarCoins исчез из игры");
            Assert.That(AnyMethod(user, "DeductLunarCoins"), Is.Not.Null, "NetworkUser.DeductLunarCoins исчез из игры");
            Assert.That(InstanceProp(user, "lunarCoins"), Is.Not.Null, "NetworkUser.lunarCoins исчез из игры");

            Assert.That(Member(team, "instance"), Is.Not.Null, "TeamManager.instance исчез из игры");
            Assert.That(AnyMethod(team, "GiveTeamMoney"), Is.Not.Null, "TeamManager.GiveTeamMoney исчез из игры");
            Assert.That(AnyMethod(team, "SetTeamLevel"), Is.Not.Null, "TeamManager.SetTeamLevel исчез из игры");
            Assert.That(AnyMethod(team, "GiveTeamExperience"), Is.Not.Null, "TeamManager.GiveTeamExperience исчез из игры");
            Assert.That(AnyMethod(team, "GetTeamLevel"), Is.Not.Null, "TeamManager.GetTeamLevel исчез из игры");
            Assert.That(AnyMethod(team, "IsTeamEnemy"), Is.Not.Null, "TeamManager.IsTeamEnemy исчез из игры");

            Assert.That(AnyMethod(body, "AddBuff"), Is.Not.Null, "CharacterBody.AddBuff исчез из игры");
            Assert.That(AnyMethod(body, "RemoveBuff"), Is.Not.Null, "CharacterBody.RemoveBuff исчез из игры");
            Assert.That(AnyMethod(body, "GetBuffCount"), Is.Not.Null, "CharacterBody.GetBuffCount исчез из игры");
            Assert.That(Member(buffCatalog, "buffCount"), Is.Not.Null, "BuffCatalog.buffCount исчез из игры");
            Assert.That(AnyMethod(buffCatalog, "GetBuffDef"), Is.Not.Null, "BuffCatalog.GetBuffDef исчез из игры");

            Assert.That(AnyMethod(tracker, "GetInstancesList"), Is.Not.Null,
                    "InstanceTracker.GetInstancesList исчез из игры: перечисление врагов сломается");

        }

        /// <summary>Цели управления забегом: таймер, сложность, стадии, телепортер, артефакты, телепорт.</summary>
        [Test]
        public void RunControlTargets_ExistInGameAssembly() {

            var run = _game.GetType("RoR2.Run", true);
            var sceneCatalog = _game.GetType("RoR2.SceneCatalog", true);
            var sceneDef = _game.GetType("RoR2.SceneDef", true);
            var teleporter = _game.GetType("RoR2.TeleporterInteraction", true);
            var holdout = _game.GetType("RoR2.HoldoutZoneController", true);
            var artifacts = _game.GetType("RoR2.RunArtifactManager", true);
            var artifactCatalog = _game.GetType("RoR2.ArtifactCatalog", true);
            var artifactDef = _game.GetType("RoR2.ArtifactDef", true);
            var teleportHelper = _game.GetType("RoR2.TeleportHelper", true);
            var buffCatalog = _game.GetType("RoR2.BuffCatalog", true);

            Assert.That(AnyMethod(run, "SetForcePauseRunStopwatch"), Is.Not.Null,
                    "Run.SetForcePauseRunStopwatch исчез из игры: заморозка таймера сломается");
            Assert.That(Method(run, "SetRunStopwatch", "Single"), Is.Not.Null,
                    "Run.SetRunStopwatch(float) исчез из игры");
            Assert.That(AnyMethod(run, "GetRunStopwatch"), Is.Not.Null,
                    "Run.GetRunStopwatch исчез из игры");
            Assert.That(Method(run, "AdvanceStage", "SceneDef"), Is.Not.Null,
                    "Run.AdvanceStage(SceneDef) исчез из игры: переход по стадиям сломается");
            Assert.That(AnyMethod(run, "RecalculateDifficultyCoefficent"), Is.Not.Null,
                    "Run.RecalculateDifficultyCoefficent исчез из игры");
            Assert.That(WritableProp(run, "selectedDifficulty"), Is.Not.Null,
                    "Run.selectedDifficulty (settable) исчез из игры");
            Assert.That(Member(run, "instance"), Is.Not.Null, "Run.instance исчез из игры");

            Assert.That(Member(sceneCatalog, "sceneDefCount"), Is.Not.Null,
                    "SceneCatalog.sceneDefCount исчез из игры");
            Assert.That(AnyMethod(sceneCatalog, "GetSceneDef"), Is.Not.Null,
                    "SceneCatalog.GetSceneDef исчез из игры");
            Assert.That(AnyMethod(sceneCatalog, "GetSceneDefFromSceneName"), Is.Not.Null,
                    "SceneCatalog.GetSceneDefFromSceneName исчез из игры");
            Assert.That(InstanceProp(sceneDef, "cachedName"), Is.Not.Null,
                    "SceneDef.cachedName исчез из игры");
            Assert.That(InstanceField(sceneDef, "nameToken"), Is.Not.Null,
                    "SceneDef.nameToken исчез из игры");
            Assert.That(InstanceField(sceneDef, "sceneType"), Is.Not.Null,
                    "SceneDef.sceneType исчез из игры");

            Assert.That(Member(teleporter, "instance"), Is.Not.Null,
                    "TeleporterInteraction.instance исчез из игры");
            Assert.That(InstanceProp(teleporter, "holdoutZoneController"), Is.Not.Null,
                    "TeleporterInteraction.holdoutZoneController исчез из игры: мгновенная зарядка сломается");
            Assert.That(InstanceProp(holdout, "charge"), Is.Not.Null,
                    "HoldoutZoneController.charge исчез из игры");
            Assert.That(InstanceField(holdout, "_charge"), Is.Not.Null,
                    "HoldoutZoneController._charge исчез из игры: reflection-зарядку пора обновлять");
            Assert.That(InstanceField(holdout, "maxCharge"), Is.Not.Null,
                    "HoldoutZoneController.maxCharge исчез из игры");

            Assert.That(Member(artifacts, "instance"), Is.Not.Null,
                    "RunArtifactManager.instance исчез из игры");
            Assert.That(AnyMethod(artifacts, "IsArtifactEnabled"), Is.Not.Null,
                    "RunArtifactManager.IsArtifactEnabled исчез из игры");
            Assert.That(AnyMethod(artifacts, "SetArtifactEnabledServer"), Is.Not.Null,
                    "RunArtifactManager.SetArtifactEnabledServer исчез из игры");
            Assert.That(Member(artifactCatalog, "artifactCount"), Is.Not.Null,
                    "ArtifactCatalog.artifactCount исчез из игры");
            Assert.That(AnyMethod(artifactCatalog, "GetArtifactDef"), Is.Not.Null,
                    "ArtifactCatalog.GetArtifactDef исчез из игры");
            Assert.That(InstanceField(artifactDef, "nameToken"), Is.Not.Null,
                    "ArtifactDef.nameToken исчез из игры");
            Assert.That(Member(buffCatalog, "nonHiddenBuffIndices"), Is.Not.Null,
                    "BuffCatalog.nonHiddenBuffIndices исчез из игры: список бафов опустеет");

            Assert.That(Method(teleportHelper, "TeleportBody", "CharacterBody", "Vector3"), Is.Not.Null,
                    "TeleportHelper.TeleportBody(CharacterBody, Vector3) исчез из игры");

        }

        /// <summary>Цели свободного полёта: мотор RoR2 и KinematicCharacterMotor из соседней сборки.</summary>
        [Test]
        public void FlightTargets_ExistInGameAssemblies() {

            var motor = _game.GetType("RoR2.CharacterMotor", true);

            Assert.That(InstanceField(motor, "gravityMultiplier"), Is.Not.Null,
                    "CharacterMotor.gravityMultiplier исчез из игры: полёт не сможет гасить гравитацию");

            string refsDir = Environment.GetEnvironmentVariable("GAME_REFS_DIR");
            string kcmPath = string.IsNullOrEmpty(refsDir)
                    ? null
                    : Path.Combine(refsDir, "KinematicCharacterController.dll");

            if (kcmPath == null || !File.Exists(kcmPath)) {

                Assert.Ignore("KinematicCharacterController.dll не найден в GAME_REFS_DIR: проверки мотора полёта пропущены");
                return;

            }

            var kcmAssembly = _context.LoadFromAssemblyPath(kcmPath);
            var baseController = kcmAssembly.GetType("KinematicCharacterController.BaseCharacterController", true);
            var kcm = kcmAssembly.GetType("KinematicCharacterController.KinematicCharacterMotor", true);

            Assert.That(WritableProp(baseController, "Motor"), Is.Not.Null,
                    "BaseCharacterController.Motor исчез из игры: полёт не дотянется до мотора");
            Assert.That(Method(kcm, "SetPosition", "Vector3", "Boolean"), Is.Not.Null,
                    "KinematicCharacterMotor.SetPosition(Vector3, bool) исчез из игры");
            Assert.That(InstanceField(kcm, "BaseVelocity"), Is.Not.Null,
                    "KinematicCharacterMotor.BaseVelocity исчез из игры: скорость полёта не обнуляется");

        }

        /// <summary>Поле экземпляра (публичное или нет) в объявлении типа.</summary>
        private static FieldInfo InstanceField(Type owner, string name) {

            return owner.GetField(name, BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        }

        /// <summary>Свойство экземпляра в объявлении типа (без проверки сеттера).</summary>
        private static PropertyInfo InstanceProp(Type owner, string name) {

            return owner.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);

        }

        /// <summary>Свойство с публичным или приватным сеттером: плагины пишут в него.</summary>
        private static PropertyInfo WritableProp(Type owner, string name) {

            var property = InstanceProp(owner, name);

            if (property == null || !property.CanWrite) {
                return null;
            }

            return property;

        }

        /// <summary>Ищет метод по имени и простым именам типов параметров (перегрузки).</summary>
        private static MethodInfo Method(Type owner, string name, params string[] parameterTypeNames) {

            const BindingFlags every = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var method in owner.GetMethods(every)) {

                if (method.Name != name) {
                    continue;
                }

                var parameters = method.GetParameters();

                if (parameters.Length != parameterTypeNames.Length) {
                    continue;
                }

                bool matched = true;

                for (int index = 0; index < parameters.Length; index++) {

                    if (parameters[index].ParameterType.Name != parameterTypeNames[index]) {
                        matched = false;
                    }

                }

                if (matched) {
                    return method;
                }

            }

            return null;

        }

        /// <summary>Ищет метод по имени с любой сигнатурой по всей иерархии типов:
        /// nameof и Harmony резолвят и унаследованные члены, и перегрузки.</summary>
        private static MethodInfo AnyMethod(Type owner, string name) {

            const BindingFlags every = BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.Static;

            foreach (var method in owner.GetMethods(every)) {

                if (method.Name == name) {
                    return method;
                }

            }

            return null;

        }

        /// <summary>Ищет поле или свойство по имени: счётчики каталогов в разных сборках игры были и тем, и другим.</summary>
        private static MemberInfo Member(Type owner, string name) {

            var field = owner.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            if (field != null) {
                return field;
            }

            return owner.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        }
    }
}