using System;
using NUnit.Framework;

namespace BananaParty.WebSocketRelay.Tests
{
    public class SocketDisposalTests
    {
        // Nothing listens there, these tests only need a socket that started connecting.
        private const string UnreachableAddress = "ws://127.0.0.1:1";

        [Test]
        public void Dispose_BeforeConnect_DoesNotThrow()
        {
            Socket socket = new(UnreachableAddress);
            Assert.DoesNotThrow(() => socket.Dispose());
        }

        [Test]
        public void Dispose_MultipleTimes_DoesNotThrow()
        {
            Socket socket = new(UnreachableAddress);
            socket.Connect();
            socket.Dispose();
            Assert.DoesNotThrow(() => socket.Dispose());
        }

        [Test]
        public void Send_AfterDispose_ThrowsInvalidOperationException()
        {
            Socket socket = new(UnreachableAddress);
            socket.Connect();
            socket.Dispose();

            Assert.IsFalse(socket.IsConnected);
            Assert.Throws<InvalidOperationException>(() => socket.Send(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void ReadPayloadQueue_AfterDispose_ThrowsInvalidOperationException()
        {
            Socket socket = new(UnreachableAddress);
            socket.Connect();
            socket.Dispose();

            Assert.Throws<InvalidOperationException>(() => socket.ReadPayloadQueue());
        }
    }
}
