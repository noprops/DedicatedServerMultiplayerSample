#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using Unity.Netcode;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Evaluates whether an incoming connection should be approved based on authId and capacity rules.
    /// </summary>
    public sealed class ServerConnectionGate
    {
        private readonly ConnectionDirectory _directory;

        /// <summary>
        /// Optional callback that reports whether an auth identifier is already in use.
        /// </summary>
        public Func<string, bool> AuthInUse { get; set; } = _ => false;

        /// <summary>
        /// Creates a new gate that encapsulates connection approval rules.
        /// </summary>
        public ServerConnectionGate(NetworkManager networkManager, ConnectionDirectory directory)
        {
            _ = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        }

        /// <summary>
        /// Provides the current number of connected players.
        /// </summary>
        public Func<int> CurrentPlayers { get; set; } = () => 0;
        /// <summary>
        /// Provides the maximum allowed number of players.
        /// </summary>
        public Func<int> Capacity { get; set; } = () => int.MaxValue;
        /// <summary>
        /// Supplies the list of expected auth identifiers, if any.
        /// </summary>
        public Func<IReadOnlyCollection<string>> ExpectedAuthIds { get; set; } = Array.Empty<string>;
        /// <summary>
        /// Indicates whether new connections are currently accepted.
        /// </summary>
        public bool AllowNewConnections { get; set; } = true;

        /// <summary>
        /// Determines whether the supplied auth identifier should be approved and produces a rejection reason when denied.
        /// </summary>
        public bool ShouldApprove(string authId, out string reason)
        {
            if (string.IsNullOrWhiteSpace(authId))
            {
                reason = "Missing authId";
                return false;
            }

            if (!AllowNewConnections)
            {
                reason = "Game already started";
                return false;
            }

            var currentPlayers = CurrentPlayers();
            var capacity = Capacity();
            if (currentPlayers >= Math.Max(1, capacity))
            {
                reason = "Server full";
                return false;
            }

            var expected = ExpectedAuthIds?.Invoke();
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
                    reason = "AuthId not expected";
                    return false;
                }
            }

            if (AuthInUse != null && AuthInUse(authId))
            {
                reason = "Duplicate login";
                return false;
            }

            reason = null;
            return true;
        }
    }
}
#endif
