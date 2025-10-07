#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Threading.Tasks;
using Unity.Services.Multiplayer;
using Unity.Services.Matchmaker.Models;
using UnityEngine;

namespace MultiplayerServicesTest.Server
{
    /// <summary>
    /// Multiplayアロケーション処理をシンプルにするヘルパー
    /// </summary>
    public class ServerAllocationHelper
    {
        private TaskCompletionSource<IMultiplayAllocation> m_AllocationTcs;
        private ServerMultiplayIntegration m_Integration;
        private readonly ServerRuntimeConfig m_RuntimeConfig;

        public ServerAllocationHelper(ServerRuntimeConfig runtimeConfig)
        {
            m_RuntimeConfig = runtimeConfig ?? throw new ArgumentNullException(nameof(runtimeConfig));
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
                m_Integration = new ServerMultiplayIntegration(m_RuntimeConfig);
                m_AllocationTcs = new TaskCompletionSource<IMultiplayAllocation>();

                void OnAllocated(IMultiplayAllocation allocation)
                {
                    m_Integration.OnAllocationComplete -= OnAllocated;
                    m_AllocationTcs.TrySetResult(allocation);
                }

                // アロケーションコールバックを設定
                m_Integration.OnAllocationComplete += OnAllocated;

                bool connected = await m_Integration.ConnectAsync();
                if (!connected)
                {
                    Debug.LogError("[ServerAllocationHelper] Failed to connect to Multiplay");
                    return (false, null, null);
                }

                // ================================================================
                // 2. アロケーション完了を待つ
                // ================================================================
                Debug.Log("[ServerAllocationHelper] Waiting for allocation...");
                var allocation = await m_AllocationTcs.Task;
                Debug.Log("[ServerAllocationHelper] Allocation received");

                // ================================================================
                // 3. マッチメイキング結果を待つ
                // ================================================================
                Debug.Log("[ServerAllocationHelper] Getting matchmaking results...");
                try
                {
                    if (m_Integration.IsConnected)
                    {
                        var results = await m_Integration.GetMatchmakingResultsAsync();
                        m_Integration.UpdateServerMetadata(results);
                        Debug.Log("[ServerAllocationHelper] Allocation process complete");
                        return (results != null, results, m_Integration);
                    }
                    Debug.LogError("[ServerAllocationHelper] Integration disconnected while fetching results");
                    return (false, null, m_Integration);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ServerAllocationHelper] Error getting matchmaking results: {e.Message}");
                    return (false, null, m_Integration);
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
