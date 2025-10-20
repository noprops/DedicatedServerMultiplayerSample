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

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// マッチメイキングの結果種別。
    /// </summary>
    public enum MatchResult
    {
        Success,
        UserCancelled,
        Failed,
        Timeout
    }

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
        private readonly int maxPlayers;
        private ISession currentSession;
        private CancellationTokenSource matchmakerCancellationSource;
        private bool isDisposed = false;

        // ========== Events ==========
        public event Action<ClientConnectionState> OnStateChanged;

        // ========== Factory Method ==========
        /// <summary>
        /// ClientGameManagerを作成して初期化（ファクトリメソッド）
        /// </summary>
        public static async Task<ClientGameManager> CreateAsync(NetworkManager networkManager, IMatchmakingPayloadProvider payloadProvider, int maxPlayers)
        {
            var manager = new ClientGameManager(networkManager, payloadProvider, maxPlayers);
            await manager.InitializeAsync();
            return manager;
        }

        // ========== Constructor ==========
        private ClientGameManager(NetworkManager networkManager, IMatchmakingPayloadProvider payloadProvider, int maxPlayers)
        {
            this.networkManager = networkManager;
            this.payloadProvider = payloadProvider;
            this.maxPlayers = Mathf.Max(1, maxPlayers);
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
                Disconnect();
            }
        }

        /// <summary>
        /// ネットワークを切断し、必要に応じてloadingシーンに戻る
        /// </summary>
        public void Disconnect()
        {
            Debug.Log("[ClientGameManager] Disconnect called");

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

                Dictionary<string, object> playerProperties = null;
                Dictionary<string, object> ticketAttributes = null;
                Dictionary<string, object> connectionPayload = null;
                Dictionary<string, object> sessionMetadata = null;
                var authId = AuthenticationWrapper.PlayerId;

                if (payloadProvider != null)
                {
                    try
                    {
                        playerProperties = payloadProvider.GetPlayerProperties();
                        ticketAttributes = payloadProvider.GetTicketAttributes();
                        connectionPayload = payloadProvider.GetConnectionData();
                        sessionMetadata = payloadProvider.GetSessionProperties();
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

                if (playerProperties == null)
                {
                    Debug.LogWarning("[ClientGameManager] Payload provider returned no player properties. Using empty dictionary.");
                    playerProperties = new Dictionary<string, object>();
                }

                if (ticketAttributes == null)
                {
                    Debug.LogWarning("[ClientGameManager] Payload provider returned no ticket attributes. Using empty dictionary.");
                    ticketAttributes = new Dictionary<string, object>();
                }

                if (sessionMetadata == null)
                {
                    sessionMetadata = new Dictionary<string, object>();
                }

                var matchmakerOptions = new MatchmakerOptions
                {
                    QueueName = queueName,
                    PlayerProperties = MatchmakingPayloadConverter.ToPlayerProperties(playerProperties),
                    TicketAttributes = ticketAttributes
                };

                var connectionBytes = MatchmakingPayloadConverter.ToConnectionPayload(connectionPayload, authId);
                networkManager.NetworkConfig.ConnectionData = connectionBytes;

                // セッションオプション設定
                var sessionOptions = new SessionOptions()
                {
                    MaxPlayers = this.maxPlayers,
                    SessionProperties = MatchmakingPayloadConverter.ToSessionProperties(sessionMetadata)
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
