using System;
using BananaParty.WebSocketRelay;
using NUnit.Framework;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Tests
{
    public class JsonStateTests
    {
        [Test]
        public void ShouldWriteAndReadPrimitives()
        {
            var output = new JsonStateOutput(prettyPrint: false, bracesOnNewLine: false);
            output.WriteInt("Score", 10);
            output.WriteInt("Level", 5);

            var input = new JsonStateInput(output.ToString());

            Assert.AreEqual(10, input.ReadInt("Score"));
            Assert.AreEqual(5, input.ReadInt("Level"));
        }

        [Test]
        public void ShouldWriteAndReadVector3()
        {
            var output = new JsonStateOutput(prettyPrint: false, bracesOnNewLine: false);
            output.WriteVector3("Position", new Vector3(1, 2, 3));

            var input = new JsonStateInput(output.ToString());

            Assert.AreEqual(new Vector3(1, 2, 3), input.ReadVector3("Position"));
        }

        [Test]
        public void ShouldHandlePrettyPrint()
        {
            var output = new JsonStateOutput(prettyPrint: true, bracesOnNewLine: true);
            output.WriteInt("X", 1);

            string json = output.ToString();

            Assert.IsTrue(json.Contains("\n"));
            Assert.IsTrue(json.Contains("\"X\":"));
        }

        [Test]
        public void ShouldPrettyPrintNetworkStateStructure()
        {
            Guid networkId = Guid.Parse("054c4725-f87c-4acd-98dc-81dcb03fd235");
            Guid networkOwner = Guid.Parse("b27c471a-b17d-4a89-8285-0dee8e74b771");

            var output = new JsonStateOutput(prettyPrint: true, bracesOnNewLine: true);
            output.BeginArrayElement();
            output.BeginObjectElement();
            output.WriteGuid("NetworkIdentifier", networkId);
            output.WriteGuid("NetworkOwner", networkOwner);
            output.WriteBool("NetworkAuthority", false);
            output.BeginArrayProperty("NetworkStates");
            output.BeginObjectElement();
            output.WriteString("StateName", "Character");
            output.WriteInt("_health", 100);
            output.WriteVector3("_position", Vector3.zero);
            output.EndObject();
            output.EndArray();
            output.EndObject();
            output.EndArray();

            string json = output.ToString();

            Assert.IsFalse(json.EndsWith("]]"), "Should not emit duplicate array closers.");
            Assert.IsFalse(json.EndsWith("}]"), "Root array closer should be on its own line.");
            Assert.IsTrue(json.TrimEnd().EndsWith("]"));
            Assert.AreEqual(CountOccurrences(json, '['), CountOccurrences(json, ']'));
            Assert.IsTrue(json.Contains("{\n"));
            Assert.IsTrue(json.Contains("}\n"));
            Assert.IsTrue(json.Contains("\"NetworkStates\":\n"));
            Assert.IsTrue(json.Contains("[\n"));
            Assert.IsTrue(json.Contains("]\n") || json.TrimEnd().EndsWith("]"));
            Assert.IsTrue(json.Contains("\"_position\":{\"x\":0,\"y\":0,\"z\":0}"));
        }

        private static int CountOccurrences(string text, char value)
        {
            int count = 0;
            foreach (char character in text)
            {
                if (character == value)
                    count++;
            }

            return count;
        }

        [Test]
        public void ShouldRoundTripPrettyPrintedNetworkStates()
        {
            Guid networkId1 = Guid.Parse("bf0c3839-ff9c-4ef4-9442-482648647d53");
            Guid networkOwner1 = Guid.Parse("bea8ee69-bdcf-4eda-8755-bf4c4a886c29");
            Guid networkId2 = Guid.Parse("5640008b-7dd5-4056-a15e-2c18d65e9018");
            Guid networkOwner2 = Guid.Parse("dcf6650b-88cb-42d7-8bda-1875e41a75fa");

            var characterState1 = new MockCharacterState { Health = 100, Position = new Vector3(1f, 2f, 3f) };
            var characterState2 = new MockCharacterState { Health = 75, Position = new Vector3(4f, 5f, 6f) };

            var output = new JsonStateOutput(prettyPrint: true, bracesOnNewLine: true);
            WriteNetworkSnapshot(
                output,
                (networkId1, networkOwner1, characterState1),
                (networkId2, networkOwner2, characterState2));

            characterState1.Health = 0;
            characterState1.Position = Vector3.zero;
            characterState2.Health = 0;
            characterState2.Position = Vector3.zero;

            var input = new JsonStateInput(output.ToString());
            ReadNetworkSnapshot(
                input,
                out Guid readNetworkId1,
                out Guid readNetworkOwner1,
                out MockCharacterState readCharacterState1,
                out Guid readNetworkId2,
                out Guid readNetworkOwner2,
                out MockCharacterState readCharacterState2);

            Assert.AreEqual(networkId1, readNetworkId1);
            Assert.AreEqual(networkOwner1, readNetworkOwner1);
            Assert.AreEqual(100, readCharacterState1.Health);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), readCharacterState1.Position);

            Assert.AreEqual(networkId2, readNetworkId2);
            Assert.AreEqual(networkOwner2, readNetworkOwner2);
            Assert.AreEqual(75, readCharacterState2.Health);
            Assert.AreEqual(new Vector3(4f, 5f, 6f), readCharacterState2.Position);
        }

        private static void WriteNetworkSnapshot(
            IStateOutput stateOutput,
            (Guid NetworkIdentifier, Guid NetworkOwner, MockCharacterState CharacterState) identity1,
            (Guid NetworkIdentifier, Guid NetworkOwner, MockCharacterState CharacterState) identity2)
        {
            stateOutput.BeginArrayElement();
            WriteIdentity(stateOutput, identity1.NetworkIdentifier, identity1.NetworkOwner, identity1.CharacterState);
            WriteIdentity(stateOutput, identity2.NetworkIdentifier, identity2.NetworkOwner, identity2.CharacterState);
            stateOutput.EndArray();
        }

        private static void WriteIdentity(
            IStateOutput stateOutput,
            Guid networkIdentifier,
            Guid networkOwner,
            MockCharacterState characterState)
        {
            stateOutput.BeginObjectElement();
            stateOutput.WriteGuid("NetworkIdentifier", networkIdentifier);
            stateOutput.WriteGuid("NetworkOwner", networkOwner);
            stateOutput.BeginArrayProperty("NetworkStates");
            stateOutput.BeginObjectElement();
            stateOutput.WriteString("StateName", characterState.NetworkStateName);
            characterState.WriteNetworkState(stateOutput);
            stateOutput.EndObject();
            stateOutput.EndArray();
            stateOutput.EndObject();
        }

        private static void ReadNetworkSnapshot(
            IStateInput stateInput,
            out Guid networkId1,
            out Guid networkOwner1,
            out MockCharacterState characterState1,
            out Guid networkId2,
            out Guid networkOwner2,
            out MockCharacterState characterState2)
        {
            stateInput.BeginArrayElement();
            characterState1 = ReadIdentity(stateInput, out networkId1, out networkOwner1);
            characterState2 = ReadIdentity(stateInput, out networkId2, out networkOwner2);
            stateInput.EndArray();
        }

        private static MockCharacterState ReadIdentity(
            IStateInput stateInput,
            out Guid networkIdentifier,
            out Guid networkOwner)
        {
            stateInput.BeginObjectElement();
            networkIdentifier = stateInput.ReadGuid("NetworkIdentifier");
            networkOwner = stateInput.ReadGuid("NetworkOwner");
            stateInput.BeginArrayProperty("NetworkStates");
            stateInput.BeginObjectElement();
            Assert.AreEqual(nameof(MockCharacterState), stateInput.ReadString("StateName"));
            var characterState = new MockCharacterState();
            characterState.ReadNetworkState(stateInput);
            stateInput.EndObject();
            stateInput.EndArray();
            stateInput.EndObject();
            return characterState;
        }

        private sealed class MockCharacterState : INetworkState
        {
            public string NetworkStateName => nameof(MockCharacterState);
            public int Health { get; set; }
            public Vector3 Position { get; set; }

            public void WriteNetworkState(IStateOutput stateOutput)
            {
                stateOutput.WriteInt("_health", Health);
                stateOutput.WriteVector3("_position", Position);
            }

            public void ReadNetworkState(IStateInput stateInput)
            {
                Health = stateInput.ReadInt("_health");
                Position = stateInput.ReadVector3("_position");
            }
        }

        [Test]
        public void ShouldRoundTripBinary()
        {
            using var output = new BinaryStateOutput();
            output.WriteInt("Score", 10);
            output.WriteVector3("Position", new Vector3(1, 2, 3));

            var input = new BinaryStateInput(output.GetBuffer());

            Assert.AreEqual(10, input.ReadInt("Score"));
            Assert.AreEqual(new Vector3(1, 2, 3), input.ReadVector3("Position"));
        }
    }
}
