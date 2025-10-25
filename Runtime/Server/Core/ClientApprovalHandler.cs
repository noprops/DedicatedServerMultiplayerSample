#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Handles Netcode approval callbacks by validating auth identifiers and deferring pending responses until ready.
    /// </summary>
    internal sealed class ClientApprovalHandler : IDisposable
    {
        private readonly NetworkManager _network;
        private readonly ConnectionDirectory _directory;
        private readonly ServerConnectionGate _gate;
        private readonly Func<bool> _isSceneLoaded;
        private readonly List<NetworkManager.ConnectionApprovalResponse> _pending = new();

        public ClientApprovalHandler(NetworkManager network,
                                     ConnectionDirectory directory,
                                     ServerConnectionGate gate,
                                     Func<bool> isSceneLoaded)
        {
            _network = network ?? throw new ArgumentNullException(nameof(network));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _isSceneLoaded = isSceneLoaded ?? throw new ArgumentNullException(nameof(isSceneLoaded));

            _network.ConnectionApprovalCallback = OnApproval;
        }

        public void ReleasePending()
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                var response = _pending[i];
                response.Pending = false;
                _pending[i] = response;
            }
            _pending.Clear();
        }

        public void Dispose()
        {
            if (_network != null)
            {
                _network.ConnectionApprovalCallback = null;
            }
            _pending.Clear();
        }

        private void OnApproval(NetworkManager.ConnectionApprovalRequest request,
                                 NetworkManager.ConnectionApprovalResponse response)
        {
            if (!_directory.TryParseAuthId(request.Payload, out var authId))
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = "Missing authId";
                return;
            }

            if (!_gate.ShouldApprove(authId, out var reason))
            {
                response.Approved = false;
                response.Pending = false;
                response.Reason = reason;
                return;
            }

            var payload = ConnectionPayloadSerializer.DeserializeFromBytes(request.Payload)
                          ?? new Dictionary<string, object>();
            _directory.Register(request.ClientNetworkId, payload);

            response.Approved = true;
            response.CreatePlayerObject = false;

            if (!_isSceneLoaded())
            {
                response.Pending = true;
                _pending.Add(response);
            }
            else
            {
                response.Pending = false;
            }
        }
    }
}
#endif
