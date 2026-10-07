using System;
using System.Collections;
using System.Text;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class JsonStateIntegrationTests
    {
        private const string Channel = "state-sync";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [UnityTest]
        public IEnumerator FullSerializationDeserializationFlow_OverRelay_Success()
        {
            MockGameState stateA = new() { PlayTime = 10, Health = 80f, Position = new Vector3(1, 2, 3), Name = "Quote \" and \\ backslash" };
            MockGameState stateB = new();

            TestRelayListener listenerA = new();
            TestRelayListener listenerB = new();
            using RelayClient relayA = new(TestParameters.RelayServerAddress, listenerA, Guid.NewGuid());
            using RelayClient relayB = new(TestParameters.RelayServerAddress, listenerB, Guid.NewGuid());

            relayA.Connect();
            relayB.Connect();
            yield return TestParameters.WaitUntilRelayConnected(relayA, relayB);
            Assert.IsTrue(relayA.IsConnected && relayB.IsConnected, "Relays failed to connect.");

            relayA.SubscribeToChannel(Channel);
            relayB.SubscribeToChannel(Channel);

            // The relay does not acknowledge subscriptions, so give them time to arrive before publishing.
            yield return TestParameters.WaitForDuration(0.1f, () =>
            {
                relayA.ProcessIncomingMessages();
                relayB.ProcessIncomingMessages();
            });

            JsonStateOutput stateOutput = new();
            stateA.WriteNetworkState(stateOutput);

            bool isReceived = false;
            listenerB.ChannelMessageReceived += (_, channel, data) =>
            {
                if (channel != Channel || isReceived)
                    return;

                stateB.ReadNetworkState(new JsonStateInput(Encoding.UTF8.GetString(data)));
                isReceived = true;
            };

            relayA.Send(Channel, Encoding.UTF8.GetBytes(stateOutput.ToString()));

            yield return TestParameters.WaitForCondition(
                () => isReceived,
                TestParameters.ReceiveTimeoutThreshold,
                () => relayB.ProcessIncomingMessages());

            Assert.IsTrue(isReceived, "Channel message was never processed.");
            Assert.AreEqual(stateA.PlayTime, stateB.PlayTime);
            Assert.AreEqual(stateA.Health, stateB.Health);
            Assert.AreEqual(stateA.Position, stateB.Position);
            Assert.AreEqual(stateA.Name, stateB.Name);
        }

        private sealed class MockGameState : INetworkState
        {
            public int PlayTime { get; set; }
            public float Health { get; set; }
            public Vector3 Position { get; set; }
            public string Name { get; set; } = string.Empty;

            public void WriteNetworkState(IStateOutput stateOutput)
            {
                stateOutput.WriteInt(nameof(PlayTime), PlayTime);
                stateOutput.WriteFloat(nameof(Health), Health);
                stateOutput.WriteVector3(nameof(Position), Position);
                stateOutput.WriteString(nameof(Name), Name);
            }

            public void ReadNetworkState(IStateInput stateInput)
            {
                PlayTime = stateInput.ReadInt(nameof(PlayTime));
                Health = stateInput.ReadFloat(nameof(Health));
                Position = stateInput.ReadVector3(nameof(Position));
                Name = stateInput.ReadString(nameof(Name));
            }
        }
    }
}
