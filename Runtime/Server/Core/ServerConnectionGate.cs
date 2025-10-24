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

        private readonly NetworkManager _networkManager;
        private readonly ConnectionDirectory _directory;
        private readonly IConnectionPolicy _policy;
        private readonly List<NetworkManager.ConnectionApprovalResponse> _pendingApprovals = new();

        public ServerConnectionGate(NetworkManager networkManager, ConnectionDirectory directory, IConnectionPolicy policy)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));

            _networkManager.ConnectionApprovalCallback = HandleApproval;
        }

        public Func<bool> SceneIsLoaded { get; set; } = () => false;
        public Func<int> CurrentPlayers { get; set; } = () => 0;
        public Func<int> Capacity { get; set; } = () => int.MaxValue;
        public Func<IReadOnlyCollection<string>> ExpectedAuthIds { get; set; } = Array.Empty<string>;
        public bool AllowNewConnections { get; set; } = true;

        public void ReleasePendingApprovals()
        {
            for (int i = 0; i < _pendingApprovals.Count; i++)
            {
                var response = _pendingApprovals[i];
                response.Pending = false;
                _pendingApprovals[i] = response;
            }

            _pendingApprovals.Clear();
        }

        public void Dispose()
        {
            _pendingApprovals.Clear();
            if (_networkManager != null)
            {
                _networkManager.ConnectionApprovalCallback = null;
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

            var (success, reason) = _policy.Validate(
                payload,
                CurrentPlayers(),
                Capacity(),
                ExpectedAuthIds(),
                _directory.IsAuthConnected);

            if (!success)
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = reason;
                return;
            }

            _directory.Register(request.ClientNetworkId, payload);

            response.Approved = true;
            response.CreatePlayerObject = false;

            if (!SceneIsLoaded())
            {
                response.Pending = true;
                _pendingApprovals.Add(response);
                return;
            }

            response.Pending = false;
        }
    }
}
#endif
