#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Server.Infrastructure;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Orchestrates server startup, session locking, and shutdown using helper components.
    /// </summary>
    public class ServerGameManager : IDisposable
    {
        private readonly NetworkManager m_NetworkManager;
        private readonly int m_DefaultMaxPlayers;

        private ServerRuntimeConfig m_RuntimeConfig;
        private ServerMultiplayIntegration m_MultiplayIntegration;
        private readonly ConnectionDirectory m_ConnectionDirectory = new();
        private ServerConnectionGate m_ConnectionGate;
        private ServerConnectionTracker m_ConnectionTracker;
        private ServerSceneLoader m_SceneLoader;
        private readonly List<string> m_ExpectedAuthIds = new();

        private int m_TeamCount = 2;
        private bool m_IsSceneLoaded;
        private bool m_IsDisposed;

        public int TeamCount => m_TeamCount;
        public ServerConnectionTracker ConnectionTracker => m_ConnectionTracker;

        public ServerGameManager(NetworkManager networkManager, int defaultMaxPlayers)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
            m_DefaultMaxPlayers = Mathf.Max(1, defaultMaxPlayers);
            Debug.Log("[ServerGameManager] Created");
        }

        public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
        {
            Debug.Log("[ServerGameManager] ========== SERVER STARTUP BEGIN ==========");

            try
            {
                var configurator = new ServerAllocationConfigurator(m_NetworkManager, m_DefaultMaxPlayers);
                var allocationResult = await configurator.RunAsync(cancellationToken);
                if (!allocationResult.Success)
                {
                    Debug.LogError("[ServerGameManager] Allocation failed");
                    CloseServer();
                    return false;
                }

                cancellationToken.ThrowIfCancellationRequested();

                m_RuntimeConfig = allocationResult.RuntimeConfig;
                m_MultiplayIntegration = allocationResult.MultiplayIntegration;
                m_TeamCount = allocationResult.TeamCount;

                m_ExpectedAuthIds.Clear();
                if (allocationResult.ExpectedAuthIds != null)
                {
                    m_ExpectedAuthIds.AddRange(allocationResult.ExpectedAuthIds);
                }

                m_ConnectionDirectory.Clear();

                m_ConnectionGate = new ServerConnectionGate(
                    m_NetworkManager,
                    m_ConnectionDirectory,
                    new ServerConnectionPolicy())
                {
                    SceneIsLoaded = () => m_IsSceneLoaded,
                    CurrentPlayers = () => m_ConnectionDirectory.Count,
                    Capacity = () => m_ExpectedAuthIds.Count > 0 ? m_ExpectedAuthIds.Count : m_DefaultMaxPlayers,
                    ExpectedAuthIds = () => m_ExpectedAuthIds,
                    AllowNewConnections = true
                };

                m_ConnectionTracker = new ServerConnectionTracker(m_NetworkManager, m_ConnectionDirectory, m_TeamCount);

                m_SceneLoader = new ServerSceneLoader(m_NetworkManager);
                m_IsSceneLoaded = false;
                var sceneLoaded = await m_SceneLoader.LoadAsync("game", 5000, () =>
                {
                    m_IsSceneLoaded = true;
                    m_ConnectionGate?.ReleasePendingApprovals();
                }, cancellationToken);

                if (!sceneLoaded)
                {
                    Debug.LogError("[ServerGameManager] Failed to load game scene");
                    CloseServer();
                    return false;
                }

                if (m_MultiplayIntegration != null && m_MultiplayIntegration.IsConnected)
                {
                    await m_MultiplayIntegration.SetPlayerReadinessAsync(true);
                }

                Debug.Log("[ServerGameManager] ========== SERVER STARTUP COMPLETE ==========");
                return true;
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[ServerGameManager] Startup cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ServerGameManager] Server startup failed: {ex.Message}");
                CloseServer();
                return false;
            }
        }

        public async Task LockSessionForGameStartAsync()
        {
            if (m_ConnectionGate != null)
            {
                m_ConnectionGate.AllowNewConnections = false;
            }
            Debug.Log("[ServerGameManager] Locking session; new connections will be rejected");

            if (m_MultiplayIntegration != null && m_MultiplayIntegration.IsConnected)
            {
                await m_MultiplayIntegration.LockSessionAsync();
                await m_MultiplayIntegration.SetPlayerReadinessAsync(false);
            }
            Debug.Log("[ServerGameManager] Player readiness disabled");
        }

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectedPlayers()
        {
            return m_ConnectionDirectory?.GetAllConnectionData() ?? new Dictionary<ulong, Dictionary<string, object>>();
        }

        public void DisconnectClient(ulong clientId, string reason = "Forced disconnect")
        {
            if (m_NetworkManager == null || !m_NetworkManager.IsServer)
            {
                return;
            }

            m_NetworkManager.DisconnectClient(clientId, reason);
        }

        public void CloseServer()
        {
            Dispose();
            Application.Quit();
        }

        public void Dispose()
        {
            if (m_IsDisposed)
            {
                return;
            }

            m_IsDisposed = true;

            m_ConnectionGate?.Dispose();
            m_ConnectionTracker?.Dispose();
            m_MultiplayIntegration?.Dispose();

            if (m_NetworkManager != null)
            {
                m_NetworkManager.ConnectionApprovalCallback = null;

                if (m_NetworkManager.IsListening || m_NetworkManager.IsServer)
                {
                    m_NetworkManager.Shutdown();
                }
            }

            m_ConnectionDirectory.Clear();

            Debug.Log("[ServerGameManager] Disposed");
        }
    }
}
#endif
