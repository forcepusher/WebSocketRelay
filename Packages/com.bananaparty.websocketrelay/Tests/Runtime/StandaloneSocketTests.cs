#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Collections;
using BananaParty.WebSocketRelay.Transport;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class StandaloneSocketTests
    {
        private StandaloneSocket _socket;
        private TcpFaultProxy _proxy;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [TearDown]
        public void TearDown()
        {
            _socket?.Dispose();
            _proxy?.Dispose();
            _socket = null;
            _proxy = null;
        }

        [UnityTest]
        public IEnumerator Connect_NothingListening_ClosesWithReason()
        {
            System.Net.Sockets.TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            int unusedPort = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            _socket = new StandaloneSocket($"ws://127.0.0.1:{unusedPort}");
            _socket.Connect();
            yield return WaitUntil(() => _socket.IsClosed, TestParameters.ConnectTimeoutThreshold);

            Assert.IsTrue(_socket.IsClosed);
            Assert.IsFalse(_socket.IsConnected);
            StringAssert.StartsWith("Connect failed", _socket.CloseReason);
        }

        [UnityTest]
        public IEnumerator Disconnect_ClosesWithLocalReason()
        {
            yield return ConnectToRelay(TestParameters.RelayServerAddress);
            Assert.IsNull(_socket.CloseReason);
            Assert.IsFalse(_socket.IsClosed);

            _socket.Disconnect();
            yield return WaitUntil(() => _socket.IsClosed, TestParameters.DisconnectTimeoutThreshold);

            Assert.IsTrue(_socket.IsClosed);
            Assert.AreEqual("Disconnected locally", _socket.CloseReason);
        }

        [UnityTest]
        public IEnumerator ConnectionReset_ClosesWithReason()
        {
            _proxy = new TcpFaultProxy(TestParameters.RelayServerPort);
            yield return ConnectToRelay(_proxy.ServerAddress);

            _proxy.ResetAllConnections();
            yield return WaitUntil(() => _socket.IsClosed, TestParameters.DisconnectTimeoutThreshold);

            Assert.IsTrue(_socket.IsClosed);
            Assert.IsFalse(_socket.IsConnected);
            Assert.IsNotEmpty(_socket.CloseReason);
            Assert.AreNotEqual("Disconnected locally", _socket.CloseReason);
        }

        [UnityTest]
        public IEnumerator PendingSendBytes_ReturnsToZeroOnceSent()
        {
            yield return ConnectToRelay(TestParameters.RelayServerAddress);

            byte[] ping = RelayMessageCodec.CreatePingMessage(1d);
            for (int i = 0; i < 100; i++)
                _socket.Send(ping);
            Assert.Greater(_socket.PendingSendBytes, 0);

            yield return WaitUntil(() => _socket.PendingSendBytes == 0, TestParameters.ReceiveTimeoutThreshold);
            Assert.AreEqual(0, _socket.PendingSendBytes);
        }

        [UnityTest]
        public IEnumerator PendingSendBytes_GrowsWhileStalled()
        {
            _proxy = new TcpFaultProxy(TestParameters.RelayServerPort);
            yield return ConnectToRelay(_proxy.ServerAddress);
            _proxy.IsStalled = true;

            // Large enough to fill the loopback socket buffers so sends stop completing.
            byte[] payload = new byte[256 * 1024];
            for (int i = 0; i < 64; i++)
                _socket.Send(payload);
            yield return WaitForFrames(10);

            Assert.Greater(_socket.PendingSendBytes, 0);
            Assert.IsTrue(_socket.IsConnected);
        }

        [UnityTest]
        public IEnumerator Connect_Twice_Throws()
        {
            yield return ConnectToRelay(TestParameters.RelayServerAddress);

            Assert.Throws<InvalidOperationException>(() => _socket.Connect());
        }

        [Test]
        public void Disconnect_BeforeConnect_ClosesImmediately()
        {
            _socket = new StandaloneSocket(TestParameters.RelayServerAddress);

            _socket.Disconnect();

            Assert.IsTrue(_socket.IsClosed);
            Assert.AreEqual("Disconnected locally", _socket.CloseReason);
            Assert.Throws<InvalidOperationException>(() => _socket.Connect());
        }

        [Test]
        public void Send_BeforeConnect_Throws()
        {
            _socket = new StandaloneSocket(TestParameters.RelayServerAddress);

            Assert.Throws<InvalidOperationException>(() => _socket.Send(new byte[] { 1 }));
            Assert.AreEqual(0, _socket.PendingSendBytes);
        }

        private IEnumerator ConnectToRelay(string address)
        {
            _socket = new StandaloneSocket(address);
            _socket.Connect();
            yield return WaitUntil(() => _socket.IsConnected || _socket.IsClosed, TestParameters.ConnectTimeoutThreshold);
            Assert.IsTrue(_socket.IsConnected, $"Socket did not connect: {_socket.CloseReason}");
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            yield return TestParameters.WaitForCondition(condition, timeoutSeconds, null);
        }

        private static IEnumerator WaitForFrames(int frameCount)
        {
            for (int frame = 0; frame < frameCount; frame++)
                yield return null;
        }
    }
}
#endif
