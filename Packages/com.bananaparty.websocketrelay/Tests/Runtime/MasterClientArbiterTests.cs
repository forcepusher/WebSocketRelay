using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BananaParty.WebSocketRelay.Tests
{
    public class MasterClientArbiterTests
    {
        [Test]
        public void Elect_SoloClient_BecomesMaster()
        {
            Guid localClient = Guid.NewGuid();

            Guid master = MasterClientArbiter.Elect(
                localClient,
                playTimes: new Dictionary<Guid, float> { [localClient] = 1f },
                alivePlayers: Array.Empty<NetworkPlayer>(),
                currentMaster: Guid.Empty);

            Assert.AreEqual(localClient, master);
        }

        [Test]
        public void Elect_HighestPlayTime_Wins()
        {
            Guid localClient = Guid.NewGuid();
            Guid incumbent = Guid.NewGuid();
            Guid challenger = Guid.NewGuid();

            List<NetworkPlayer> alivePlayers = new()
            {
                new NetworkPlayer(incumbent),
                new NetworkPlayer(challenger),
            };

            Guid master = MasterClientArbiter.Elect(
                localClient,
                playTimes: new Dictionary<Guid, float>
                {
                    [localClient] = 5f,
                    [incumbent] = 20f,
                    [challenger] = 50f,
                },
                alivePlayers,
                currentMaster: Guid.Empty);

            Assert.AreEqual(challenger, master);
        }

        [Test]
        public void Elect_TiedPlayTime_PrefersLowerGuid()
        {
            Guid lowerGuid = Guid.Parse("00000000-0000-0000-0000-000000000001");
            Guid higherGuid = Guid.Parse("00000000-0000-0000-0000-000000000002");

            List<NetworkPlayer> alivePlayers = new()
            {
                new NetworkPlayer(higherGuid),
                new NetworkPlayer(lowerGuid),
            };

            Guid master = MasterClientArbiter.Elect(
                Guid.Parse("00000000-0000-0000-0000-000000000003"),
                playTimes: new Dictionary<Guid, float>
                {
                    [lowerGuid] = 10f,
                    [higherGuid] = 10f,
                },
                alivePlayers,
                currentMaster: Guid.Empty);

            Assert.AreEqual(lowerGuid, master);
        }

        [Test]
        public void Elect_KeepsCurrentMasterUntilTimeout()
        {
            Guid localClient = Guid.NewGuid();
            Guid currentMaster = Guid.NewGuid();
            Guid challenger = Guid.NewGuid();

            List<NetworkPlayer> alivePlayers = new()
            {
                new NetworkPlayer(currentMaster),
                new NetworkPlayer(challenger),
            };

            Guid master = MasterClientArbiter.Elect(
                localClient,
                playTimes: new Dictionary<Guid, float>
                {
                    [localClient] = 1f,
                    [currentMaster] = 10f,
                    [challenger] = 20f,
                },
                alivePlayers,
                currentMaster);

            Assert.AreEqual(currentMaster, master);
        }

        [Test]
        public void Elect_ReplacesTimedOutMasterWithNextHighest()
        {
            Guid localClient = Guid.NewGuid();
            Guid timedOutMaster = Guid.NewGuid();
            Guid nextMaster = Guid.NewGuid();

            List<NetworkPlayer> alivePlayers = new()
            {
                new NetworkPlayer(nextMaster),
            };

            Guid master = MasterClientArbiter.Elect(
                localClient,
                playTimes: new Dictionary<Guid, float>
                {
                    [localClient] = 5f,
                    [timedOutMaster] = 100f,
                    [nextMaster] = 30f,
                },
                alivePlayers,
                currentMaster: timedOutMaster);

            Assert.AreEqual(nextMaster, master);
        }

        [Test]
        public void Elect_UsesSyncedPlayTimeFromArbiter()
        {
            Guid localClient = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();

            List<NetworkPlayer> alivePlayers = new()
            {
                new NetworkPlayer(remotePlayer),
            };

            Guid master = MasterClientArbiter.Elect(
                localClient,
                playTimes: new Dictionary<Guid, float>
                {
                    [localClient] = 1f,
                    [remotePlayer] = 40f,
                },
                alivePlayers,
                currentMaster: Guid.Empty);

            Assert.AreEqual(remotePlayer, master);
        }
    }
}
