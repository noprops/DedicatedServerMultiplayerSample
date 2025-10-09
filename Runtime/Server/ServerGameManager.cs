#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Server
{
    /// <summary>
    /// サーバー管理クラス（完全に直線的な処理）
    /// </summary>
    public class ServerGameManager : IDisposable
    {
        private bool m_IsDisposed = false;

        // ========== Properties ==========
        public int TeamCount => m_TeamCount;

        // ========== Fields ==========
        private readonly NetworkManager m_NetworkManager;
        private ServerMultiplayIntegration m_MultiplayIntegration;
        private ServerPlayerValidator m_PlayerValidator;
        private readonly List<string> m_ExpectedAuthIds = new List<string>();
        private readonly Dictionary<ulong, string> m_ConnectedAuthIds = new Dictionary<ulong, string>();
        private int m_TeamCount = 2;

        // シーンロード完了管理
        private TaskCompletionSource<bool> m_SceneLoadTcs;
        private bool m_IsSceneLoaded = false;


        // ゲーム開始後は新規接続を拒否するフラグ
        private bool m_GameStarted = false;

        // ========== Constructor ==========
        public ServerGameManager(NetworkManager networkManager)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            Debug.Log("[ServerGameManager] Created");
        }


        private ServerRuntimeConfig m_RuntimeConfig;

        // ========== メイン処理（上から下へ一直線） ==========
        public async Task<bool> StartServerAsync()
        {
            Debug.Log("[ServerGameManager] ========== SERVER STARTUP BEGIN ==========");

            try
            {
                m_RuntimeConfig = ServerRuntimeConfig.Capture();
                m_RuntimeConfig.LogSummary();

                // ================================================================
                // STEP 1: Multiplayアロケーション取得
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 1: Getting allocation from Multiplay...");

                var allocationHelper = new ServerAllocationHelper(m_RuntimeConfig);
                var (success, matchResults, integration) = await allocationHelper.GetAllocationAsync();

                if (!success)
                {
                    Debug.LogError("[ServerGameManager] Failed to get server allocation");
                    CloseServer();
                    return false;
                }

                m_MultiplayIntegration = integration;
                Debug.Log("[ServerGameManager] ✓ Allocation received");

                // ================================================================
                // STEP 2: マッチメイキング情報を解析
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 2: Processing matchmaking info...");

                if (matchResults?.MatchProperties?.Teams != null)
                {
                    m_TeamCount = matchResults.MatchProperties.Teams.Count;
                    Debug.Log($"[ServerGameManager] Match Info:");
                    Debug.Log($"  - Match ID: {matchResults.MatchId}");

                    // プール情報を表示（MatchmakingResultsから推測）
                    Debug.Log($"[ServerGameManager] ========== POOL INFO ==========");
                    if (matchResults.PoolName != null)
                    {
                        Debug.Log($"  - Pool Name: {matchResults.PoolName}");
                    }
                    else
                    {
                        Debug.Log($"  - Pool Name: Not available in MatchmakingResults");
                    }
                    Debug.Log($"[ServerGameManager] ===============================");

                    Debug.Log($"  - Team Count: {m_TeamCount}");
                    Debug.Log($"  - Total Players Expected: {matchResults.MatchProperties.Players.Count}");

                    // NOTE: MatchAttributesはMatchPropertiesに存在しない可能性がある
                    // TicketAttributesはマッチメイキング時のフィルタリングに使われ、
                    // サーバー側で直接アクセスできない可能性がある

                    foreach (var player in matchResults.MatchProperties.Players)
                    {
                        m_ExpectedAuthIds.Add(player.Id);
                        Debug.Log($"  - Expected AuthId: {player.Id}");
                    }

                    // チーム情報の表示
                    foreach (var team in matchResults.MatchProperties.Teams)
                    {
                        Debug.Log($"  - Team '{team.TeamName}': {team.PlayerIds.Count} players");
                    }
                }
                Debug.Log("[ServerGameManager] ✓ Matchmaking info processed");

                // ================================================================
                // STEP 3: NetworkManager設定
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 3: Configuring NetworkManager...");

                var transport = m_NetworkManager.GetComponent<UnityTransport>();

                ushort port = m_RuntimeConfig.GamePort;
                transport.SetConnectionData("0.0.0.0", port);
                m_NetworkManager.NetworkConfig.NetworkTransport = transport;

                Debug.Log($"[ServerGameManager] ✓ Configured on port {port}");

                // ================================================================
                // STEP 4: 接続承認処理の設定
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 4: Setting up connection approval...");

                // シーンロードTCSを初期化
                m_SceneLoadTcs = new TaskCompletionSource<bool>();

                // プレイヤー検証クラスを作成
                m_PlayerValidator = new ServerPlayerValidator(
                    m_NetworkManager,
                    m_ExpectedAuthIds,
                    m_ConnectedAuthIds
                );

                m_NetworkManager.ConnectionApprovalCallback = HandleConnectionApproval;
                m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
                m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;

                Debug.Log("[ServerGameManager] ✓ Connection callbacks set");

                // ================================================================
                // STEP 5: サーバーが自動起動されているか確認
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 5: Checking server status...");
                Debug.Log($"[ServerGameManager] IsServer: {m_NetworkManager.IsServer}, IsListening: {m_NetworkManager.IsListening}");

                // MultiplaySessionManagerがアロケーション時に自動的にStartServerを呼んでいる
                if (!m_NetworkManager.IsServer)
                {
                    Debug.LogError("[ServerGameManager] Server was not started by Multiplay");
                    CloseServer();
                    return false;
                }
                Debug.Log("[ServerGameManager] ✓ Server is running");

                // ================================================================
                // STEP 6: ゲームシーンをロード
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 6: Loading game scene...");

                bool sceneLoaded = await LoadSceneAsync("game");
                if (!sceneLoaded)
                {
                    Debug.LogError("[ServerGameManager] Failed to load game scene");
                    CloseServer();
                    return false;
                }
                Debug.Log("[ServerGameManager] ✓ Game scene loaded");

                // ================================================================
                // STEP 7: プレイヤー受け入れ準備
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 7: Setting player readiness...");

                await SetPlayerReadinessAsync(true);
                Debug.Log("[ServerGameManager] ✓ Ready to accept players");

                // ================================================================
                // STEP 8: ゲームセッションコントローラーがプレイヤー管理を担当
                // ================================================================
                Debug.Log("[ServerGameManager] STEP 8: GameSessionController will handle player management...");

                Debug.Log("[ServerGameManager] ========== SERVER STARTUP COMPLETE ==========");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerGameManager] Server startup failed: {e.Message}");
                CloseServer();
                return false;
            }
        }

        // ========== シーンロード（TCSでシンプルに） ==========
        private async Task<bool> LoadSceneAsync(string sceneName, int timeout = 5000)
        {
            var loadTcs = new TaskCompletionSource<bool>();

            // コールバックをTCSに変換
            void OnSceneLoaded(string loadedSceneName, LoadSceneMode mode, List<ulong> completed, List<ulong> timedOut)
            {
                if (loadedSceneName == sceneName)
                {
                    m_NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                    m_IsSceneLoaded = true;
                    m_SceneLoadTcs?.TrySetResult(true);
                    loadTcs.TrySetResult(true);
                }
            }

            m_NetworkManager.SceneManager.OnLoadEventCompleted += OnSceneLoaded;

            var status = m_NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                m_NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                return false;
            }

            // タイムアウト付き待機
            var timeoutTask = Task.Delay(timeout);
            var completedTask = await Task.WhenAny(loadTcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                m_NetworkManager.SceneManager.OnLoadEventCompleted -= OnSceneLoaded;
                return false;
            }

            return await loadTcs.Task;
        }

        // ========== 接続承認（バリデーション付き） ==========
        private void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request,
                                             NetworkManager.ConnectionApprovalResponse response)
        {
            Debug.Log($"[ServerGameManager] Connection approval request. ClientId: {request.ClientNetworkId}");

            // ゲーム開始後は全ての新規接続を拒否
            if (m_GameStarted)
            {
                Debug.LogWarning($"[ServerGameManager] Rejecting connection - game already started. ClientId: {request.ClientNetworkId}");
                response.Approved = false;
                response.Reason = "Game already started";
                response.Pending = false;
                return;
            }

            // プレイヤー検証を実行
            var (success, connectionData, errorReason) = m_PlayerValidator.ValidateConnectionRequest(request);

            if (!success)
            {
                response.Approved = false;
                response.Reason = errorReason;
                response.Pending = false;
                return;
            }

            // 承認
            response.Approved = true;
            response.CreatePlayerObject = false;
            var connectionAuthId = ExtractString(connectionData, "authId") ?? "Unknown";
            m_PlayerValidator.RegisterConnectedPlayer(request.ClientNetworkId, connectionAuthId);

            // シーンロード待ち
            if (!m_IsSceneLoaded)
            {
                response.Pending = true;
                _ = WaitForSceneAndApproveAsync(response);
            }
            else
            {
                response.Pending = false;
            }
        }


        private async Task WaitForSceneAndApproveAsync(NetworkManager.ConnectionApprovalResponse response)
        {
            if (m_SceneLoadTcs != null)
            {
                await m_SceneLoadTcs.Task;
            }

            response.Pending = false;
        }

        private void OnClientConnected(ulong clientId)
        {
            string authId = m_PlayerValidator.GetAuthId(clientId);
            Debug.Log($"[ServerGameManager] Client connected - ClientId: {clientId}, AuthId: {authId}");
            Debug.Log($"[ServerGameManager] Total clients: {m_NetworkManager.ConnectedClients.Count}");


            // ConnectionData（接続時に取得したデータ）
            var connectionData = m_PlayerValidator.GetConnectionData(clientId);
            if (connectionData != null && connectionData.Count > 0)
            {
                Debug.Log("[ServerGameManager] Connection payload values:");
                foreach (var kvp in connectionData)
                {
                    Debug.Log($"  - {kvp.Key}: {kvp.Value}");
                }
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[ServerGameManager] Client disconnected: {clientId}");
            m_PlayerValidator.HandlePlayerDisconnect(clientId);
            Debug.Log($"[ServerGameManager] Remaining clients: {m_NetworkManager.ConnectedClients.Count}");
        }

        private async Task SetPlayerReadinessAsync(bool ready)
        {
            if (m_MultiplayIntegration != null && m_MultiplayIntegration.IsConnected)
            {
                await m_MultiplayIntegration.SetPlayerReadinessAsync(ready);
            }
        }

        /// <summary>
        /// すべての接続中プレイヤーのUserDataを取得
        /// </summary>
        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectedPlayers()
        {
            return m_PlayerValidator?.GetAllConnectionData() ?? new Dictionary<ulong, Dictionary<string, object>>();
        }

        /// <summary>
        /// ゲーム開始時にセッションをロックし、新規プレイヤーの受け入れを停止する
        /// GameSessionControllerから呼ばれる
        /// </summary>
        public async Task LockSessionForGameStartAsync()
        {
            // ゲーム開始フラグを立てる（ConnectionApprovalで即座に拒否するため）
            m_GameStarted = true;
            Debug.Log("[ServerGameManager] Locking session - no new connections will be accepted");

            // セッションをロック（新規マッチング不可にする）
            if (m_MultiplayIntegration != null)
            {
                await m_MultiplayIntegration.LockSessionAsync();
            }

            // 新規プレイヤー受け入れを停止
            await SetPlayerReadinessAsync(false);
            Debug.Log("[ServerGameManager] Player readiness set to false");
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

        public void CloseServer()
        {
            Dispose();
            Application.Quit();
        }

        public void Dispose()
        {
            if (m_IsDisposed) return;

            Debug.Log("[ServerGameManager] Disposing");

            // コールバックをクリーンアップ
            if (m_NetworkManager != null)
            {
                m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
                m_NetworkManager.ConnectionApprovalCallback = null;

                if (m_NetworkManager.IsServer)
                {
                    m_NetworkManager.Shutdown();
                }
            }

            m_IsDisposed = true;
        }
    }
}
#endif
