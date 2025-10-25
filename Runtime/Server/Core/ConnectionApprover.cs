#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Centralizes connection approval wiring, validation, registration, and pending release.
    /// </summary>
    internal sealed class ConnectionApprover : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly Func<bool> _isSceneLoaded;
        private readonly Func<int> _currentPlayers;
        private readonly Func<int> _capacity;
        private readonly Func<IReadOnlyCollection<string>> _expectedAuthIds;
        private readonly Func<string, bool> _authInUse;
        private readonly Func<byte[], string> _resolveAuthId;
        private readonly Action<ulong, Dictionary<string, object>> _registerPayload;
        private readonly List<NetworkManager.ConnectionApprovalResponse> _pendingResponses = new();

        /// <summary>
        /// Creates a new approver that wires approval callbacks into the given network manager and draws
        /// decision context from the supplied delegates.
        /// </summary>
        public ConnectionApprover(
            NetworkManager networkManager,
            Func<bool> isSceneLoaded,
            Func<int> currentPlayers,
            Func<int> capacity,
            Func<IReadOnlyCollection<string>> expectedAuthIds,
            Func<string, bool> authInUse,
            Func<byte[], string> resolveAuthId,
            Action<ulong, Dictionary<string, object>> registerPayload)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _isSceneLoaded = isSceneLoaded ?? throw new ArgumentNullException(nameof(isSceneLoaded));
            _currentPlayers = currentPlayers ?? throw new ArgumentNullException(nameof(currentPlayers));
            _capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
            _expectedAuthIds = expectedAuthIds ?? throw new ArgumentNullException(nameof(expectedAuthIds));
            _authInUse = authInUse ?? throw new ArgumentNullException(nameof(authInUse));
            _resolveAuthId = resolveAuthId ?? throw new ArgumentNullException(nameof(resolveAuthId));
            _registerPayload = registerPayload ?? throw new ArgumentNullException(nameof(registerPayload));

            _networkManager.ConnectionApprovalCallback = OnApproval;
        }

        /// <summary>
        /// Gets or sets whether the approver currently accepts new connections.
        /// </summary>
        public bool AllowNewConnections { get; set; } = true;

        /// <summary>
        /// Releases any pending approval responses that were deferred until the scene finished loading.
        /// </summary>
        public void ReleasePending()
        {
            for (var i = 0; i < _pendingResponses.Count; i++)
            {
                var response = _pendingResponses[i];
                response.Pending = false;
                _pendingResponses[i] = response;
            }

            _pendingResponses.Clear();
        }

        /// <summary>
        /// Clears the approval callback from the network manager and pending responses.
        /// </summary>
        public void Dispose()
        {
            if (_networkManager != null)
            {
                _networkManager.ConnectionApprovalCallback = null;
            }

            _pendingResponses.Clear();
        }

        private void OnApproval(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            var authId = _resolveAuthId(request.Payload);
            if (string.IsNullOrWhiteSpace(authId))
            {
                Reject(response, "Missing authId");
                return;
            }

            if (!AllowNewConnections)
            {
                Reject(response, "Game already started");
                return;
            }

            var currentPlayers = _currentPlayers();
            var capacity = _capacity();
            if (currentPlayers >= Math.Max(1, capacity))
            {
                Reject(response, "Server full");
                return;
            }

            var expected = _expectedAuthIds();
            if (expected != null && expected.Count > 0)
            {
                var match = false;
                foreach (var expectedId in expected)
                {
                    if (string.Equals(expectedId, authId, StringComparison.Ordinal))
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                {
                    Reject(response, "AuthId not expected");
                    return;
                }
            }

            if (_authInUse(authId))
            {
                Reject(response, "Duplicate login");
                return;
            }

            var payload = ConnectionPayloadSerializer.DeserializeFromBytes(request.Payload)
                          ?? new Dictionary<string, object>();
            _registerPayload(request.ClientNetworkId, payload);

            response.Approved = true;
            response.CreatePlayerObject = false;

            if (!_isSceneLoaded())
            {
                response.Pending = true;
                _pendingResponses.Add(response);
            }
            else
            {
                response.Pending = false;
            }
        }

        private static void Reject(NetworkManager.ConnectionApprovalResponse response, string reason)
        {
            response.Approved = false;
            response.Pending = false;
            response.Reason = reason;
        }
    }
}
#endif
