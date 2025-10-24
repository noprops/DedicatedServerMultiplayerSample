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

        public bool HasRequiredPlayers => _directory.Count >= _requiredPlayers;

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectionData()
        {
            return _directory.GetAllConnectionData();
        }

        public IReadOnlyCollection<ulong> GetKnownClientIds()
        {
            var snapshot = _directory.GetAllConnectionData();
            var result = new HashSet<ulong>(snapshot.Keys);
            result.UnionWith(_disconnectedClients);
            return result;
        }

        public void Dispose()
        {
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

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
