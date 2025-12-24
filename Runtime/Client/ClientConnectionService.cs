using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Manages client connection callbacks and disconnect flow.
    /// </summary>
    internal sealed class ClientConnectionService
    {
        private const string GameSceneName = "game";
        private const string LoadingSceneName = "loading";

        private readonly NetworkManager _networkManager;
        private bool _registered;

        public ClientConnectionService(NetworkManager networkManager)
        {
            _networkManager = networkManager;
        }

        public void RegisterCallbacks()
        {
            if (_registered || _networkManager == null)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
            _registered = true;
        }

        public void Dispose()
        {
            if (!_registered || _networkManager == null)
            {
                return;
            }

            _networkManager.OnClientConnectedCallback -= OnClientConnected;
            _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            _registered = false;
        }

        public void DisconnectFromServer()
        {
            if (_networkManager != null && _networkManager.IsConnectedClient)
            {
                Debug.Log("[ClientConnectionService] Shutting down NetworkManager.");
                _networkManager.Shutdown();
            }

            if (SceneManager.GetActiveScene().name == GameSceneName)
            {
                SceneManager.LoadScene(LoadingSceneName);
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[ClientConnectionService] Client connected: {clientId}");
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (_networkManager != null && clientId == _networkManager.LocalClientId)
            {
                Debug.LogWarning("[ClientConnectionService] Local client disconnected. Cleaning up.");
                DisconnectFromServer();
            }
        }
    }
}
