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
        private readonly List<string> m_ExpectedAuthIds;
        private readonly Dictionary<ulong, string> m_ConnectedAuthIds;
        private readonly Dictionary<ulong, Dictionary<string, object>> m_ConnectionDataMap;

        // ========== Constructor ==========
        public ServerPlayerValidator(
            NetworkManager networkManager,
            List<string> expectedAuthIds,
            Dictionary<ulong, string> connectedAuthIds,
            int fallbackMaxPlayers)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            m_ExpectedAuthIds = expectedAuthIds ?? new List<string>();
            m_ConnectedAuthIds = connectedAuthIds ?? new Dictionary<ulong, string>();
            m_ConnectionDataMap = new Dictionary<ulong, Dictionary<string, object>>();
            m_FallbackMaxPlayers = Mathf.Max(1, fallbackMaxPlayers);
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
            // 詳細なペイロードログは開発時のみ有効化すること
            // Debug.Log($"[ServerPlayerValidator] Payload length: {request.Payload?.Length ?? 0}");

            // ===== ConnectionData JSON版 =====
            var connectionData = ConnectionPayloadSerializer.DeserializeFromBytes(request.Payload);

            var authId = connectionData.Count > 0 ? ExtractString(connectionData, "authId") : null;

            if (string.IsNullOrEmpty(authId))
            {
                Debug.LogError("[ServerPlayerValidator] ✗ Connection payload missing required 'authId'.");
                return (false, null, "Missing authId");
            }

            Debug.Log($"[ServerPlayerValidator] ✓ Parsed connection payload. authId: '{authId}'");

            if (!ValidatePlayer(authId, request.ClientNetworkId))
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
        public void RegisterConnectedPlayer(ulong clientNetworkId, string authId)
        {
            m_ConnectedAuthIds[clientNetworkId] = authId;
            Debug.Log($"[ServerPlayerValidator] Registered player - ClientId: {clientNetworkId}, AuthId: {authId}");
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
            if (m_ConnectedAuthIds.ContainsKey(clientNetworkId))
            {
                Debug.Log($"[ServerPlayerValidator] Removed player {m_ConnectedAuthIds[clientNetworkId]}");
                m_ConnectedAuthIds.Remove(clientNetworkId);
            }
            if (m_ConnectionDataMap.ContainsKey(clientNetworkId))
            {
                m_ConnectionDataMap.Remove(clientNetworkId);
            }
        }

        /// <summary>
        /// 接続済みプレイヤーの認証IDを取得
        /// </summary>
        public string GetAuthId(ulong clientNetworkId)
        {
            return m_ConnectedAuthIds.ContainsKey(clientNetworkId)
                ? m_ConnectedAuthIds[clientNetworkId]
                : "Unknown";
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

        private bool ValidatePlayer(string authId, ulong clientNetworkId)
        {
            // 最大プレイヤー数チェック
            int maxAllowedPlayers = GetExpectedPlayerCount();
            if (m_ConnectedAuthIds.Count >= maxAllowedPlayers)
            {
                Debug.LogWarning("[ServerPlayerValidator] Server is full");
                return false;
            }

            // 期待プレイヤーリストチェック
            if (m_ExpectedAuthIds.Count > 0 && !string.IsNullOrEmpty(authId))
            {
                if (!m_ExpectedAuthIds.Contains(authId))
                {
                    Debug.LogWarning($"[ServerPlayerValidator] AuthId {authId} not in expected list");
                    return false;
                }
            }

            // 重複接続チェックと処理
            HandleDuplicateConnection(authId);

            return true;
        }

        private int GetExpectedPlayerCount()
        {
            if (m_ExpectedAuthIds != null && m_ExpectedAuthIds.Count > 0)
            {
                return m_ExpectedAuthIds.Count;
            }

            return m_FallbackMaxPlayers;
        }

        private void HandleDuplicateConnection(string authId)
        {
            if (string.IsNullOrEmpty(authId))
                return;

            ulong duplicateClientId = 0;
            bool foundDuplicate = false;

            foreach (var kvp in m_ConnectedAuthIds)
            {
                if (kvp.Value == authId)
                {
                    duplicateClientId = kvp.Key;
                    foundDuplicate = true;
                    break;
                }
            }

            if (!foundDuplicate)
            {
                return;
            }

            Debug.LogWarning($"[ServerPlayerValidator] Duplicate connection for authId: {authId}");

            if (m_NetworkManager.ConnectedClients.ContainsKey(duplicateClientId))
            {
                m_NetworkManager.DisconnectClient(duplicateClientId);
            }

            m_ConnectedAuthIds.Remove(duplicateClientId);
        }
    }
}
#endif
