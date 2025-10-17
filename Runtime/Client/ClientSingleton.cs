using System;
using Unity.Netcode;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Client
{
    public class ClientSingleton : MonoBehaviour
    {
        public static ClientSingleton Instance { get; private set; }

        [SerializeField] private MatchmakingPayloadProviderBase matchmakingPayloadProvider;
        [SerializeField] private int maxPlayers = 2;

        private ClientGameManager gameManager;
        public ClientGameManager GameManager => gameManager;

        private void Awake()
        {
            Debug.Log("[ClientSingleton] Awake called");
            
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[ClientSingleton] Instance created");

                if (matchmakingPayloadProvider == null)
                {
                    matchmakingPayloadProvider = GetComponent<MatchmakingPayloadProviderBase>();
                }

                if (matchmakingPayloadProvider != null)
                {
                    Debug.Log($"[ClientSingleton] Using payload provider: {matchmakingPayloadProvider.GetType().Name}");
                }
                else
                {
                    Debug.LogWarning("[ClientSingleton] No matchmaking payload provider assigned.");
                }
            }
            else
            {
                Debug.LogWarning("[ClientSingleton] Instance already exists, destroying duplicate");
                Destroy(gameObject);
            }
        }
        
        private async void Start()
        {
            Debug.Log("[ClientSingleton] ========== CLIENT INITIALIZATION START ==========");

            try
            {
                // ================================================================
                // STEP 1: Unity Services初期化（アプリ起動時に1回だけ実行）
                // ================================================================
                Debug.Log("[ClientSingleton] STEP 1: Initializing Unity Services...");
                await UnityServices.InitializeAsync();
                Debug.Log("[ClientSingleton] ✓ Unity Services initialized");

                // ================================================================
                // STEP 2: 認証
                // ================================================================
                Debug.Log("[ClientSingleton] STEP 2: Authenticating...");
                bool authenticated = await AuthenticationWrapper.SignInAnonymouslyAsync();

                if (!authenticated)
                {
                    Debug.LogError("[ClientSingleton] Authentication failed - cannot continue");
                    return;
                }

                Debug.Log($"[ClientSingleton] ✓ Authenticated as: {AuthenticationWrapper.PlayerId}");

                // ================================================================
                // STEP 3: ClientGameManager作成
                // ================================================================
                Debug.Log("[ClientSingleton] STEP 3: Creating ClientGameManager...");

                if (NetworkManager.Singleton == null)
                {
                    Debug.LogError("[ClientSingleton] NetworkManager.Singleton is null!");
                    return;
                }

                gameManager = await ClientGameManager.CreateAsync(NetworkManager.Singleton, matchmakingPayloadProvider, Mathf.Max(1, maxPlayers));
                Debug.Log("[ClientSingleton] ✓ ClientGameManager created and initialized");

                // ================================================================
                // STEP 4: loadingシーンへ遷移
                // ================================================================
                Debug.Log("[ClientSingleton] STEP 4: Loading loading scene...");
                SceneManager.LoadScene("loading", LoadSceneMode.Single);

                Debug.Log("[ClientSingleton] ========== CLIENT INITIALIZATION COMPLETE ==========");
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClientSingleton] Initialization failed: {e.Message}");
                Debug.LogError($"[ClientSingleton] Stack trace: {e.StackTrace}");
            }
        }
        
        private void OnDestroy()
        {
            Debug.Log("[ClientSingleton] Destroying ClientSingleton");
            gameManager?.Dispose();
        }
        
        private void OnApplicationQuit()
        {
            Debug.Log("[ClientSingleton] Application quitting, cleaning up client");
            gameManager?.Dispose();
        }
    }
}
