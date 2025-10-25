#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Tracks active clients, raises threshold events, and exposes useful snapshots for external systems.
    /// </summary>
    public sealed class ServerConnectionTracker : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly ConnectionDirectory _directory;
        private readonly HashSet<ulong> _connectedClients = new();
        private readonly HashSet<ulong> _disconnectedClients = new();

        private int _requiredPlayers;
        private bool _readyNotified;

        public ServerConnectionTracker(NetworkManager networkManager,
                                       ConnectionDirectory directory,
                                       int requiredPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _requiredPlayers = Math.Max(1, requiredPlayers);

            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        }

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action RequiredPlayersReady;
        public event Action AllPlayersDisconnected;

        /// <summary>
        /// <summary>
        /// Current number of connected clients.
        /// </summary>
        public int ActiveClientCount => _connectedClients.Count;

        /// <summary>
        /// True when the number of currently connected clients meets the configured requirement.
        /// </summary>
        public bool HasRequiredPlayers => ActiveClientCount >= _requiredPlayers;

        /// <summary>
        /// Updates the required player threshold at runtime.
        /// </summary>
        public void UpdateRequiredPlayers(int requiredPlayers)
        {
            _requiredPlayers = Math.Max(1, requiredPlayers);
            CheckRequiredPlayersReached();
        }

        /// <summary>
        /// Provides the set of known client ids, including those that recently disconnected.
        /// </summary>
        public IReadOnlyCollection<ulong> GetKnownClientIds()
        {
            var result = new HashSet<ulong>(_connectedClients);
            result.UnionWith(_disconnectedClients);
            return result;
        }

        /// <summary>
        /// Determines whether the specified auth identifier is currently associated with an active client.
        /// </summary>
        public bool IsAuthConnected(string authId)
        {
            if (string.IsNullOrEmpty(authId))
            {
                return false;
            }

            foreach (var clientId in _connectedClients)
            {
                if (_directory.TryGetAuthId(clientId, out var stored) &&
                    string.Equals(stored, authId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public void Dispose()
        {
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            _connectedClients.Add(clientId);
            _disconnectedClients.Remove(clientId);

            if (!_directory.TryGetAuthId(clientId, out var authId))
            {
                authId = "Unknown";
            }

            Debug.Log($"[ConnectionTracker] Client connected. ClientId={clientId}, AuthId={authId}");
            Debug.Log($"[ConnectionTracker] Total clients: {_connectedClients.Count}");

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

        private void HandleClientDisconnected(ulong clientId)
        {
            _connectedClients.Remove(clientId);
            _disconnectedClients.Add(clientId);

            Debug.Log($"[ConnectionTracker] Client disconnected. ClientId={clientId}");
            Debug.Log($"[ConnectionTracker] Remaining clients: {_connectedClients.Count}");

            if (_connectedClients.Count == 0)
            {
                _readyNotified = false;
                AllPlayersDisconnected?.Invoke();
            }

            ClientDisconnected?.Invoke(clientId);
        }

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
