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
        private readonly NetworkManager m_NetworkManager;
        private readonly ConnectionDirectory m_Directory;
        private readonly HashSet<ulong> m_DisconnectedClients = new();

        private int m_RequiredPlayers;
        private bool m_ReadyNotified;

        public ServerConnectionTracker(NetworkManager networkManager,
                                       ConnectionDirectory directory,
                                       int requiredPlayers)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            m_Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            m_RequiredPlayers = Math.Max(1, requiredPlayers);

            m_NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            CheckRequiredPlayersReached();
        }

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action RequiredPlayersReady;
        public event Action AllPlayersDisconnected;

        public bool HasRequiredPlayers => m_Directory.Count >= m_RequiredPlayers;

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectionData()
        {
            return m_Directory.GetAllConnectionData();
        }

        public IReadOnlyCollection<ulong> GetKnownClientIds()
        {
            var snapshot = m_Directory.GetAllConnectionData();
            var result = new HashSet<ulong>(snapshot.Keys);
            result.UnionWith(m_DisconnectedClients);
            return result;
        }

        public void UpdateRequiredPlayers(int requiredPlayers)
        {
            m_RequiredPlayers = Math.Max(1, requiredPlayers);
            if (m_Directory.Count < m_RequiredPlayers)
            {
                m_ReadyNotified = false;
            }

            CheckRequiredPlayersReached();
        }

        public void Dispose()
        {
            m_NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }

        private void HandleClientConnected(ulong clientId)
        {
            m_DisconnectedClients.Remove(clientId);

            if (!m_Directory.TryGetAuthId(clientId, out var authId))
            {
                authId = "Unknown";
            }

            Debug.Log($"[ConnectionTracker] Client connected. ClientId={clientId}, AuthId={authId}");
            Debug.Log($"[ConnectionTracker] Total clients: {m_Directory.Count}");

            var payload = m_Directory.GetPayload(clientId);
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
            m_Directory.Unregister(clientId);
            m_DisconnectedClients.Add(clientId);

            Debug.Log($"[ConnectionTracker] Client disconnected. ClientId={clientId}");
            Debug.Log($"[ConnectionTracker] Remaining clients: {m_Directory.Count}");

            if (m_Directory.Count == 0)
            {
                m_ReadyNotified = false;
                AllPlayersDisconnected?.Invoke();
            }

            ClientDisconnected?.Invoke(clientId);
        }

        private void CheckRequiredPlayersReached()
        {
            if (m_ReadyNotified || !HasRequiredPlayers)
            {
                return;
            }

            m_ReadyNotified = true;
            RequiredPlayersReady?.Invoke();
        }
    }
}
#endif
