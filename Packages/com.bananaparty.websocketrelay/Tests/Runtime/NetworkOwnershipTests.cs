using System;
using System.Collections.Generic;
using BananaParty.WebSocketRelay;
using NUnit.Framework;
using UnityEngine;

namespace BananaParty.WebSocketRelay.Tests
{
    public class NetworkOwnershipTests
    {
        private static readonly Guid PlayerA = Guid.Parse("00000000-0000-0000-0000-000000000001");
        private static readonly Guid PlayerB = Guid.Parse("00000000-0000-0000-0000-000000000002");
        private static readonly Guid LocalClient = Guid.Parse("00000000-0000-0000-0000-000000000099");

        [Test]
        public void TryApplyOwnershipClaim_HigherVersionWins()
        {
            INetworkIdentity identity = CreateBotIdentity(PlayerA, networkOwnerVersion: 1);

            Assert.IsTrue(identity.TryApplyOwnershipClaim(PlayerB, 2));
            Assert.AreEqual(PlayerB, identity.NetworkOwner);
            Assert.AreEqual(2, identity.NetworkOwnerVersion);
        }

        [Test]
        public void TryApplyOwnershipClaim_LowerVersionRejected()
        {
            INetworkIdentity identity = CreateBotIdentity(PlayerB, networkOwnerVersion: 2);

            Assert.IsFalse(identity.TryApplyOwnershipClaim(PlayerA, 1));
            Assert.AreEqual(PlayerB, identity.NetworkOwner);
            Assert.AreEqual(2, identity.NetworkOwnerVersion);
        }

        [Test]
        public void TryApplyOwnershipClaim_EqualVersionLowerGuidWins()
        {
            INetworkIdentity identity = CreateBotIdentity(PlayerB, networkOwnerVersion: 1);

            Assert.IsTrue(identity.TryApplyOwnershipClaim(PlayerA, 1));
            Assert.AreEqual(PlayerA, identity.NetworkOwner);
            Assert.AreEqual(1, identity.NetworkOwnerVersion);
        }

        [Test]
        public void TryApplyOwnershipClaim_EqualVersionHigherGuidRejected()
        {
            INetworkIdentity identity = CreateBotIdentity(PlayerA, networkOwnerVersion: 1);

            Assert.IsFalse(identity.TryApplyOwnershipClaim(PlayerB, 1));
            Assert.AreEqual(PlayerA, identity.NetworkOwner);
            Assert.AreEqual(1, identity.NetworkOwnerVersion);
        }

        [Test]
        public void ConcurrentClaims_DifferentArrivalOrder_ConvergeToSameOwner()
        {
            Assert.Less(PlayerA.CompareTo(PlayerB), 0);

            INetworkIdentity claimPlayerAFirst = CreateBotIdentity(PlayerB);
            claimPlayerAFirst.TryApplyOwnershipClaim(PlayerA, 1);
            claimPlayerAFirst.TryApplyOwnershipClaim(PlayerB, 1);

            INetworkIdentity claimPlayerBFirst = CreateBotIdentity(PlayerB);
            claimPlayerBFirst.TryApplyOwnershipClaim(PlayerB, 1);
            claimPlayerBFirst.TryApplyOwnershipClaim(PlayerA, 1);

            Assert.AreEqual(PlayerA, claimPlayerAFirst.NetworkOwner);
            Assert.AreEqual(PlayerA, claimPlayerBFirst.NetworkOwner);
        }

        [Test]
        public void TakeAuthorityRpc_AppliesOwnershipClaim()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = LocalClient;

            NetworkIdentity authorityPlayer = NetworkContextTestHelpers.CreatePlayerActor(context, PlayerA, Vector3.zero);
            StubNetworkIdentity bot = CreateRegisteredBot(context, PlayerB);

            byte[] rpcMessage = NetworkContextTestHelpers.CreateRpcMessage(
                authorityPlayer.NetworkIdentifier,
                nameof(AuthorityOrigin),
                NetworkContextTestHelpers.CreateTakeAuthorityRpcParameters(bot.NetworkIdentifier, PlayerA, 1));

            context.ProcessChannelMessage(PlayerA, "room", rpcMessage);

