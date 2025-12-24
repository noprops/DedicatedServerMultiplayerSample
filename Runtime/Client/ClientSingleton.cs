using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Client
{
    public class ClientSingleton : MonoBehaviour
    {
        public static ClientSingleton Instance { get; private set; }

        private const string LoadingSceneName = "loading";

        [SerializeField] private int maxPlayers = 2;
        private ClientStartupRunner _startupRunner;
        private ClientConnectionService _connectionService;
        private ClientMatchmaker _matchmaker;

        public ClientMatchmaker Matchmaker => _matchmaker;

        private void Awake()
        {
            Debug.Log("[ClientSingleton] Awake called");
            
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[ClientSingleton] Instance created");
            }
            else
            {
                Debug.LogWarning("[ClientSingleton] Instance already exists, destroying duplicate");
                Destroy(gameObject);
            }
        }
        
        private async void Start()
        {
            _startupRunner = new ClientStartupRunner();
            if (!await _startupRunner.InitializeAsync())
            {
                Debug.LogError("[ClientSingleton] Client initialization failed.");
                return;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
            {
                Debug.LogError("[ClientSingleton] NetworkManager.Singleton is null.");
                return;
            }

            _connectionService = new ClientConnectionService(networkManager);
            _connectionService.RegisterCallbacks();
            _matchmaker = new ClientMatchmaker(networkManager, maxPlayers);

            SceneManager.LoadScene(LoadingSceneName, LoadSceneMode.Single);
        }
        
        private void OnDestroy()
        {
            Debug.Log("[ClientSingleton] Destroying ClientSingleton");
            _matchmaker?.Dispose();
            _matchmaker = null;
            _connectionService?.Dispose();
            _connectionService = null;
            _startupRunner = null;
        }
        
        private void OnApplicationQuit()
        {
            Debug.Log("[ClientSingleton] Application quitting, cleaning up client");
            _matchmaker?.Dispose();
            _matchmaker = null;
            _connectionService?.Dispose();
            _connectionService = null;
            _startupRunner = null;
        }

        public void DisconnectFromServer()
        {
            _connectionService?.DisconnectFromServer();
        }
    }
}
