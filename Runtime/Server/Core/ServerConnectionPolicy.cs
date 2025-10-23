#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    public interface IConnectionPolicy
    {
        (bool success, string reason) Validate(
            Dictionary<string, object> payload,
            int currentPlayerCount,
            int capacity,
            IReadOnlyCollection<string> expectedAuthIds,
            Func<string, bool> isAuthConnected);
    }

    /// <summary>
    /// Basic connection policy enforcing payload requirements, expected players, and capacity.
    /// </summary>
    public sealed class ServerConnectionPolicy : IConnectionPolicy
    {
        public (bool success, string reason) Validate(
            Dictionary<string, object> payload,
            int currentPlayerCount,
            int capacity,
            IReadOnlyCollection<string> expectedAuthIds,
            Func<string, bool> isAuthConnected)
        {
            if (payload == null)
            {
                return (false, "Missing connection payload");
            }

            var authId = ConnectionDirectory.ExtractAuthId(payload);

            if (string.IsNullOrEmpty(authId))
            {
                return (false, "Missing authId");
            }

            if (currentPlayerCount >= Math.Max(1, capacity))
            {
                return (false, "Server is full");
            }

            if (isAuthConnected != null && isAuthConnected(authId))
            {
                return (false, "Duplicate authId");
            }

            if (expectedAuthIds != null && expectedAuthIds.Count > 0)
            {
                var match = false;
                foreach (var expected in expectedAuthIds)
                {
                    if (string.Equals(expected, authId, StringComparison.Ordinal))
                    {
                        match = true;
                        break;
                    }
                }

                if (!match)
                {
                    return (false, "AuthId not in expected player list");
                }
            }

            return (true, null);
        }
    }
}
#endif
