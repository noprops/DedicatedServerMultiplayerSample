using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Server
{
    public readonly struct GameStartInfo
    {
        public bool Success { get; }
        public IReadOnlyList<ulong> ClientIds { get; }

        public GameStartInfo(bool success, IReadOnlyList<ulong> clientIds)
        {
            Success = success;
            ClientIds = clientIds ?? Array.Empty<ulong>();
        }
    }

    /// <summary>
    /// ゲームセッションの進行（プレイヤー待機〜ゲーム終了〜シャットダウン）を管理するコンポーネント。
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
            StartFailed,
            Failed
        }

        [Header("Timeouts")]
        [SerializeField] private int waitingPlayersTimeoutSeconds = 10;

        [Header("Shutdown Delays")]
        [SerializeField] private float notEnoughPlayersShutdownDelaySeconds = 5f;
        [SerializeField] private float gameCompletedShutdownDelaySeconds = 10f;
        [SerializeField] private float fatalErrorShutdownDelaySeconds = 5f;

        private SessionState m_State = SessionState.WaitingForPlayers;
        private PlayerConnectionTracker m_PlayerTracker;

        private DeferredActionScheduler m_ShutdownScheduler;
        private readonly TaskCompletionSource<GameStartInfo> m_GameStartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> m_GameEndedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        #region Unity lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            var requiredPlayers = ServerSingleton.Instance?.GameManager?.TeamCount ?? 2;
            m_PlayerTracker = new PlayerConnectionTracker(requiredPlayers);
            m_ShutdownScheduler = new DeferredActionScheduler(() => ServerSingleton.Instance?.GameManager?.CloseServer());
            m_PlayerTracker.AllPlayersDisconnected += HandleAllPlayersDisconnected;

            SetState(SessionState.WaitingForPlayers);
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
            _ = m_ShutdownScheduler.ScheduleAsync(reason, delay);
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

            m_ShutdownScheduler.Dispose();
            m_GameStartedTcs.TrySetCanceled();
            m_GameEndedTcs.TrySetCanceled();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// ゲーム開始を待機し、開始したかどうかを返す。
        /// </summary>
        public Task<GameStartInfo> WaitForGameStartAsync(CancellationToken ct = default)
        {
            if (!ct.CanBeCanceled)
            {
                return m_GameStartedTcs.Task;
            }

            return m_GameStartedTcs.Task.WaitOrCancel(ct);
        }

        /// <summary>
        /// ゲーム終了を待機する。
        /// </summary>
        public Task WaitForGameEndAsync(CancellationToken ct = default)
        {
            return m_GameEndedTcs.Task.WaitOrCancel(ct);
        }

        /// <summary>
        /// ゲーム終了を通知する。
        /// </summary>
        public void NotifyGameEnded()
        {
            if (m_State != SessionState.InGame) return;

            Debug.Log("[Session] Game end notified");
            SetState(SessionState.GameEnded);
        }

        #endregion

        #region Session flow

        private async Task RunSessionLifecycleAsync()
        {
            Debug.Log("[Session] === START ===");

            try
            {
                if (!await WaitForPlayersAsync(waitingPlayersTimeoutSeconds))
                {
                    SetState(SessionState.StartFailed);
                    Debug.LogError("[Session] Timeout: not enough players");
                    await m_ShutdownScheduler.ScheduleAsync("Not enough players", notEnoughPlayersShutdownDelaySeconds);
                    return;
                }

                SetState(SessionState.InGame);
                Debug.Log("[Session] Game started");

                await WaitForGameEndAsync();
                Debug.Log("[Session] Game ended");

                await m_ShutdownScheduler.ScheduleAsync("Game completed", gameCompletedShutdownDelaySeconds);
            }
            catch (Exception e)
            {
                SetState(SessionState.Failed);
                Debug.LogError($"[Session] Error: {e.Message}");
                await m_ShutdownScheduler.ScheduleAsync("Fatal error", fatalErrorShutdownDelaySeconds);
            }
            finally
            {
                Debug.Log("[Session] === END ===");
            }
        }

        private async Task<bool> WaitForPlayersAsync(int timeoutSeconds)
        {
            var clampedSeconds = Mathf.Max(0, timeoutSeconds);

            var ok = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => m_PlayerTracker.HasRequiredPlayers,
                subscribe: handler => m_PlayerTracker.RequiredPlayersReady += handler,
                unsubscribe: handler => m_PlayerTracker.RequiredPlayersReady -= handler,
                timeout: TimeSpan.FromSeconds(clampedSeconds)
            );

            if (ok)
            {
                Debug.Log("[Session] Required players reached");
            }

            return ok;
        }

        #endregion

        #region Helpers

        private void SetState(SessionState newState)
        {
            if (m_State == newState) return;
            m_State = newState;
            switch (newState)
            {
                case SessionState.InGame:
                    m_GameStartedTcs.TrySetResult(new GameStartInfo(true, CollectKnownPlayerIds()));
                    break;
                case SessionState.StartFailed:
                    m_GameStartedTcs.TrySetResult(new GameStartInfo(false, Array.Empty<ulong>()));
                    break;
                case SessionState.Failed:
                    m_GameStartedTcs.TrySetResult(new GameStartInfo(false, Array.Empty<ulong>()));
                    m_GameEndedTcs.TrySetResult(null);
                    break;
                case SessionState.GameEnded:
                    m_GameEndedTcs.TrySetResult(null);
                    break;
            }
        }

        private IReadOnlyList<ulong> CollectKnownPlayerIds()
        {
            if (m_PlayerTracker == null)
            {
                return Array.Empty<ulong>();
            }

            var connected = m_PlayerTracker.ConnectedClientIds;
            var disconnected = m_PlayerTracker.DisconnectedClientIds;

            var result = new List<ulong>(connected.Count + disconnected.Count);
            var seen = new HashSet<ulong>();
            foreach (var id in connected)
            {
                if (seen.Add(id))
                {
                    result.Add(id);
                }
            }

            foreach (var id in disconnected)
            {
                if (seen.Add(id))
                {
                    result.Add(id);
                }
            }

            return result;
        }

        #endregion

#endif
    }
}
