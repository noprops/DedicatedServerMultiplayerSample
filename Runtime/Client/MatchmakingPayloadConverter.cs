using System.Collections.Generic;
using Unity.Services.Multiplayer;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Client
{
    internal static class MatchmakingPayloadConverter
    {
        public static Dictionary<string, PlayerProperty> ToPlayerProperties(Dictionary<string, object> source)
        {
            var result = new Dictionary<string, PlayerProperty>();

            if (source == null)
            {
                return result;
            }

            foreach (var kvp in source)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                {
                    continue;
                }

                var stringValue = ConvertObjectToString(kvp.Value) ?? string.Empty;
                result[kvp.Key] = new PlayerProperty(stringValue);
            }

            return result;
        }

        public static Dictionary<string, SessionProperty> ToSessionProperties(Dictionary<string, object> source)
        {
            var result = new Dictionary<string, SessionProperty>();

            if (source == null)
            {
                return result;
            }

            foreach (var kvp in source)
            {
                if (string.IsNullOrEmpty(kvp.Key))
                {
                    continue;
                }

                var stringValue = ConvertObjectToString(kvp.Value) ?? string.Empty;
                result[kvp.Key] = new SessionProperty(stringValue);
            }

            return result;
        }

        public static byte[] ToConnectionPayload(Dictionary<string, object> connectionData, string authId)
        {
            var payload = connectionData != null
                ? new Dictionary<string, object>(connectionData)
                : new Dictionary<string, object>();

            if (!string.IsNullOrEmpty(authId))
            {
                payload["authId"] = authId;
            }

            return ConnectionPayloadSerializer.SerializeToBytes(payload);
        }

        private static string ConvertObjectToString(object value)
        {
            switch (value)
            {
                case null:
                    return null;
                case string str:
                    return str;
                case int i:
                    return i.ToString();
                case long l:
                    return l.ToString();
                case float f:
                    return f.ToString();
                case double d:
                    return d.ToString();
                case bool b:
                    return b.ToString();
                case PlayerProperty property:
                    return property.Value;
                default:
                    return value.ToString();
            }
        }
    }
}
