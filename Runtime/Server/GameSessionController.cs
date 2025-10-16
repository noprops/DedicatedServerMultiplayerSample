using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server
{
    /// <summary>
    /// サーバーセッション管理
    /// </summary>
    public class GameSessionController : MonoBehaviour
    {
#if UNITY_SERVER || ENABLE_UCS_SERVER

        public static GameSessionController Instance { get; private set; }

        private enum SessionState
        {
            WaitingForPlayers,
            StartFailed,
            InGame,
            GameEnded
        }

        // ========== State & Events ==========
        private SessionState m_State;
        public bool IsInGame => m_State == SessionState.InGame;

        // ========== Configuration ==========
        [Header("Timeouts")]
        [SerializeField] private float waitingPlayersTimeout = 10f;

        // ========== Components ==========
        private int m_RequiredPlayers;
        private PlayerConnectionTracker m_PlayerTracker;

        // ========== Flow Control (Direct TCS) ==========
        private TaskCompletionSource<bool> m_PlayersReadyTcs;
        private TaskCompletionSource<bool> m_GameEndTcs;
        private TaskCompletionSource<bool> m_GameStartTcs;
        private CancellationTokenSource m_ShutdownCts;
        private Task m_ShutdownTask;
        private Dictionary<ulong, Dictionary<string, object>> m_ConnectedPlayers = new();

        // ========== Public API ==========

        /// <summary>
        /// ゲーム正常終了を通知
        /// </summary>
        public void NotifyGameEnded()
        {
            if (m_State != SessionState.InGame) return;

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

        /// <summary>
        /// ゲーム開始状態になるまで待機する。成功で true、失敗で false を返す
        /// </summary>
        public Task<bool> WaitForGameStartAsync()
        {
            if (m_State == SessionState.InGame)
                return Task.FromResult(true);
            if (m_State == SessionState.StartFailed)
                return Task.FromResult(false);

            m_GameStartTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            return m_GameStartTcs.Task;
        }

        public IReadOnlyDictionary<ulong, Dictionary<string, object>> GetConnectedPlayers() => m_ConnectedPlayers;

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
            m_GameStartTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // プレイヤー数変化を監視
            m_PlayerTracker.OnPlayerCountChanged += count =>
            {
                if (m_State == SessionState.WaitingForPlayers && count >= m_RequiredPlayers)
                {
                    Debug.Log("[GameSessionController] Required players reached");
                    m_PlayersReadyTcs?.TrySetResult(true);
                }
            };

            m_PlayerTracker.OnAllPlayersDisconnected += () =>
            {
                var delay = m_State == SessionState.InGame ? 30f : 5f;
                var reason = m_State == SessionState.InGame
                    ? "All players disconnected during game"
                    : "All players disconnected";

                Debug.Log($"[GameSessionController] All players disconnected. Scheduling shutdown in {delay}s.");
                _ = ScheduleShutdownAsync(reason, delay);
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
            m_GameStartTcs?.TrySetCanceled();
            m_ShutdownCts?.Cancel();
            m_ShutdownCts?.Dispose();

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
                m_GameStartTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                // 1. プレイヤー待機
                SetState(SessionState.WaitingForPlayers);
                Debug.Log($"[GameSessionController] Waiting for {m_RequiredPlayers} players...");
                bool ready = await WaitForPlayersAsync();

                if (!ready)
                {
                    // タイムアウト → 即座にシャットダウン
                    Debug.LogError("[GameSessionController] Timeout - not enough players");
                    SetState(SessionState.StartFailed);
                    await ScheduleShutdownAsync("Start timeout - not enough players", 10f);
                    return;
                }

                // 2. ゲーム開始
                Debug.Log("[GameSessionController] Starting game");
                CacheConnectedPlayers();
                SetState(SessionState.InGame);
                await gameManager.LockSessionForGameStartAsync();

                // 3. 終了条件を待つ
                Debug.Log("[GameSessionController] Waiting for end condition");
                await m_GameEndTcs.Task;

                // 4. 終了処理
                SetState(SessionState.GameEnded);
                await ScheduleShutdownAsync("Game completed normally", 20f);
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

        public async Task ScheduleShutdownAsync(string reason, float delaySeconds = 10f)
        {
            try
            {
                m_ShutdownCts?.Cancel();
                if (m_ShutdownTask != null)
                {
                    await m_ShutdownTask;
                }
            }
            catch (TaskCanceledException)
            {
                // ignored
            }
            finally
            {
                m_ShutdownCts?.Dispose();
            }

            m_ShutdownCts = new CancellationTokenSource();
            var token = m_ShutdownCts.Token;

            Debug.Log($"[Shutdown] Scheduled in {delaySeconds}s - Reason: {reason}");
            m_ShutdownTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                    if (!token.IsCancellationRequested)
                    {
                        Debug.Log($"[Shutdown] Executing shutdown - Reason: {reason}");
                        ServerSingleton.Instance?.GameManager?.CloseServer();
                    }
                }
                catch (TaskCanceledException)
                {
                    // cancellation expected when a new schedule overrides the previous one
                }
            }, token);
        }

        // ========== State Management ==========

        private void SetState(SessionState newState)
        {
            if (m_State == newState) return;

            var oldState = m_State;
            m_State = newState;
            Debug.Log($"[GameSessionController] State: {oldState} → {newState}");

            if (newState == SessionState.InGame)
                m_GameStartTcs?.TrySetResult(true);
            else if (newState == SessionState.StartFailed)
                m_GameStartTcs?.TrySetResult(false);
        }

        private void CacheConnectedPlayers()
        {
            var source = ServerSingleton.Instance?.GameManager?.GetAllConnectedPlayers();
            if (source == null)
            {
                m_ConnectedPlayers = new Dictionary<ulong, Dictionary<string, object>>();
                return;
            }

            m_ConnectedPlayers = new Dictionary<ulong, Dictionary<string, object>>(source);
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
            public event Action OnAllPlayersDisconnected;

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

                if (CurrentCount == 0)
                {
                    OnAllPlayersDisconnected?.Invoke();
                }
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
