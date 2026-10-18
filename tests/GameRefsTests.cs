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