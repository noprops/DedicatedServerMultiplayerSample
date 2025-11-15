using System;
using Unity.Netcode;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    public class ClientSingleton : MonoBehaviour
    {
        public static ClientSingleton Instance { get; private set; }

        [SerializeField] private int maxPlayers = 2;
        private ClientStartupService startupService;

        public ClientMatchmaker Matchmaker => startupService?.Matchmaker;

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
            startupService = new ClientStartupService(maxPlayers);
            await startupService.InitializeAsync();
        }
        
        private void OnDestroy()
        {
            Debug.Log("[ClientSingleton] Destroying ClientSingleton");
            startupService?.Dispose();
            startupService = null;
        }
        
        private void OnApplicationQuit()
        {
            Debug.Log("[ClientSingleton] Application quitting, cleaning up client");
            startupService?.Dispose();
            startupService = null;
        }

        public void DisconnectFromServer()
        {
            startupService?.DisconnectFromServer();
        }
    }
}