            Assert.AreEqual(PlayerA, bot.NetworkOwner);
            Assert.AreEqual(1, bot.NetworkOwnerVersion);

            UnityEngine.Object.DestroyImmediate(authorityPlayer.gameObject);
            UnityEngine.Object.DestroyImmediate(bot.GameObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void ChannelStateSync_AppliesNetworkOwnerFromPayload()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = LocalClient;
            StubNetworkIdentity bot = CreateRegisteredBot(context, PlayerB);

            byte[] message = NetworkContextTestHelpers.CreateSyncIdentitiesMessage(
                bot,
                PlayerA,
                networkOwnerVersion: 1);

            context.ProcessChannelMessage(PlayerA, "room", message);

            Assert.AreEqual(PlayerA, bot.NetworkOwner);
            Assert.AreEqual(1, bot.NetworkOwnerVersion);

            UnityEngine.Object.DestroyImmediate(bot.GameObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void ChannelStateSync_RecoversMissedRpcViaOwnerBroadcast()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = LocalClient;
            StubNetworkState networkState = new();
            StubNetworkIdentity bot = CreateRegisteredBot(context, PlayerB, networkState);

            byte[] message = NetworkContextTestHelpers.CreateSyncIdentitiesMessage(
                bot,
                PlayerA,
                networkOwnerVersion: 1,
                componentValue: 42,
                includeComponentState: true);

            context.ProcessChannelMessage(PlayerA, "room", message);

            Assert.AreEqual(PlayerA, bot.NetworkOwner);
            Assert.AreEqual(1, bot.NetworkOwnerVersion);
            Assert.AreEqual(42, networkState.LastReadValue);

            UnityEngine.Object.DestroyImmediate(bot.GameObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        [Test]
        public void ChannelStateSync_RejectsStaleComponentStateAfterOwnershipTransfer()
        {
            NetworkContext context = NetworkContextTestHelpers.CreateContext();
            context.LocalClientIdentity = LocalClient;
            StubNetworkState networkState = new();
            StubNetworkIdentity bot = CreateRegisteredBot(context, PlayerB, networkState);
            bot.NetworkOwnerVersion = 1;

            byte[] message = NetworkContextTestHelpers.CreateSyncIdentitiesMessage(
                bot,
                PlayerB,
                networkOwnerVersion: 1,
                componentValue: 99,
                includeComponentState: true);

            context.ProcessChannelMessage(PlayerA, "room", message);

            Assert.AreEqual(PlayerB, bot.NetworkOwner);
            Assert.AreEqual(1, bot.NetworkOwnerVersion);
            Assert.AreEqual(0, networkState.LastReadValue);

            UnityEngine.Object.DestroyImmediate(bot.GameObject);
            UnityEngine.Object.DestroyImmediate(context);
        }

        private static StubNetworkIdentity CreateBotIdentity(Guid networkOwner, int networkOwnerVersion = 0)
        {
            return new StubNetworkIdentity(
                new GameObject("Bot"),
                "BotCharacter",
                networkOwner,
                Guid.NewGuid())
            {
                NetworkOwnerVersion = networkOwnerVersion,
                DistanceBasedAuthority = true
            };
        }

        private static StubNetworkIdentity CreateRegisteredBot(
            NetworkContext context,
            Guid networkOwner,
            StubNetworkState networkState = null)
        {
            IReadOnlyList<INetworkState> networkStates = networkState == null
                ? null
                : new INetworkState[] { networkState };

            StubNetworkIdentity bot = new(
                new GameObject("Bot"),
                "BotCharacter",
                networkOwner,
                Guid.NewGuid(),
                channel: "room",
                networkStates: networkStates)
            {
                DistanceBasedAuthority = true
            };

            context.RegisterNetworkIdentity(bot);
            return bot;
        }

        private sealed class StubNetworkState : INetworkState
        {
            public string NetworkStateName => nameof(StubNetworkState);

            public int LastReadValue { get; private set; }

            public void WriteNetworkState(IStateOutput stateOutput) =>
                throw new NotImplementedException();

            public void ReadNetworkState(IStateInput stateInput) =>
                LastReadValue = stateInput.ReadInt("value");
        }
    }
}
