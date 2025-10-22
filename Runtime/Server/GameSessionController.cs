using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Server
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
        private PlayerConnectionTracker m_PlayerTracker;

        private DeferredActionScheduler m_ShutdownScheduler;

        public event Action<ulong[]> GameStartSucceeded;
        public event Action GameStartFailed;
        public event Action GameEnded;

        private bool m_StartEmitted;
        private bool m_StartFailed;
        private ulong[] m_StartIdsSnapshot = Array.Empty<ulong>();

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
            var requiredPlayers = ServerSingleton.Instance?.GameManager?.TeamCount ?? 2;
            m_PlayerTracker = new PlayerConnectionTracker(requiredPlayers);
            m_ShutdownScheduler = new DeferredActionScheduler(() => ServerSingleton.Instance?.GameManager?.CloseServer());
            m_PlayerTracker.AllPlayersDisconnected += HandleAllPlayersDisconnected;

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
            if (m_PlayerTracker != null)
            {
                m_PlayerTracker.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
                m_PlayerTracker.Dispose();
                m_PlayerTracker = null;
            }

            m_ShutdownScheduler.Dispose();

            if (Instance == this)
            {
                Instance = null;
            }

            m_StartEmitted = false;
            m_StartFailed = false;
            m_StartIdsSnapshot = Array.Empty<ulong>();
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
        public async Task<ulong[]> WaitForGameStartSucceededAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            if (_startEmitted)
            {
                return (ulong[])_startIdsSnapshot.Clone();
            }

            Action<ulong[]> wrapper = null;
            var success = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => _startEmitted,
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
                timeout: timeout,
                ct: ct).ConfigureAwait(false);

            if (!success)
            {
                throw new TimeoutException("Game start did not succeed before the timeout elapsed.");
            }

            return (ulong[])_startIdsSnapshot.Clone();
        }

        /// <summary>
        /// Awaitable alternative to the GameStartFailed event with timeout and cancellation support.
        /// </summary>
        public async Task WaitForGameStartFailedAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            if (_startFailed)
            {
                return;
            }

            var success = await AsyncExtensions.WaitSignalAsync(
                isAlreadyTrue: () => _startFailed,
                subscribe: handler => GameStartFailed += handler,
                unsubscribe: handler => GameStartFailed -= handler,
                timeout: timeout,
                ct: ct).ConfigureAwait(false);

            if (!success)
            {
                throw new TimeoutException("Game start did not fail before the timeout elapsed.");
            }
        }

        /// <summary>
        /// Awaitable alternative to the GameEnded event with timeout and cancellation support.
        /// </summary>
        public async Task WaitForGameEndedAsync(TimeSpan timeout, CancellationToken ct = default)
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
                    m_StartIdsSnapshot = CollectKnownPlayerIds().ToArray();
                    GameStartSucceeded?.Invoke((ulong[])m_StartIdsSnapshot.Clone());
                    break;
                case SessionState.StartFailed:
                    m_StartFailed = true;
                    m_StartEmitted = false;
                    m_StartIdsSnapshot = Array.Empty<ulong>();
                    GameStartFailed?.Invoke();
                    break;
                case SessionState.Failed:
                    m_StartFailed = true;
                    m_StartEmitted = false;
                    m_StartIdsSnapshot = Array.Empty<ulong>();
                    GameStartFailed?.Invoke();
                    GameEnded?.Invoke();
                    break;
                case SessionState.GameEnded:
                    GameEnded?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Builds a unique list of all players the tracker currently knows about.
        /// </summary>
        private List<ulong> CollectKnownPlayerIds()
        {
            if (m_PlayerTracker == null)
            {
                return new List<ulong>();
            }

            var connected = m_PlayerTracker.ConnectedClientIds;
            var disconnected = m_PlayerTracker.DisconnectedClientIds;

            var seen = new HashSet<ulong>();
            var result = new List<ulong>(connected.Count + disconnected.Count);

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
