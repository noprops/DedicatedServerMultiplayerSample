#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Services.Authentication.Server;
using Unity.Services.Core;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Simple façade over Unity's <see cref="IMultiplaySessionManager"/> so the rest of the server can
    /// (1) wait for an allocation, (2) toggle readiness, and (3) lock the session when players are ready.
    /// </summary>
    internal sealed class MultiplaySessionService : IDisposable
    {
        private readonly ServerRuntimeConfig _runtimeConfig;
        private readonly int _defaultMaxPlayers;

        private IMultiplaySessionManager _sessionManager;
        private MultiplaySessionManagerEventCallbacks _sessionCallbacks;
        private bool _disposed;

        public MultiplaySessionService(ServerRuntimeConfig runtimeConfig, int defaultMaxPlayers)
        {
            _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
        }

        public bool IsConnected => _sessionManager != null;

        public async Task<MatchAllocationResult> AwaitAllocationAsync(CancellationToken ct)
        {
            if (!await EnsureConnectedAsync().ConfigureAwait(false))
            {
                return MatchAllocationResult.Failed();
            }

            var allocation = await WaitForAllocationEventAsync(ct).ConfigureAwait(false);
            if (allocation == null)
            {
                return MatchAllocationResult.Failed();
            }

            var results = await GetMatchmakingResultsAsync().ConfigureAwait(false);
            LogMatchSummary(results);

            if (results == null)
            {
                return MatchAllocationResult.Failed();
            }

            return new MatchAllocationResult(true, ExtractAuthIds(results), ExtractTeamCount(results));
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (_sessionManager != null)
            {
                return true;
            }

            if (UnityServices.Instance.GetMultiplayerService() == null)
            {
                Debug.LogWarning("[MultiplaySessionService] Multiplayer service unavailable; cannot integrate with Multiplay.");
                return false;
            }

            try
            {
                await ServerAuthenticationService.Instance.SignInFromServerAsync().ConfigureAwait(false);

                _sessionCallbacks = new MultiplaySessionManagerEventCallbacks();
                _sessionCallbacks.Allocated += HandleAllocatedNoop;

                var serverName = !string.IsNullOrWhiteSpace(_runtimeConfig.GeneratedServerName)
                    ? _runtimeConfig.GeneratedServerName
                    : "GameServer";

                var sessionManagerOptions = new MultiplaySessionManagerOptions
                {
                    SessionOptions = new SessionOptions
                    {
                        MaxPlayers = (ushort)Mathf.Clamp(_defaultMaxPlayers, 1, ushort.MaxValue)
                    }.WithDirectNetwork(),
                    MultiplayServerOptions = new MultiplayServerOptions(
                        serverName: serverName,
                        gameType: "default",
                        buildId: null,
                        map: "default",
                        autoReady: false),
                    Callbacks = _sessionCallbacks
                };

                _sessionManager = await MultiplayerServerService.Instance.StartMultiplaySessionManagerAsync(sessionManagerOptions).ConfigureAwait(false);
                Debug.Log("[MultiplaySessionService] Multiplay session manager created.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiplaySessionService] Failed to connect to Multiplay: {ex.Message}");
                return false;
            }
        }

        public async Task SetPlayerReadinessAsync(bool ready)
        {
            if (_sessionManager == null)
            {
                Debug.LogWarning("[MultiplaySessionService] Session manager unavailable; cannot update readiness.");
                return;
            }

            await _sessionManager.SetPlayerReadinessAsync(ready).ConfigureAwait(false);
            Debug.Log($"[MultiplaySessionService] Player readiness set to {ready}.");
        }

        public async Task LockSessionAsync()
        {
            if (_sessionManager == null)
            {
                Debug.LogWarning("[MultiplaySessionService] Session manager unavailable; cannot lock session.");
                return;
            }

            var session = _sessionManager.Session;
            var hostSession = session?.AsHost();
            if (hostSession == null)
            {
                Debug.LogWarning("[MultiplaySessionService] Host session unavailable; cannot lock session.");
                return;
            }

            try
            {
                hostSession.IsLocked = true;
                await hostSession.SavePropertiesAsync().ConfigureAwait(false);
                Debug.Log("[MultiplaySessionService] Session locked.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiplaySessionService] Failed to lock session: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_sessionCallbacks != null)
            {
                _sessionCallbacks.Allocated -= HandleAllocatedNoop;
                _sessionCallbacks = null;
            }

            if (_sessionManager is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _sessionManager = null;
        }

        private async Task<IMultiplayAllocation> WaitForAllocationEventAsync(CancellationToken ct)
        {
            if (_sessionCallbacks == null)
            {
                Debug.LogError("[MultiplaySessionService] Session callbacks missing; cannot await allocation.");
                return null;
            }

            var tcs = new TaskCompletionSource<IMultiplayAllocation>(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnAllocated(IMultiplayAllocation allocation)
            {
                Debug.Log("[MultiplaySessionService] Allocation event received.");
                tcs.TrySetResult(allocation);
            }

            _sessionCallbacks.Allocated += OnAllocated;

            try
            {
                using (ct.Register(() => tcs.TrySetCanceled(ct)))
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is TaskCanceledException or OperationCanceledException)
            {
                Debug.LogWarning("[MultiplaySessionService] Allocation wait cancelled.");
                return null;
            }
            finally
            {
                _sessionCallbacks.Allocated -= OnAllocated;
            }
        }

        private async Task<MatchmakingResults> GetMatchmakingResultsAsync()
        {
            if (_sessionManager == null)
            {
                return null;
            }

            try
            {
                return await _sessionManager.GetAllocationPayloadFromJsonAsAsync<MatchmakingResults>().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MultiplaySessionService] Failed to fetch matchmaking results: {ex.Message}");
                return null;
            }
        }

        private static IReadOnlyList<string> ExtractAuthIds(MatchmakingResults results)
        {
            var ids = new List<string>();
            var players = results?.MatchProperties?.Players;
            if (players == null)
            {
                return ids;
            }

            foreach (var player in players)
            {
                if (!string.IsNullOrEmpty(player?.Id))
                {
                    ids.Add(player.Id);
                }
            }

            return ids;
        }

        private static int ExtractTeamCount(MatchmakingResults results)
        {
            return results?.MatchProperties?.Teams?.Count ?? 2;
        }

        private static void LogMatchSummary(MatchmakingResults results)
        {
            if (results == null)
            {
                Debug.LogWarning("[MultiplaySessionService] Matchmaking results missing.");
                return;
            }

            Debug.Log($"[MultiplaySessionService] Match found in region {results.MatchProperties?.Region}, queue {results.QueueName}");
        }

        private void HandleAllocatedNoop(IMultiplayAllocation allocation)
        {
            // Required callback registration so Multiplay knows this server handles allocations.
        }
    }

    /// <summary>
    /// Metadata extracted from the Multiplay allocation payload that the server needs to gate connections.
    /// </summary>
    internal readonly struct MatchAllocationResult
    {
        public static MatchAllocationResult Failed() => new(false, Array.Empty<string>(), 2);

        public MatchAllocationResult(bool success, IReadOnlyList<string> expectedAuthIds, int teamCount)
        {
            Success = success;
            ExpectedAuthIds = expectedAuthIds ?? Array.Empty<string>();
            TeamCount = Mathf.Max(1, teamCount);
        }

        public bool Success { get; }
        public IReadOnlyList<string> ExpectedAuthIds { get; }
        public int TeamCount { get; }
    }
}
#endif
