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
            InGame,
            GameEnded,
            Failed
        }

        [Header("Timeouts")]
        [SerializeField] private float waitingPlayersTimeoutSeconds = 10f;

        [Header("Shutdown Delays")]
        [SerializeField] private float notEnoughPlayersShutdownDelaySeconds = 5f;
        [SerializeField] private float gameCompletedShutdownDelaySeconds = 10f;
        [SerializeField] private float fatalErrorShutdownDelaySeconds = 5f;

        private SessionState m_State;
        private int m_RequiredPlayers;
        private PlayerConnectionTracker m_PlayerTracker;

        private CancellationTokenSource m_ShutdownCts;
        private Task m_ShutdownTask;

        private event Action GameEnded;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            m_RequiredPlayers = ServerSingleton.Instance?.GameManager?.TeamCount ?? 2;
            m_PlayerTracker = new PlayerConnectionTracker(m_RequiredPlayers);
            m_PlayerTracker.AllPlayersDisconnected += HandleAllPlayersDisconnected;
        }

        private async void Start()
        {
            await RunSessionLifecycleAsync();
        }

        private void OnDestroy()
        {
            if (m_PlayerTracker != null)
            {
                m_PlayerTracker.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
                m_PlayerTracker.Dispose();
                m_PlayerTracker = null;
            }
            m_ShutdownCts?.Cancel();
            m_ShutdownCts?.Dispose();
            GameEnded = null;

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void NotifyGameEnded()
        {
            if (m_State != SessionState.InGame) return;

            Debug.Log("[Session] Game end notified");
            GameEnded?.Invoke();
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

            Debug.Log($"[Shutdown] scheduled in {delaySeconds}s : {reason}");
            m_ShutdownTask = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), token);
                    if (!token.IsCancellationRequested)
                    {
                        Debug.Log($"[Shutdown] executing: {reason}");
                        ServerSingleton.Instance?.GameManager?.CloseServer();
                    }
                }
                catch (TaskCanceledException)
                {
                    // expected when rescheduled
                }
            }, token);
        }

        private async Task RunSessionLifecycleAsync()
        {
            Debug.Log("[Session] === START ===");

            try
            {
                if (!await WaitForPlayersAsync(TimeSpan.FromSeconds(waitingPlayersTimeoutSeconds)))
                {
                    m_State = SessionState.Failed;
                    Debug.LogError("[Session] Timeout: not enough players");
                    await ScheduleShutdownAsync("Not enough players", notEnoughPlayersShutdownDelaySeconds);
                    return;
                }

                m_State = SessionState.InGame;
                Debug.Log("[Session] Game started");

                await WaitForGameEndAsync();
                m_State = SessionState.GameEnded;
                Debug.Log("[Session] Game ended");

                await ScheduleShutdownAsync("Game completed", gameCompletedShutdownDelaySeconds);
            }
            catch (Exception e)
            {
                m_State = SessionState.Failed;
                Debug.LogError($"[Session] Error: {e.Message}");
                await ScheduleShutdownAsync("Fatal error", fatalErrorShutdownDelaySeconds);
            }
            finally
            {
                Debug.Log("[Session] === END ===");
            }
        }

        private async Task<bool> WaitForPlayersAsync(TimeSpan timeout)
        {
            if (m_PlayerTracker.HasRequiredPlayers)
                return true;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnReady()
            {
                tcs.TrySetResult(true);
            }

            m_PlayerTracker.RequiredPlayersReady += OnReady;

            try
            {
                var timeoutTask = Task.Delay(timeout);
                var completed = await Task.WhenAny(tcs.Task, timeoutTask).ConfigureAwait(false);

                if (completed == timeoutTask)
                {
                    return false;
                }

                await tcs.Task.ConfigureAwait(false);
                Debug.Log("[Session] Required players reached");
                return true;
            }
            finally
            {
                m_PlayerTracker.RequiredPlayersReady -= OnReady;
            }
        }

        private async Task WaitForGameEndAsync()
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnEnded()
            {
                GameEnded -= OnEnded;
                tcs.TrySetResult(true);
            }

            GameEnded += OnEnded;

            try
            {
                await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                GameEnded -= OnEnded;
            }
        }

        private void HandleAllPlayersDisconnected()
        {
            var delay = m_State == SessionState.InGame
                ? gameCompletedShutdownDelaySeconds
                : notEnoughPlayersShutdownDelaySeconds;

            var reason = m_State == SessionState.InGame
                ? "All players disconnected during game"
                : "All players disconnected";

            Debug.Log($"[Session] All players disconnected. Scheduling shutdown in {delay}s.");
            _ = ScheduleShutdownAsync(reason, delay);
        }

        private class PlayerConnectionTracker : IDisposable
        {
            private readonly HashSet<ulong> m_Players = new();
            private readonly int m_RequiredPlayers;
            private bool m_ReadyNotified;

            public int CurrentCount => m_Players.Count;
            public bool HasRequiredPlayers => CurrentCount >= m_RequiredPlayers;
            public event Action RequiredPlayersReady;
            public event Action AllPlayersDisconnected;

            public PlayerConnectionTracker(int requiredPlayers)
            {
                m_RequiredPlayers = Math.Max(1, requiredPlayers);

                var nm = NetworkManager.Singleton;
                if (nm == null) return;
                nm.OnClientConnectedCallback += OnConnect;
                nm.OnClientDisconnectCallback += OnDisconnect;

                if (nm.ConnectedClients != null)
                {
                    foreach (var id in nm.ConnectedClients.Keys)
                    {
                        m_Players.Add(id);
                    }
                }

                CheckRequiredPlayersReached();
            }

            private void OnConnect(ulong id)
            {
                if (!m_Players.Add(id)) return;
                CheckRequiredPlayersReached();
            }

            private void OnDisconnect(ulong id)
            {
                if (!m_Players.Remove(id)) return;
                if (CurrentCount == 0)
                {
                    AllPlayersDisconnected?.Invoke();
                }
            }

            private void CheckRequiredPlayersReached()
            {
                if (m_ReadyNotified || !HasRequiredPlayers) return;
                m_ReadyNotified = true;
                RequiredPlayersReady?.Invoke();
            }

            public void Dispose()
            {
                var nm = NetworkManager.Singleton;
                if (nm == null) return;
                nm.OnClientConnectedCallback -= OnConnect;
                nm.OnClientDisconnectCallback -= OnDisconnect;
            }
        }
#endif
    }
}
