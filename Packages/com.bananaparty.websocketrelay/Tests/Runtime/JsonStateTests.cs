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
