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

        /// <summary>
        /// True once the number of connected players meets or exceeds the required threshold.
        /// </summary>
        public bool HasRequiredPlayers => m_ConnectedPlayers.Count >= m_RequiredPlayers;

        /// <summary>
        /// Players currently connected to the server.
        /// </summary>
        public IReadOnlyCollection<ulong> ConnectedClientIds => m_ConnectedPlayers;

        /// <summary>
        /// Players that were connected but have since disconnected.
        /// </summary>
        public IReadOnlyCollection<ulong> DisconnectedClientIds => m_DisconnectedPlayers;

        /// <summary>
        /// Fired once when the required number of players is reached.
        /// </summary>
        public event Action RequiredPlayersReady;

        /// <summary>
        /// Fired whenever all players have disconnected.
        /// </summary>
        public event Action AllPlayersDisconnected;

        /// <summary>
        /// Creates a tracker that monitors player connections against the desired count.
        /// </summary>
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

        /// <summary>
        /// Handles newly connected clients.
        /// </summary>
        private void OnConnect(ulong id)
        {
            if (!m_ConnectedPlayers.Add(id)) return;
            m_DisconnectedPlayers.Remove(id);
            CheckRequiredPlayersReached();
        }

        /// <summary>
        /// Handles client disconnections.
        /// </summary>
        private void OnDisconnect(ulong id)
        {
            if (!m_ConnectedPlayers.Remove(id)) return;
            m_DisconnectedPlayers.Add(id);
            if (m_ConnectedPlayers.Count == 0)
            {
                AllPlayersDisconnected?.Invoke();
            }
        }

        /// <summary>
        /// Checks whether the required player count has been reached and raises the event once.
        /// </summary>
        private void CheckRequiredPlayersReached()
        {
            if (m_ReadyNotified || !HasRequiredPlayers) return;
            m_ReadyNotified = true;
            RequiredPlayersReady?.Invoke();
        }

        /// <summary>
        /// Releases network callbacks associated with this tracker.
        /// </summary>
        public void Dispose()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnClientConnectedCallback -= OnConnect;
            nm.OnClientDisconnectCallback -= OnDisconnect;
        }

        /// <summary>
        /// Returns a snapshot of all client IDs this tracker knows about (connected and disconnected).
        /// </summary>
        /// <summary>
        /// Returns a snapshot of every client ID the tracker knows about (connected or disconnected).
        /// </summary>
        public IReadOnlyCollection<ulong> GetKnownClientIds()
        {
            var result = new HashSet<ulong>(m_ConnectedPlayers.Count + m_DisconnectedPlayers.Count);
            result.UnionWith(m_ConnectedPlayers);
            result.UnionWith(m_DisconnectedPlayers);
            return result;
        }
    }
}
