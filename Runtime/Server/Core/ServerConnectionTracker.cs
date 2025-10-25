#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Tracks connected/disconnected clients, exposes connection metadata, and raises threshold events.
    /// </summary>
    public sealed class ServerConnectionTracker : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly ConnectionDirectory _directory;
        private readonly HashSet<ulong> _disconnectedClients = new();

        private int _requiredPlayers;
        private bool _readyNotified;

        /// <summary>
        /// Creates a tracker that observes client connections using the provided NetworkManager and directory.
        /// </summary>
        public ServerConnectionTracker(NetworkManager networkManager,
                                       ConnectionDirectory directory,
                                       int requiredPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _requiredPlayers = Math.Max(1, requiredPlayers);

            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            CheckRequiredPlayersReached();
        }

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action RequiredPlayersReady;
        public event Action AllPlayersDisconnected;

        /// <summary>
        /// True when the number of connected players meets or exceeds the configured requirement.
        /// </summary>
        public bool HasRequiredPlayers => _directory.Count >= _requiredPlayers;

        /// <summary>
        /// Returns a snapshot of known client ids including those recently disconnected.
        /// </summary>
        public IReadOnlyCollection<ulong> GetKnownClientIds()
        {
            var result = new HashSet<ulong>(_directory.GetClientIds());
            result.UnionWith(_disconnectedClients);
            return result;
        }

        /// <summary>
        /// Unsubscribes from NetworkManager callbacks.
        /// </summary>
        public void Dispose()
        {
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        /// <summary>
        /// Handles newly connected clients, registers metadata, and emits events.
        /// </summary>
        private void HandleClientConnected(ulong clientId)
        {
            _disconnectedClients.Remove(clientId);

            if (!_directory.TryGetAuthId(clientId, out var authId))
            {
                authId = "Unknown";
            }

            Debug.Log($"[ConnectionTracker] Client connected. ClientId={clientId}, AuthId={authId}");
            Debug.Log($"[ConnectionTracker] Total clients: {_directory.Count}");

            var payload = _directory.GetPayload(clientId);
            if (payload is { Count: > 0 })
            {
                Debug.Log("[ConnectionTracker] Connection payload:");
                foreach (var kvp in payload)
                {
                    Debug.Log($"  - {kvp.Key}: {kvp.Value}");
                }
            }

            CheckRequiredPlayersReached();
            ClientConnected?.Invoke(clientId);
        }

        /// <summary>
        /// Handles client disconnects, unregisters metadata, and raises notifications.
        /// </summary>
        private void HandleClientDisconnected(ulong clientId)
        {
            _directory.Unregister(clientId);
            _disconnectedClients.Add(clientId);

            Debug.Log($"[ConnectionTracker] Client disconnected. ClientId={clientId}");
            Debug.Log($"[ConnectionTracker] Remaining clients: {_directory.Count}");

            if (_directory.Count == 0)
            {
                _readyNotified = false;
                AllPlayersDisconnected?.Invoke();
            }

            ClientDisconnected?.Invoke(clientId);
        }

        /// <summary>
        /// Emits the required players event when the threshold is first reached.
        /// </summary>
        private void CheckRequiredPlayersReached()
        {
            if (_readyNotified || !HasRequiredPlayers)
            {
                return;
            }

            _readyNotified = true;
            RequiredPlayersReady?.Invoke();
        }
    }
}
#endif
