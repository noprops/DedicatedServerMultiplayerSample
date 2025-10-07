using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using MultiplayerServicesTest.Shared;

namespace MultiplayerServicesTest.Server
{
    public enum GameSessionState
    {
        WaitingForPlayers,
        StartFailed,
        InGame,
        GameEnded
    }

    /// <summary>
    /// サーバーセッション管理
    /// </summary>
    public class GameSessionController : MonoBehaviour
    {
#if UNITY_SERVER || ENABLE_UCS_SERVER

        public static GameSessionController Instance { get; private set; }

        // ========== State & Events ==========
        public GameSessionState State { get; private set; }
        public event Action<GameSessionState> OnStateChanged;

        // ========== Configuration ==========
        [Header("Timeouts")]
        [SerializeField] private float waitingPlayersTimeout = 10f;
        private float inGameTimeout;

        // ========== Components ==========
        private int m_RequiredPlayers;
        private PlayerConnectionTracker m_PlayerTracker;

        // ========== Flow Control (Direct TCS) ==========
        private TaskCompletionSource<bool> m_PlayersReadyTcs;
        private TaskCompletionSource<bool> m_GameEndTcs;

        // ========== Public API ==========

        /// <summary>
        /// ゲーム正常終了を通知
        /// </summary>
        public void NotifyGameEnded()
        {
            if (State != GameSessionState.InGame) return;

            Debug.Log("[GameSessionController] Game ended notification received");
            m_GameEndTcs?.TrySetResult(true);
        }

        /// <summary>
        /// 特定クライアントを強制切断
        /// </summary>
        public void DisconnectClient(ulong clientId, string reason = "Forced disconnect")
        {
            Debug.Log($"[GameSessionController] Disconnecting client {clientId}: {reason}");

            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                NetworkManager.Singleton.DisconnectClient(clientId, reason);
            }
        }

        // ========== Unity Lifecycle ==========

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            m_RequiredPlayers = ServerSingleton.Instance?.GameManager?.TeamCount ?? 2;
            m_PlayerTracker = new PlayerConnectionTracker();

            var config = GameConfig.Instance;
            if (config != null)
            {
                inGameTimeout = config.MaxGameDurationSeconds;
            }
            else
            {
                inGameTimeout = 300f;
            }

