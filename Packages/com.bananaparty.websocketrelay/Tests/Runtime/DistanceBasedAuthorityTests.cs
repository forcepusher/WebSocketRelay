using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class DistanceBasedAuthorityTests
    {
        private readonly List<UnityEngine.Object> _cleanup = new();

        [TearDown]
        public void TearDown()
        {
            for (int objectIndex = _cleanup.Count - 1; objectIndex >= 0; objectIndex -= 1)
            {
                if (_cleanup[objectIndex] != null)
                    UnityEngine.Object.DestroyImmediate(_cleanup[objectIndex]);
            }

            _cleanup.Clear();
        }

        private NetworkContext Track(NetworkContext context)
        {
            _cleanup.Add(context);
            return context;
        }

        private void Track(GameObject gameObject) => _cleanup.Add(gameObject);

        [Test]
        public void GetClosestAuthorityOrigin_ReturnsNull_WhenNoPlayersAreInSession()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());

            AuthorityOrigin closestAuthorityOrigin = context.GetClosestAuthorityOrigin(Vector3.zero);

            Assert.IsNull(closestAuthorityOrigin);
        }

        [Test]
        public void GetClosestAuthorityOrigin_ReturnsNearestPlayerToWorldObject()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid playerNear = Guid.NewGuid();
            Guid playerFar = Guid.NewGuid();

            NetworkIdentity nearPlayer = NetworkContextTestHelpers.CreatePlayerActor(context, playerNear, new Vector3(2f, 0f, 0f), "NearPlayer");
            NetworkContextTestHelpers.CreatePlayerActor(context, playerFar, new Vector3(20f, 0f, 0f), "FarPlayer");
            Track(nearPlayer.gameObject);

            AuthorityOrigin closestAuthorityOrigin = context.GetClosestAuthorityOrigin(new Vector3(3f, 0f, 0f));

            Assert.AreEqual(playerNear, closestAuthorityOrigin.NetworkIdentity.NetworkOwner);
        }

        [Test]
        public void GetClosestAuthorityOrigin_OnEqualDistance_KeepsFirstRegisteredPlayer()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid firstPlayer = Guid.NewGuid();
            Guid secondPlayer = Guid.NewGuid();

            NetworkIdentity firstRegisteredPlayer = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                firstPlayer,
                new Vector3(-5f, 0f, 0f),
                "FirstPlayer");
            NetworkContextTestHelpers.CreatePlayerActor(context, secondPlayer, new Vector3(5f, 0f, 0f), "SecondPlayer");
            Track(firstRegisteredPlayer.gameObject);

            AuthorityOrigin closestAuthorityOrigin = context.GetClosestAuthorityOrigin(Vector3.zero);

            Assert.AreEqual(firstPlayer, closestAuthorityOrigin.NetworkIdentity.NetworkOwner);
        }

        [Test]
        public void NetworkAuthority_OwnerBasedCharacter_KeepsAuthorityRegardlessOfDistance()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid localPlayer = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            NetworkIdentity localCharacter = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                localPlayer,
                new Vector3(0f, 0f, 0f),
                "LocalCharacter");
            NetworkContextTestHelpers.CreatePlayerActor(
                context,
                remotePlayer,
                new Vector3(0.1f, 0f, 0f),
                "RemoteCharacter");
            Track(localCharacter.gameObject);

            Assert.IsTrue(localCharacter.NetworkAuthority);
        }

        [Test]
        public void NetworkAuthority_DistanceBasedCrate_LocalPlayerClosest_HasAuthority()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid localPlayer = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            NetworkContextTestHelpers.CreatePlayerActor(context, localPlayer, new Vector3(0f, 0f, 0f), "LocalPlayer");
            NetworkContextTestHelpers.CreatePlayerActor(context, remotePlayer, new Vector3(30f, 0f, 0f), "RemotePlayer");

            NetworkIdentity crate = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                new Vector3(4f, 0f, 0f),
                remotePlayer,
                "Crate");
            Track(crate.gameObject);

            Assert.IsTrue(crate.NetworkAuthority);
        }

        [Test]
        public void NetworkAuthority_DistanceBasedCrate_RemotePlayerClosest_NoAuthority()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid localPlayer = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            NetworkContextTestHelpers.CreatePlayerActor(context, localPlayer, new Vector3(30f, 0f, 0f), "LocalPlayer");
            NetworkContextTestHelpers.CreatePlayerActor(context, remotePlayer, new Vector3(0f, 0f, 0f), "RemotePlayer");

            NetworkIdentity crate = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                new Vector3(4f, 0f, 0f),
                localPlayer,
                "Crate");
            Track(crate.gameObject);

            Assert.IsFalse(crate.NetworkAuthority);
        }

        [Test]
        public void NetworkAuthority_TransfersWhenCloserPlayerWalksToSharedCrate()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid localPlayer = Guid.NewGuid();
            Guid remotePlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            NetworkIdentity localPlayerActor = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                localPlayer,
                new Vector3(0f, 0f, 0f),
                "LocalPlayer");
            NetworkIdentity remotePlayerActor = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                remotePlayer,
                new Vector3(20f, 0f, 0f),
                "RemotePlayer");

            NetworkIdentity crate = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                new Vector3(5f, 0f, 0f),
                remotePlayer,
                "SharedCrate");
            Track(localPlayerActor.gameObject);
            Track(remotePlayerActor.gameObject);
            Track(crate.gameObject);

            Assert.IsTrue(crate.NetworkAuthority);

            localPlayerActor.transform.position = new Vector3(25f, 0f, 0f);

            Assert.IsFalse(crate.NetworkAuthority);

            context.LocalClientIdentity = remotePlayer;

            Assert.IsTrue(crate.NetworkAuthority);
        }

        [Test]
        public void NetworkAuthority_SoloPlayerNearPickup_HasAuthority()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid soloPlayer = Guid.NewGuid();
            context.LocalClientIdentity = soloPlayer;

            NetworkContextTestHelpers.CreatePlayerActor(context, soloPlayer, new Vector3(0f, 0f, 0f), "SoloPlayer");

            NetworkIdentity pickup = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                new Vector3(2f, 0f, 0f),
                Guid.NewGuid(),
                "Pickup");
            Track(pickup.gameObject);

            Assert.IsTrue(pickup.NetworkAuthority);
        }

        [Test]
        public void NetworkAuthority_DistanceBasedObjectWithNoPlayersNearby_HasNoAuthority()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            context.LocalClientIdentity = Guid.NewGuid();

            NetworkIdentity abandonedCrate = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                new Vector3(0f, 0f, 0f),
                Guid.NewGuid(),
                "AbandonedCrate");
            Track(abandonedCrate.gameObject);

            Assert.IsFalse(abandonedCrate.NetworkAuthority);
        }

        [UnityTest]
        public IEnumerator NetworkAuthority_PlayerDisconnect_ReassignsClosestRemainingPlayer()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid localPlayer = Guid.NewGuid();
            Guid disconnectingPlayer = Guid.NewGuid();
            context.LocalClientIdentity = localPlayer;

            GameObject disconnectingPlayerObject = NetworkContextTestHelpers
                .CreatePlayerActor(context, disconnectingPlayer, new Vector3(1f, 0f, 0f), "DisconnectingPlayer")
                .gameObject;
            NetworkContextTestHelpers.CreatePlayerActor(context, localPlayer, new Vector3(15f, 0f, 0f), "LocalPlayer");

            NetworkIdentity crate = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                new Vector3(2f, 0f, 0f),
                disconnectingPlayer,
                "Crate");
            Track(crate.gameObject);
            Track(disconnectingPlayerObject);

            Assert.IsFalse(crate.NetworkAuthority);

            disconnectingPlayerObject.SetActive(false);
            yield return null;

            Assert.AreEqual(1, NetworkContextTestHelpers.GetAuthorityOriginCount(context));
            Assert.IsTrue(crate.NetworkAuthority);
        }

        [Test]
        public void GetClosestAuthorityOrigin_ThreePlayerLobby_SelectsNearestToCentralObjective()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid northPlayer = Guid.NewGuid();
            Guid eastPlayer = Guid.NewGuid();
            Guid southPlayer = Guid.NewGuid();

            NetworkContextTestHelpers.CreatePlayerActor(context, northPlayer, new Vector3(0f, 0f, 8f), "NorthPlayer");
            NetworkIdentity eastPlayerActor = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                eastPlayer,
                new Vector3(6f, 0f, 0f),
                "EastPlayer");
            NetworkContextTestHelpers.CreatePlayerActor(context, southPlayer, new Vector3(0f, 0f, -12f), "SouthPlayer");
            Track(eastPlayerActor.gameObject);

            AuthorityOrigin closestAuthorityOrigin = context.GetClosestAuthorityOrigin(new Vector3(5f, 0f, 1f));

            Assert.AreEqual(eastPlayer, closestAuthorityOrigin.NetworkIdentity.NetworkOwner);
        }

        [Test]
        public void NetworkAuthority_TugOfWarOverPhysicsObject_FlipsAsPlayersTradePositions()
        {
            NetworkContext context = Track(NetworkContextTestHelpers.CreateContext());
            Guid playerA = Guid.NewGuid();
            Guid playerB = Guid.NewGuid();

            NetworkIdentity playerAActor = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                playerA,
                new Vector3(-6f, 0f, 0f),
                "PlayerA");
            NetworkIdentity playerBActor = NetworkContextTestHelpers.CreatePlayerActor(
                context,
                playerB,
                new Vector3(6f, 0f, 0f),
                "PlayerB");

            NetworkIdentity physicsBall = NetworkContextTestHelpers.CreateDistanceBasedObject(
                context,
                Vector3.zero,
                playerA,
                "PhysicsBall");
            Track(playerAActor.gameObject);
            Track(playerBActor.gameObject);
            Track(physicsBall.gameObject);

            context.LocalClientIdentity = playerA;
            Assert.IsTrue(physicsBall.NetworkAuthority);

            playerBActor.transform.position = new Vector3(-1f, 0f, 0f);
            context.LocalClientIdentity = playerB;
            Assert.IsTrue(physicsBall.NetworkAuthority);

            context.LocalClientIdentity = playerA;
            Assert.IsFalse(physicsBall.NetworkAuthority);

            playerAActor.transform.position = new Vector3(1f, 0f, 0f);
            context.LocalClientIdentity = playerA;
            Assert.IsTrue(physicsBall.NetworkAuthority);
        }
    }
}
