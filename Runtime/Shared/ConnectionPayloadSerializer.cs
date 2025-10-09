using System;
using System.Collections.Generic;
using System.Collections;
using System.Text;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Shared
{
    /// <summary>
    /// Helper methods to serialize and deserialize connection payload dictionaries.
    /// </summary>
    public static class ConnectionPayloadSerializer
    {
        /// <summary>
        /// Serialize a dictionary payload to UTF8 bytes.
        /// </summary>
        public static byte[] SerializeToBytes(Dictionary<string, object> payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return Array.Empty<byte>();
            }

            var wrapper = new PayloadWrapper
            {
                root = ConvertToPayloadValue(payload)
            };

            var json = JsonUtility.ToJson(wrapper);
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
                var wrapper = JsonUtility.FromJson<PayloadWrapper>(json);
                return ConvertToDictionary(wrapper?.root) ?? new Dictionary<string, object>();
            }
            catch (Exception)
            {
                return new Dictionary<string, object>();
            }
        }

        private static PayloadValue ConvertToPayloadValue(object value)
        {
            if (value == null)
            {
                return new PayloadValue { type = PayloadValueType.Null };
            }

            switch (value)
            {
                case string s:
                    return new PayloadValue { type = PayloadValueType.String, stringValue = s };
                case bool b:
                    return new PayloadValue { type = PayloadValueType.Boolean, boolValue = b };
                case int i:
                    return new PayloadValue { type = PayloadValueType.Number, isIntegral = true, longValue = i };
                case long l:
                    return new PayloadValue { type = PayloadValueType.Number, isIntegral = true, longValue = l };
                case float f:
                    return new PayloadValue { type = PayloadValueType.Number, numberValue = f, isIntegral = false };
                case double d:
                    return new PayloadValue { type = PayloadValueType.Number, numberValue = d, isIntegral = false };
                case IDictionary<string, object> dict:
                    return ConvertDictionary(dict);
                case IDictionary dictionary:
                    var converted = new Dictionary<string, object>();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        converted[Convert.ToString(entry.Key)] = entry.Value;
                    }
                    return ConvertDictionary(converted);
                case IEnumerable enumerable when value is not string:
                    var arrayValue = new PayloadValue { type = PayloadValueType.Array };
                    arrayValue.arrayValues = new List<PayloadValue>();
                    foreach (var item in enumerable)
                    {
                        arrayValue.arrayValues.Add(ConvertToPayloadValue(item));
                    }
                    return arrayValue;
                default:
                    return new PayloadValue { type = PayloadValueType.String, stringValue = value.ToString() };
            }
        }

        private static PayloadValue ConvertDictionary(IDictionary<string, object> dict)
        {
            var payload = new PayloadValue
            {
                type = PayloadValueType.Object,
                objectValues = new List<PayloadEntry>()
            };

            foreach (var kvp in dict)
            {
                payload.objectValues.Add(new PayloadEntry
                {
                    key = kvp.Key,
                    value = ConvertToPayloadValue(kvp.Value)
                });
            }

            return payload;
        }

        private static Dictionary<string, object> ConvertToDictionary(PayloadValue value)
        {
            if (value == null || value.type != PayloadValueType.Object || value.objectValues == null)
            {
                return new Dictionary<string, object>();
            }

            var dict = new Dictionary<string, object>();
            foreach (var entry in value.objectValues)
            {
                if (entry == null)
                {
                    continue;
                }

                dict[entry.key] = ConvertPayloadValueToObject(entry.value);
            }
            return dict;
        }

        private static object ConvertPayloadValueToObject(PayloadValue value)
        {
            if (value == null)
            {
                return null;
            }

            switch (value.type)
            {
                case PayloadValueType.Null:
                    return null;
                case PayloadValueType.String:
                    return value.stringValue ?? string.Empty;
                case PayloadValueType.Boolean:
                    return value.boolValue;
                case PayloadValueType.Number:
                    if (value.isIntegral)
                    {
                        if (value.longValue >= int.MinValue && value.longValue <= int.MaxValue)
                        {
                            return (int)value.longValue;
                        }
                        return value.longValue;
                    }
                    return value.numberValue;
                case PayloadValueType.Array:
                    var list = new List<object>();
                    if (value.arrayValues != null)
                    {
                        foreach (var item in value.arrayValues)
                        {
                            list.Add(ConvertPayloadValueToObject(item));
                        }
                    }
                    return list;
                case PayloadValueType.Object:
                    return ConvertToDictionary(value);
                default:
                    return null;
            }
        }

        [Serializable]
        private class PayloadWrapper
        {
            public PayloadValue root;
        }

        [Serializable]
        private class PayloadValue
        {
            public PayloadValueType type;
            public string stringValue;
            public double numberValue;
            public bool boolValue;
            public bool isIntegral;
            public long longValue;
            public List<PayloadValue> arrayValues;
            public List<PayloadEntry> objectValues;
        }

        [Serializable]
        private class PayloadEntry
        {
            public string key;
            public PayloadValue value;
        }

        private enum PayloadValueType
        {
            Null = 0,
            String = 1,
            Number = 2,
            Boolean = 3,
            Array = 4,
            Object = 5
        }
    }
}
