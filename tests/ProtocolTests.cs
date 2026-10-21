using NUnit.Framework;
using SandboxMenu;
using UnityEngine.Networking;

namespace SandboxMenu.Tests
{
    /// <summary>
    /// Тесты чистой части протокола: сериализация запроса, серверные клампы
    /// и rate-limit. Не требуют игровых сборок: кодек опирается на стаб HLAPI.
    /// </summary>
    [TestFixture]
    public class ProtocolTests
    {
        [Test]
        public void RequestRoundTrip_KeepsAllFields()
        {
            var req = new SandboxRequest
            {
                Action = ActionCode.SpawnMaster,
                TargetNetId = 123456789u,
                P1 = 7,
                P2 = -9,
                Team = 3,
                Name = "BeetleGuardMaster"
            };

            var writer = new NetworkWriter();
            req.Serialize(writer);
            var back = new SandboxRequest();
            back.Deserialize(new NetworkReader(writer.ToArray()));

            Assert.That(back.Version, Is.EqualTo(Protocol.ProtoVersion));
            Assert.That(back.Action, Is.EqualTo(req.Action));
            Assert.That(back.TargetNetId, Is.EqualTo(req.TargetNetId));
            Assert.That(back.P1, Is.EqualTo(req.P1));
            Assert.That(back.P2, Is.EqualTo(req.P2));
            Assert.That(back.Team, Is.EqualTo(req.Team));
            Assert.That(back.Name, Is.EqualTo(req.Name));
        }

        [Test]
        public void RequestRoundTrip_NullNameStaysNull()
        {
            var req = new SandboxRequest { Action = ActionCode.ClearDrops, Name = null };
            var writer = new NetworkWriter();
            req.Serialize(writer);
            var back = new SandboxRequest();
            back.Deserialize(new NetworkReader(writer.ToArray()));
            Assert.That(back.Name, Is.Null);
        }

        [Test]
        public void Serialize_TruncatesOverlongName()
        {
            var req = new SandboxRequest { Name = new string('x', Protocol.MaxNameLength + 50) };
            var writer = new NetworkWriter();
            req.Serialize(writer);
            var back = new SandboxRequest();
            back.Deserialize(new NetworkReader(writer.ToArray()));
            Assert.That(back.Name.Length, Is.EqualTo(Protocol.MaxNameLength));
        }

        [Test]
        public void ClampCount_BoundsPerAction()
        {
            Assert.That(Protocol.ClampCount(ActionCode.GiveItem, 100000), Is.EqualTo(500));
            Assert.That(Protocol.ClampCount(ActionCode.GiveItem, 0), Is.EqualTo(1));
            Assert.That(Protocol.ClampCount(ActionCode.DropPickup, 9999), Is.EqualTo(100));
            Assert.That(Protocol.ClampCount(ActionCode.SpawnMaster, 999), Is.EqualTo(50));
            Assert.That(Protocol.ClampCount(ActionCode.SpawnProp, -3), Is.EqualTo(1));
            Assert.That(Protocol.ClampCount(ActionCode.CloneSpawnNow, 20), Is.EqualTo(20));
        }

