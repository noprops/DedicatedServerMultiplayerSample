#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Stores connection payloads and auth identifiers for connected clients.
    /// Acts as the single source of truth for connection metadata.
    /// </summary>
    public sealed class ConnectionDirectory
    {
        private readonly Dictionary<ulong, Dictionary<string, object>> _payloadByClient = new();
        private readonly Dictionary<ulong, string> _authByClient = new();
        private readonly Dictionary<string, int> _authRefCounts = new(StringComparer.Ordinal);

        public int Count => _authByClient.Count;

        public void Register(ulong clientId, Dictionary<string, object> payload)
        {
            payload = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var authId = ExtractAuthId(payload) ?? "Unknown";

            if (_authByClient.TryGetValue(clientId, out var previousAuth))
            {
                DecrementAuth(previousAuth);
            }

            _payloadByClient[clientId] = payload;
            _authByClient[clientId] = authId;
            IncrementAuth(authId);
        }

        public void Unregister(ulong clientId)
        {
            if (_authByClient.TryGetValue(clientId, out var authId))
            {
                _authByClient.Remove(clientId);
                DecrementAuth(authId);
            }

            _payloadByClient.Remove(clientId);
        }

        public bool TryGetAuthId(ulong clientId, out string authId)
        {
            return _authByClient.TryGetValue(clientId, out authId);
        }

        public bool IsAuthConnected(string authId)
        {
            if (string.IsNullOrEmpty(authId))
            {
                return false;
            }

            return _authRefCounts.TryGetValue(authId, out var count) && count > 0;
        }

        public Dictionary<string, object> GetPayload(ulong clientId)
        {
            return _payloadByClient.TryGetValue(clientId, out var payload) ? payload : null;
        }

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectionData()
        {
            var snapshot = new Dictionary<ulong, Dictionary<string, object>>(_payloadByClient.Count);
            foreach (var pair in _payloadByClient)
            {
                snapshot[pair.Key] = new Dictionary<string, object>(pair.Value);
            }

            return snapshot;
        }

        public void Clear()
        {
            _payloadByClient.Clear();
            _authByClient.Clear();
            _authRefCounts.Clear();
        }

        public static string ExtractAuthId(Dictionary<string, object> payload)
        {
            if (payload == null || !payload.TryGetValue("authId", out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                string s => s,
                int i => i.ToString(),
                long l => l.ToString(),
                double d => d.ToString(),
                bool b => b.ToString(),
                _ => value.ToString(),
            };
        }

        private void IncrementAuth(string authId)
        {
            if (_authRefCounts.TryGetValue(authId, out var count))
            {
                _authRefCounts[authId] = count + 1;
            }
            else
            {
                _authRefCounts[authId] = 1;
            }
        }

        private void DecrementAuth(string authId)
        {
            if (!_authRefCounts.TryGetValue(authId, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _authRefCounts.Remove(authId);
            }
            else
            {
                _authRefCounts[authId] = count - 1;
            }
        }
    }
}
#endif
