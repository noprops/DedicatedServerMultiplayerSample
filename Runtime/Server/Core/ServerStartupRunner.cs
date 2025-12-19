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
        private readonly TaskCompletionSource<bool> _startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

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

        #region Public API

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
                    _startupCompletion.TrySetResult(false);
                    ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, "Match allocation failed", ShutdownDelaySeconds);
                    return false;
                }

                ServerTransportConfigurator.Configure(_networkManager, runtimeConfig);
                _connectionStack.Configure(allocation.ExpectedAuthIds ?? Array.Empty<string>(), allocation.TeamCount);

                var sceneLoadTimeoutMs = (int)TimeSpan.FromSeconds(SceneLoadTimeoutSeconds).TotalMilliseconds;
                if (!await _connectionStack.LoadSceneAsync("game", sceneLoadTimeoutMs, CancellationToken.None))
                {
                    _startupCompletion.TrySetResult(false);
                    ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, "Failed to load game scene", ShutdownDelaySeconds);
                    return false;
                }

                if (_multiplaySessionService.IsConnected)
                {
                    await _multiplaySessionService.SetPlayerReadinessAsync(true);
                }

                var ready = await WaitForClientsWithTimeoutAsync(TimeSpan.FromSeconds(WaitingPlayersTimeoutSeconds));
                if (!ready)
                {
                    ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.StartTimeout, "Not enough players", ShutdownDelaySeconds);
                    return true;
                }

                await LockSessionAsync();

                return true;
            }
            catch (Exception ex)
            {
                _startupCompletion.TrySetResult(false);
                Debug.LogError($"[ServerStartupRunner] Startup failed: {ex.Message}");
                ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.Error, ex.Message, ShutdownDelaySeconds);
                return false;
            }
        }

        /// <summary>
        /// Await the result of <see cref="StartAsync"/> (true = clients gathered, false = failed/timeout).
        /// </summary>
        public Task<bool> WaitForStartupCompletionAsync() => _startupCompletion.Task;

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

        public async Task LockSessionAsync()
        {
            if (_multiplaySessionService != null && _multiplaySessionService.IsConnected)
            {
                await _multiplaySessionService.LockSessionAsync();
                await _multiplaySessionService.SetPlayerReadinessAsync(false);
            }
        }

        public IReadOnlyList<ulong> GetReadyClientsSnapshot()
        {
            return _connectionStack.GetReadyClientsSnapshot();
        }

        #endregion

        private async Task<bool> WaitForClientsWithTimeoutAsync(TimeSpan timeout)
        {
            if (_startupCompletion.Task.IsCompleted)
            {
                return await _startupCompletion.Task;
            }

            var waitTask = _connectionStack.WaitForAllClientsAsync();
            bool success;

            if (timeout <= TimeSpan.Zero)
            {
                var snapshotImmediate = await waitTask;
                success = snapshotImmediate != null && snapshotImmediate.Count > 0;
            }
            else
            {
                var completed = await Task.WhenAny(waitTask, Task.Delay(timeout));
                if (completed == waitTask)
                {
                    var snapshot = await waitTask;
                    success = snapshot != null && snapshot.Count > 0;
                }
                else
                {
                    success = false;
                }
            }

            _startupCompletion.TrySetResult(success);
            return success;
        }

        private void HandleAllPlayersDisconnected()
        {
            ServerSingleton.Instance?.ScheduleShutdown(ShutdownKind.AllPlayersDisconnected, "All players disconnected", ShutdownDelaySeconds);
        }
    }
}
#endif
