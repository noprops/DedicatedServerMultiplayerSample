using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Handles connection approval, player tracking, and scene loading for the server startup flow.
    /// </summary>
    public sealed class ServerConnectionManager : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly int _defaultMaxPlayers;
        private readonly ConnectionDirectory _directory = new();
        private readonly List<NetworkManager.ConnectionApprovalResponse> _pendingResponses = new();

        private ServerConnectionTracker _tracker;
        private IReadOnlyList<string> _expectedAuthIds = Array.Empty<string>();
        private bool _sceneLoaded;
        private bool _allowNewConnections = true;
        private bool _disposed;
        private bool _clientWaitCompleted;
        private IReadOnlyList<ulong> _readyClientsSnapshot = Array.Empty<ulong>();

        public event Action AllPlayersDisconnected;
        public event Action<ulong> ClientDisconnected;

        public ServerConnectionManager(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);

            _tracker = new ServerConnectionTracker(_networkManager, _directory, 1);
            _tracker.AllPlayersDisconnected += HandleAllDisconnected;
            _tracker.ClientDisconnected += HandleClientDisconnected;
            _networkManager.ConnectionApprovalCallback = OnApproval;
        }

        public void Configure(IReadOnlyList<string> expectedAuthIds, int teamCount)
        {
            _expectedAuthIds = expectedAuthIds ?? Array.Empty<string>();
            _tracker.UpdateRequiredPlayers(Mathf.Max(1, teamCount));
            _allowNewConnections = true;
            _sceneLoaded = false;
            _directory.Clear();
            _clientWaitCompleted = false;
            _readyClientsSnapshot = Array.Empty<ulong>();
        }

        public async Task<bool> LoadSceneAsync(string sceneName, int timeoutSeconds, CancellationToken token)
        {
            var sceneLoader = new ServerSceneLoader(_networkManager);
            _sceneLoaded = false;
            var timeoutMs = (int)TimeSpan.FromSeconds(timeoutSeconds).TotalMilliseconds;

            var loaded = await sceneLoader.LoadAsync(sceneName, timeoutMs, () =>
            {
                _sceneLoaded = true;
                ReleasePendingResponses();
            }, token);

            if (!loaded)
            {
                ReleasePendingResponses();
            }

            return loaded;
        }

        public async Task<IReadOnlyList<ulong>> WaitForAllClientsAsync()
        {
            if (_clientWaitCompleted)
            {
                return _readyClientsSnapshot ?? Array.Empty<ulong>();
            }

            if (_tracker == null)
            {
                _clientWaitCompleted = true;
                _readyClientsSnapshot = Array.Empty<ulong>();
                return _readyClientsSnapshot;
            }

            if (_tracker.HasRequiredPlayers)
            {
                _allowNewConnections = false;
                _readyClientsSnapshot = _tracker.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
                _clientWaitCompleted = true;
                return _readyClientsSnapshot;
            }

            var awaiter = new SimpleSignalAwaiter(CancellationToken.None);

            void Handler() => awaiter.OnSignal();

            _tracker.AllClientsConnected += Handler;

            try
            {
                await awaiter.WaitAsync();

                if (_tracker == null || !_tracker.HasRequiredPlayers)
                {
                    _clientWaitCompleted = true;
                    _readyClientsSnapshot = Array.Empty<ulong>();
                    return _readyClientsSnapshot;
                }

                _allowNewConnections = false;
                _readyClientsSnapshot = _tracker.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
                _clientWaitCompleted = true;
                return _readyClientsSnapshot;
            }
            finally
            {
                if (_tracker != null)
                {
                    _tracker.AllClientsConnected -= Handler;
                }

                awaiter.Dispose();
            }
        }

        public IReadOnlyList<ulong> GetReadyClientsSnapshot() => _readyClientsSnapshot ?? Array.Empty<ulong>();

        public bool TryGetPlayerPayloadValue<T>(ulong clientId, string key, out T value)
        {
            return _directory.TryGet(clientId, key, out value);
        }

        public bool TryGetPlayerName(ulong clientId, out string name)
        {
            return _directory.TryGet(clientId, "playerName", out name);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_tracker != null)
            {
                _tracker.AllPlayersDisconnected -= HandleAllDisconnected;
                _tracker.ClientDisconnected -= HandleClientDisconnected;
                _tracker.Dispose();
                _tracker = null;
            }

            if (_networkManager != null)
            {
                _networkManager.ConnectionApprovalCallback = null;
            }

            _pendingResponses.Clear();
        }

        private void OnApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            void Reject(string reason)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = reason;
            }

            var payload = ConnectionPayloadSerializer.DeserializeFromBytes(request.Payload)
                          ?? new Dictionary<string, object>();
            payload.TryGetValue("authId", out var authObj);
            var authId = authObj as string;
            if (string.IsNullOrWhiteSpace(authId))
            {
                Reject("Missing authId");
                return;
            }

            if (!_allowNewConnections)
            {
                Reject("Game already started");
                return;
            }
            
            int capacity = _expectedAuthIds.Count > 0 ? _expectedAuthIds.Count : _defaultMaxPlayers;
            if (_tracker.ActiveClientCount >= Math.Max(1, capacity))
            {
                Reject("Server full");
                return;
            }

            if (_expectedAuthIds.Count > 0 &&
                !_expectedAuthIds.Any(expected => string.Equals(expected, authId, StringComparison.Ordinal)))
            {
                Reject("AuthId not expected");
                return;
            }
            
            if (_tracker.IsAuthConnected(authId))
            {
                Reject("Duplicate login");
                return;
            }

            _directory.Register(request.ClientNetworkId, payload);

            response.Approved = true;
            response.CreatePlayerObject = false;

            if (!_sceneLoaded)
            {
                response.Pending = true;
                _pendingResponses.Add(response);
            }
            else
            {
                response.Pending = false;
            }
        }

        private void ReleasePendingResponses()
        {
            for (var i = 0; i < _pendingResponses.Count; i++)
            {
                var response = _pendingResponses[i];
                response.Pending = false;
                _pendingResponses[i] = response;
            }

            _pendingResponses.Clear();
        }

        private void HandleAllDisconnected()
        {
            AllPlayersDisconnected?.Invoke();
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            ClientDisconnected?.Invoke(clientId);
        }
    }
}
