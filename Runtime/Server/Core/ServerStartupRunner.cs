#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Allocation;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Minimal orchestrator that wires together Multiplay allocation, scene loading, and shutdown scheduling.
    /// </summary>
    public sealed class ServerStartupRunner : IDisposable
    {
        private readonly NetworkManager _networkManager;
        private readonly int _defaultMaxPlayers;

        private readonly ServerConnectionStack _connectionStack;
        private MultiplaySessionService _multiplaySessionService;

        private bool _disposed;

        private const int WaitingPlayersTimeoutSeconds = 10;
        private const int SceneLoadTimeoutSeconds = 5;
        private const int ShutdownDelaySeconds = 10;

        public ServerStartupRunner(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
            _connectionStack = new ServerConnectionStack(_networkManager, _defaultMaxPlayers);
            _connectionStack.AllPlayersDisconnected += HandleAllPlayersDisconnected;
        }

        public async Task<bool> StartAsync()
        {
            try
            {
                var runtimeConfig = ServerRuntimeConfig.Capture();
                runtimeConfig.LogSummary();

                _multiplaySessionService = new MultiplaySessionService(runtimeConfig, _defaultMaxPlayers);
                var allocation = await _multiplaySessionService.AwaitAllocationAsync(CancellationToken.None);
                if (!allocation.Success)
                {
                    ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, "Match allocation failed", ShutdownDelaySeconds);
                    return false;
                }

                ServerTransportConfigurator.Configure(_networkManager, runtimeConfig);
                _connectionStack.Configure(allocation.ExpectedAuthIds ?? Array.Empty<string>(), allocation.TeamCount);

                var sceneLoadTimeoutMs = (int)TimeSpan.FromSeconds(SceneLoadTimeoutSeconds).TotalMilliseconds;
                if (!await _connectionStack.LoadSceneAsync("game", sceneLoadTimeoutMs, CancellationToken.None))
                {
                    ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, "Failed to load game scene", ShutdownDelaySeconds);
                    return false;
                }

                if (_multiplaySessionService.IsConnected)
                {
                    await _multiplaySessionService.SetPlayerReadinessAsync(true);
                }

                var waitingTimeout = TimeSpan.FromSeconds(WaitingPlayersTimeoutSeconds);
                var ready = await _connectionStack.WaitForAllClientsAsync(waitingTimeout, CancellationToken.None);
                if (!ready)
                {
                    ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.StartTimeout, "Not enough players", ShutdownDelaySeconds);
                    return true;
                }

                if (_multiplaySessionService != null && _multiplaySessionService.IsConnected)
                {
                    await _multiplaySessionService.LockSessionAsync();
                    await _multiplaySessionService.SetPlayerReadinessAsync(false);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerStartupRunner] Startup failed: {ex.Message}");
                ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, ex.Message, ShutdownDelaySeconds);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _connectionStack.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
            _connectionStack.Dispose();

            _multiplaySessionService?.Dispose();
            _multiplaySessionService = null;
        }

        public bool TryGetPlayerName(ulong clientId, out string name)
        {
            return _connectionStack.TryGetPlayerPayloadValue(clientId, "playerName", out name);
        }

        public bool TryGetPlayerPayloadValue<T>(ulong clientId, string key, out T value)
        {
            return _connectionStack.TryGetPlayerPayloadValue(clientId, key, out value);
        }

        public Task<bool> WaitForAllClientsAsync(TimeSpan timeout = default, CancellationToken token = default)
        {
            return _connectionStack.WaitForAllClientsAsync(timeout, token);
        }

        public IReadOnlyList<ulong> GetReadyClientsSnapshot()
        {
            return _connectionStack.GetReadyClientsSnapshot();
        }

        private void HandleAllPlayersDisconnected()
        {
            ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.AllPlayersDisconnected, "All players disconnected", ShutdownDelaySeconds);
        }
    }
}
#endif
