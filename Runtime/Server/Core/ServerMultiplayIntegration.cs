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
        private IMultiplaySessionManager _sessionManager;
        private MultiplayServerOptions _serverOptions;
        private readonly ServerRuntimeConfig _runtimeConfig;
        private readonly int _defaultMaxPlayers;
        private bool _disposed;

        /// <summary>
        /// Creates a new Multiplay integration helper bound to the provided runtime configuration data.
        /// </summary>
        public ServerMultiplayIntegration(ServerRuntimeConfig runtimeConfig, int defaultMaxPlayers)
        {
            _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
        }

        // ========== Events ==========
        public event Action<MatchmakingResults> OnMatchInfoReceived;
        public event Action<IMultiplayAllocation> OnAllocationComplete;

        // ========== Public Properties ==========
        /// <summary>
        /// True when the multiplay session manager has been started successfully.
        /// </summary>
        public bool IsConnected => _sessionManager != null;

        // ========== Public Methods ==========

        /// <summary>
        /// Marks whether the game server is ready to accept players.
        /// </summary>
        public async Task SetPlayerReadinessAsync(bool ready)
        {
            if (_sessionManager != null)
            {
                await _sessionManager.SetPlayerReadinessAsync(ready);
                Debug.Log($"[ServerMultiplayIntegration] Player readiness set to: {ready}");
            }
            else
            {
                Debug.LogWarning("[ServerMultiplayIntegration] SessionManager is null, cannot set player readiness");
            }
        }

        /// <summary>
        /// Locks the current session, preventing new players from joining.
        /// </summary>
        public async Task LockSessionAsync()
        {
            if (_sessionManager != null)
            {
                try
                {
                    // セッションを取得
                    var session = _sessionManager.Session;
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
        /// Connects to Unity Services and starts the multiplay session manager.
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

                    var serverName = !string.IsNullOrEmpty(_runtimeConfig?.GeneratedServerName)
                        ? _runtimeConfig.GeneratedServerName
                        : "GameServer";
                    Debug.Log($"[ServerMultiplayIntegration] Using server name: {serverName}");
                    
                    if (_runtimeConfig != null && _runtimeConfig.ServerConfigAvailable)
                    {
                        Debug.Log($"[ServerMultiplayIntegration] ServerConfig detected - ServerId: {_runtimeConfig.ServerId}, AllocationId: {_runtimeConfig.AllocationId}");
                    }
                    else
                    {
                        Debug.Log("[ServerMultiplayIntegration] ServerConfig not available at connect time");
                    }

                    // セッションマネージャーのオプション設定
                    _serverOptions = new MultiplayServerOptions(
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
                        MaxPlayers = (ushort)Mathf.Clamp(_defaultMaxPlayers, 1, ushort.MaxValue)
                    }.WithDirectNetwork(),

                        MultiplayServerOptions = _serverOptions,
                        Callbacks = callbacks
                    };

                    // セッションマネージャーを開始
                    _sessionManager = await MultiplayerServerService.Instance.StartMultiplaySessionManagerAsync(sessionManagerOptions);
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
        /// Returns the active Multiplay session, if available.
        /// </summary>
        public ISession GetCurrentSession()
        {
            return _sessionManager?.Session;
        }

        /// <summary>
        /// Retrieves matchmaking results that were supplied with the allocation payload.
        /// </summary>
        public async Task<MatchmakingResults> GetMatchmakingResultsAsync()
        {
            if (_sessionManager == null)
            {
                Debug.LogWarning("[ServerMultiplayIntegration] SessionManager is null");
                return null;
            }

            try
            {
                var results = await _sessionManager.GetAllocationPayloadFromJsonAsAsync<Unity.Services.Matchmaker.Models.MatchmakingResults>();
                return results;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerMultiplayIntegration] Failed to get matchmaking results: {e.Message}");
                return null;
            }
        }

        // ========== Private Methods ==========

        /// <summary>
        /// Dispatches the allocation event when the server is assigned a match.
        /// </summary>
        private void HandleServerAllocated(IMultiplayAllocation allocation)
        {
            Debug.Log("[ServerMultiplayIntegration] Server allocated");
            OnAllocationComplete?.Invoke(allocation);
        }

        /// <summary>
        /// Applies metadata derived from matchmaking results to the server options and raises the match-info event.
        /// </summary>
        public void UpdateServerMetadata(MatchmakingResults results)
        {
            if (_serverOptions == null)
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
            int resolvedPlayerCount = _defaultMaxPlayers;

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
                var session = _sessionManager?.Session;
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

            _serverOptions.Map = resolvedMap;
            _serverOptions.GameType = resolvedGameMode;

            Debug.Log($"[ServerMultiplayIntegration] Updated server metadata - GameType: {resolvedGameMode}, Map: {resolvedMap}, MaxPlayers: {resolvedPlayerCount}");

            OnMatchInfoReceived?.Invoke(results);
        }

        /// <summary>
        /// Attempts to read a named value from the matchmaker custom data payload.
        /// </summary>
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

        /// <summary>
        /// Releases owned resources and disposes the underlying session manager.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_sessionManager is IDisposable disposableManager)
            {
                disposableManager.Dispose();
            }

            _sessionManager = null;
            _serverOptions = null;
            OnMatchInfoReceived = null;
            OnAllocationComplete = null;
        }
    }
}
#endif
