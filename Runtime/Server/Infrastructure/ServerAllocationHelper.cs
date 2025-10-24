#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using Unity.Services.Matchmaker.Models;
using UnityEngine;

using DedicatedServerMultiplayerSample.Server.Core;

namespace DedicatedServerMultiplayerSample.Server.Infrastructure
{
    /// <summary>
    /// Multiplayアロケーション処理をシンプルにするヘルパー
    /// </summary>
    public class ServerAllocationHelper
    {
        private TaskCompletionSource<IMultiplayAllocation> _allocationTcs;
        private ServerMultiplayIntegration _integration;
        private readonly ServerRuntimeConfig _runtimeConfig;
        private readonly int _defaultMaxPlayers;

        public ServerAllocationHelper(ServerRuntimeConfig runtimeConfig, int defaultMaxPlayers)
        {
            _runtimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
            _defaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
        }

        /// <summary>
        /// アロケーションを取得（上から下に一直線）
        /// </summary>
        public async Task<(bool success, MatchmakingResults results, ServerMultiplayIntegration integration)> GetAllocationAsync()
        {
            Debug.Log("[ServerAllocationHelper] Starting allocation process");

            try
            {
                // ================================================================
                // 1. ServerMultiplayIntegrationを作成して接続
                // ================================================================
                _integration = new ServerMultiplayIntegration(_runtimeConfig, _defaultMaxPlayers);
                _allocationTcs = new TaskCompletionSource<IMultiplayAllocation>();

                void OnAllocated(IMultiplayAllocation allocation)
                {
                    _integration.OnAllocationComplete -= OnAllocated;
                    _allocationTcs.TrySetResult(allocation);
                }

                // アロケーションコールバックを設定
                _integration.OnAllocationComplete += OnAllocated;

                bool connected = await _integration.ConnectAsync();
                if (!connected)
                {
                    Debug.LogError("[ServerAllocationHelper] Failed to connect to Multiplay");
                    return (false, null, null);
                }

                // ================================================================
                // 2. アロケーション完了を待つ
                // ================================================================
                Debug.Log("[ServerAllocationHelper] Waiting for allocation...");
                var allocation = await _allocationTcs.Task;
                Debug.Log("[ServerAllocationHelper] Allocation received");

                // ================================================================
                // 3. マッチメイキング結果を待つ
                // ================================================================
                Debug.Log("[ServerAllocationHelper] Getting matchmaking results...");
                try
                {
                    if (_integration.IsConnected)
                    {
                        var results = await _integration.GetMatchmakingResultsAsync();
                        _integration.UpdateServerMetadata(results);
                        Debug.Log("[ServerAllocationHelper] Allocation process complete");
                        return (results != null, results, _integration);
                    }
                    Debug.LogError("[ServerAllocationHelper] Integration disconnected while fetching results");
                    return (false, null, _integration);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ServerAllocationHelper] Error getting matchmaking results: {e.Message}");
                    return (false, null, _integration);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerAllocationHelper] Allocation failed: {e.Message}");
                return (false, null, null);
            }
        }
    }
}
#endif
