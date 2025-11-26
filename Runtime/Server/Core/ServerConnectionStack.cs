#if UNITY_SERVER || ENABLE_UCS_SERVER
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
    internal sealed class ServerConnectionStack : IDisposable
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

        public event Action AllPlayersDisconnected;

        public ServerConnectionStack(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);

            _tracker = new ServerConnectionTracker(_networkManager, _directory, 1);
            _tracker.AllPlayersDisconnected += HandleAllDisconnected;
            _networkManager.ConnectionApprovalCallback = OnApproval;
        }

        public void Configure(IReadOnlyList<string> expectedAuthIds, int teamCount)
        {
            _expectedAuthIds = expectedAuthIds ?? Array.Empty<string>();
            _tracker.UpdateRequiredPlayers(Mathf.Max(1, teamCount));
            _allowNewConnections = true;
            _sceneLoaded = false;
            _directory.Clear();
        }

        public async Task<bool> LoadSceneAsync(string sceneName, int timeoutMs, CancellationToken token)
        {
            var sceneLoader = new ServerSceneLoader(_networkManager);
            _sceneLoaded = false;

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

        public async Task<IReadOnlyList<ulong>> WaitForAllClientsAsync(TimeSpan timeout = default, CancellationToken token = default)
        {
            if (_tracker == null)
            {
                return Array.Empty<ulong>();
            }

            if (_tracker.HasRequiredPlayers)
            {
                _allowNewConnections = false;
                return _tracker.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
            }

            SimpleSignalAwaiter awaiter = timeout > TimeSpan.Zero
                ? new SimpleSignalAwaiter(timeout, token)
                : new SimpleSignalAwaiter(token);

            void Handler()
            {
                awaiter.OnSignal();
            }

            _tracker.AllClientsConnected += Handler;

            try
            {
                var signalled = await awaiter.WaitAsync(token);

                if (!signalled || _tracker == null || !_tracker.HasRequiredPlayers)
                {
                    return Array.Empty<ulong>();
                }

                _allowNewConnections = false;
                return _tracker.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
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

        public bool TryGetPlayerPayloadValue<T>(ulong clientId, string key, out T value)
        {
            return _directory.TryGet(clientId, key, out value);
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
    }
}
#endif
