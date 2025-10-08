#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Server
{
    /// <summary>
    /// プレイヤーの接続検証を行うクラス
    /// </summary>
    public class ServerPlayerValidator
    {
        // ========== Constants ==========
        private const int k_MaxConnectPayload = 1024;
        private readonly int m_FallbackMaxPlayers;

        // ========== Fields ==========
        private readonly NetworkManager m_NetworkManager;
        private readonly List<string> m_ExpectedPlayerIds;
        private readonly Dictionary<ulong, string> m_ConnectedPlayers;
        private readonly Dictionary<ulong, Dictionary<string, object>> m_ConnectionDataMap;

        // ========== Constructor ==========
        public ServerPlayerValidator(
            NetworkManager networkManager,
            List<string> expectedPlayerIds,
            Dictionary<ulong, string> connectedPlayers)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            m_ExpectedPlayerIds = expectedPlayerIds ?? new List<string>();
            m_ConnectedPlayers = connectedPlayers ?? new Dictionary<ulong, string>();
            m_ConnectionDataMap = new Dictionary<ulong, Dictionary<string, object>>();
            m_FallbackMaxPlayers = Mathf.Max(1, DedicatedServerMultiplayerSample.Shared.GameConfig.Instance.MaxHumanPlayers);
        }

        // ========== Public Methods ==========

        /// <summary>
        /// 接続リクエストを検証
        /// </summary>
        /// <returns>(success, connectionData, errorReason)</returns>
        public (bool success, Dictionary<string, object> connectionData, string errorReason) ValidateConnectionRequest(
            NetworkManager.ConnectionApprovalRequest request)
        {
            Debug.Log($"[ServerPlayerValidator] Validating connection request. ClientId: {request.ClientNetworkId}");

            // 1. ペイロードサイズチェック
            if (request.Payload.Length > k_MaxConnectPayload)
            {
                Debug.LogError($"[ServerPlayerValidator] Payload too large: {request.Payload.Length} / {k_MaxConnectPayload}");
                return (false, null, "Payload too large");
            }

            // ===== デバッグ用詳細ログ =====
            Debug.Log($"[ServerPlayerValidator] ===== PAYLOAD DEBUG START =====");
            Debug.Log($"[ServerPlayerValidator] Payload is null? {request.Payload == null}");
            Debug.Log($"[ServerPlayerValidator] Payload length: {request.Payload?.Length ?? 0}");

            if (request.Payload != null && request.Payload.Length > 0)
            {
                // バイト配列を16進数で表示
                string hexString = BitConverter.ToString(request.Payload).Replace("-", " ");
                Debug.Log($"[ServerPlayerValidator] Payload hex: {hexString}");

                // UTF8文字列として表示
                try
                {
                    string payloadString = System.Text.Encoding.UTF8.GetString(request.Payload);
                    Debug.Log($"[ServerPlayerValidator] Payload as string: '{payloadString}'");
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ServerPlayerValidator] Failed to decode as UTF8: {e.Message}");
                }
            }
            Debug.Log($"[ServerPlayerValidator] ===== PAYLOAD DEBUG END =====");

            // ===== ConnectionData JSON版 =====
            string playerId = null;
            Dictionary<string, object> connectionData = ConnectionPayloadSerializer.DeserializeFromBytes(request.Payload);

            if (connectionData.Count > 0)
            {
                Debug.Log($"[ServerPlayerValidator] ✓ Parsed connection payload with keys: {string.Join(", ", connectionData.Keys)}");
            }

            if (connectionData.Count > 0)
            {
                playerId = ExtractString(connectionData, "authId");
                Debug.Log($"[ServerPlayerValidator] ✓ Parsed connection payload. authId: '{playerId ?? "null"}'");
            }

            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogWarning("[ServerPlayerValidator] Payload missing authId. Attempting fallback.");

                if (m_ExpectedPlayerIds.Count > 0)
                {
                    foreach (var expectedId in m_ExpectedPlayerIds)
                    {
                        if (!m_ConnectedPlayers.ContainsValue(expectedId))
                        {
                            playerId = expectedId;
                            connectionData = BuildFallbackPayload(request.ClientNetworkId, expectedId);
                            break;
                        }
                    }
                }
            }

            // 検証結果
            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError("[ServerPlayerValidator] ✗ No valid PlayerId found!");
                return (false, null, "No valid PlayerId");
            }

            // 4. プレイヤー検証
            if (!ValidatePlayer(playerId, request.ClientNetworkId))
            {
                return (false, null, "Authentication failed or server is full");
            }

            // ConnectionDataを保存
            m_ConnectionDataMap[request.ClientNetworkId] = connectionData;

            return (true, connectionData, null);
        }

        /// <summary>
        /// プレイヤーを接続済みとして登録
        /// </summary>
        public void RegisterConnectedPlayer(ulong clientNetworkId, string playerId)
        {
            m_ConnectedPlayers[clientNetworkId] = playerId;
            Debug.Log($"[ServerPlayerValidator] Registered player - ClientId: {clientNetworkId}, PlayerId: {playerId}");
        }

        /// <summary>
        /// ConnectionDataを取得
        /// </summary>
        public Dictionary<string, object> GetConnectionData(ulong clientNetworkId)
        {
            return m_ConnectionDataMap.ContainsKey(clientNetworkId) ? m_ConnectionDataMap[clientNetworkId] : null;
        }

        /// <summary>
        /// すべてのConnectionDataを取得
        /// </summary>
        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectionData()
        {
            return new Dictionary<ulong, Dictionary<string, object>>(m_ConnectionDataMap);
        }

        /// <summary>
        /// プレイヤーの切断を処理
        /// </summary>
        public void HandlePlayerDisconnect(ulong clientNetworkId)
        {
            if (m_ConnectedPlayers.ContainsKey(clientNetworkId))
            {
                Debug.Log($"[ServerPlayerValidator] Removed player {m_ConnectedPlayers[clientNetworkId]}");
                m_ConnectedPlayers.Remove(clientNetworkId);
            }
            if (m_ConnectionDataMap.ContainsKey(clientNetworkId))
            {
                m_ConnectionDataMap.Remove(clientNetworkId);
            }
        }

        /// <summary>
        /// 接続済みプレイヤーのIDを取得
        /// </summary>
        public string GetPlayerId(ulong clientNetworkId)
        {
            return m_ConnectedPlayers.ContainsKey(clientNetworkId)
                ? m_ConnectedPlayers[clientNetworkId]
                : "Unknown";
        }

        /// <summary>
        /// バージョン文字列を整数に変換
        /// </summary>
        private static int ConvertVersionToInt(string version)
        {
            if (string.IsNullOrEmpty(version))
                return 0;

            try
            {
                // "1.2.3" -> "123" -> 123
                string cleanVersion = version.Replace(".", "").Replace(",", "");
                if (int.TryParse(cleanVersion, out int versionInt))
                {
                    return versionInt;
                }
                return 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to convert version '{version}' to int: {e.Message}");
                return 0;
            }
        }

        private static Dictionary<string, object> BuildFallbackPayload(ulong clientNetworkId, string authId)
        {
            return new Dictionary<string, object>
            {
                ["playerName"] = $"Player{clientNetworkId}",
                ["authId"] = authId,
                ["gameVersion"] = ConvertVersionToInt(Application.version),
                ["rank"] = 1000
            };
        }

        private static string ExtractString(Dictionary<string, object> payload, string key)
        {
            if (payload == null || !payload.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            return value switch
            {
                string str => str,
                int i => i.ToString(),
                long l => l.ToString(),
                double d => d.ToString(),
                bool b => b.ToString(),
                _ => value.ToString()
            };
        }

        // ========== Private Methods ==========

        private bool ValidatePlayer(string playerId, ulong clientNetworkId)
        {
            // 最大プレイヤー数チェック
            int maxAllowedPlayers = GetExpectedPlayerCount();
            if (m_ConnectedPlayers.Count >= maxAllowedPlayers)
            {
                Debug.LogWarning("[ServerPlayerValidator] Server is full");
                return false;
            }

            // 期待プレイヤーリストチェック
            if (m_ExpectedPlayerIds.Count > 0 && !string.IsNullOrEmpty(playerId))
            {
                if (!m_ExpectedPlayerIds.Contains(playerId))
                {
                    Debug.LogWarning($"[ServerPlayerValidator] Player {playerId} not in expected list");
                    return false;
                }
            }

            // 重複接続チェックと処理
            HandleDuplicateConnection(playerId);

            return true;
        }

        private int GetExpectedPlayerCount()
        {
            if (m_ExpectedPlayerIds != null && m_ExpectedPlayerIds.Count > 0)
            {
                return m_ExpectedPlayerIds.Count;
            }

            return m_FallbackMaxPlayers;
        }

        private void HandleDuplicateConnection(string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            // 同じプレイヤーIDで既に接続しているクライアントを探す
            foreach (var kvp in m_ConnectedPlayers)
            {
                if (kvp.Value == playerId)
                {
                    Debug.LogWarning($"[ServerPlayerValidator] Duplicate connection for PlayerId: {playerId}");

                    // 既存の接続を切断
                    if (m_NetworkManager.ConnectedClients.ContainsKey(kvp.Key))
                    {
                        m_NetworkManager.DisconnectClient(kvp.Key);
                    }
                    m_ConnectedPlayers.Remove(kvp.Key);
                    break;
                }
            }
        }
    }
}
#endif
