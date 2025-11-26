using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Core;
using UnityEngine;
#if UNITY_SERVER || ENABLE_UCS_SERVER
using DedicatedServerMultiplayerSample.Server.Core;
using DedicatedServerMultiplayerSample.Server.Infrastructure;
#endif

namespace DedicatedServerMultiplayerSample.Server.Bootstrap
{
    public class ServerSingleton : MonoBehaviour
    {
#if UNITY_SERVER || ENABLE_UCS_SERVER
        public static ServerSingleton Instance { get; private set; }

        [SerializeField] private int defaultMaxPlayers = 2;

        private ServerStartupRunner startupRunner;
        private readonly ServerShutdownScheduler shutdownScheduler = new();
        public ServerStartupRunner StartupRunner => startupRunner;
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[ServerSingleton] Instance created");

                // サーバーのパフォーマンス最適化を専用クラスで実行
                ServerPerformanceOptimizer.Initialize();
            }
            else
            {
                Debug.LogWarning("[ServerSingleton] Instance already exists, destroying duplicate");
                Destroy(gameObject);
            }
        }
        
        private async void Start()
        {
            Debug.Log("[ServerSingleton] Starting server initialization");
            await CreateServer();
        }
        
        public async Task CreateServer()
        {
            try
            {
                Debug.Log("[ServerSingleton] Initializing Unity Services for server");
                await UnityServices.InitializeAsync();
                Debug.Log("[ServerSingleton] Unity Services initialized");
                
                if (NetworkManager.Singleton == null)
                {
                    Debug.LogError("[ServerSingleton] NetworkManager.Singleton is null!");
                    return;
                }

                startupRunner = new ServerStartupRunner(NetworkManager.Singleton, Mathf.Max(1, defaultMaxPlayers));
                await startupRunner.StartAsync();

                Debug.Log("[ServerSingleton] Server created and started successfully");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ServerSingleton] Failed to create server: {e.Message}");
                Debug.LogError($"[ServerSingleton] Stack trace: {e.StackTrace}");
            }
        }
        
        private void OnDestroy()
        {
            Debug.Log("[ServerSingleton] Destroying ServerSingleton");
            startupRunner?.Dispose();
            shutdownScheduler.Cancel();
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[ServerSingleton] Application quitting, cleaning up server");
            startupRunner?.Dispose();
            shutdownScheduler.Cancel();
        }

        public void ScheduleShutdown(ShutdownKind kind, string reason, int timeoutSeconds)
        {
            TimeSpan delay = TimeSpan.FromSeconds(timeoutSeconds);
            shutdownScheduler.Schedule(kind, reason, delay, () => startupRunner?.Dispose());
        }
#else
        private void Awake()
        {
            Debug.LogWarning("[ServerSingleton] Awake but UNITY_SERVER / ENABLE_UCS_SERVER is NOT defined. Destroying this component.");
            Destroy(this);
        }
#endif
    }
}
