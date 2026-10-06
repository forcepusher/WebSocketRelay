using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using BananaParty.WebSocketRelay.Transport;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace BananaParty.WebSocketRelay.SoakTest
{
    /// <summary>
    /// Headless client for multi-process reliability runs, started only when the soak arguments are present.
    /// Plays a session like the game sample does, reports what it sees to the soak orchestrator every second
    /// and runs the commands it gets back, such as freezing the main thread or sending a burst of RPCs.
    /// </summary>
    public class SoakClient : MonoBehaviour
    {
        private const string Channel = "soak";
        private const float SyncIntervalSeconds = 0.1f;
        private const float ReportIntervalSeconds = 1f;
        private const float ConnectRetrySeconds = 2f;
        private const int MaxErrorSamplesPerReport = 5;
        private const string HubGuid = "50a1c000-0000-4000-8000-000000000001";

        private static readonly Vector3[] SharedObjectPositions =
        {
            new(-10f, 0f, -10f),
            new(10f, 0f, -10f),
            new(10f, 0f, 10f),
            new(-10f, 0f, 10f),
        };

        private static readonly string[] SharedObjectGuids =
        {
            "50a1c000-0000-4000-8000-000000000010",
            "50a1c000-0000-4000-8000-000000000011",
            "50a1c000-0000-4000-8000-000000000012",
            "50a1c000-0000-4000-8000-000000000013",
        };

        private readonly SoakStats _stats = new();
        private readonly List<NetworkIdentity> _sharedObjects = new();
        private readonly List<string> _events = new();
        private readonly List<string> _errorSamples = new();
        private readonly object _logLock = new();
        private readonly Queue<string> _commands = new();

        private SoakOptions _options;
        private NetworkContext _context;
        private NetworkChannel _channel;
        private Network _network;
        private GameObject _templateHolder;
        private NetworkIdentity _avatarTemplate;
        private SoakHub _hub;
        private SoakAvatar _localAvatar;

        private bool _isSessionStarted;
        private RelayConnectionState _lastConnectionState;
        private float _connectRetryTimer;
        private float _syncTimer;
        private float _rpcTimer;
        private float _reportTimer;
        private float _rpcRate;
        private int _rpcSequence;
        private int _framesSinceReport;
        private int _errorCount;
        private int _exceptionCount;
        private int _warningCount;
        private int _reconnectingCount;
        private int _gaveUpCount;
        private int _connectFailureCount;
        private long _startTime;
        private bool _isQuitting;
        private float _quitDeadline;
        private UnityWebRequest _reportRequest;

        public static SoakClient Instance { get; private set; }

        public static Vector3 GetAvatarPosition(int clientIndex, float time)
        {
            // Every avatar tours the shared objects, starting at a different one, so owners keep changing.
            const float legSeconds = 6f;
            float leg = time / legSeconds + clientIndex * 1.37f;
            int from = (int)Mathf.Floor(leg) % SharedObjectPositions.Length;
            int to = (from + 1) % SharedObjectPositions.Length;
            float progress = Mathf.SmoothStep(0f, 1f, leg - Mathf.Floor(leg));
            return Vector3.Lerp(SharedObjectPositions[from], SharedObjectPositions[to], progress) * 1.2f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SoakOptions options = SoakOptions.Parse();
            if (options == null)
                return;

            GameObject clientObject = new(nameof(SoakClient));
            DontDestroyOnLoad(clientObject);
            clientObject.AddComponent<SoakClient>().Initialize(options);
        }

        private void Initialize(SoakOptions options)
        {
            Instance = this;
            _options = options;
            _rpcRate = options.RpcRate;
            _startTime = SoakClock.NowMilliseconds;

            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = options.TargetFrameRate;
            Application.runInBackground = true;
            Application.logMessageReceivedThreaded += OnLogMessage;

            _context = ScriptableObject.CreateInstance<NetworkContext>();
            SetField(_context, "_useBinary", options.UseBinary);
            SetField(_context, "_playerTimeoutSeconds", options.PlayerTimeoutSeconds);
            _channel = ScriptableObject.CreateInstance<NetworkChannel>();

            // Templates sit under an inactive parent, so they behave like prefab assets and never run themselves.
            _templateHolder = new GameObject("SoakTemplates");
            _templateHolder.SetActive(false);
            DontDestroyOnLoad(_templateHolder);
            _avatarTemplate = CreateAvatarTemplate();
            SetField(_context, "_networkPrefabs", new List<NetworkIdentity> { _avatarTemplate });

            CreateSceneObjects();

            _network = new Network(options.RelayAddress, _context);
            AddEvent($"started index={options.Index} relay={options.RelayAddress} binary={options.UseBinary} fps={options.TargetFrameRate}");
            Connect();
        }

        private void Update()
        {
            if (_isQuitting)
            {
                UpdateQuit();
                return;
            }

            RunCommands();
            _framesSinceReport++;

            if (_network.HasRelayClient)
                _network.ManualUpdate(Time.unscaledDeltaTime);

            TrackConnectionState();
            UpdateConnection();
            UpdateSession();

            _reportTimer += Time.unscaledDeltaTime;
            if (_reportTimer >= ReportIntervalSeconds)
            {
                _reportTimer = 0f;
                Report(isFinal: false);
            }
        }

        private void OnDestroy()
        {
            Application.logMessageReceivedThreaded -= OnLogMessage;
            if (Instance == this)
                Instance = null;
        }

        private void Connect()
        {
            if (_hub == null)
                CreateSceneObjects();

            _network.Connect(Guid.NewGuid());
        }

        private void UpdateConnection()
        {
            if (_network.HasRelayClient && _network.ConnectionState == RelayConnectionState.Disconnected)
            {
                // A failed first attempt keeps the relay client until it is released.
                _connectFailureCount++;
                _network.Disconnect();
            }

            if (_network.HasRelayClient)
                return;

            _isSessionStarted = false;
            _connectRetryTimer += Time.unscaledDeltaTime;
            if (_connectRetryTimer < ConnectRetrySeconds)
                return;

            _connectRetryTimer = 0f;
            Connect();
        }

        private void UpdateSession()
        {
            if (_network.IsConnected && !_isSessionStarted)
                StartSession();

            if (!_isSessionStarted)
                return;

            if (_network.IsConnected)
            {
                _syncTimer += Time.unscaledDeltaTime;
                if (_syncTimer >= SyncIntervalSeconds)
                {
                    _syncTimer = 0f;
                    _network.SendSyncIdentities();
                }
            }

            // Ticks keep coming while reconnecting, like gameplay RPCs would, and are queued until the connection is back.
            if (_rpcRate <= 0f)
                return;

            _rpcTimer += Time.unscaledDeltaTime;
            float interval = 1f / _rpcRate;
            while (_rpcTimer >= interval)
            {
                _rpcTimer -= interval;
                SendTick();
            }
        }

        private void StartSession()
        {
            _isSessionStarted = true;
            _network.SubscribeToChannel(Channel);
            _channel.SetChannel(Channel);

            NetworkIdentity avatarIdentity = _context.Instantiate(_avatarTemplate, Channel);
            _localAvatar = avatarIdentity.GetComponent<SoakAvatar>();
            _localAvatar.ClientIndex = _options.Index;
            _localAvatar.Padding = new string('p', _options.PaddingBytes);
            AddEvent($"session started guid={Short(_context.LocalClientIdentity)}");
        }

        private void SendTick()
        {
            if (_hub == null)
                return;

            _rpcSequence++;
            _hub.SendTick(_options.Index, _rpcSequence);
        }

        private void TrackConnectionState()
        {
            RelayConnectionState state = _network.ConnectionState;
            if (state == _lastConnectionState)
                return;

            if (state == RelayConnectionState.Reconnecting)
                _reconnectingCount++;

            if (state == RelayConnectionState.Disconnected && _lastConnectionState == RelayConnectionState.Reconnecting)
                _gaveUpCount++;

            AddEvent($"state {_lastConnectionState}->{state}");
            _lastConnectionState = state;
        }

        public void RecordState(int senderIndex, long latencyMilliseconds) => _stats.For(senderIndex).RecordState(latencyMilliseconds);

        public void RecordRpc(int senderIndex, int sequence, long latencyMilliseconds) => _stats.For(senderIndex).RecordRpc(sequence, latencyMilliseconds);

        private void RunCommands()
        {
            while (_commands.Count > 0)
            {
                string[] parts = _commands.Dequeue().Split(' ');
                switch (parts[0])
                {
                    case "hitch":
                        int hitchMilliseconds = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        AddEvent($"hitch {hitchMilliseconds} ms");
                        // Spins instead of sleeping, because WebGL has no threads to sleep.
                        Stopwatch hitch = Stopwatch.StartNew();
                        while (hitch.ElapsedMilliseconds < hitchMilliseconds)
                        {
                        }

                        break;
                    case "burst":
                        int burstCount = int.Parse(parts[1], CultureInfo.InvariantCulture);
                        AddEvent($"burst {burstCount}");
                        for (int index = 0; index < burstCount; index++)
                            SendTick();
                        break;
                    case "rate":
                        _rpcRate = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        AddEvent($"rate {_rpcRate}");
                        break;
                    case "freeze":
                        SoakAvatar.IsMovementFrozen = true;
                        AddEvent("freeze");
                        break;
                    case "quit":
                        Quit();
                        break;
                }
            }
        }

        private void Quit()
        {
            if (_isQuitting)
                return;

            _isQuitting = true;
            _quitDeadline = Time.realtimeSinceStartup + 3f;
            Report(isFinal: true);
            if (_network.HasRelayClient)
                _network.Disconnect(clearSession: false);
        }

        private void UpdateQuit()
        {
            // Waits for the final report to go out before the process exits.
            if (_reportRequest != null && !_reportRequest.isDone && Time.realtimeSinceStartup < _quitDeadline)
                return;

            Application.Quit();
        }

        private void Report(bool isFinal)
        {
            string json = BuildReport(isFinal);
            Debug.Log($"SOAK|{json}");

            if (string.IsNullOrEmpty(_options.CollectorUrl))
                return;

            if (_reportRequest != null && !_reportRequest.isDone && !isFinal)
                return;

            _reportRequest?.Dispose();
            _reportRequest = new UnityWebRequest($"{_options.CollectorUrl}/report?index={_options.Index}", UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 3,
            };
            _reportRequest.SetRequestHeader("Content-Type", "application/json");
            UnityWebRequestAsyncOperation operation = _reportRequest.SendWebRequest();
            UnityWebRequest request = _reportRequest;
            operation.completed += _ =>
            {
                if (request.result != UnityWebRequest.Result.Success)
                    return;

                foreach (string command in request.downloadHandler.text.Split('\n'))
                {
                    if (!string.IsNullOrWhiteSpace(command))
                        _commands.Enqueue(command.Trim());
                }
            };
        }

        private string BuildReport(bool isFinal)
        {
            long now = SoakClock.NowMilliseconds;
            StringBuilder json = new(2048);
            json.Append('{');
            AppendField(json, "type", isFinal ? "final" : "report");
            AppendField(json, "t", now);
            AppendField(json, "index", _options.Index);
            AppendField(json, "elapsed", (now - _startTime) / 1000.0);
            AppendField(json, "frames", _framesSinceReport);
            AppendField(json, "state", _network.ConnectionState.ToString());
            AppendField(json, "rtt", Math.Round(_network.RoundTripTimeSeconds * 1000.0, 1));
            AppendField(json, "pending", GetPendingSendBytes());
            AppendField(json, "interrupted", _context.IsConnectionInterrupted);
            AppendField(json, "guid", Short(_context.LocalClientIdentity));
            AppendField(json, "players", _context.NetworkPlayers.Count);
            AppendField(json, "identities", _context.NetworkIdentities.Count);
            AppendField(json, "rpcSent", _rpcSequence);
            AppendField(json, "reconnecting", _reconnectingCount);
            AppendField(json, "gaveUp", _gaveUpCount);
            AppendField(json, "connectFailures", _connectFailureCount);
            AppendAvatars(json);
            AppendSharedObjects(json);
            AppendSenders(json, isFinal);

            lock (_logLock)
            {
                AppendField(json, "errors", _errorCount);
                AppendField(json, "exceptions", _exceptionCount);
                AppendField(json, "warnings", _warningCount);
                AppendStrings(json, "errorSamples", _errorSamples);
                _errorSamples.Clear();
            }

            AppendStrings(json, "events", _events);
            _events.Clear();

            json.Length--;
            json.Append('}');
            _framesSinceReport = 0;
            return json.ToString();
        }

        private void AppendAvatars(StringBuilder json)
        {
            Dictionary<Guid, int> avatarsByOwner = new();
            List<int> remoteIndices = new();
            foreach (INetworkIdentity identity in _context.NetworkIdentities)
            {
                if (identity.PrefabName != _avatarTemplate.PrefabName)
                    continue;

                avatarsByOwner.TryGetValue(identity.NetworkAuthorityOwner, out int count);
                avatarsByOwner[identity.NetworkAuthorityOwner] = count + 1;

                SoakAvatar avatar = identity.GameObject.GetComponent<SoakAvatar>();
                if (!identity.NetworkAuthority && avatar != null)
                    remoteIndices.Add(avatar.ClientIndex);
            }

            int duplicates = 0;
            foreach (int count in avatarsByOwner.Values)
                duplicates += count - 1;

            remoteIndices.Sort();
            AppendField(json, "avatarOwners", avatarsByOwner.Count);
            AppendField(json, "duplicateAvatars", duplicates);
            json.Append("\"remoteAvatars\":[").Append(string.Join(",", remoteIndices)).Append("],");
        }

        private void AppendSharedObjects(StringBuilder json)
        {
            json.Append("\"shared\":[");
            foreach (NetworkIdentity sharedObject in _sharedObjects)
            {
                if (sharedObject == null)
                {
                    json.Append("null,");
                    continue;
                }

                json.Append('{');
                AppendField(json, "owner", Short(sharedObject.NetworkAuthorityOwner));
                AppendField(json, "version", sharedObject.NetworkAuthorityVersion);
                AppendField(json, "writes", sharedObject.GetComponent<SoakShared>().Writes);
                json.Length--;
                json.Append("},");
            }

            if (json[json.Length - 1] == ',')
                json.Length--;
            json.Append("],");
        }

        private void AppendSenders(StringBuilder json, bool isFinal)
        {
            json.Append("\"senders\":{");
            foreach (KeyValuePair<int, SoakSenderStats> pair in _stats.Senders)
            {
                SoakSenderStats stats = pair.Value;
                json.Append('"').Append(pair.Key).Append("\":{");
                AppendField(json, "states", stats.StateCount);
                AppendField(json, "stateLatencyAvg", stats.StateCount > 0 ? stats.StateLatencySum / stats.StateCount : 0);
                AppendField(json, "stateLatencyMax", stats.StateLatencyMax);
                AppendField(json, "rpcs", stats.RpcCount);
                AppendField(json, "rpcGaps", stats.RpcGaps);
                AppendField(json, "rpcDuplicates", stats.RpcDuplicates);
                AppendField(json, "rpcLatencyAvg", stats.RpcCount > 0 ? stats.RpcLatencySum / stats.RpcCount : 0);
                AppendField(json, "rpcLatencyMax", stats.RpcLatencyMax);
                AppendField(json, "lastRpcSequence", stats.LastRpcSequence);
                if (isFinal)
                {
                    AppendField(json, "totalStates", stats.TotalStates);
                    AppendField(json, "totalRpcs", stats.TotalRpcs);
                    AppendField(json, "totalRpcGaps", stats.TotalRpcGaps);
                    AppendField(json, "totalRpcDuplicates", stats.TotalRpcDuplicates);
                    AppendField(json, "totalStateLatencyMax", stats.TotalStateLatencyMax);
                    AppendField(json, "totalRpcLatencyMax", stats.TotalRpcLatencyMax);
                }

                json.Length--;
                json.Append("},");
                stats.ResetWindow();
            }

            if (json[json.Length - 1] == ',')
                json.Length--;
            json.Append("},");
        }

        private void OnLogMessage(string condition, string stackTrace, LogType type)
        {
            if (condition.StartsWith("SOAK|", StringComparison.Ordinal))
                return;

            lock (_logLock)
            {
                switch (type)
                {
                    case LogType.Exception:
                        _exceptionCount++;
                        break;
                    case LogType.Error:
                    case LogType.Assert:
                        _errorCount++;
                        break;
                    case LogType.Warning:
                        _warningCount++;
                        break;
                    default:
                        return;
                }

                if (_errorSamples.Count < MaxErrorSamplesPerReport)
                    _errorSamples.Add($"{type}: {condition}");
            }
        }

        private void AddEvent(string message) => _events.Add($"{(SoakClock.NowMilliseconds - _startTime) / 1000.0:0.00}s {message}");

        private int GetPendingSendBytes()
        {
            FieldInfo relayClientField = typeof(Network).GetField("_relayClient", BindingFlags.Instance | BindingFlags.NonPublic);
            return relayClientField?.GetValue(_network) is RelayClient relayClient ? relayClient.PendingSendBytes : 0;
        }

        private NetworkIdentity CreateAvatarTemplate()
        {
            GameObject avatarObject = new("SoakAvatar");
            avatarObject.transform.SetParent(_templateHolder.transform);
            NetworkIdentity identity = AddNetworkIdentity(avatarObject, "SoakAvatar", distanceBasedAuthority: false);
            AuthorityOrigin authorityOrigin = avatarObject.AddComponent<AuthorityOrigin>();
            SetField(authorityOrigin, "_networkContext", _context);
            avatarObject.AddComponent<SoakAvatar>();
            return identity;
        }

        private void CreateSceneObjects()
        {
            _sharedObjects.Clear();
            for (int index = 0; index < SharedObjectPositions.Length; index++)
            {
                NetworkIdentity sharedObject = CreateSceneIdentity($"SoakShared{index}", SharedObjectGuids[index], SharedObjectPositions[index], distanceBasedAuthority: true, typeof(SoakShared));
                _sharedObjects.Add(sharedObject);
            }

            NetworkIdentity hubIdentity = CreateSceneIdentity("SoakHub", HubGuid, new Vector3(0f, -1000f, 0f), distanceBasedAuthority: false, typeof(SoakHub));
            _hub = hubIdentity.GetComponent<SoakHub>();
        }

        /// <summary>
        /// Builds an object the way it would be placed in a scene, with a <see cref="NetworkBinding"/> and a fixed identifier.
        /// </summary>
        private NetworkIdentity CreateSceneIdentity(string name, string guid, Vector3 position, bool distanceBasedAuthority, Type componentType)
        {
            GameObject sceneObject = new(name);
            sceneObject.SetActive(false);
            sceneObject.transform.position = position;
            NetworkIdentity identity = AddNetworkIdentity(sceneObject, name, distanceBasedAuthority);
            sceneObject.AddComponent(componentType);

            NetworkBinding binding = sceneObject.AddComponent<NetworkBinding>();
            SetField(binding, "_networkChannel", _channel);
            SetField(binding, "_guid", guid);

            sceneObject.SetActive(true);
            return identity;
        }

        private NetworkIdentity AddNetworkIdentity(GameObject gameObject, string prefabName, bool distanceBasedAuthority)
        {
            NetworkIdentity identity = gameObject.AddComponent<NetworkIdentity>();
            SetField(identity, "_networkContext", _context);
            SetField(identity, "_prefabName", prefabName);
            SetField(identity, "_distanceBasedAuthority", distanceBasedAuthority);
            return identity;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        private static string Short(Guid guid) => guid == Guid.Empty ? "none" : guid.ToString("N").Substring(0, 8);

        private static void AppendString(StringBuilder json, string value)
        {
            json.Append('"');
            foreach (char character in value)
            {
                if (character == '"' || character == '\\')
                    json.Append('\\').Append(character);
                else if (character < ' ')
                    json.Append(' ');
                else
                    json.Append(character);
            }

            json.Append('"');
        }

        private static void AppendField(StringBuilder json, string name, string value)
        {
            json.Append('"').Append(name).Append("\":");
            AppendString(json, value);
            json.Append(',');
        }

        private static void AppendField(StringBuilder json, string name, long value) => json.Append('"').Append(name).Append("\":").Append(value).Append(',');

        private static void AppendField(StringBuilder json, string name, double value) =>
            json.Append('"').Append(name).Append("\":").Append(value.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');

        private static void AppendField(StringBuilder json, string name, bool value) => json.Append('"').Append(name).Append("\":").Append(value ? "true" : "false").Append(',');

        private static void AppendStrings(StringBuilder json, string name, List<string> values)
        {
            json.Append('"').Append(name).Append("\":[");
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0)
                    json.Append(',');
                AppendString(json, values[index]);
            }

            json.Append("],");
        }
    }
}
