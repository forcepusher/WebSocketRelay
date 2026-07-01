using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using BananaParty.WebSocketRelay;
using BananaParty.WebSocketRelay.Transport;

namespace BananaParty.WebSocketRelay.Tests
{
    public class JsonStateIntegrationTests
    {
        private static string ServerAddress => $"ws://127.0.0.1:{TestParameters.RelayServerPort}";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [UnityTest]
        public IEnumerator FullSerializationDeserializationFlow_OverRelay_Success()
        {
            // Arrange: Create two clients and their respective game states
            GameObject clientAObj = new GameObject("ClientA");
            GameObject clientBObj = new GameObject("ClientB");

            var stateA = clientAObj.AddComponent<MockGameState>();
            var stateB = clientBObj.AddComponent<MockGameState>();

            stateA.PlayTime = 10;
            stateA.Health = 80f;
            stateA.Position = new Vector3(1, 2, 3);

            TestRelayListener listenerA = new();
            TestRelayListener listenerB = new();
            using RelayClient relayA = new(ServerAddress, listenerA);
            using RelayClient relayB = new(ServerAddress, listenerB);

            relayA.Connect();
            relayB.Connect();

            yield return new WaitWhile(() => !relayA.IsConnected || !relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);
            Assert.IsTrue(relayA.IsConnected && relayB.IsConnected, "Relays failed to connect.");

            relayA.Subscribe("state-sync");
            relayB.Subscribe("state-sync");
            relayA.ProcessIncomingMessages();
            relayB.ProcessIncomingMessages();
            yield return null;

            // Act: Client A serializes and sends state via topic
            JsonStateOutput writeGraph = new();
            stateA.WriteState(writeGraph);
            byte[] sentBytes = Encoding.UTF8.GetBytes(writeGraph.ToString());

            bool captured = false;
            listenerB.TopicMessageReceived += (_, topic, data) =>
            {
                if (topic != "state-sync" || captured)
                    return;

                JsonStateInput readGraph = new(Encoding.UTF8.GetString(data));
                stateB.ReadState(readGraph);
                captured = true;
            };

            relayA.Send("state-sync", sentBytes);

            yield return TestParameters.WaitForCondition(
                () => captured,
                TestParameters.ReceiveTimeoutThreshold,
                () => relayB.ProcessIncomingMessages());

            Assert.IsTrue(captured, "Topic message was never processed.");

            // Assert: Verify values were synchronized
            Assert.AreEqual(stateA.PlayTime, stateB.PlayTime);
            Assert.AreEqual(stateA.Health, stateB.Health, 0.01f);
            Assert.AreEqual(stateA.Position, stateB.Position);

            UnityEngine.Object.DestroyImmediate(clientAObj);
            UnityEngine.Object.DestroyImmediate(clientBObj);
        }

        private class MockGameState : MonoBehaviour, IState
        {
            private IntegerState _playTimeState = new("PlayTime", 0);
            private FloatState _healthState = new("Health", 0f);
            private Vector3State _positionState = new("Position", Vector3.zero);
            private List<IState> _states;

            private List<IState> StatesList => _states ??= new List<IState>
            {
                _playTimeState,
                _healthState,
                _positionState
            };

            public int PlayTime
            {
                get => _playTimeState.Value;
                set => _playTimeState.Value = value;
            }

            public float Health
            {
                get => _healthState.Value;
                set => _healthState.Value = value;
            }

            public Vector3 Position
            {
                get => _positionState.Value;
                set => _positionState.Value = value;
            }

            public string StateName => "MockGameState";

            public void WriteState(IStateOutput stateOutput) => stateOutput.WriteObject(StateName, StatesList);

            public void ReadState(IStateInput stateInput) => stateInput.ReadObject(StateName, StatesList);
        }
    }
}
