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
