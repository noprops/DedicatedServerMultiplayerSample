#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using DedicatedServerMultiplayerSample.Server.Infrastructure;
using DedicatedServerMultiplayerSample.Shared;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    public enum ShutdownKind
    {
        Normal,
        Error,
        StartTimeout,
        AllPlayersDisconnected
    }

    /// <summary>
    /// Orchestrates server startup, session locking, and shutdown using helper components.
    /// </summary>
    public class ServerGameManager : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly int _defaultMaxPlayers;

        private ServerRuntimeConfig _runtimeConfig;
        private ServerMultiplayIntegration _multiplayIntegration;
        private readonly ConnectionDirectory _connectionDirectory = new();
        private ConnectionApprover _connectionApprover;
        private ServerConnectionTracker _connectionTracker;
        private ServerSceneLoader _sceneLoader;
        // auth ids supplied by the matchmaker for the players assigned to this session
        private readonly List<string> _expectedAuthIds = new();
        private DeferredActionScheduler _shutdownScheduler;
        private CancellationTokenSource _sessionCts;

        private int _teamCount = 2;
        private bool _isSceneLoaded;
        private bool _isDisposed;
        private bool _allConnectedEmitted;
        private ulong[] _allConnectedIds = Array.Empty<ulong>();

        private bool _shutdownEmitted;
        private ShutdownKind _lastShutdownKind = ShutdownKind.Normal;
        private string _lastShutdownReason = string.Empty;

        private const float WaitingPlayersTimeoutSeconds = 10f;
        private const float StartTimeoutShutdownDelaySeconds = 5f;
        private const float NormalShutdownDelaySeconds = 10f;
        private const float ErrorShutdownDelaySeconds = 5f;

        public int TeamCount => _teamCount;

        /// <summary>
        /// Fired once when the required clients have connected. Replayed to late subscribers if requested.
        /// </summary>
        public event Action<ulong[]> AllClientsConnected;
        /// <summary>
        /// Indicates whether <see cref="AllClientsConnected"/> has already been raised for the current session.
        /// </summary>
        public bool AreAllClientsConnected => _allConnectedEmitted;
        /// <summary>
        /// Returns the cached client identifiers captured when <see cref="AllClientsConnected"/> fired.
        /// </summary>
        public IReadOnlyList<ulong> ConnectedClientSnapshot => _allConnectedIds;
        /// <summary>
        /// Fired when a shutdown is requested. Replayed to late subscribers if requested.
        /// </summary>
        public event Action<ShutdownKind, string> ShutdownRequested;

        /// <summary>
        /// Constructs a new server game manager with the provided network manager and player capacity.
        /// </summary>
        public ServerGameManager(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
            _shutdownScheduler = new DeferredActionScheduler(CloseServer);
            Debug.Log("[ServerGameManager] Created");
        }

        /// <summary>
        /// Subscribes to shutdown notifications. When <paramref name="replay"/> is true and a shutdown was already
        /// requested, the handler is invoked immediately with the cached kind and reason.
        /// </summary>
        public void AddShutdownRequested(Action<ShutdownKind, string> handler, bool replay = true)
        {
            if (handler == null) return;
            ShutdownRequested += handler;
            if (replay && _shutdownEmitted)
            {
                handler(_lastShutdownKind, _lastShutdownReason);
            }
        }

        /// <summary>
        /// Removes a previously registered shutdown handler.
        /// </summary>
        public void RemoveShutdownRequested(Action<ShutdownKind, string> handler)
        {
            if (handler == null) return;
            ShutdownRequested -= handler;
        }

        /// <summary>
        /// Attempts to resolve a display name for the specified client using registered payload information.
        /// </summary>
        public bool TryGetPlayerDisplayName(ulong clientId, out string playerName)
        {
            playerName = null;

            if (_connectionDirectory.TryGet<string>(clientId, "playerName", out var payloadName) &&
                !string.IsNullOrWhiteSpace(payloadName))
            {
                playerName = payloadName;
                return true;
            }

            if (_connectionDirectory.TryGetAuthId(clientId, out var authId) &&
                !string.IsNullOrWhiteSpace(authId))
            {
                playerName = authId;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Starts the dedicated server workflow, acquiring allocations, loading scenes, and opening the gate for clients.
        /// </summary>
        public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
        {
            Debug.Log("[ServerGameManager] ========== SERVER STARTUP BEGIN ==========");

            try
            {
                var configurator = new ServerAllocationConfigurator(_networkManager, _defaultMaxPlayers);
                var allocationResult = await configurator.RunAsync(cancellationToken);
                if (!allocationResult.Success)
                {
                    Debug.LogError("[ServerGameManager] Allocation failed");
                    RequestShutdown(ShutdownKind.Error, "Match allocation failed", ErrorShutdownDelaySeconds);
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                _runtimeConfig = allocationResult.RuntimeConfig;
                _multiplayIntegration = allocationResult.MultiplayIntegration;
                _teamCount = allocationResult.TeamCount;

                _expectedAuthIds.Clear();
                if (allocationResult.ExpectedAuthIds != null)
                {
                    _expectedAuthIds.AddRange(allocationResult.ExpectedAuthIds);
                }

                BuildConnectionStack();

                if (!await LoadGameSceneOrShutdownAsync(cancellationToken))
                {
                    return false;
                }

                if (_multiplayIntegration != null && _multiplayIntegration.IsConnected)
                {
                    await _multiplayIntegration.SetPlayerReadinessAsync(true);
                }

                if (_connectionTracker != null)
                {
                    _connectionTracker.AllPlayersDisconnected += HandleAllPlayersDisconnected;
                }
                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var ready = await AwaitRequiredPlayersAsync(_sessionCts.Token);

                if (_sessionCts.IsCancellationRequested)
                {
                    return true;
                }

                if (ready)
                {
                    await LockSessionAsync();
                    EmitAllClientsConnected();
                }
                else
                {
                    RequestShutdown(ShutdownKind.StartTimeout, "Not enough players", StartTimeoutShutdownDelaySeconds);
                }

                Debug.Log("[ServerGameManager] ========== SERVER STARTUP COMPLETE ==========");
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[ServerGameManager] Startup cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerGameManager] Server startup failed: {ex.Message}");
                RequestShutdown(ShutdownKind.Error, ex.Message, ErrorShutdownDelaySeconds);
                return false;
            }
        }

        /// <summary>
        /// Schedules a shutdown with the given reason, emitting a single notification the first time it is requested.
        /// </summary>
        public bool RequestShutdown(ShutdownKind kind, string reason, float delaySeconds = NormalShutdownDelaySeconds)
        {
            var firstRequest = !_shutdownEmitted;

            if (firstRequest)
            {
                _shutdownEmitted = true;
                _lastShutdownKind = kind;
                _lastShutdownReason = reason;
                Debug.Log($"[ServerGameManager] Emitting shutdown request: {kind} - {reason}");
                ShutdownRequested?.Invoke(kind, reason);
            }
            else
            {
                Debug.Log($"[ServerGameManager] Shutdown already requested ({_lastShutdownKind}); rescheduling with {kind} - {reason}");
            }

            if (_shutdownScheduler != null)
            {
                _ = _shutdownScheduler.ScheduleAsync(reason, delaySeconds);
            }

            return firstRequest;
        }

        /// <summary>
        /// Builds the connection gate and tracker used to control and observe incoming clients.
        /// </summary>
        private void BuildConnectionStack()
        {
            _connectionDirectory.Clear();

            _connectionApprover?.Dispose();
            _connectionApprover = null;

            if (_connectionTracker != null)
            {
                _connectionTracker.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
                _connectionTracker.Dispose();
            }

            _connectionTracker = new ServerConnectionTracker(_networkManager, _connectionDirectory, _teamCount);

            _connectionApprover = new ConnectionApprover(
                networkManager: _networkManager,
                isSceneLoaded: () => _isSceneLoaded,
                currentPlayers: () => _connectionTracker?.ActiveClientCount ?? 0,
                capacity: () => _expectedAuthIds.Count > 0 ? _expectedAuthIds.Count : _defaultMaxPlayers,
                expectedAuthIds: () => _expectedAuthIds,
                authInUse: auth => _connectionTracker != null && _connectionTracker.IsAuthConnected(auth),
                resolveAuthId: payloadBytes => _connectionDirectory.TryParseAuthId(payloadBytes, out var parsed) ? parsed : null,
                registerPayload: (clientId, payload) => _connectionDirectory.Register(clientId, payload));
        }

        /// <summary>
        /// Loads the gameplay scene and requests shutdown if the operation fails.
        /// </summary>
        private async Task<bool> LoadGameSceneOrShutdownAsync(CancellationToken ct)
        {
            _sceneLoader ??= new ServerSceneLoader(_networkManager);
            _isSceneLoaded = false;

            var ok = await _sceneLoader.LoadAsync("game", 5000, () =>
            {
                _isSceneLoaded = true;
                _connectionApprover?.ReleasePending();
            }, ct);

            if (!ok)
            {
                Debug.LogError("[ServerGameManager] Failed to load game scene");
                RequestShutdown(ShutdownKind.Error, "Failed to load game scene", ErrorShutdownDelaySeconds);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Forces the specified client to disconnect with an optional reason.
        /// </summary>
        public void DisconnectClient(ulong clientId, string reason = "Forced disconnect")
        {
            if (_networkManager == null || !_networkManager.IsServer)
            {
                return;
            }

            _networkManager.DisconnectClient(clientId, reason);
        }

        /// <summary>
        /// Releases all managed resources and stops the network server if it is running.
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _sessionCts?.Cancel();
            _sessionCts?.Dispose();
            _sessionCts = null;

            if (_connectionTracker != null)
            {
                _connectionTracker.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
            }

            _connectionApprover?.Dispose();
            _connectionApprover = null;
            _connectionTracker?.Dispose();
            _multiplayIntegration?.Dispose();
            _shutdownScheduler?.Dispose();
            _shutdownScheduler = null;

            if (_networkManager != null)
            {
                _networkManager.ConnectionApprovalCallback = null;

                if (_networkManager.IsListening || _networkManager.IsServer)
                {
                    _networkManager.Shutdown();
                }
            }

            _connectionDirectory.Clear();

            Debug.Log("[ServerGameManager] Disposed");
        }

        /// <summary>
        /// Waits until the required number of clients have connected or a timeout elapses, then signals outcome.
        /// </summary>
        private async Task<bool> AwaitRequiredPlayersAsync(CancellationToken token)
        {
            if (_connectionTracker == null)
            {
                Debug.LogWarning("[ServerGameManager] Connection tracker unavailable; skipping client readiness monitoring.");
                return false;
            }

            if (_connectionTracker.HasRequiredPlayers)
            {
                // すでに全くライアントが揃っている場合
                return true;
            }

            try
            {
                var timeout = TimeSpan.FromSeconds(WaitingPlayersTimeoutSeconds);
                using var awaiter = new SimpleSignalAwaiter(timeout, token);

                void OnReady()
                {
                    awaiter.OnSignal();
                }

                _connectionTracker.RequiredPlayersReady += OnReady;

                try
                {
                    var ready = await awaiter.WaitAsync(token).ConfigureAwait(false);

                    if (token.IsCancellationRequested)
                    {
                        return false;
                    }

                    return ready;
                }
                finally
                {
                    _connectionTracker.RequiredPlayersReady -= OnReady;
                }

            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// Locks the session to reject further connections once participants are confirmed.
        /// </summary>
        private async Task LockSessionAsync()
        {
            if (_connectionApprover != null)
            {
                _connectionApprover.AllowNewConnections = false;
            }

            if (_multiplayIntegration != null && _multiplayIntegration.IsConnected)
            {
                try
                {
                    await _multiplayIntegration.LockSessionAsync();
                    await _multiplayIntegration.SetPlayerReadinessAsync(false);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ServerGameManager] Failed to lock session: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Emits the client-connected notification exactly once with a cached snapshot.
        /// </summary>
        private void EmitAllClientsConnected()
        {
            if (_shutdownEmitted || _allConnectedEmitted)
            {
                return;
            }

            _allConnectedEmitted = true;
            _allConnectedIds = _connectionTracker?.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
            var replayPayload = (ulong[])_allConnectedIds.Clone();
            Debug.Log($"[ServerGameManager] All required clients connected ({replayPayload.Length})");
            AllClientsConnected?.Invoke(replayPayload);
        }

        /// <summary>
        /// Handles the "all players disconnected" event by scheduling a shutdown.
        /// </summary>
        private void HandleAllPlayersDisconnected()
        {
            RequestShutdown(ShutdownKind.AllPlayersDisconnected, "All players disconnected", StartTimeoutShutdownDelaySeconds);
        }

        /// <summary>
        /// Initiates server shutdown and quits the application immediately.
        /// </summary>
        private void CloseServer()
        {
            Dispose();
            Application.Quit();
        }

    }
}
#endif
