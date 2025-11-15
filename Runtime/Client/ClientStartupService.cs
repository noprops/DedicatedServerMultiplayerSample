using System;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Handles all client bootstrap steps (Unity Services, auth, matchmaker setup).
    /// </summary>
    internal sealed class ClientStartupService : IDisposable
    {
        private readonly int _maxPlayers;

        private NetworkManager _networkManager;
        private bool _initialized;

        public ClientMatchmaker Matchmaker { get; private set; }

        public ClientStartupService(int maxPlayers)
        {
            _maxPlayers = Mathf.Max(1, maxPlayers);
        }

        public async Task<bool> InitializeAsync()
        {
            if (_initialized)
            {
                Debug.LogWarning("[ClientStartupService] Already initialized.");
                return true;
            }

            Debug.Log("[ClientStartupService] Initializing Unity Services...");
            await Unity.Services.Core.UnityServices.InitializeAsync();

            Debug.Log("[ClientStartupService] Signing in anonymously...");
            if (!await AuthenticationWrapper.SignInAnonymouslyAsync())
            {
                Debug.LogError("[ClientStartupService] Authentication failed.");
                return false;
            }

            _networkManager = NetworkManager.Singleton;
            if (_networkManager == null)
            {
                Debug.LogError("[ClientStartupService] NetworkManager.Singleton is null.");
                return false;
            }

            RegisterNetworkCallbacks();

            Matchmaker = new ClientMatchmaker(_networkManager, _maxPlayers);

            SceneManager.LoadScene("loading", LoadSceneMode.Single);

            _initialized = true;
            Debug.Log("[ClientStartupService] Client bootstrap complete.");
            return true;
        }

        public void DisconnectFromServer()
        {
            if (_networkManager != null && _networkManager.IsConnectedClient)
            {
                Debug.Log("[ClientStartupService] Shutting down NetworkManager.");
                _networkManager.Shutdown();
            }

            if (SceneManager.GetActiveScene().name == "game")
            {
                SceneManager.LoadScene("loading");
            }
        }

        public void Dispose()
        {
            UnregisterNetworkCallbacks();
            Matchmaker?.Dispose();
            Matchmaker = null;
            _networkManager = null;
            _initialized = false;
        }

        private void RegisterNetworkCallbacks()
        {
            if (_networkManager == null)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        private void UnregisterNetworkCallbacks()
        {
            if (_networkManager == null)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback -= OnClientConnected;
            _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ClientStartupService] Client connected: {clientId}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_networkManager != null && clientId == _networkManager.LocalClientId)
            {
                Debug.LogWarning("[ClientStartupService] Local client disconnected. Cleaning up.");
                DisconnectFromServer();
            }
        }
    }
}