            // プレイヤー数変化を監視
            m_PlayerTracker.OnPlayerCountChanged += count =>
            {
                if (State == GameSessionState.WaitingForPlayers && count >= m_RequiredPlayers)
                {
                    Debug.Log("[GameSessionController] Required players reached");
                    m_PlayersReadyTcs?.TrySetResult(true);
                }
            };
        }

        private async void Start()
        {
            await RunSessionLifecycleAsync();
        }

        private void OnDestroy()
        {
            m_PlayerTracker?.Dispose();

            // Cancel any pending TCS
            m_PlayersReadyTcs?.TrySetCanceled();
            m_GameEndTcs?.TrySetCanceled();

            if (Instance == this)
                Instance = null;
        }

        // ========== Main Session Flow ==========

        private async Task RunSessionLifecycleAsync()
        {
            Debug.Log("[GameSessionController] ========== SESSION START ==========");

            try
            {
                var gameManager = ServerSingleton.Instance?.GameManager;

                // Initialize TCS for this session
                m_PlayersReadyTcs = new TaskCompletionSource<bool>();
                m_GameEndTcs = new TaskCompletionSource<bool>();

                // 1. プレイヤー待機
                SetState(GameSessionState.WaitingForPlayers);
                Debug.Log($"[GameSessionController] Waiting for {m_RequiredPlayers} players...");
                bool ready = await WaitForPlayersAsync();

                if (!ready)
                {
                    // タイムアウト → 即座にシャットダウン
                    Debug.LogError("[GameSessionController] Timeout - not enough players");
                    SetState(GameSessionState.StartFailed);
                    await ScheduleShutdownAsync("Start timeout - not enough players");
                    return;
                }

                // 2. ゲーム開始
                Debug.Log("[GameSessionController] Starting game");
                SetState(GameSessionState.InGame);
                await gameManager.LockSessionForGameStartAsync();

                // 3. 終了条件を待つ
                Debug.Log("[GameSessionController] Waiting for end condition");
                bool gameEnd = await WaitForGameEndAsync();

                Debug.Log($"[GameSessionController] gameEnd = {gameEnd}");

                // 4. 終了処理
                SetState(GameSessionState.GameEnded);
                string endReason = gameEnd ? "Game completed normally" : "InGame timeout";
                await ScheduleShutdownAsync(endReason);
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameSessionController] Fatal error: {e.Message}");
                await ScheduleShutdownAsync($"Fatal error: {e.Message}");
            }
            finally
            {
                Debug.Log("[GameSessionController] ========== SESSION END ==========");
            }
        }

        private async Task<bool> WaitForPlayersAsync()
        {
            // 既に揃っている
            if (m_PlayerTracker.CurrentCount >= m_RequiredPlayers)
            {
                Debug.Log("[GameSessionController] Players already connected");
                return true;
            }

            // タイムアウト付き待機
            var timeout = Task.Delay(TimeSpan.FromSeconds(waitingPlayersTimeout));
            var ready = m_PlayersReadyTcs.Task;

            return await Task.WhenAny(ready, timeout) == ready;
        }

        private async Task<bool> WaitForGameEndAsync()
        {
            var timeout = Task.Delay(TimeSpan.FromSeconds(inGameTimeout));
            var gameEnd = m_GameEndTcs.Task;
            return await Task.WhenAny(gameEnd, timeout) == gameEnd;
        }

        public async Task ScheduleShutdownAsync(string reason)
        {
            const float SHUTDOWN_DELAY = 10f;
            Debug.Log($"[Shutdown] Scheduled in {SHUTDOWN_DELAY}s - Reason: {reason}");
            await Task.Delay(TimeSpan.FromSeconds(SHUTDOWN_DELAY));

            Debug.Log($"[Shutdown] Executing shutdown - Reason: {reason}");
            ServerSingleton.Instance?.GameManager?.CloseServer();
        }

        // ========== State Management ==========

        private void SetState(GameSessionState newState)
        {
            if (State == newState) return;

            var oldState = State;
            State = newState;
            Debug.Log($"[GameSessionController] State: {oldState} → {newState}");

            OnStateChanged?.Invoke(newState);
        }

        // ========== Support Classes ==========

        /// <summary>
        /// プレイヤー接続追跡
        /// </summary>
        private class PlayerConnectionTracker : IDisposable
        {
            private readonly HashSet<ulong> m_ConnectedPlayers = new();

            public int CurrentCount => m_ConnectedPlayers.Count;
            public event Action<int> OnPlayerCountChanged;

            public PlayerConnectionTracker()
            {
                var nm = NetworkManager.Singleton;
                if (nm != null)
                {
                    nm.OnClientConnectedCallback += HandleConnect;
                    nm.OnClientDisconnectCallback += HandleDisconnect;

                    // 初期状態を反映
                    if (nm.ConnectedClients != null)
                    {
                        foreach (var id in nm.ConnectedClients.Keys)
                            m_ConnectedPlayers.Add(id);
                    }
                }
            }

            private void HandleConnect(ulong clientId)
            {
                m_ConnectedPlayers.Add(clientId);
                Debug.Log($"[PlayerTracker] Connected: {clientId} (Total: {CurrentCount})");
                OnPlayerCountChanged?.Invoke(CurrentCount);
            }

            private void HandleDisconnect(ulong clientId)
            {
                m_ConnectedPlayers.Remove(clientId);
                Debug.Log($"[PlayerTracker] Disconnected: {clientId} (Total: {CurrentCount})");
                OnPlayerCountChanged?.Invoke(CurrentCount);
            }

            public void Dispose()
            {
                var nm = NetworkManager.Singleton;
                if (nm != null)
                {
                    nm.OnClientConnectedCallback -= HandleConnect;
                    nm.OnClientDisconnectCallback -= HandleDisconnect;
                }
            }
        }
#endif
    }
}