        [Test]
        public void ClampCount_CheatActionsAllowMeaningfulZero()
        {
            // ноль осмыслен: радиус 0 = вся карта, деньги/таймер 0
            Assert.That(Protocol.ClampCount(ActionCode.KillEnemies, 0), Is.EqualTo(0));
            Assert.That(Protocol.ClampCount(ActionCode.KillEnemies, -5), Is.EqualTo(0));
            Assert.That(Protocol.ClampCount(ActionCode.KillEnemies, 99999), Is.EqualTo(CheatState.MaxKillRadius));
            Assert.That(Protocol.ClampCount(ActionCode.SetMoney, 0), Is.EqualTo(0));
            Assert.That(Protocol.ClampCount(ActionCode.GiveMoney, 100000000), Is.EqualTo(CheatState.MaxMoney));
            Assert.That(Protocol.ClampCount(ActionCode.SetLunarCoins, -1), Is.EqualTo(0));
            Assert.That(Protocol.ClampCount(ActionCode.SetTimer, 0), Is.EqualTo(0));
            Assert.That(Protocol.ClampCount(ActionCode.SetTimer, 99999999), Is.EqualTo(CheatState.MaxTimerSeconds));
            Assert.That(Protocol.ClampCount(ActionCode.SetTeamLevel, 0), Is.EqualTo(1), "уровень 0 не бывает");
            Assert.That(Protocol.ClampCount(ActionCode.SetTeamLevel, 5000), Is.EqualTo(CheatState.MaxLevel));
            Assert.That(Protocol.ClampCount(ActionCode.GiveExperience, 999999999), Is.EqualTo(CheatState.MaxExperience));
            Assert.That(Protocol.ClampCount(ActionCode.FreezeTimer, 5), Is.EqualTo(1));
            Assert.That(Protocol.ClampCount(ActionCode.FreezeTimer, 0), Is.EqualTo(0));
        }

        [Test]
        public void ClampCount_DualMeaningParamsPassThrough()
        {
            // P1/P2 несут разный смысл (id и значение): клампает исполнитель на сервере
            Assert.That(Protocol.ClampCount(ActionCode.SetCheatValue, 100000), Is.EqualTo(100000));
            Assert.That(Protocol.ClampCount(ActionCode.GiveBuff, 777), Is.EqualTo(777));
            Assert.That(Protocol.ClampCount(ActionCode.SetArtifact, 42), Is.EqualTo(42));
            Assert.That(Protocol.ClampCount(ActionCode.TeleportPlayer, 1), Is.EqualTo(1));
            Assert.That(CheatState.ClampPct(100000), Is.EqualTo(CheatState.MaxPct),
                    "исполнитель клампает проценты сам");
        }

        [Test]
        public void ClampCount_MapTeleportAndTimeScale()
        {
            Assert.That(Protocol.ClampCount(ActionCode.TeleportLandmark, 0), Is.EqualTo(1),
                    "нулевого ориентира нет - kinds начинаются с 1");
            Assert.That(Protocol.ClampCount(ActionCode.TeleportLandmark, 99), Is.EqualTo(RunControl.LastLandmarkKind));
            Assert.That(Protocol.ClampCount(ActionCode.WaypointSave, 0), Is.EqualTo(1));
            Assert.That(Protocol.ClampCount(ActionCode.WaypointLoad, 77), Is.EqualTo(RunControl.WaypointCount));
            Assert.That(Protocol.ClampCount(ActionCode.SetTimeScale, 0), Is.EqualTo(CheatState.MinPct));
            Assert.That(Protocol.ClampCount(ActionCode.SetTimeScale, 100000), Is.EqualTo(CheatState.MaxPct));
            Assert.That(Protocol.ClampCount(ActionCode.RespawnAs, 12345), Is.EqualTo(12345),
                    "имя тела уходит полем Name, числа запрос не использует");
        }

        [Test]
        public void RateLimiter_AllowsWindowThenBlocks()
        {
            var rl = new RateLimiter();
            for (int i = 0; i < RateLimiter.MaxPerWindow; i++)
            {
                Assert.That(rl.TryConsume(1, 100f), Is.True, $"request {i}");
            }
            Assert.That(rl.TryConsume(1, 100f), Is.False, "over the window limit");
            Assert.That(rl.TryConsume(2, 100f), Is.True, "other sender unaffected");
            Assert.That(rl.TryConsume(1, 100f + RateLimiter.WindowSeconds), Is.True, "window slides");
        }

        [Test]
        public void RateLimiter_ForgetFreesSender()
        {
            var rl = new RateLimiter();
            for (int i = 0; i < RateLimiter.MaxPerWindow; i++) rl.TryConsume(7, 0f);
            Assert.That(rl.TryConsume(7, 0f), Is.False);
            rl.Forget(7);
            Assert.That(rl.TryConsume(7, 0f), Is.True);
        }
    }
}

