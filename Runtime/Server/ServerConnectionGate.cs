#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server
{
    /// <summary>
    /// Bridges Netcode connection events with ServerPlayerValidator, enforces approval logic,
    /// queues pending responses until the gameplay scene is ready, and mirrors client connect/disconnect logging.
    /// </summary>
    internal sealed class ServerConnectionGate : IDisposable
    {
        private readonly NetworkManager m_NetworkManager;
        private readonly ServerPlayerValidator m_PlayerValidator;
        private readonly List<NetworkManager.ConnectionApprovalResponse> m_PendingApprovals = new();
        private bool m_GameStarted;
        private bool m_IsInstalled;

        public ServerConnectionGate(NetworkManager networkManager, ServerPlayerValidator playerValidator)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            m_PlayerValidator = playerValidator ?? throw new ArgumentNullException(nameof(playerValidator));
        }

        /// <summary>
        /// Delegate used to query whether the game scene has finished loading.
        /// </summary>
        public Func<bool> SceneIsLoaded { get; set; } = () => false;

        public void Install()
        {
            if (m_IsInstalled)
            {
                return;
            }

            m_IsInstalled = true;
            m_NetworkManager.ConnectionApprovalCallback = HandleApproval;
            m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        public void SetGameStarted()
        {
            m_GameStarted = true;
        }

        public void ReleasePendings()
        {
            for (int i = 0; i < m_PendingApprovals.Count; i++)
            {
                var response = m_PendingApprovals[i];
                response.Pending = false;
                m_PendingApprovals[i] = response;
            }

            m_PendingApprovals.Clear();
        }

        private void HandleApproval(NetworkManager.ConnectionApprovalRequest request,
                                     NetworkManager.ConnectionApprovalResponse response)
        {
            Debug.Log($"[ConnectionGate] Approval request. ClientId={request.ClientNetworkId}");

            if (m_GameStarted)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = "Game already started";
                return;
            }

            var (success, payload, error) = m_PlayerValidator.ValidateConnectionRequest(request);
            if (!success)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = error;
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = false;

            var authId = ExtractString(payload, "authId") ?? "Unknown";
            m_PlayerValidator.RegisterConnectedPlayer(request.ClientNetworkId, authId);

            if (!IsSceneLoaded())
            {
                response.Pending = true;
                m_PendingApprovals.Add(response);
                return;
            }

            response.Pending = false;
        }

        private bool IsSceneLoaded()
        {
            return SceneIsLoaded?.Invoke() ?? false;
        }

        private void OnClientConnected(ulong clientId)
        {
            var authId = m_PlayerValidator.GetAuthId(clientId);
            Debug.Log($"[ConnectionGate] Client connected. ClientId={clientId}, AuthId={authId}");
            Debug.Log($"[ConnectionGate] Total clients: {m_NetworkManager.ConnectedClients.Count}");

            var payload = m_PlayerValidator.GetConnectionData(clientId);
            if (payload is { Count: > 0 })
            {
                Debug.Log("[ConnectionGate] Connection payload:");
                foreach (var kvp in payload)
                {
                    Debug.Log($"  - {kvp.Key}: {kvp.Value}");
                }
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[ConnectionGate] Client disconnected. ClientId={clientId}");
            m_PlayerValidator.HandlePlayerDisconnect(clientId);
            Debug.Log($"[ConnectionGate] Remaining clients: {m_NetworkManager.ConnectedClients.Count}");
        }

        public void Dispose()
        {
            m_PendingApprovals.Clear();

            if (!m_IsInstalled || m_NetworkManager == null)
            {
                return;
            }

            m_IsInstalled = false;
            m_NetworkManager.ConnectionApprovalCallback = null;
            m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private static string ExtractString(Dictionary<string, object> payload, string key)
        {
            if (payload == null || !payload.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                string str => str,
                int i => i.ToString(),
                long l => l.ToString(),
                double d => d.ToString(),
                bool b => b.ToString(),
                _ => value.ToString(),
            };
        }
    }
}
#endif
