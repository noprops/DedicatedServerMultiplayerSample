#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMultiplayerSample.Shared;

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

            var authId = ResolveAuthId(payload);

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

        public IReadOnlyCollection<ulong> GetClientIds()
        {
            return new List<ulong>(_payloadByClient.Keys);
        }

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
                // ignored: conversion failure
            }

            return false;
        }

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

        public void Clear()
        {
            _payloadByClient.Clear();
            _authByClient.Clear();
            _authRefCounts.Clear();
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
