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
        private readonly Dictionary<ulong, Dictionary<string, object>> m_PayloadByClient = new();
        private readonly Dictionary<ulong, string> m_AuthByClient = new();
        private readonly Dictionary<string, int> m_AuthRefCounts = new(StringComparer.Ordinal);

        public int Count => m_AuthByClient.Count;

        public void Register(ulong clientId, Dictionary<string, object> payload)
        {
            payload = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var authId = ExtractAuthId(payload) ?? "Unknown";

            if (m_AuthByClient.TryGetValue(clientId, out var previousAuth))
            {
                DecrementAuth(previousAuth);
            }

            m_PayloadByClient[clientId] = payload;
            m_AuthByClient[clientId] = authId;
            IncrementAuth(authId);
        }

        public void Unregister(ulong clientId)
        {
            if (m_AuthByClient.TryGetValue(clientId, out var authId))
            {
                m_AuthByClient.Remove(clientId);
                DecrementAuth(authId);
            }

            m_PayloadByClient.Remove(clientId);
        }

        public bool TryGetAuthId(ulong clientId, out string authId)
        {
            return m_AuthByClient.TryGetValue(clientId, out authId);
        }

        public bool IsAuthConnected(string authId)
        {
            if (string.IsNullOrEmpty(authId))
            {
                return false;
            }

            return m_AuthRefCounts.TryGetValue(authId, out var count) && count > 0;
        }

        public Dictionary<string, object> GetPayload(ulong clientId)
        {
            return m_PayloadByClient.TryGetValue(clientId, out var payload) ? payload : null;
        }

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectionData()
        {
            var snapshot = new Dictionary<ulong, Dictionary<string, object>>(m_PayloadByClient.Count);
            foreach (var pair in m_PayloadByClient)
            {
                snapshot[pair.Key] = new Dictionary<string, object>(pair.Value);
            }

            return snapshot;
        }

        public void Clear()
        {
            m_PayloadByClient.Clear();
            m_AuthByClient.Clear();
            m_AuthRefCounts.Clear();
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
            if (m_AuthRefCounts.TryGetValue(authId, out var count))
            {
                m_AuthRefCounts[authId] = count + 1;
            }
            else
            {
                m_AuthRefCounts[authId] = 1;
            }
        }

        private void DecrementAuth(string authId)
        {
            if (!m_AuthRefCounts.TryGetValue(authId, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                m_AuthRefCounts.Remove(authId);
            }
            else
            {
                m_AuthRefCounts[authId] = count - 1;
            }
        }
    }
}
#endif
