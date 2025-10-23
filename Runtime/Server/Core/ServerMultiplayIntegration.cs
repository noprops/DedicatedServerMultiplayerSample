#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Matchmaker.Models;
using UnityEngine;
using Unity.Services.Authentication.Server;
using Unity.Services.Matchmaker.Http;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Wraps Unity Multiplay session APIs: authenticates the server, starts the session manager,
    /// reacts to allocation callbacks, and exposes helpers to lock the session or report readiness.
    /// </summary>
    public class ServerMultiplayIntegration : IDisposable
    {
        // ========== Member Variables ==========
        private IMultiplaySessionManager m_SessionManager;
        private MultiplayServerOptions m_ServerOptions;
        private readonly ServerRuntimeConfig m_RuntimeConfig;
        private readonly int m_DefaultMaxPlayers;
        private bool m_Disposed;

        public ServerMultiplayIntegration(ServerRuntimeConfig runtimeConfig, int defaultMaxPlayers)
        {
            m_RuntimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            m_DefaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
        }

        // ========== Events ==========
        public event Action<MatchmakingResults> OnMatchInfoReceived;
        public event Action<IMultiplayAllocation> OnAllocationComplete;

        // ========== Public Properties ==========
        public bool IsConnected => m_SessionManager != null;

        // ========== Public Methods ==========

        /// <summary>
        /// プレイヤー受け入れ準備状態を設定
        /// </summary>
        public async Task SetPlayerReadinessAsync(bool ready)
        {
            if (m_SessionManager != null)
            {
                await m_SessionManager.SetPlayerReadinessAsync(ready);
                Debug.Log($"[ServerMultiplayIntegration] Player readiness set to: {ready}");
            }
            else
            {
                Debug.LogWarning("[ServerMultiplayIntegration] SessionManager is null, cannot set player readiness");
            }
        }

        /// <summary>
        /// セッションをロックして新規プレイヤーの参加を防ぐ
        /// </summary>
        public async Task LockSessionAsync()
        {
            if (m_SessionManager != null)
            {
                try
                {
                    // セッションを取得
                    var session = m_SessionManager.Session;
                    if (session != null)
                    {
                        // IHostSessionとして取得（サーバーはホストなので可能）
                        var hostSession = session.AsHost();
                        hostSession.IsLocked = true;

                        // 変更を保存
                        await hostSession.SavePropertiesAsync();

                        Debug.Log("[ServerMultiplayIntegration] Session locked - no new players can join");
                    }
                    else
                    {
                        Debug.LogWarning("[ServerMultiplayIntegration] Session is null, cannot lock");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ServerMultiplayIntegration] Failed to lock session: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("[ServerMultiplayIntegration] SessionManager is null, cannot lock session");
            }
        }

        /// <summary>
        /// Multiplayサービスに接続して初期化
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            Debug.Log("[ServerMultiplayIntegration] ConnectAsync called");

            if (UnityServices.Instance.GetMultiplayerService() != null)
            {
                Debug.Log("[ServerMultiplayIntegration] GetMultiplayerService is not null, proceeding with authentication");

                try
                {
                    // サーバー認証
                    await ServerAuthenticationService.Instance.SignInFromServerAsync();
                    var token = ServerAuthenticationService.Instance.AccessToken;

                    // コールバック設定
                    var callbacks = new MultiplaySessionManagerEventCallbacks();
                    callbacks.Allocated += HandleServerAllocated;

                    var serverName = !string.IsNullOrEmpty(m_RuntimeConfig?.GeneratedServerName)
                        ? m_RuntimeConfig.GeneratedServerName
                        : "GameServer";
                    Debug.Log($"[ServerMultiplayIntegration] Using server name: {serverName}");
                    
                    if (m_RuntimeConfig != null && m_RuntimeConfig.ServerConfigAvailable)
                    {
                        Debug.Log($"[ServerMultiplayIntegration] ServerConfig detected - ServerId: {m_RuntimeConfig.ServerId}, AllocationId: {m_RuntimeConfig.AllocationId}");
                    }
                    else
                    {
                        Debug.Log("[ServerMultiplayIntegration] ServerConfig not available at connect time");
                    }

                    // セッションマネージャーのオプション設定
                    m_ServerOptions = new MultiplayServerOptions(
                        serverName: serverName,
                        gameType: "default",
                        buildId: null,
                        map: "default",
                        autoReady: false
                    );

                    var sessionManagerOptions = new MultiplaySessionManagerOptions()
                    {
                        SessionOptions = new SessionOptions()
                        {
                        MaxPlayers = (ushort)Mathf.Clamp(m_DefaultMaxPlayers, 1, ushort.MaxValue)
                    }.WithDirectNetwork(),

                        MultiplayServerOptions = m_ServerOptions,
                        Callbacks = callbacks
                    };

                    // セッションマネージャーを開始
                    m_SessionManager = await MultiplayerServerService.Instance.StartMultiplaySessionManagerAsync(sessionManagerOptions);
                    Debug.Log("[ServerMultiplayIntegration] Session manager started successfully");
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ServerMultiplayIntegration] Failed to connect to Multiplay: {e.Message}");
                    return false;
                }
            }
            else
            {
                Debug.LogWarning("[ServerMultiplayIntegration] GetMultiplayerService() returned null - Multiplay integration disabled");
                return false;
            }
        }

        /// <summary>
        /// 現在のセッション情報を取得
        /// </summary>
        public ISession GetCurrentSession()
        {
            return m_SessionManager?.Session;
        }

        /// <summary>
        /// マッチメイキング結果を取得
        /// </summary>
        public async Task<MatchmakingResults> GetMatchmakingResultsAsync()
        {
            if (m_SessionManager == null)
            {
                Debug.LogWarning("[ServerMultiplayIntegration] SessionManager is null");
                return null;
            }

            try
            {
                var results = await m_SessionManager.GetAllocationPayloadFromJsonAsAsync<Unity.Services.Matchmaker.Models.MatchmakingResults>();
                return results;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerMultiplayIntegration] Failed to get matchmaking results: {e.Message}");
                return null;
            }
        }

        // ========== Private Methods ==========

        private void HandleServerAllocated(IMultiplayAllocation allocation)
        {
            Debug.Log("[ServerMultiplayIntegration] Server allocated");
            OnAllocationComplete?.Invoke(allocation);
        }

        public void UpdateServerMetadata(MatchmakingResults results)
        {
            if (m_ServerOptions == null)
            {
                Debug.LogWarning("[ServerMultiplayIntegration] Server options not initialized; skipping metadata update");
                return;
            }

            if (results == null)
            {
                Debug.LogWarning("[ServerMultiplayIntegration] MatchmakingResults is null; keeping default server metadata");
                return;
            }

            string resolvedMap = null;
            string resolvedGameMode = null;
            int resolvedPlayerCount = m_DefaultMaxPlayers;

            var players = results?.MatchProperties?.Players;
            if (players != null)
            {
                resolvedPlayerCount = players.Count;
                foreach (var player in players)
                {
                    if (player?.CustomData == null)
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(resolvedMap) && TryGetCustomDataValue(player.CustomData, "map", out var mapValue))
                    {
                        resolvedMap = mapValue;
                    }

                    if (string.IsNullOrEmpty(resolvedGameMode) && TryGetCustomDataValue(player.CustomData, "gameMode", out var modeValue))
                    {
                        resolvedGameMode = modeValue;
                    }

                    if (!string.IsNullOrEmpty(resolvedMap) && !string.IsNullOrEmpty(resolvedGameMode))
                    {
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(resolvedMap) || string.IsNullOrEmpty(resolvedGameMode))
            {
                var session = m_SessionManager?.Session;
                if (session != null)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(resolvedMap)
                            && session.Properties != null
                            && session.Properties.TryGetValue("map", out var mapProperty))
                        {
                            resolvedMap = mapProperty?.Value;
                        }

                        if (string.IsNullOrEmpty(resolvedGameMode)
                            && session.Properties != null
                            && session.Properties.TryGetValue("gameMode", out var modeProperty))
                        {
                            resolvedGameMode = modeProperty?.Value;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[ServerMultiplayIntegration] Failed to read session properties: {e.Message}");
                    }
                }
            }

            resolvedMap ??= "default";
            resolvedGameMode ??= "standard";

            m_ServerOptions.Map = resolvedMap;
            m_ServerOptions.GameType = resolvedGameMode;

            Debug.Log($"[ServerMultiplayIntegration] Updated server metadata - GameType: {resolvedGameMode}, Map: {resolvedMap}, MaxPlayers: {resolvedPlayerCount}");

            OnMatchInfoReceived?.Invoke(results);
        }

        private static bool TryGetCustomDataValue(IDeserializable customData, string key, out string value)
        {
            value = null;
            if (customData == null)
            {
                return false;
            }

            try
            {
                var data = customData.GetAs<Dictionary<string, object>>();
                if (data != null && data.TryGetValue(key, out var rawValue) && rawValue != null)
                {
                    var asString = rawValue.ToString();
                    if (!string.IsNullOrEmpty(asString))
                    {
                        value = asString;
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ServerMultiplayIntegration] Failed to parse custom data for key '{key}': {e.Message}");
            }

            return false;
        }

        public void Dispose()
        {
            if (m_Disposed)
            {
                return;
            }

            m_Disposed = true;

            if (m_SessionManager is IDisposable disposableManager)
            {
                disposableManager.Dispose();
            }

            m_SessionManager = null;
            m_ServerOptions = null;
            OnMatchInfoReceived = null;
            OnAllocationComplete = null;
        }
    }
}
#endif
