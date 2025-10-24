#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Infrastructure;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
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

        private int _teamCount = 2;
        private bool _isSceneLoaded;
        private bool _isDisposed;

        public int TeamCount => _teamCount;
        public ServerConnectionTracker ConnectionTracker => _connectionTracker;

        public ServerGameManager(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
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
                    CloseServer();
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
                    CloseServer();
                    return false;
                }

                if (_multiplayIntegration != null && _multiplayIntegration.IsConnected)
                {
                    await _multiplayIntegration.SetPlayerReadinessAsync(true);
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
                CloseServer();
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

            _connectionGate?.Dispose();
            _connectionTracker?.Dispose();
            _multiplayIntegration?.Dispose();

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
    }
}
#endif
