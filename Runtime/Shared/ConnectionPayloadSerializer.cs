using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace DedicatedServerMultiplayerSample.Shared
{
    /// <summary>
    /// Helper methods to serialize and deserialize connection payload dictionaries.
    /// </summary>
    public static class ConnectionPayloadSerializer
    {
        private static readonly JsonSerializerOptions s_JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false
        };

        /// <summary>
        /// Serialize a dictionary payload to UTF8 bytes.
        /// </summary>
        public static byte[] SerializeToBytes(Dictionary<string, object> payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return Array.Empty<byte>();
            }

            var json = JsonSerializer.Serialize(payload, s_JsonOptions);
            return Encoding.UTF8.GetBytes(json);
        }

        /// <summary>
        /// Deserialize UTF8 bytes into a dictionary payload.
        /// </summary>
        public static Dictionary<string, object> DeserializeFromBytes(byte[] payloadBytes)
        {
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                return new Dictionary<string, object>();
            }

            try
            {
                var json = Encoding.UTF8.GetString(payloadBytes);
                using var document = JsonDocument.Parse(json);
                return JsonElementToDictionary(document.RootElement);
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        private static Dictionary<string, object> JsonElementToDictionary(JsonElement element)
        {
            var dict = new Dictionary<string, object>();
            foreach (var property in element.EnumerateObject())
            {
                dict[property.Name] = ConvertJsonElement(property.Value);
            }
            return dict;
        }

        private static object ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    return JsonElementToDictionary(element);
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in element.EnumerateArray())
                    {
                        list.Add(ConvertJsonElement(item));
                    }
                    return list;
                case JsonValueKind.String:
                    return element.GetString();
                case JsonValueKind.Number:
                    if (element.TryGetInt64(out var longValue))
                    {
                        if (longValue >= int.MinValue && longValue <= int.MaxValue)
                        {
                            return (int)longValue;
                        }
                        return longValue;
                    }

                    if (element.TryGetDouble(out var doubleValue))
                    {
                        return doubleValue;
                    }

                    return element.GetRawText();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                case JsonValueKind.Null:
                    return null;
                default:
                    return element.GetRawText();
            }
        }
    }
}
