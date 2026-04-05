using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Compatibility façade for server startup. The current VM migration path is self-hosted only and
    /// does not rely on the Multiplay-specific session manager APIs that are unavailable in this package set.
    /// </summary>
    internal sealed class MultiplaySessionService : IDisposable
    {
        private readonly ServerRuntimeConfig _runtimeConfig;

        public MultiplaySessionService(ServerRuntimeConfig runtimeConfig, int defaultMaxPlayers)
        {
            _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
        }

        public bool IsConnected => false;

        public Task<MatchAllocationResult> AwaitAllocationAsync(CancellationToken ct)
        {
            if (!_runtimeConfig.UseMultiplayAllocation)
            {
                Debug.Log("[MultiplaySessionService] Self-hosted mode active. Skipping Multiplay allocation wait.");
                return Task.FromResult(new MatchAllocationResult(
                    true,
                    _runtimeConfig.ExpectedAuthIds,
                    _runtimeConfig.ExpectedPlayerCount));
            }

            Debug.LogError("[MultiplaySessionService] Multiplay allocation mode is not supported by the current package/API set. Use selfHosted mode for VM deployment.");
            return Task.FromResult(MatchAllocationResult.Failed());
        }

        public Task SetPlayerReadinessAsync(bool ready)
        {
            return Task.CompletedTask;
        }

        public Task LockSessionAsync()
        {
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
