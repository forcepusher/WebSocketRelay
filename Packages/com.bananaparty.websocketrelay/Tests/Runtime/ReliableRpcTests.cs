using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Tests
{
    /// <summary>
    /// Two network contexts passing reliable RPCs by hand, so each test decides what is lost, repeated or reordered.
    /// </summary>
    public class ReliableRpcTests
    {
        private const string Channel = "room";
        private const string Subject = "ReliableSubject";
        private static readonly Guid SenderGuid = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        private static readonly Guid ReceiverGuid = Guid.Parse("00000000-0000-0000-0000-00000000000b");

        private readonly List<UnityEngine.Object> _createdObjects = new();
        private NetworkContext _sender;
        private NetworkContext _receiver;
        private StubRpcTarget _receivedRpcs;
        private Guid _targetIdentifier;

        [SetUp]
        public void SetUp()
        {
            _sender = CreateContext(SenderGuid);
            _receiver = CreateContext(ReceiverGuid);

            GameObject target = new("ReliableTarget");
            _createdObjects.Add(target);
            _targetIdentifier = Guid.NewGuid();
            _receivedRpcs = new StubRpcTarget(new StubNetworkIdentity(target, "ReliableTarget", SenderGuid, _targetIdentifier, Channel), Subject);
            _receiver.RegisterRpcTarget(_receivedRpcs);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (UnityEngine.Object createdObject in _createdObjects)
                UnityEngine.Object.DestroyImmediate(createdObject);

            _createdObjects.Clear();
        }

        [Test]
        public void ReliableRpc_IsDeliveredOnceWhenItArrivesTwice()
        {
            SenderHearsReceiver();
            Send(1);
            List<byte[]> messages = CollectRpcs(_sender);

            Deliver(_receiver, SenderGuid, messages);
            Deliver(_receiver, SenderGuid, messages);

            CollectionAssert.AreEqual(new[] { 1 }, _receivedRpcs.ReceivedValues);
        }

        [Test]
        public void ReliableRpcs_AreDeliveredInOrderWhenTheyArriveOutOfOrder()
        {
            SenderHearsReceiver();
            Send(1);
            Send(2);
            Send(3);
            List<byte[]> messages = CollectRpcs(_sender);
            messages.Reverse();

            Deliver(_receiver, SenderGuid, messages);

            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, _receivedRpcs.ReceivedValues);
        }

        [Test]
        public void LostReliableRpc_IsSentAgainOnceTheReceiverIsHeardFrom()
        {
            SenderHearsReceiver();
            Send(1);
            CollectRpcs(_sender);

            Advance(1.2f);
            Assert.IsEmpty(CollectRpcs(_sender), "Resending to a receiver that went silent would only be lost too.");

            SenderHearsReceiver();
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));

            CollectionAssert.AreEqual(new[] { 1 }, _receivedRpcs.ReceivedValues);
        }

        [Test]
        public void AcknowledgedReliableRpc_IsNotSentAgain()
        {
            SenderHearsReceiver();
            Send(1);
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));
            Advance(0.2f);
            Deliver(_sender, ReceiverGuid, Collect(_receiver));

            Advance(1.2f);
            SenderHearsReceiver();

            Assert.IsEmpty(CollectRpcs(_sender));
        }

        [Test]
        public void Receiver_AcknowledgesWhatArrivedAndKeepsAcknowledgingWhileQuiet()
        {
            SenderHearsReceiver();
            Send(1);
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));

            Advance(0.2f);
            Assert.AreEqual(1, CountAcknowledgements(Collect(_receiver)));

            Advance(0.5f);
            Assert.AreEqual(0, CountAcknowledgements(Collect(_receiver)), "Nothing new arrived.");

            Advance(0.6f);
            Assert.AreEqual(1, CountAcknowledgements(Collect(_receiver)), "Quiet receivers stay heard, so senders can resend to them.");
        }

        [Test]
        public void ReceiverThatTimedOutOnTheSender_SkipsWhatTheSenderGaveUpOn()
        {
            NetworkContextTestHelpers.SetPlayerTimeoutSeconds(_sender, 1f);
            SenderHearsReceiver();
            Send(1);
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));
            Send(2);
            CollectRpcs(_sender);

            NetworkContextTestHelpers.Advance(_sender, 1.2f);
            Send(3);
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));

            CollectionAssert.AreEqual(new[] { 1, 3 }, _receivedRpcs.ReceivedValues);
        }

        [Test]
        public void SenderThatClearedItsSession_IsDeliveredFromItsNewStart()
        {
            SenderHearsReceiver();
            Send(1);
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));

            _sender.ClearNetworkSession();
            _sender.LocalClientIdentity = SenderGuid;
            SenderHearsReceiver();
            Send(2);
            Deliver(_receiver, SenderGuid, CollectRpcs(_sender));

            CollectionAssert.AreEqual(new[] { 1, 2 }, _receivedRpcs.ReceivedValues);
        }

        private NetworkContext CreateContext(Guid localClientGuid)
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = localClientGuid;
            _createdObjects.Add(context);
            return context;
        }

        private void Send(int value)
        {
            _sender.SendRpc(_targetIdentifier, Subject, NetworkContextTestHelpers.CreateRpcParameters(value), Channel, invokeLocally: false, reliable: true);
        }

        // The sender learns about peers from anything they send, like their syncs.
        private void SenderHearsReceiver()
        {
            _sender.ProcessChannelMessage(ReceiverGuid, Channel, NetworkContextTestHelpers.CreateEmptySyncIdentitiesMessage());
        }

        private void Advance(float seconds)
        {
            NetworkContextTestHelpers.Advance(_sender, seconds);
            NetworkContextTestHelpers.Advance(_receiver, seconds);
        }

        private static List<byte[]> Collect(NetworkContext context)
        {
            var outgoing = new List<(string channel, byte[] message)>();
            context.CollectReliableRpcMessages(outgoing);
            return outgoing.Select(entry => entry.message).ToList();
        }

        private static List<byte[]> CollectRpcs(NetworkContext context)
            => Collect(context).Where(message => message[0] == NetworkMessage.ReliableRpc).ToList();

        private static int CountAcknowledgements(IEnumerable<byte[]> messages)
            => messages.Count(message => message[0] == NetworkMessage.RpcAcknowledgement);

        private static void Deliver(NetworkContext receiver, Guid senderGuid, IEnumerable<byte[]> messages)
        {
            foreach (byte[] message in messages)
                receiver.ProcessChannelMessage(senderGuid, Channel, message);
        }
    }
}
