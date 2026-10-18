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

