#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Services.Matchmaker.Models;

using DedicatedServerMultiplayerSample.Server.Core;

namespace DedicatedServerMultiplayerSample.Server.Infrastructure
{
    /// <summary>
    /// Handles runtime configuration capture, Multiplay allocation, and transport setup.
    /// </summary>
    internal sealed class ServerAllocationConfigurator
    {
        private readonly NetworkManager _networkManager;
        private readonly int _defaultMaxPlayers;

        public ServerAllocationConfigurator(NetworkManager networkManager, int defaultMaxPlayers)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
        }

        /// <summary>
        /// Runs the allocation workflow and returns the configuration result.
        /// </summary>
        public async Task<ServerAllocationResult> RunAsync(CancellationToken ct = default)
        {
            var runtimeConfig = ServerRuntimeConfig.Capture();
            runtimeConfig.LogSummary();

            ct.ThrowIfCancellationRequested();

            var helper = new ServerAllocationHelper(runtimeConfig, _defaultMaxPlayers);
            var (success, matchResults, integration) = await helper.GetAllocationAsync();
            if (!success)
            {
                Debug.LogError("[AllocationConfigurator] Allocation request failed");
                return ServerAllocationResult.Failure;
            }

            ConfigureNetworkTransport(runtimeConfig);

            var expectedAuthIds = ExtractExpectedAuthIds(matchResults);
            var teamCount = matchResults?.MatchProperties?.Teams?.Count ?? 2;

            Debug.Log($"[AllocationConfigurator] TeamCount={teamCount}, ExpectedPlayers={expectedAuthIds.Count}");

            return new ServerAllocationResult(
                true,
                runtimeConfig,
                integration,
                expectedAuthIds,
                teamCount);
        }

        private void ConfigureNetworkTransport(ServerRuntimeConfig runtimeConfig)
        {
            var transport = _networkManager.GetComponent<UnityTransport>();
            ushort port = runtimeConfig.GamePort;
            transport.SetConnectionData("0.0.0.0", port);
            _networkManager.NetworkConfig.NetworkTransport = transport;
            Debug.Log($"[AllocationConfigurator] Listening on port {port}");
        }

        private static List<string> ExtractExpectedAuthIds(MatchmakingResults results)
        {
            var expectedIds = new List<string>();

            var players = results?.MatchProperties?.Players;
            if (players == null)
            {
                return expectedIds;
            }

            foreach (var player in players)
            {
                if (!string.IsNullOrEmpty(player?.Id))
                {
                    expectedIds.Add(player.Id);
                }
            }

            return expectedIds;
        }
    }

    internal readonly struct ServerAllocationResult
    {
        public static readonly ServerAllocationResult Failure = new(false, null, null, Array.Empty<string>(), 2);

        public ServerAllocationResult(
            bool success,
            ServerRuntimeConfig runtimeConfig,
            ServerMultiplayIntegration multiplayIntegration,
            IReadOnlyList<string> expectedAuthIds,
            int teamCount)
        {
            Success = success;
            RuntimeConfig = runtimeConfig;
            MultiplayIntegration = multiplayIntegration;
            ExpectedAuthIds = expectedAuthIds;
            TeamCount = teamCount;
        }

        public bool Success { get; }
        public ServerRuntimeConfig RuntimeConfig { get; }
        public ServerMultiplayIntegration MultiplayIntegration { get; }
        public IReadOnlyList<string> ExpectedAuthIds { get; }
        public int TeamCount { get; }
    }
}
#endif
