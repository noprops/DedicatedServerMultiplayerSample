using System;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Allocation;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Minimal orchestrator that wires together VM-hosted startup, scene loading, and shutdown scheduling.
    /// </summary>
    public sealed class ServerStartupRunner : IDisposable
    {
        private readonly NetworkManager _networkManager;

        private readonly ServerConnectionManager _connectionManager;
        private bool _disposed;
        private readonly TaskCompletionSource<bool> _startupCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private const int WaitingPlayersTimeoutSeconds = 180;
        private const int SceneLoadTimeoutSeconds = 5;

        public ServerStartupRunner(NetworkManager networkManager, ServerConnectionManager connectionManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        }

        #region Public API

        internal async Task<bool> StartAsync(ServerRuntimeConfig runtimeConfig)
        {
            var startupSucceeded = false;
            try
            {
                Debug.Log($"[MM-PROBE][ServerStartupRunner] StartAsync t={Time.realtimeSinceStartup:F3}");
                if (runtimeConfig == null)
                {
                    throw new ArgumentNullException(nameof(runtimeConfig));
                }

                Debug.Log($"[MM-PROBE][ServerStartupRunner] Runtime ready t={Time.realtimeSinceStartup:F3} teamCount={runtimeConfig.ExpectedPlayerCount} expectedAuthIds={runtimeConfig.ExpectedAuthIds?.Count ?? 0}");

                ServerTransportConfigurator.Configure(_networkManager, runtimeConfig);
                if (!_networkManager.StartServer())
                {
                    Debug.LogError("[ServerStartupRunner] StartServer failed.");
                    return false;
                }

                _connectionManager.Configure(runtimeConfig.ExpectedAuthIds ?? Array.Empty<string>(), runtimeConfig.ExpectedPlayerCount);

                if (!await _connectionManager.LoadSceneAsync("game", SceneLoadTimeoutSeconds, CancellationToken.None))
                {
                    return false;
                }
                Debug.Log($"[MM-PROBE][ServerStartupRunner] Scene loaded t={Time.realtimeSinceStartup:F3}");

                Debug.Log($"[MM-PROBE][ServerStartupRunner] WaitForClients begin t={Time.realtimeSinceStartup:F3}");
                var ready = await WaitForClientsWithTimeoutAsync(TimeSpan.FromSeconds(WaitingPlayersTimeoutSeconds));
                Debug.Log($"[MM-PROBE][ServerStartupRunner] WaitForClients done={ready} t={Time.realtimeSinceStartup:F3}");
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
