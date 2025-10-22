using System;
using System.Collections.Generic;
using System.Threading;
using Unity.Netcode;

namespace DedicatedServerMultiplayerSample.Server
{
    /// <summary>
    /// Tracks connected/disconnected player IDs and raises events when thresholds are reached.
    /// </summary>
    public sealed class PlayerConnectionTracker : IDisposable
    {
        private readonly HashSet<ulong> m_ConnectedPlayers = new();
        private readonly HashSet<ulong> m_DisconnectedPlayers = new();
        private readonly int m_RequiredPlayers;
        private bool m_ReadyNotified;

        public bool HasRequiredPlayers => m_ConnectedPlayers.Count >= m_RequiredPlayers;
        public IReadOnlyCollection<ulong> ConnectedClientIds => m_ConnectedPlayers;
        public IReadOnlyCollection<ulong> DisconnectedClientIds => m_DisconnectedPlayers;

        public event Action RequiredPlayersReady;
        public event Action AllPlayersDisconnected;

        public PlayerConnectionTracker(int requiredPlayers)
        {
            m_RequiredPlayers = Math.Max(1, requiredPlayers);

            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientConnectedCallback += OnConnect;
            nm.OnClientDisconnectCallback += OnDisconnect;

            if (nm.ConnectedClients != null)
            {
                foreach (var id in nm.ConnectedClients.Keys)
                {
                    m_ConnectedPlayers.Add(id);
                }
            }

            CheckRequiredPlayersReached();
        }

        private void OnConnect(ulong id)
        {
            if (!m_ConnectedPlayers.Add(id)) return;
            m_DisconnectedPlayers.Remove(id);
            CheckRequiredPlayersReached();
        }

        private void OnDisconnect(ulong id)
        {
            if (!m_ConnectedPlayers.Remove(id)) return;
            m_DisconnectedPlayers.Add(id);
            if (m_ConnectedPlayers.Count == 0)
            {
                AllPlayersDisconnected?.Invoke();
            }
        }

        private void CheckRequiredPlayersReached()
        {
            if (m_ReadyNotified || !HasRequiredPlayers) return;
            m_ReadyNotified = true;
            RequiredPlayersReady?.Invoke();
        }

        public void Dispose()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientConnectedCallback -= OnConnect;
            nm.OnClientDisconnectCallback -= OnDisconnect;
        }
    }
}
