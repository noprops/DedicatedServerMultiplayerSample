#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Stores payload data and auth identifiers for clients. Entries remain available even after disconnection.
    /// </summary>
    public sealed class ConnectionDirectory
    {
        private readonly Dictionary<ulong, Dictionary<string, object>> _payloadByClient = new();
        private readonly Dictionary<ulong, string> _authByClient = new();

        /// <summary>
        /// Total number of payload entries tracked in the directory.
        /// </summary>
        public int Count => _payloadByClient.Count;

        /// <summary>
        /// Registers or updates payload information for the specified client.
        /// </summary>
        public void Register(ulong clientId, Dictionary<string, object> payload)
        {
            payload = payload != null
                ? new Dictionary<string, object>(payload)
                : new Dictionary<string, object>();

            var authId = ResolveAuthId(payload);

            _payloadByClient[clientId] = payload;
            _authByClient[clientId] = authId;
        }

        /// <summary>
        /// Retrieves the auth identifier associated with the specified client, if any.
        /// </summary>
        public bool TryGetAuthId(ulong clientId, out string authId)
        {
            return _authByClient.TryGetValue(clientId, out authId);
        }

        /// <summary>
        /// Retrieves the payload stored for the specified client. Returns null if the client has never registered.
        /// </summary>
        public Dictionary<string, object> GetPayload(ulong clientId)
        {
            return _payloadByClient.TryGetValue(clientId, out var payload) ? payload : null;
        }

        /// <summary>
        /// Returns all client identifiers that have registered payload data.
        /// </summary>
        public IReadOnlyCollection<ulong> GetClientIds()
        {
            return new List<ulong>(_payloadByClient.Keys);
        }

        /// <summary>
        /// Attempts to parse an auth identifier from serialized payload bytes.
        /// </summary>
        public bool TryParseAuthId(byte[] payloadBytes, out string authId)
        {
            authId = null;
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return false;
            }

            var payload = ConnectionPayloadSerializer.DeserializeFromBytes(payloadBytes);
            return TryExtractAuthId(payload, out authId);
        }

        /// <summary>
        /// Attempts to retrieve a payload value associated with the specified client.
        /// </summary>
        public bool TryGet(ulong clientId, string key, out object value)
        {
            value = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (!_payloadByClient.TryGetValue(clientId, out var payload) || payload == null)
            {
                return false;
            }

            if (!payload.TryGetValue(key, out var raw) || raw == null)
            {
                return false;
            }

            value = raw;
            return true;
        }

        /// <summary>
        /// Attempts to retrieve and cast a payload value for the specified client.
        /// </summary>
        public bool TryGet<T>(ulong clientId, string key, out T value)
        {
            value = default;
            if (!TryGet(clientId, key, out var boxed))
            {
                return false;
            }

            if (boxed is T t)
            {
                value = t;
                return true;
            }

            try
            {
                if (boxed is IConvertible)
                {
                    value = (T)Convert.ChangeType(boxed, typeof(T));
                    return true;
                }
            }
            catch
            {
                // ignored - conversion failure
            }

            return false;
        }

        /// <summary>
        /// Retrieves the specified keys for each client id and returns them as a nested dictionary.
        /// </summary>
        public Dictionary<ulong, Dictionary<string, object>> GetValues(IEnumerable<ulong> clientIds, params string[] keys)
        {
            var result = new Dictionary<ulong, Dictionary<string, object>>();
            if (clientIds == null || keys == null || keys.Length == 0)
            {
                return result;
            }

            foreach (var clientId in clientIds)
            {
                if (!_payloadByClient.TryGetValue(clientId, out var payload) || payload == null)
                {
                    continue;
                }

                Dictionary<string, object> row = null;
                foreach (var key in keys)
                {
                    if (string.IsNullOrEmpty(key))
                    {
                        continue;
                    }

                    if (!payload.TryGetValue(key, out var value) || value == null)
                    {
                        continue;
                    }

                    row ??= new Dictionary<string, object>();
                    row[key] = value;
                }

                if (row != null)
                {
                    result[clientId] = row;
                }
            }

            return result;
        }

        /// <summary>
        /// Clears all stored payload and auth data.
        /// </summary>
        public void Clear()
        {
            _payloadByClient.Clear();
            _authByClient.Clear();
        }

        private static string ResolveAuthId(Dictionary<string, object> payload)
        {
            return TryExtractAuthId(payload, out var authId) ? authId : "Unknown";
        }

        private static bool TryExtractAuthId(Dictionary<string, object> payload, out string authId)
        {
            authId = null;
            if (payload == null)
            {
                return false;
            }

            if (!payload.TryGetValue("authId", out var value) || value == null)
            {
                return false;
            }

            var str = value as string ?? value.ToString();
            if (string.IsNullOrWhiteSpace(str))
            {
                return false;
            }

            authId = str;
            return true;
        }
    }
}
#endif
