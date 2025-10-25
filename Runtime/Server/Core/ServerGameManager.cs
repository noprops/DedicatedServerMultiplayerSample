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
        private ServerConnectionGate _connectionGate;
        private ServerConnectionTracker _connectionTracker;
        private ServerSceneLoader _sceneLoader;
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
        public ServerConnectionTracker ConnectionTracker => _connectionTracker;
        public bool HasAllClientsConnected => _allConnectedEmitted;
        public ulong[] ConnectedClientsSnapshot => _allConnectedIds;
        public bool IsShutdownRequested => _shutdownEmitted;
        public ShutdownKind? LastRequestedShutdownKind => _shutdownEmitted ? _lastShutdownKind : (ShutdownKind?)null;
        public string LastRequestedShutdownReason => _lastShutdownReason;

        public event Action<ulong[]> AllClientsConnected;
        public event Action<ShutdownKind, string> ShutdownRequested;

        public void AddAllClientsConnected(Action<ulong[]> handler, bool replay = true)
        {
            if (handler == null) return;
            AllClientsConnected += handler;
            if (replay && _allConnectedEmitted)
            {
                handler((ulong[])_allConnectedIds.Clone());
            }
        }

        public void RemoveAllClientsConnected(Action<ulong[]> handler)
        {
            if (handler == null) return;
            AllClientsConnected -= handler;
        }

        public void AddShutdownRequested(Action<ShutdownKind, string> handler, bool replay = true)
        {
            if (handler == null) return;
            ShutdownRequested += handler;
            if (replay && _shutdownEmitted)
            {
                handler(_lastShutdownKind, _lastShutdownReason);
            }
        }

        public void RemoveShutdownRequested(Action<ShutdownKind, string> handler)
        {
            if (handler == null) return;
            ShutdownRequested -= handler;
        }

        public ServerGameManager(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
            _shutdownScheduler = new DeferredActionScheduler(CloseServer);
            Debug.Log("[ServerGameManager] Created");
        }

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

                _connectionDirectory.Clear();

                _connectionGate = new ServerConnectionGate(
                    _networkManager,
                    _connectionDirectory,
                    new ServerConnectionPolicy())
                {
                    SceneIsLoaded = () => _isSceneLoaded,
                    CurrentPlayers = () => _connectionDirectory.Count,
                    Capacity = () => _expectedAuthIds.Count > 0 ? _expectedAuthIds.Count : _defaultMaxPlayers,
                    ExpectedAuthIds = () => _expectedAuthIds,
                    AllowNewConnections = true
                };

                _connectionTracker = new ServerConnectionTracker(_networkManager, _connectionDirectory, _teamCount);

                _sceneLoader = new ServerSceneLoader(_networkManager);
                _isSceneLoaded = false;
                var sceneLoaded = await _sceneLoader.LoadAsync("game", 5000, () =>
                {
                    _isSceneLoaded = true;
                    _connectionGate?.ReleasePendingApprovals();
                }, cancellationToken);
                if (!sceneLoaded)
                {
                    Debug.LogError("[ServerGameManager] Failed to load game scene");
                    RequestShutdown(ShutdownKind.Error, "Failed to load game scene", ErrorShutdownDelaySeconds);
                    return false;
                }

                if (_multiplayIntegration != null && _multiplayIntegration.IsConnected)
                {
                    await _multiplayIntegration.SetPlayerReadinessAsync(true);
                }

                SubscribeSessionEvents();
                _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = MonitorClientReadinessAsync(_sessionCts.Token);

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

        public async Task LockSessionForGameStartAsync()
        {
            if (_connectionGate != null)
            {
                _connectionGate.AllowNewConnections = false;
            }
            Debug.Log("[ServerGameManager] Locking session; new connections will be rejected");

            if (_multiplayIntegration != null && _multiplayIntegration.IsConnected)
            {
                await _multiplayIntegration.LockSessionAsync();
                await _multiplayIntegration.SetPlayerReadinessAsync(false);
            }
            Debug.Log("[ServerGameManager] Player readiness disabled");
        }

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectedPlayers()
        {
            return _connectionDirectory?.GetAllConnectionData() ?? new Dictionary<ulong, Dictionary<string, object>>();
        }

        public void DisconnectClient(ulong clientId, string reason = "Forced disconnect")
        {
            if (_networkManager == null || !_networkManager.IsServer)
            {
                return;
            }

            _networkManager.DisconnectClient(clientId, reason);
        }

        public void CloseServer()
        {
            Dispose();
            Application.Quit();
        }

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

            _connectionGate?.Dispose();
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

        public void RequestShutdown(ShutdownKind kind, string reason, float delaySeconds = NormalShutdownDelaySeconds)
        {
            if (!_shutdownEmitted)
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

            var scheduler = _shutdownScheduler ??= new DeferredActionScheduler(CloseServer);
            _ = scheduler.ScheduleAsync(reason, delaySeconds);
        }

        private void SubscribeSessionEvents()
        {
            if (_connectionTracker != null)
            {
                _connectionTracker.AllPlayersDisconnected += HandleAllPlayersDisconnected;
            }
        }

        private async Task MonitorClientReadinessAsync(CancellationToken token)
        {
            if (_connectionTracker == null)
            {
                Debug.LogWarning("[ServerGameManager] Connection tracker unavailable; skipping client readiness monitoring.");
                return;
            }

            try
            {
                var ready = await AsyncExtensions.WaitSignalAsync(
                    isAlreadyTrue: () => _connectionTracker.HasRequiredPlayers,
                    subscribe: handler => _connectionTracker.RequiredPlayersReady += handler,
                    unsubscribe: handler => _connectionTracker.RequiredPlayersReady -= handler,
                    timeout: TimeSpan.FromSeconds(WaitingPlayersTimeoutSeconds),
                    ct: token).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (ready)
                {
                    EmitAllClientsConnected();
                }
                else
                {
                    RequestShutdown(ShutdownKind.StartTimeout, "Not enough players", StartTimeoutShutdownDelaySeconds);
                }
            }
            catch (OperationCanceledException)
            {
                // ignored - session ended/cancelled
            }
        }

        private void EmitAllClientsConnected()
        {
            if (_allConnectedEmitted)
            {
                return;
            }

            _allConnectedEmitted = true;
            _allConnectedIds = _connectionTracker?.GetKnownClientIds()?.ToArray() ?? Array.Empty<ulong>();
            var replayPayload = (ulong[])_allConnectedIds.Clone();
            Debug.Log($"[ServerGameManager] All required clients connected ({replayPayload.Length})");
            AllClientsConnected?.Invoke(replayPayload);
        }

        private void HandleAllPlayersDisconnected()
        {
            RequestShutdown(ShutdownKind.AllPlayersDisconnected, "All players disconnected", StartTimeoutShutdownDelaySeconds);
        }
    }
}
#endif
