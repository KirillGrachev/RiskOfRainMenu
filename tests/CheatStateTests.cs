using NUnit.Framework;
using SandboxMenu;

namespace SandboxMenu.Tests {

    /// <summary>
    /// Тесты чистого состояния читов: флаги, множители, клампы и сброс.
    /// Как и ProtocolTests, не требуют игровых сборок.
    /// </summary>
    [TestFixture]
    public class CheatStateTests {

        [SetUp]
        public void Reset() {

            CheatState.ResetRun();

        }

        [Test]
        public void ClampPct_BoundsToWorkingRange() {

            Assert.That(CheatState.ClampPct(-50), Is.EqualTo(CheatState.MinPct));
            Assert.That(CheatState.ClampPct(0), Is.EqualTo(CheatState.MinPct));
            Assert.That(CheatState.ClampPct(100), Is.EqualTo(100));
            Assert.That(CheatState.ClampPct(99999), Is.EqualTo(CheatState.MaxPct));

        }

        [Test]
        public void ClampStacks_BoundsToOneToFifty() {

            Assert.That(CheatState.ClampStacks(0), Is.EqualTo(1));
            Assert.That(CheatState.ClampStacks(-3), Is.EqualTo(1));
            Assert.That(CheatState.ClampStacks(7), Is.EqualTo(7));
            Assert.That(CheatState.ClampStacks(1000), Is.EqualTo(CheatState.MaxBuffStacks));

        }

        [Test]
        public void Flags_PerPlayerIndependent() {

            CheatState.SetFlag(11u, CheatFlag.GodMode, true);

            Assert.That(CheatState.HasFlag(11u, CheatFlag.GodMode), Is.True);
            Assert.That(CheatState.HasFlag(22u, CheatFlag.GodMode), Is.False);
            Assert.That(CheatState.HasFlag(11u, CheatFlag.OneHitKill), Is.False);

            CheatState.SetFlag(11u, CheatFlag.GodMode, false);

            Assert.That(CheatState.HasFlag(11u, CheatFlag.GodMode), Is.False);

        }

        [Test]
        public void GodModeAll_CoversEveryPlayer() {

            CheatState.SetFlag(0u, CheatFlag.GodModeAll, true);

            Assert.That(CheatState.GodModeAll, Is.True);
            Assert.That(CheatState.HasFlag(11u, CheatFlag.GodMode), Is.True);
            Assert.That(CheatState.HasFlag(22u, CheatFlag.GodMode), Is.True);
            Assert.That(CheatState.HasFlag(33u, CheatFlag.OneHitKill), Is.False,
                    "GodModeAll не должен включать другие флаги");

            CheatState.SetFlag(0u, CheatFlag.GodModeAll, false);

            Assert.That(CheatState.HasFlag(11u, CheatFlag.GodMode), Is.False);

        }

        [Test]
        public void ZeroNetId_IsIgnored() {

            CheatState.SetFlag(0u, CheatFlag.GodMode, true);

            Assert.That(CheatState.HasFlag(0u, CheatFlag.GodMode), Is.False);

        }

        [Test]
        public void Values_DefaultIsHundred() {

            Assert.That(CheatState.ValueOf(11u, CheatValue.DamagePct), Is.EqualTo(CheatState.DefaultPct));

            CheatState.SetValue(11u, CheatValue.DamagePct, 250);

            Assert.That(CheatState.ValueOf(11u, CheatValue.DamagePct), Is.EqualTo(250));
            Assert.That(CheatState.ValueOf(11u, CheatValue.MoveSpeedPct), Is.EqualTo(CheatState.DefaultPct),
                    "другой множитель не затронут");
            Assert.That(CheatState.ValueOf(22u, CheatValue.DamagePct), Is.EqualTo(CheatState.DefaultPct),
                    "другой игрок не затронут");

        }

        [Test]
        public void Values_SetClampsAndDefaultRemovesEntry() {

            CheatState.SetValue(11u, CheatValue.AttackSpeedPct, 100000);
            Assert.That(CheatState.ValueOf(11u, CheatValue.AttackSpeedPct), Is.EqualTo(CheatState.MaxPct));
            Assert.That(CheatState.TunedPlayerCount, Is.EqualTo(1));

            CheatState.SetValue(11u, CheatValue.AttackSpeedPct, CheatState.DefaultPct);
            Assert.That(CheatState.TunedPlayerCount, Is.EqualTo(0),
                    "значение по умолчанию не держит запись");

        }

        [Test]
        public void KnownIds_RejectGarbageFromNetwork() {

            Assert.That(CheatState.IsKnownFlag(1), Is.True);
            Assert.That(CheatState.IsKnownFlag(4), Is.True);
            Assert.That(CheatState.IsKnownFlag(0), Is.False);
            Assert.That(CheatState.IsKnownFlag(200), Is.False);
            Assert.That(CheatState.IsKnownValue(3), Is.True);
            Assert.That(CheatState.IsKnownValue(9), Is.False);

        }

        [Test]
        public void Forget_DropsPlayerRecords() {

            CheatState.SetFlag(11u, CheatFlag.NoCooldown, true);
            CheatState.SetValue(11u, CheatValue.DamagePct, 300);
            Assert.That(CheatState.FlaggedPlayerCount, Is.EqualTo(1));

            CheatState.Forget(11u);

            Assert.That(CheatState.HasFlag(11u, CheatFlag.NoCooldown), Is.False);
            Assert.That(CheatState.ValueOf(11u, CheatValue.DamagePct), Is.EqualTo(CheatState.DefaultPct));
            Assert.That(CheatState.FlaggedPlayerCount, Is.EqualTo(0));

        }

        [Test]
        public void ResetRun_ClearsEverything() {

            CheatState.SetFlag(11u, CheatFlag.GodMode, true);
            CheatState.SetValue(22u, CheatValue.MoveSpeedPct, 200);
            CheatState.GodModeAll = true;
            CheatState.FreezeTimer = true;

            CheatState.ResetRun();

            Assert.That(CheatState.HasFlag(11u, CheatFlag.GodMode), Is.False);
            Assert.That(CheatState.ValueOf(22u, CheatValue.MoveSpeedPct), Is.EqualTo(CheatState.DefaultPct));
            Assert.That(CheatState.GodModeAll, Is.False);
            Assert.That(CheatState.FreezeTimer, Is.False);

        }

    }

}
