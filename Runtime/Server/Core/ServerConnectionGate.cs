#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Handles Netcode connection approval and defers approved responses until the gameplay scene is ready.
    /// </summary>
    public sealed class ServerConnectionGate : IDisposable
    {
        private const int k_MaxConnectPayload = 1024;

        private readonly NetworkManager m_NetworkManager;
        private readonly ConnectionDirectory m_Directory;
        private readonly IConnectionPolicy m_Policy;
        private readonly List<NetworkManager.ConnectionApprovalResponse> m_PendingApprovals = new();

        public ServerConnectionGate(NetworkManager networkManager, ConnectionDirectory directory, IConnectionPolicy policy)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            m_Directory = directory ?? throw new ArgumentNullException(nameof(directory));
            m_Policy = policy ?? throw new ArgumentNullException(nameof(policy));

            m_NetworkManager.ConnectionApprovalCallback = HandleApproval;
        }

        public Func<bool> SceneIsLoaded { get; set; } = () => false;
        public Func<int> CurrentPlayers { get; set; } = () => 0;
        public Func<int> Capacity { get; set; } = () => int.MaxValue;
        public Func<IReadOnlyCollection<string>> ExpectedAuthIds { get; set; } = Array.Empty<string>;
        public bool AllowNewConnections { get; set; } = true;

        public void ReleasePendingApprovals()
        {
            for (int i = 0; i < m_PendingApprovals.Count; i++)
            {
                var response = m_PendingApprovals[i];
                response.Pending = false;
                m_PendingApprovals[i] = response;
            }

            m_PendingApprovals.Clear();
        }

        public void Dispose()
        {
            m_PendingApprovals.Clear();
            if (m_NetworkManager != null)
            {
                m_NetworkManager.ConnectionApprovalCallback = null;
            }
        }

        private void HandleApproval(NetworkManager.ConnectionApprovalRequest request,
                                     NetworkManager.ConnectionApprovalResponse response)
        {
            if (!AllowNewConnections)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = "Game already started";
                return;
            }

            if (request.Payload.Length > k_MaxConnectPayload)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = "Payload too large";
                return;
            }

            var payload = ConnectionPayloadSerializer.DeserializeFromBytes(request.Payload);

            var (success, reason) = m_Policy.Validate(
                payload,
                CurrentPlayers(),
                Capacity(),
                ExpectedAuthIds(),
                m_Directory.IsAuthConnected);

            if (!success)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = reason;
                return;
            }

            m_Directory.Register(request.ClientNetworkId, payload);

            response.Approved = true;
            response.CreatePlayerObject = false;

            if (!SceneIsLoaded())
            {
                response.Pending = true;
                m_PendingApprovals.Add(response);
                return;
            }

            response.Pending = false;
        }
    }
}
#endif
