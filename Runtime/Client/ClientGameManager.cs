using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Client
{
    // Client connection states
    public enum ClientConnectionState
    {
        Idle,
        SearchingMatch,
        MatchFound,
        ConnectingToServer,
        Connected,
        Failed,
        Cancelling,
        Cancelled
    }

    /// <summary>
    /// クライアント管理クラス（完全に直線的な処理）
    /// </summary>
    public class ClientGameManager : IDisposable
    {
        // ========== Properties ==========
        public bool IsConnected => networkManager != null && networkManager.IsClient && networkManager.IsConnectedClient;
        public bool IsMatchmaking { get; private set; }
        // ========== Fields ==========
        private readonly NetworkManager networkManager;
        private readonly IMatchmakingPayloadProvider payloadProvider;
        private ISession currentSession;
        private CancellationTokenSource matchmakerCancellationSource;
        private bool isDisposed = false;

        // ========== Events ==========
        public event Action<ClientConnectionState> OnStateChanged;

        // ========== Factory Method ==========
        /// <summary>
        /// ClientGameManagerを作成して初期化（ファクトリメソッド）
        /// </summary>
        public static async Task<ClientGameManager> CreateAsync(NetworkManager networkManager, IMatchmakingPayloadProvider payloadProvider)
        {
            var manager = new ClientGameManager(networkManager, payloadProvider);
            await manager.InitializeAsync();
            return manager;
        }

        // ========== Constructor ==========
        private ClientGameManager(NetworkManager networkManager, IMatchmakingPayloadProvider payloadProvider)
        {
            this.networkManager = networkManager;
            this.payloadProvider = payloadProvider;
            Debug.Log("[ClientGameManager] Created");

            // NetworkManagerのコールバックを監視
            networkManager.OnClientConnectedCallback += OnClientConnected;
            networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ClientGameManager] OnClientConnected - ClientId: {clientId}, IsLocalClient: {clientId == networkManager.LocalClientId}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.LogWarning($"[ClientGameManager] OnClientDisconnected - ClientId: {clientId}, IsLocalClient: {clientId == networkManager.LocalClientId}");

            // 自分が切断された場合
            if (clientId == networkManager.LocalClientId)
            {
                Debug.Log("[ClientGameManager] Local client disconnected - handling network shutdown");
                ShutdownNetwork();
            }
        }

        /// <summary>
        /// ネットワークをシャットダウンし、必要に応じてloadingシーンに戻る
        /// </summary>
        public void ShutdownNetwork()
        {
            Debug.Log("[ClientGameManager] ShutdownNetwork called");

            // NetworkManagerをシャットダウン
            if (networkManager != null && networkManager.IsConnectedClient)
            {
                Debug.Log("[ClientGameManager] Shutting down NetworkManager");
                networkManager.Shutdown();
            }

            // gameシーンにいる場合はloadingシーンに戻る
            var currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "game")
            {
                Debug.Log("[ClientGameManager] Currently in game scene - returning to loading scene");
                SceneManager.LoadScene("loading");
            }
        }

        // ========== メイン処理（上から下へ一直線） ==========

        /// <summary>
        /// クライアントゲームマネージャーの初期化
        /// Unity ServicesはClientSingletonで既に初期化済み
        /// </summary>
        private async Task InitializeAsync()
        {
            Debug.Log("[ClientGameManager] ========== INITIALIZATION BEGIN ==========");

            try
            {
                // Unity ServicesとAuthenticationはClientSingletonで初期化済み
                // ここでは確認のみ
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    Debug.LogError("[ClientGameManager] Unity Services not initialized. Should be done in ClientSingleton.");
                    throw new InvalidOperationException("Unity Services not initialized");
                }

                if (!AuthenticationWrapper.IsSignedIn)
                {
                    Debug.LogError("[ClientGameManager] Not authenticated. Should be done in ClientSingleton.");
                    throw new InvalidOperationException("Not authenticated");
                }

                Debug.Log($"[ClientGameManager] ✓ Using authenticated player: {AuthenticationWrapper.PlayerId}");

                Debug.Log("[ClientGameManager] ========== INITIALIZATION COMPLETE ==========");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientGameManager] Initialization failed: {e.Message}");
                throw;
            }
        }

        // ========== Main Operations ==========

        /// <summary>
        /// マッチメイキングとサーバー接続（上から下へ一直線）
        /// </summary>
        public async Task<MatchResult> MatchmakeAsync(string queueName)
        {
            if (IsMatchmaking)
            {
                Debug.LogWarning("[ClientGameManager] Already matchmaking");
                return MatchResult.Failed;
            }

            if (currentSession != null)
            {
                Debug.LogError($"[ClientGameManager] Session still exists (Code: {currentSession.Code}). Should be cleared in LoadingScene.");
                await LeaveCurrentSessionAsync();
            }

            Debug.Log("[ClientGameManager] ========== MATCHMAKING BEGIN ==========");
            IsMatchmaking = true;

            try
            {
                // ================================================================
                // STEP 1: マッチメイキング開始
                // ================================================================
                Debug.Log($"[ClientGameManager] STEP 1: Starting matchmaking with queue: {queueName}");
                OnStateChanged?.Invoke(ClientConnectionState.SearchingMatch);

                // キャンセレーショントークン作成
                matchmakerCancellationSource = new CancellationTokenSource();

                Debug.Log("[ClientGameManager] ====================================");

                Dictionary<string, PlayerProperty> playerProperties = null;
                Dictionary<string, object> ticketAttributes = null;
                Dictionary<string, object> connectionPayload = null;
                var authId = AuthenticationWrapper.PlayerId;
                if (string.IsNullOrEmpty(authId))
                {
                    Debug.LogWarning("[ClientGameManager] Authentication player ID not available. Using fallback auth id.");
                    authId = "unknown-player";
                }

                if (payloadProvider != null)
                {
                    try
                    {
                        playerProperties = payloadProvider.BuildPlayerProperties();
                        ticketAttributes = payloadProvider.BuildTicketAttributes();
                        connectionPayload = payloadProvider.BuildConnectionData(authId);
                    }
                    catch (Exception providerException)
                    {
                        Debug.LogError($"[ClientGameManager] Payload provider error: {providerException.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning("[ClientGameManager] No matchmaking payload provider configured.");
                }

                playerProperties?.Remove("queueName");
                ticketAttributes?.Remove("queueName");

                if (playerProperties == null)
                {
                    Debug.LogWarning("[ClientGameManager] Payload provider returned no player properties. Using empty dictionary.");
                    playerProperties = new Dictionary<string, PlayerProperty>();
                }

                if (ticketAttributes == null)
                {
                    Debug.LogWarning("[ClientGameManager] Payload provider returned no ticket attributes. Using empty dictionary.");
                    ticketAttributes = new Dictionary<string, object>();
                }

                var matchmakerOptions = new MatchmakerOptions
                {
                    QueueName = queueName,
                    playerProperties = playerProperties,
                    ticketAttributes = ticketAttributes
                };

                var connectionBytes = BuildConnectionDataBytes(connectionPayload, playerProperties, authId);
                networkManager.NetworkConfig.ConnectionData = connectionBytes;

                var gameMode = ExtractGameMode(ticketAttributes, playerProperties) ?? "default-mode";
                var map = ExtractMap(ticketAttributes, playerProperties) ?? "default-map";

                // セッションオプション設定
                var sessionOptions = new SessionOptions()
                {
                    MaxPlayers = GameConfig.Instance.MaxHumanPlayers,
                    Name = $"{gameMode}_{map}",
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        ["gameMode"] = new SessionProperty(gameMode),
                        ["map"] = new SessionProperty(map)
                    }
                }.WithDirectNetwork(); // Dedicated Server用

                // ================================================================
                // STEP 2: マッチを検索
                // ================================================================
                Debug.Log("[ClientGameManager] STEP 2: Searching for match...");
                Debug.Log($"[ClientGameManager] Calling MatchmakeSessionAsync with queue: {queueName}");

                currentSession = await MultiplayerService.Instance.MatchmakeSessionAsync(
                    matchmakerOptions,
                    sessionOptions,
                    matchmakerCancellationSource.Token
                );

                if (currentSession == null)
                {
                    Debug.LogError("[ClientGameManager] Failed to find match");
                    OnStateChanged?.Invoke(ClientConnectionState.Failed);
                    IsMatchmaking = false;
                    return MatchResult.Failed;
                }

                Debug.Log($"[ClientGameManager] ✓ Match found: {currentSession.Code}");
                Debug.Log($"[ClientGameManager] Session details - Code: {currentSession.Code}, Id: {currentSession.Id}");
                Debug.Log($"[ClientGameManager] Session state after creation - IsValid: {currentSession != null}");
                OnStateChanged?.Invoke(ClientConnectionState.MatchFound);

                // ================================================================
                // STEP 3: 接続準備
                // ================================================================
                Debug.Log("[ClientGameManager] STEP 3: Preparing connection...");

                // Transport設定（UnityTransportは事前に設定済み）
                var transport = networkManager.GetComponent<UnityTransport>();
                if (transport == null)
                {
                    Debug.LogError("[ClientGameManager] UnityTransport component not found!");
                    OnStateChanged?.Invoke(ClientConnectionState.Failed);
                    IsMatchmaking = false;
                    return MatchResult.Failed;
                }

                // NetworkManager設定
                networkManager.NetworkConfig.NetworkTransport = transport;

                Debug.Log("[ClientGameManager] ✓ Connection prepared");

                // ================================================================
                // STEP 4: サーバーに接続
                // ================================================================
                Debug.Log("[ClientGameManager] STEP 4: Connecting to server...");
                Debug.Log($"[ClientGameManager] Pre-connection state - IsClient: {networkManager.IsClient}, IsConnectedClient: {networkManager.IsConnectedClient}");
                Debug.Log($"[ClientGameManager] Transport: {transport.GetType().Name}, ConnectionData size: {networkManager.NetworkConfig.ConnectionData?.Length ?? 0}");
                OnStateChanged?.Invoke(ClientConnectionState.ConnectingToServer);

                // Multiplayer packageが自動的に接続を処理
                Debug.Log("[ClientGameManager] Multiplayer package will handle client connection automatically");
                Debug.Log("[ClientGameManager] Waiting for SDK to establish connection...");

                bool connected = await WaitForConnection();
                if (!connected)
                {
                    Debug.LogError("[ClientGameManager] Connection timeout");
                    OnStateChanged?.Invoke(ClientConnectionState.Failed);
                    IsMatchmaking = false;
                    return MatchResult.Timeout;
                }

                Debug.Log("[ClientGameManager] ✓ Connected to server");
                OnStateChanged?.Invoke(ClientConnectionState.Connected);

                // ================================================================
                // STEP 5: ゲームシーン同期待機
                // ================================================================
                Debug.Log("[ClientGameManager] STEP 5: Waiting for scene sync...");

                SetupSceneLoadCallback();

                Debug.Log("[ClientGameManager] ✓ Ready for game");

                Debug.Log("[ClientGameManager] ========== MATCHMAKING COMPLETE ==========");
                IsMatchmaking = false;
                return MatchResult.Success;
            }
            catch (Exception e)
            {
                IsMatchmaking = false;

                // キャンセルの場合はエラーログを出さない
                if (e is OperationCanceledException || e is TaskCanceledException)
                {
                    Debug.Log("[ClientGameManager] Matchmaking cancelled by user");
                    OnStateChanged?.Invoke(ClientConnectionState.Cancelled);
                    return MatchResult.UserCancelled;
                }
                else
                {
                    // キャンセル以外の場合のみエラーログ
                    Debug.LogError($"[ClientGameManager] Matchmaking failed: {e.Message}");
                    OnStateChanged?.Invoke(ClientConnectionState.Failed);
                    return MatchResult.Failed;
                }
            }
        }

        // ========== Private Helper Methods ==========

        private static string ExtractGameMode(Dictionary<string, object> ticketAttributes, Dictionary<string, PlayerProperty> playerProperties)
        {
            return ExtractAttributeAsString(ticketAttributes, "gameMode")
                   ?? ExtractPlayerPropertyAsString(playerProperties, "gameMode");
        }

        private static string ExtractMap(Dictionary<string, object> ticketAttributes, Dictionary<string, PlayerProperty> playerProperties)
        {
            return ExtractAttributeAsString(ticketAttributes, "map")
                   ?? ExtractPlayerPropertyAsString(playerProperties, "map");
        }

        private static byte[] BuildConnectionDataBytes(Dictionary<string, object> connectionData, Dictionary<string, PlayerProperty> playerProperties, string fallbackAuthId)
        {
            var payload = connectionData != null
                ? new Dictionary<string, object>(connectionData)
                : new Dictionary<string, object>();

            var playerName = ExtractAttributeAsString(payload, "playerName")
                             ?? ExtractPlayerPropertyAsString(playerProperties, "playerName")
                             ?? "Player";

            var authId = ExtractAttributeAsString(payload, "authId")
                         ?? fallbackAuthId
                         ?? "unknown-player";

            var gameVersion = ExtractAttributeAsInt(payload, "gameVersion")
                              ?? ExtractPlayerPropertyAsInt(playerProperties, "gameVersion")
                              ?? GetApplicationVersionAsInt();

            var rank = ExtractAttributeAsInt(payload, "rank")
                       ?? ExtractPlayerPropertyAsInt(playerProperties, "rank")
                       ?? 0;

            payload["playerName"] = playerName;
            payload["authId"] = authId;
            payload["gameVersion"] = gameVersion;
            payload["rank"] = rank;

            return ConnectionPayloadSerializer.SerializeToBytes(payload);
        }

        private static string ExtractAttributeAsString(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
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
                default:
                    return value.ToString();
            }
        }

        private static int? ExtractAttributeAsInt(Dictionary<string, object> source, string key)
        {
            if (source == null || !source.TryGetValue(key, out var value) || value == null)
            {
                return null;
            }

            switch (value)
            {
                case int i:
                    return i;
                case long l:
                    return (int)l;
                case float f:
                    return (int)f;
                case double d:
                    return (int)d;
                case string s when int.TryParse(s, out var parsed):
                    return parsed;
                default:
                    return null;
            }
        }

        private static string ExtractPlayerPropertyAsString(Dictionary<string, PlayerProperty> properties, string key)
        {
            if (properties == null || !properties.TryGetValue(key, out var property) || property == null)
            {
                return null;
            }

            return property.Value;
        }

        private static int? ExtractPlayerPropertyAsInt(Dictionary<string, PlayerProperty> properties, string key)
        {
            var value = ExtractPlayerPropertyAsString(properties, key);
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            return int.TryParse(value, out var parsed) ? parsed : null;
        }

        private static int GetApplicationVersionAsInt()
        {
            var version = Application.version;
            if (string.IsNullOrEmpty(version))
            {
                return 0;
            }

            try
            {
                var cleanVersion = version.Replace(".", "").Replace(",", "");
                return int.TryParse(cleanVersion, out var versionInt) ? versionInt : 0;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to convert version '{version}' to int: {e.Message}");
                return 0;
            }
        }

        private async Task<bool> WaitForConnection()
        {
            float timeout = 10f;
            float elapsed = 0f;
            int checkCount = 0;

            Debug.Log($"[WaitForConnection] Starting connection wait loop");
            Debug.Log($"[WaitForConnection] Initial NetworkManager state - IsClient: {networkManager.IsClient}, IsConnectedClient: {networkManager.IsConnectedClient}, IsListening: {networkManager.IsListening}");

            while (!networkManager.IsConnectedClient && elapsed < timeout)
            {
                await Task.Delay(100);
                elapsed += 0.1f;
                checkCount++;

                // 1秒ごとに状態をログ出力
                if (checkCount % 10 == 0)
                {
                    Debug.Log($"[WaitForConnection] Waiting... {elapsed:F1}s - IsClient: {networkManager.IsClient}, IsConnectedClient: {networkManager.IsConnectedClient}");

                    // セッションの状態も確認
                    if (currentSession != null)
                    {
                        Debug.Log($"[WaitForConnection] Session still exists: Code={currentSession.Code}");
                    }
                    else
                    {
                        Debug.LogWarning($"[WaitForConnection] Session is null!");
                    }
                }
            }

            bool connected = networkManager.IsConnectedClient;
            Debug.Log($"[WaitForConnection] Finished - Connected: {connected}, Elapsed: {elapsed:F1}s");

            if (!connected)
            {
                Debug.LogError($"[WaitForConnection] Failed to connect within timeout");

                // 最終状態をログ
                Debug.Log($"[WaitForConnection] Final state - IsClient: {networkManager.IsClient}, IsConnectedClient: {networkManager.IsConnectedClient}, IsListening: {networkManager.IsListening}");

                if (currentSession != null)
                {
                    Debug.LogWarning($"[WaitForConnection] Session still exists but not connected: Code={currentSession.Code}");
                }
            }

            return connected;
        }

        private void SetupSceneLoadCallback()
        {
            // 重複防止
            networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
            networkManager.SceneManager.OnLoadEventCompleted += OnSceneLoadCompleted;
            Debug.Log("[ClientGameManager] Scene load callback registered");
        }

        private void OnSceneLoadCompleted(string sceneName, LoadSceneMode loadSceneMode,
            System.Collections.Generic.List<ulong> clientsCompleted,
            System.Collections.Generic.List<ulong> clientsTimedOut)
        {
            Debug.Log($"[ClientGameManager] Scene loaded: {sceneName}");
            if (sceneName == "game")
            {
                Debug.Log("[ClientGameManager] ✓ Game scene loaded successfully!");
                // クリーンアップ
                networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
            }
        }

        /// <summary>
        /// マッチメイキングをキャンセル
        /// </summary>
        public async Task CancelMatchmakingAsync()
        {
            Debug.Log($"[CancelMatchmaking] Called - IsMatchmaking: {IsMatchmaking}");

            // Cancelling状態を通知
            OnStateChanged?.Invoke(ClientConnectionState.Cancelling);

            if (matchmakerCancellationSource != null && !matchmakerCancellationSource.IsCancellationRequested)
            {
                matchmakerCancellationSource.Cancel();
                Debug.Log("[ClientGameManager] Matchmaking cancel requested");
            }

            await LeaveCurrentSessionAsync();
            IsMatchmaking = false;

            // Cancelled状態を通知
            OnStateChanged?.Invoke(ClientConnectionState.Cancelled);
        }

        // ========== Session & Connection Management ==========

        /// <summary>
        /// 現在のセッションから離脱
        /// </summary>
        public async Task LeaveCurrentSessionAsync()
        {
            if (currentSession != null)
            {
                Debug.Log($"[ClientGameManager] Leaving current session (Code: {currentSession.Code})...");
                try
                {
                    await currentSession.LeaveAsync();
                    Debug.Log("[ClientGameManager] Successfully left session");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ClientGameManager] Failed to leave session: {e.Message}");
                }
                finally
                {
                    currentSession = null;
                }
            }
            else
            {
                Debug.Log("[ClientGameManager] No current session to leave");
            }
        }

        /// <summary>
        /// NetworkManagerを切断
        /// </summary>
        public void Disconnect()
        {
            if (networkManager != null)
            {
                // コールバックをクリーンアップ
                if (networkManager.SceneManager != null)
                {
                    networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
                    Debug.Log("[ClientGameManager] Cleaned up scene load callbacks");
                }

                if (networkManager.IsClient)
                {
                    networkManager.Shutdown();
                    Debug.Log("[ClientGameManager] NetworkManager shutdown");
                }
            }
        }

        // ========== IDisposable ==========

        public void Dispose()
        {
            if (isDisposed) return;

            Debug.LogWarning($"[ClientGameManager] Disposing! Stack trace:\n{System.Environment.StackTrace}");

            // Clear event handler
            OnStateChanged = null;

            // コールバックをクリーンアップ
            if (networkManager != null)
            {
                networkManager.OnClientConnectedCallback -= OnClientConnected;
                networkManager.OnClientDisconnectCallback -= OnClientDisconnected;

                if (networkManager.SceneManager != null)
                {
                    networkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoadCompleted;
                }
            }

            Debug.Log("[ClientGameManager] Calling CancelMatchmakingAsync from Dispose");
            CancelMatchmakingAsync();

            if (currentSession != null)
            {
                Debug.Log("[ClientGameManager] Calling LeaveCurrentSessionAsync from Dispose");
                Disconnect();
                _ = LeaveCurrentSessionAsync();
            }

            // キャンセレーショントークンのクリーンアップ
            matchmakerCancellationSource?.Dispose();

            isDisposed = true;
            Debug.Log("[ClientGameManager] Dispose completed");
        }
    }
}
