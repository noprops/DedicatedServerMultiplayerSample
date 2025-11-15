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

        private static readonly TimeSpan WaitingPlayersTimeout = TimeSpan.FromMilliseconds(10_000);
        private static readonly TimeSpan SceneLoadTimeout = TimeSpan.FromMilliseconds(5_000);
        private static readonly TimeSpan StartTimeoutShutdownDelay = TimeSpan.FromMilliseconds(5_000);
        private static readonly TimeSpan ErrorShutdownDelay = TimeSpan.FromMilliseconds(5_000);

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
                    ScheduleShutdown(ShutdownKind.Error, "Match allocation failed", ErrorShutdownDelay);
                    return false;
                }

                ServerTransportConfigurator.Configure(_networkManager, runtimeConfig);
                _connectionStack.Configure(allocation.ExpectedAuthIds ?? Array.Empty<string>(), allocation.TeamCount);

                if (!await _connectionStack.LoadSceneAsync("game", (int)SceneLoadTimeout.TotalMilliseconds, CancellationToken.None))
                {
                    ScheduleShutdown(ShutdownKind.Error, "Failed to load game scene", ErrorShutdownDelay);
                    return false;
                }

                if (_multiplaySessionService.IsConnected)
                {
                    await _multiplaySessionService.SetPlayerReadinessAsync(true);
                }

                var connectedSnapshot = await _connectionStack.WaitForAllClientsAsync(
                    WaitingPlayersTimeout, CancellationToken.None);
                if (connectedSnapshot == null || connectedSnapshot.Count == 0)
                {
                    ScheduleShutdown(ShutdownKind.StartTimeout, "Not enough players", StartTimeoutShutdownDelay);
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
                ScheduleShutdown(ShutdownKind.Error, ex.Message, ErrorShutdownDelay);
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

        public Task<IReadOnlyList<ulong>> WaitForAllClientsAsync(TimeSpan timeout = default, CancellationToken token = default)
        {
            return _connectionStack.WaitForAllClientsAsync(timeout, token);
        }

        private void HandleAllPlayersDisconnected()
        {
            ScheduleShutdown(ShutdownKind.AllPlayersDisconnected, "All players disconnected", StartTimeoutShutdownDelay);
        }

        private void ScheduleShutdown(ShutdownKind kind, string reason, TimeSpan delay)
        {
            ServerSingleton.Instance?.ScheduleShutdown(kind, reason, delay);
        }
    }
}
#endif
