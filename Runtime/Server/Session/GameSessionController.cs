using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Shared;
using DedicatedServerMultiplayerSample.Server.Core;
using DedicatedServerMultiplayerSample.Server.Bootstrap;

namespace DedicatedServerMultiplayerSample.Server.Session
{
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
        private ServerConnectionTracker m_ConnectionTracker;

        private DeferredActionScheduler m_ShutdownScheduler;

        public event Action<ulong[]> GameStartSucceeded;
        public event Action GameStartFailed;
        public event Action GameEnded;

        private bool m_StartEmitted;
        private bool m_StartFailed;

        #region Unity lifecycle

        /// <summary>
        /// Initializes singletons, trackers, and event subscriptions for the server session.
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            var manager = ServerSingleton.Instance?.GameManager;
            var requiredPlayers = manager?.TeamCount ?? 2;
            m_ConnectionTracker = manager?.ConnectionTracker;
            m_ConnectionTracker?.UpdateRequiredPlayers(requiredPlayers);

            if (m_ConnectionTracker != null)
            {
                m_ConnectionTracker.AllPlayersDisconnected += HandleAllPlayersDisconnected;
            }
            else
            {
                Debug.LogWarning("[Session] Connection tracker unavailable; shutdown scheduling will not respond to disconnects.");
            }

            m_ShutdownScheduler = new DeferredActionScheduler(() => ServerSingleton.Instance?.GameManager?.CloseServer());

            SetState(SessionState.WaitingForPlayers);
        }

        /// <summary>
        /// Schedules a shutdown when all players leave the session.
        /// </summary>
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

        /// <summary>
        /// Runs the session lifecycle coroutine on server start.
        /// </summary>
        private async void Start()
        {
            await RunSessionLifecycleAsync();
        }

        /// <summary>
        /// Cleans up event handlers, trackers, and cached state when the controller is destroyed.
        /// </summary>
        private void OnDestroy()
        {
            if (m_ConnectionTracker != null)
            {
                m_ConnectionTracker.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
                m_ConnectionTracker = null;
            }

            m_ShutdownScheduler.Dispose();

            if (Instance == this)
            {
                Instance = null;
            }

            m_StartEmitted = false;
            m_StartFailed = false;
            GameStartSucceeded = null;
            GameStartFailed = null;
            GameEnded = null;
        }

        #endregion

        #region Public API

        /// <summary>
        /// Signals that gameplay has finished so dependent systems can progress.
        /// </summary>
        public void NotifyGameEnded()
        {
            if (m_State != SessionState.InGame) return;

            Debug.Log("[Session] Game end notified");
            SetState(SessionState.GameEnded);
        }

        /// <summary>
        /// Awaitable alternative to the GameStartSucceeded event with timeout and cancellation support.
        /// </summary>
        public async Task<ulong[]> WaitForGameStartSucceededAsync(CancellationToken ct = default)
        {
            if (m_StartEmitted)
            {
                return GetKnownClientIdsSnapshot();
            }

            Action<ulong[]> wrapper = null;
            var success = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => m_StartEmitted,
                subscribe: handler =>
                {
                    wrapper = _ => handler();
                    GameStartSucceeded += wrapper;
                },
                unsubscribe: handler =>
                {
                    if (wrapper != null)
                    {
                        GameStartSucceeded -= wrapper;
                        wrapper = null;
                    }
                },
                ct: ct).ConfigureAwait(false);

            if (!success)
            {
                throw new TimeoutException("Game start did not succeed before the timeout elapsed.");
            }

            return GetKnownClientIdsSnapshot();
        }

        /// <summary>
        /// Awaitable alternative to the GameStartFailed event with timeout and cancellation support.
        /// </summary>
        public async Task WaitForGameStartFailedAsync(CancellationToken ct = default)
        {
            if (m_StartFailed)
            {
                return;
            }

            var success = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => m_StartFailed,
                subscribe: handler => GameStartFailed += handler,
                unsubscribe: handler => GameStartFailed -= handler,
                ct: ct).ConfigureAwait(false);

            if (!success)
            {
                throw new TimeoutException("Game start did not fail before the timeout elapsed.");
            }
        }

        /// <summary>
        /// Awaitable alternative to the GameEnded event with timeout and cancellation support.
        /// </summary>
        public async Task WaitForGameEndedAsync(TimeSpan timeout = default, CancellationToken ct = default)
        {
            if (m_State == SessionState.GameEnded || m_State == SessionState.Failed)
            {
                return;
            }

            var success = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => m_State == SessionState.GameEnded || m_State == SessionState.Failed,
                subscribe: handler => GameEnded += handler,
                unsubscribe: handler => GameEnded -= handler,
                timeout: timeout,
                ct: ct).ConfigureAwait(false);

            if (!success)
            {
                throw new TimeoutException("Game did not end before the timeout elapsed.");
            }
        }

        #endregion

        #region Session flow

        /// <summary>
        /// Coordinates waiting for players, gameplay, and shutdown timing.
        /// </summary>
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

                await WaitForGameEndedAsync(TimeSpan.Zero).ConfigureAwait(false);
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

        /// <summary>
        /// Waits until the required number of players connect or the timeout elapses.
        /// </summary>
        private async Task<bool> WaitForPlayersAsync(int timeoutSeconds)
        {
            if (m_ConnectionTracker == null)
            {
                Debug.LogWarning("[Session] Connection tracker missing; skipping player wait.");
                return true;
            }

            var clampedSeconds = Mathf.Max(0, timeoutSeconds);

            var ok = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => m_ConnectionTracker.HasRequiredPlayers,
                subscribe: handler => m_ConnectionTracker.RequiredPlayersReady += handler,
                unsubscribe: handler => m_ConnectionTracker.RequiredPlayersReady -= handler,
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

        /// <summary>
        /// Updates the internal session state and dispatches the corresponding latched events.
        /// </summary>
        private void SetState(SessionState newState)
        {
            if (m_State == newState) return;
            m_State = newState;
            switch (newState)
            {
                case SessionState.InGame:
                    m_StartFailed = false;
                    m_StartEmitted = true;
                    var ids = GetKnownClientIdsSnapshot();
                    GameStartSucceeded?.Invoke((ulong[])ids.Clone());
                    break;
                case SessionState.StartFailed:
                    m_StartFailed = true;
                    m_StartEmitted = false;
                    GameStartFailed?.Invoke();
                    break;
                case SessionState.Failed:
                    m_StartFailed = true;
                    m_StartEmitted = false;
                    GameStartFailed?.Invoke();
                    GameEnded?.Invoke();
                    break;
                case SessionState.GameEnded:
                    GameEnded?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Returns a snapshot of known clients for event consumers.
        /// </summary>
        private ulong[] GetKnownClientIdsSnapshot()
        {
            return m_ConnectionTracker?.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
        }

        /// <summary>
        #endregion

#endif
    }
}
