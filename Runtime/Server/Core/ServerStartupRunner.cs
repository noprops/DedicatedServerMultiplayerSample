#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Allocation;
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

        private readonly ServerConnectionManager _connectionManager;

        private bool _disposed;
        private readonly TaskCompletionSource<bool> _startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private const int WaitingPlayersTimeoutSeconds = 10;
        private const int SceneLoadTimeoutSeconds = 5;

        public ServerStartupRunner(NetworkManager networkManager, ServerConnectionManager connectionManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        #region Public API

        internal async Task<bool> StartAsync(ServerRuntimeConfig runtimeConfig, MultiplaySessionService multiplaySessionService)
        {
            var startupSucceeded = false;
            try
            {
                if (runtimeConfig == null)
                {
                    throw new ArgumentNullException(nameof(runtimeConfig));
                }

                if (multiplaySessionService == null)
                {
                    throw new ArgumentNullException(nameof(multiplaySessionService));
                }

                var allocation = await multiplaySessionService.AwaitAllocationAsync(CancellationToken.None);
                if (!allocation.Success)
                {
                    return false;
                }

                ServerTransportConfigurator.Configure(_networkManager, runtimeConfig);
                _connectionManager.Configure(allocation.ExpectedAuthIds ?? Array.Empty<string>(), allocation.TeamCount);

                if (!await _connectionManager.LoadSceneAsync("game", SceneLoadTimeoutSeconds, CancellationToken.None))
                {
                    return false;
                }

                if (multiplaySessionService.IsConnected)
                {
                    await multiplaySessionService.SetPlayerReadinessAsync(true);
                }

                var ready = await WaitForClientsWithTimeoutAsync(TimeSpan.FromSeconds(WaitingPlayersTimeoutSeconds));
                if (!ready)
                {
                    return false;
                }

                startupSucceeded = true;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerStartupRunner] Startup failed: {ex.Message}");
                return false;
            }
            finally
            {
                _startupCompletion.TrySetResult(startupSucceeded);
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

        }

        #endregion

        private async Task<bool> WaitForClientsWithTimeoutAsync(TimeSpan timeout)
        {
            var waitTask = _connectionManager.WaitForAllClientsAsync();
            if (timeout <= TimeSpan.Zero)
            {
                var snapshotImmediate = await waitTask;
                return snapshotImmediate != null && snapshotImmediate.Count > 0;
            }

            var completed = await Task.WhenAny(waitTask, Task.Delay(timeout));
            if (completed == waitTask)
            {
                var snapshot = await waitTask;
                return snapshot != null && snapshot.Count > 0;
            }

            return false;
        }

    }
}
#endif
