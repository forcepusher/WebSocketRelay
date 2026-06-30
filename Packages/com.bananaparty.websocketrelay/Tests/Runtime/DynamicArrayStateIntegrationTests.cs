using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BananaParty.WebSocketRelay.Tests
{
    public class DynamicArrayStateIntegrationTests
    {
        private static readonly Guid Id1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private static readonly Guid Id2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        private static readonly Guid Id3 = Guid.Parse("33333333-3333-3333-3333-333333333333");

        private static MockEntry Entry(Guid id, int value = 0)
        {
            var entry = new MockEntry { Value = value };
            entry.StateKey.Value = id;
            return entry;
        }

        private static string ServerAddress => $"ws://127.0.0.1:{TestParameters.RelayServerPort}";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return RelayServerLauncher.StartCoroutine();
        }

        [Test]
        public void ShouldWriteAndReadDynamicArrayWithMatchingCount()
        {
            var source = new List<MockEntry>
            {
                Entry(Id1, 10),
                Entry(Id2, 20)
            };
            var target = new List<MockEntry>
            {
                Entry(Id1),
                Entry(Id2)
            };

            RoundTrip(source, target);
            Assert.AreEqual(2, target.Count);
            Assert.AreEqual(10, target[0].Value);
            Assert.AreEqual(20, target[1].Value);

            BinaryRoundTrip(source, target);
            Assert.AreEqual(2, target.Count);
            Assert.AreEqual(10, target[0].Value);
            Assert.AreEqual(20, target[1].Value);
        }

        [Test]
        public void ShouldGrowDynamicArrayAndInvokeCreate()
        {
            var source = new List<MockEntry>
            {
                Entry(Id1, 10),
                Entry(Id2, 20),
                Entry(Id3, 30)
            };
            MockEntry existing = Entry(Id1);
            var target = new List<MockEntry> { existing };
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);
            Assert.AreEqual(3, target.Count);
            Assert.AreEqual(5, factory.CreateCount);
            Assert.AreEqual(3, factory.DisposeCount);
            Assert.AreSame(existing, target[0]);
            Assert.AreEqual(10, target[0].Value);
            Assert.AreEqual(20, target[1].Value);
            Assert.AreEqual(30, target[2].Value);

            // Reset factory for binary test
            var factoryBin = new MockEntryFactory();
            var targetBin = new List<MockEntry> { existing };
            BinaryRoundTrip(source, targetBin, factoryBin);
            Assert.AreEqual(3, targetBin.Count);
            Assert.AreEqual(10, targetBin[0].Value);
            Assert.AreEqual(20, targetBin[1].Value);
            Assert.AreEqual(30, targetBin[2].Value);
        }

        [Test]
        public void ShouldShrinkDynamicArrayAndInvokeDispose()
        {
            var source = new List<MockEntry> { Entry(Id1, 42) };
            var target = new List<MockEntry>
            {
                Entry(Id1, 1),
                Entry(Id2, 2),
                Entry(Id3, 3)
            };
            MockEntry removedOne = target[1];
            MockEntry removedTwo = target[2];
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);
            Assert.AreEqual(1, target.Count);
            Assert.AreEqual(42, target[0].Value);
            Assert.AreEqual(3, factory.DisposeCount);
            Assert.Contains(removedOne, factory.Disposed);
            Assert.Contains(removedTwo, factory.Disposed);

            var factoryBin = new MockEntryFactory();
            var targetBin = new List<MockEntry> { Entry(Id1, 1), Entry(Id2, 2), Entry(Id3, 3) };
            BinaryRoundTrip(source, targetBin, factoryBin);
            Assert.AreEqual(1, targetBin.Count);
            Assert.AreEqual(42, targetBin[0].Value);
        }

        [Test]
        public void ShouldUpdateExistingEntriesWithoutCreateOrDispose()
        {
            var source = new List<MockEntry>
            {
                Entry(Id1, 100),
                Entry(Id2, 200)
            };
            MockEntry first = Entry(Id1, 1);
            MockEntry second = Entry(Id2, 2);
            var target = new List<MockEntry> { first, second };
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);
            Assert.AreEqual(2, target.Count);
            Assert.AreEqual(2, factory.CreateCount);
            Assert.AreEqual(2, factory.DisposeCount);
            Assert.AreSame(first, target[0]);
            Assert.AreSame(second, target[1]);
            Assert.AreEqual(100, target[0].Value);
            Assert.AreEqual(200, target[1].Value);

            var factoryBin = new MockEntryFactory();
            var targetBin = new List<MockEntry> { Entry(Id1, 1), Entry(Id2, 2) };
            BinaryRoundTrip(source, targetBin, factoryBin);
            Assert.AreEqual(2, targetBin.Count);
            Assert.AreEqual(100, targetBin[0].Value);
            Assert.AreEqual(200, targetBin[1].Value);
        }

        [Test]
        public void ShouldNotMutateExistingStateOnReadFailure()
        {
            // Existing state must not be mutated if reading incoming data fails.
            // Snapshot is captured BEFORE mutations; read happens after snapshot;
            // therefore a read error aborts before any create/dispose of existing entries.

            var source = new List<MockEntry>
            {
                Entry(Id1, 42),
                Entry(Id2, 99)
            };

            var output = new BinaryStateOutput();
            new ObjectState("Root", new List<IState> { new DynamicArrayState<MockEntry>("Items", source) }).WriteState(output);

            byte[] data = output.ToArray();
            int truncationPoint = (int)(data.Length * 0.6f);
            if (truncationPoint < data.Length)
                Array.Resize(ref data, truncationPoint);

            MockEntry existingOne = Entry(Id1, -999);
            var target = new List<MockEntry> { existingOne };
            var factory = new MockEntryFactory();
            var targetState = new DynamicArrayState<MockEntry>("Items", target, factory);

            Assert.Throws<EndOfStreamException>(() =>
                new ObjectState("Root", new List<IState> { targetState })
                    .ReadState(new BinaryStateInput(data.AsMemory())));

            Assert.AreEqual(-999, existingOne.Value,
                "Existing entry Value must remain unchanged on read failure.");
        }

        [Test]
        public void ShouldOnlyCreateDisposeDelta()
        {
            // Only entries that actually changed should be created or disposed.
            // Existing Id1 is updated; orphaned Id3 is disposed; new Id2 is created.
            // Staging objects are internal to the input reader and counted by factory.

            var source = new List<MockEntry>
            {
                Entry(Id1, 50),
                Entry(Id2, 60)
            };
            MockEntry existingOne = Entry(Id1);
            MockEntry orphanThree = Entry(Id3);
            var target = new List<MockEntry> { existingOne, orphanThree };
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);

            Assert.AreEqual(2, target.Count);
            Assert.AreSame(existingOne, target[0]);
            Assert.AreEqual(50, target[0].Value);

            // 2 staging creates + 1 real create (Id2)
            Assert.AreEqual(3, factory.CreateCount);
            // 2 staging disposes + 1 orphan dispose (Id3)
            Assert.AreEqual(3, factory.DisposeCount);
            Assert.Contains(orphanThree, factory.Disposed,
                "Orphaned entry must be disposed.");

            var factoryBin = new MockEntryFactory();
            var targetBin = new List<MockEntry> { existingOne, orphanThree };
            BinaryRoundTrip(source, targetBin, factoryBin);

            Assert.AreEqual(2, targetBin.Count);
            Assert.AreSame(existingOne, targetBin[0]);
            Assert.AreEqual(50, targetBin[0].Value);
        }

        [Test]
        public void ShouldNotCreateOrDisposeForReorderedEntries()
        {
            // Same entries in different order must NOT trigger real creates/disposes.
            // Only internal staging objects are created/disposed by the reader.

            var source = new List<MockEntry>
            {
                Entry(Id3, 70),
                Entry(Id2, 60),
                Entry(Id1, 50)
            };
            MockEntry a = Entry(Id1);
            MockEntry b = Entry(Id2);
            MockEntry c = Entry(Id3);
            var target = new List<MockEntry> { a, b, c };
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);

            // 3 staging creates (internal), no real creates
            Assert.AreEqual(3, factory.CreateCount);
            // 3 staging disposes (internal), no orphan disposes — Disposed only contains staging objects, not originals
            Assert.AreEqual(3, factory.DisposeCount);
            Assert.IsFalse(factory.Disposed.Contains(a) || factory.Disposed.Contains(b) || factory.Disposed.Contains(c),
                "No original entries should be disposed when all exist in incoming.");

            Assert.AreEqual(3, target.Count);
            Assert.AreSame(a, target[2]);
            Assert.AreSame(b, target[1]);
            Assert.AreSame(c, target[0]);
        }

        [Test]
        public void ShouldShrinkWithoutFactory()
        {
            var source = new List<MockEntry> { Entry(Id1, 7) };
            var target = new List<MockEntry>
            {
                Entry(Id1, 1),
                Entry(Id2, 2)
            };

            RoundTrip(source, target);
            Assert.AreEqual(1, target.Count);
            Assert.AreEqual(7, target[0].Value);

            BinaryRoundTrip(source, target);
            Assert.AreEqual(1, target.Count);
            Assert.AreEqual(7, target[0].Value);
        }

        [Test]
        public void ShouldThrowWhenGrowingWithoutFactory()
        {
            var target = new List<MockEntry>();
            var itemsState = new DynamicArrayState<MockEntry>("Items", target);
            var root = new ObjectState("Root", new List<IState> { itemsState });
            var input = new JsonStateInput("{\"Root\":{\"Items\":[{\"Id\":\"11111111-1111-1111-1111-111111111111\",\"Value\":1},{\"Id\":\"22222222-2222-2222-2222-222222222222\",\"Value\":2}]}}");

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => root.ReadState(input));

            Assert.That(exception.Message, Does.Contain("requires at least 1 entries"));
            Assert.AreEqual(0, target.Count);

            // Binary test
            var sourceBin = new List<MockEntry> { Entry(Id1), Entry(Id2) };
            var sourceStateBin = new DynamicArrayState<MockEntry>("Items", sourceBin);
            var outputBin = new BinaryStateOutput();
            new ObjectState("Root", new List<IState> { sourceStateBin }).WriteState(outputBin);

            Assert.Throws<InvalidOperationException>(() =>
                new ObjectState("Root", new List<IState> { itemsState }).ReadState(new BinaryStateInput(outputBin.GetBuffer())));
        }

        [Test]
        public void ShouldHandleEmptyArrays()
        {
            var source = new List<MockEntry>();
            var target = new List<MockEntry> { Entry(Id1) };
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);
            Assert.AreEqual(0, target.Count);

            BinaryRoundTrip(source, target, factory);
            Assert.AreEqual(0, target.Count);
        }

        [Test]
        public void ShouldThrowOnMalformedData()
        {
            var target = new List<MockEntry>();
            var itemsState = new DynamicArrayState<MockEntry>("Items", target);
            var root = new ObjectState("Root", new List<IState> { itemsState });

            // JSON: Missing "Id" field in one of the entries.
            // Note: We provide an entry so that it passes the 'count' check first
            var jsonInput = new JsonStateInput("{\"Root\":{\"Items\":[{\"Value\":1}]}}");

            // Since target is empty, ReadDynamicArray throws InvalidOperationException before trying to read fields.
            // We expect either KeyNotFound (if it got to the field) or InvalidOperation (if count check failed).
            Assert.Throws<InvalidOperationException>(() => root.ReadState(jsonInput));

            // Binary: Truncated buffer (only 2 bytes instead of a full header/entry)
            var binaryInput = new BinaryStateInput(new byte[] { 0, 1 });
            Assert.Throws<EndOfStreamException>(() => root.ReadState(binaryInput));
        }

        [Test]
        public void ShouldReconcileByKeyAndPreserveExistingInstances()
        {
            var source = new List<MockEntry>
            {
                Entry(Id1, 10),
                Entry(Id2, 20)
            };
            MockEntry idTwo = Entry(Id2, 0);
            MockEntry idOne = Entry(Id1, 0);
            var target = new List<MockEntry> { idTwo, idOne };
            var factory = new MockEntryFactory();

            RoundTrip(source, target, factory);

            Assert.AreEqual(2, target.Count);
            Assert.AreEqual(2, factory.CreateCount);
            Assert.AreEqual(2, factory.DisposeCount);
            Assert.AreSame(idOne, target[0]);
            Assert.AreSame(idTwo, target[1]);
            Assert.AreEqual(10, target[0].Value);
            Assert.AreEqual(20, target[1].Value);
        }

        [Test]
        public void ShouldReadCurrentStateBeforeInvokingCreateOrDispose()
        {
            // Verifies that all staging creates (Guid.Empty) occur BEFORE any real create/dispose.
            // OrderedFactory tracks this ordering via FirstRealCreateIndex.

            var source = new List<MockEntry>
            {
                Entry(Id1, 10),
                Entry(Id2, 20)
            };
            MockEntry existingOne = Entry(Id1);
            MockEntry orphanThree = Entry(Id3);
            var target = new List<MockEntry> { existingOne, orphanThree };
            var factory = new OrderedFactory();

            RoundTrip(source, target, factory);

            Assert.AreEqual(2, factory.StagingCreateCount, "All incoming entries must create staging objects.");
            Assert.AreEqual(1, factory.RealCreateCount, "Only Id2 should trigger a real Create.");
            Assert.GreaterOrEqual(factory.FirstRealCreateIndex, 2,
                "Staging creates must precede any real mutations.");
        }

        [Test]
        public void ShouldReadCurrentStateBeforeInvokingCreateOrDispose_Binary()
        {
            // Verifies the same snapshot-then-mutate ordering for BinaryStateInput.
            // All staging creates (Guid.Empty) must occur BEFORE any real create/dispose.

            var source = new List<MockEntry>
            {
                Entry(Id1, 10),
                Entry(Id2, 20)
            };
            MockEntry existingOne = Entry(Id1);
            MockEntry orphanThree = Entry(Id3);
            var target = new List<MockEntry> { existingOne, orphanThree };
            var factory = new OrderedFactory();

            BinaryRoundTrip(source, target, factory);

            Assert.AreEqual(2, factory.StagingCreateCount, "All incoming entries must create staging objects.");
            Assert.AreEqual(1, factory.RealCreateCount, "Only Id2 should trigger a real Create.");
            Assert.GreaterOrEqual(factory.FirstRealCreateIndex, 2,
                "Staging creates must precede any real mutations in BinaryStateInput.");
        }

        [UnityTest]
        public IEnumerator ShouldSynchronizeDynamicArrayOverRelay()
        {
            yield return RelayServerLauncher.StartCoroutine();

            GameObject clientAObj = new GameObject("ClientA");
            GameObject clientBObj = new GameObject("ClientB");

            var stateA = clientAObj.AddComponent<MockGameStateWithDynamicItems>();
            var stateB = clientBObj.AddComponent<MockGameStateWithDynamicItems>();

            stateA.SetItems((Id1, 10), (Id2, 20));

            using RelayConnection relayA = new(ServerAddress);
            using RelayConnection relayB = new(ServerAddress);

            relayA.Connect();
            relayB.Connect();

            yield return new WaitWhile(() => !relayA.IsConnected || !relayB.IsConnected, TestParameters.ConnectTimeoutThreshold);
            Assert.IsTrue(relayA.IsConnected && relayB.IsConnected, "Relays failed to connect within timeout.");

            relayA.JoinRoom(999);
            relayB.JoinRoom(999);

            relayA.ProcessIncomingMessages();
            relayB.ProcessIncomingMessages();
            yield return null;

            JsonStateOutput writeGraph = new();
            stateA.WriteState(writeGraph);
            byte[] sentBytes = Encoding.UTF8.GetBytes(writeGraph.ToString());

            bool receivedDataCaptured = false;
            relayB.OnRoomMessage += (roomId, data) =>
            {
                if (roomId != 999 || receivedDataCaptured)
                    return;

                stateB.ReadState(new JsonStateInput(Encoding.UTF8.GetString(data)));
                receivedDataCaptured = true;
            };

            relayA.Send(999, sentBytes);

            yield return TestParameters.WaitForCondition(
                () => receivedDataCaptured,
                TestParameters.ReceiveTimeoutThreshold,
                () => relayB.ProcessIncomingMessages());

            Assert.IsTrue(receivedDataCaptured, "Room message was never processed.");
            Assert.AreEqual(2, stateB.Items.Count);
            Assert.AreEqual(4, stateB.CreateCount);
            Assert.AreEqual(10, stateB.Items[0].Value);
            Assert.AreEqual(20, stateB.Items[1].Value);

            UnityEngine.Object.DestroyImmediate(clientAObj);
            UnityEngine.Object.DestroyImmediate(clientBObj);
        }

        private static void RoundTrip(List<MockEntry> source, List<MockEntry> target)
        {
            var sourceState = new DynamicArrayState<MockEntry>("Items", source);
            var targetState = new DynamicArrayState<MockEntry>("Items", target);
            var output = new JsonStateOutput(prettyPrint: false, bracesOnNewLine: false);

            new ObjectState("Root", new List<IState> { sourceState }).WriteState(output);
            new ObjectState("Root", new List<IState> { targetState }).ReadState(new JsonStateInput(output.ToString()));
        }

        private static void RoundTrip(List<MockEntry> source, List<MockEntry> target, IFactory<MockEntry> factory)
        {
            var sourceState = new DynamicArrayState<MockEntry>("Items", source);
            var targetState = new DynamicArrayState<MockEntry>("Items", target, factory);
            var output = new JsonStateOutput(prettyPrint: false, bracesOnNewLine: false);

            new ObjectState("Root", new List<IState> { sourceState }).WriteState(output);
            new ObjectState("Root", new List<IState> { targetState }).ReadState(new JsonStateInput(output.ToString()));
        }

        private static void BinaryRoundTrip(List<MockEntry> source, List<MockEntry> target)
        {
            var sourceState = new DynamicArrayState<MockEntry>("Items", source);
            var targetState = new DynamicArrayState<MockEntry>("Items", target);
            var output = new BinaryStateOutput();

            new ObjectState("Root", new List<IState> { sourceState }).WriteState(output);
            new ObjectState("Root", new List<IState> { targetState }).ReadState(new BinaryStateInput(output.GetBuffer()));
        }

        private static void BinaryRoundTrip(List<MockEntry> source, List<MockEntry> target, IFactory<MockEntry> factory)
        {
            var sourceState = new DynamicArrayState<MockEntry>("Items", source);
            var targetState = new DynamicArrayState<MockEntry>("Items", target, factory);
            var output = new BinaryStateOutput();

            new ObjectState("Root", new List<IState> { sourceState }).WriteState(output);
            new ObjectState("Root", new List<IState> { targetState }).ReadState(new BinaryStateInput(output.GetBuffer()));
        }

        private class MockEntry : IKeyedState
        {
            public string StateName => string.Empty;
            public GuidState StateKey { get; } = new("Id", Guid.Empty);
            public int Value { get; set; }

            public void WriteState(IStateOutput stateOutput)
            {
                stateOutput.WriteGuid(StateKey.StateName, StateKey.Value);
                stateOutput.WriteInt("Value", Value);
            }

            public void ReadState(IStateInput stateInput)
            {
                StateKey.Value = stateInput.ReadGuid(StateKey.StateName);
                Value = stateInput.ReadInt("Value");
            }
        }

        private class MockEntryFactory : IFactory<MockEntry>
        {
            public int CreateCount { get; private set; }
            public int DisposeCount { get; private set; }
            public List<MockEntry> Disposed { get; } = new();

            public MockEntry Create(Guid id)
            {
                CreateCount++;
                return Entry(id);
            }

            public void Dispose(MockEntry entry)
            {
                DisposeCount++;
                Disposed.Add(entry);
            }
        }

        // Records factory operations to prove snapshot-then-mutate pattern.
        private class OrderedFactory : IFactory<MockEntry>
        {
            public int StagingCreateCount { get; private set; }      // Create(Guid.Empty) calls
            public int RealCreateCount { get; private set; }         // Create(actualKey) calls
            public List<Guid> RealCreatedKeys { get; } = new();
            public int DisposeCount { get; private set; }

            // Index in combined operation sequence where first real create occurs.
            public int FirstRealCreateIndex => _firstReal;
            private int _firstReal = int.MaxValue;

            public MockEntry Create(Guid id)
            {
                if (id == Guid.Empty)
                    StagingCreateCount++;
                else
                {
                    RealCreateCount++;
                    RealCreatedKeys.Add(id);
                    if (_firstReal == int.MaxValue)
                        _firstReal = StagingCreateCount + DisposeCount;
                }

                return Entry(id);
            }

            public void Dispose(MockEntry entry)
            {
                DisposeCount++;
            }
        }

        private class MockGameStateWithDynamicItems : MonoBehaviour, IState, IFactory<MockEntry>
        {
            private readonly List<MockEntry> _items = new();
            private DynamicArrayState<MockEntry> _itemsState;
            private List<IState> _states;

            public IReadOnlyList<MockEntry> Items => _items;
            public int CreateCount { get; private set; }
            public int DisposeCount { get; private set; }

            public string StateName => "MockGameStateWithDynamicItems";

            private void Awake()
            {
                _itemsState = new DynamicArrayState<MockEntry>("Items", _items, this);
                _states = new List<IState> { _itemsState };
            }

            public MockEntry Create(Guid id)
            {
                CreateCount++;
                return Entry(id);
            }

            public void Dispose(MockEntry entry) => DisposeCount++;

            public void SetItems(params (Guid id, int value)[] items)
            {
                _items.Clear();
                foreach ((Guid id, int value) in items)
                    _items.Add(Entry(id, value));
            }

            public void WriteState(IStateOutput stateOutput) => stateOutput.WriteObject(StateName, _states);

            public void ReadState(IStateInput stateInput) => stateInput.ReadObject(StateName, _states);
        }
    }
}
