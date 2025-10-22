#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Server
{
    /// <summary>
    /// Orchestrates server startup, session locking, and shutdown using helper components.
    /// </summary>
    public class ServerGameManager : IDisposable
    {
        private readonly NetworkManager m_NetworkManager;
        private readonly int m_DefaultMaxPlayers;
        private readonly List<string> m_ExpectedAuthIds = new();
        private readonly Dictionary<ulong, string> m_ConnectedAuthIds = new();

        private ServerRuntimeConfig m_RuntimeConfig;
        private ServerMultiplayIntegration m_MultiplayIntegration;
        private ServerPlayerValidator m_PlayerValidator;
        private ServerConnectionGate m_ConnectionGate;
        private ServerSceneLoader m_SceneLoader;

        private int m_TeamCount = 2;
        private bool m_IsSceneLoaded;
        private bool m_IsDisposed;

        public int TeamCount => m_TeamCount;

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
                m_ExpectedAuthIds.AddRange(allocationResult.ExpectedAuthIds);
                m_ConnectedAuthIds.Clear();

                m_PlayerValidator = new ServerPlayerValidator(
                    m_NetworkManager,
                    m_ExpectedAuthIds,
                    m_ConnectedAuthIds,
                    m_DefaultMaxPlayers);

                m_ConnectionGate = new ServerConnectionGate(m_NetworkManager, m_PlayerValidator)
                {
                    SceneIsLoaded = () => m_IsSceneLoaded
                };
                m_ConnectionGate.Install();

                m_SceneLoader = new ServerSceneLoader(m_NetworkManager);
                var sceneLoaded = await m_SceneLoader.LoadAsync("game", 5000, OnSceneLoaded, cancellationToken);
                if (!sceneLoaded)
                {
                    Debug.LogError("[ServerGameManager] Failed to load game scene");
                    CloseServer();
                    return false;
                }

                await SetPlayerReadinessAsync(true);

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
            m_ConnectionGate?.SetGameStarted();
            Debug.Log("[ServerGameManager] Locking session; new connections will be rejected");

            if (m_MultiplayIntegration != null)
            {
                await m_MultiplayIntegration.LockSessionAsync();
            }

            await SetPlayerReadinessAsync(false);
            Debug.Log("[ServerGameManager] Player readiness disabled");
        }

        public Dictionary<ulong, Dictionary<string, object>> GetAllConnectedPlayers()
        {
            return m_PlayerValidator?.GetAllConnectionData() ?? new Dictionary<ulong, Dictionary<string, object>>();
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
            m_MultiplayIntegration?.Dispose();
            m_PlayerValidator?.Dispose();

            if (m_NetworkManager != null)
            {
                m_NetworkManager.ConnectionApprovalCallback = null;

                if (m_NetworkManager.IsListening || m_NetworkManager.IsServer)
                {
                    m_NetworkManager.Shutdown();
                }
            }

            Debug.Log("[ServerGameManager] Disposed");
        }

        private void OnSceneLoaded()
        {
            m_IsSceneLoaded = true;
            m_ConnectionGate?.ReleasePendings();
        }

        private async Task SetPlayerReadinessAsync(bool ready)
        {
            if (m_MultiplayIntegration != null && m_MultiplayIntegration.IsConnected)
            {
                await m_MultiplayIntegration.SetPlayerReadinessAsync(ready);
            }
        }
    }
}
#endif
