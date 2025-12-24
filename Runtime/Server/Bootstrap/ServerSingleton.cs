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
    /// <summary>
    /// Singleton bootstrapper that initializes and hosts the dedicated server runtime.
    /// </summary>
    public class ServerSingleton : MonoBehaviour
    {
#if UNITY_SERVER || ENABLE_UCS_SERVER
        public static ServerSingleton Instance { get; private set; }

        [SerializeField] private int defaultMaxPlayers = 2;

        private ServerStartupRunner _startupRunner;
        private ServerConnectionManager _connectionManager;
        private MultiplaySessionService _multiplaySessionService;
        private readonly ServerShutdownScheduler _shutdownScheduler = new();
        private const int AllPlayersDisconnectedShutdownDelaySeconds = 10;
        public ServerStartupRunner StartupRunner => _startupRunner;
        public ServerConnectionManager ConnectionManager => _connectionManager;
        
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                Debug.Log("[ServerSingleton] Instance created");

                // Run server-side performance optimizations via the dedicated helper.
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
        
        /// <summary>
        /// Initializes Unity Services and starts the dedicated server instance.
        /// </summary>
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

                var runtimeConfig = ServerRuntimeConfig.Capture();
                runtimeConfig.LogSummary();

                _connectionManager = new ServerConnectionManager(NetworkManager.Singleton, Mathf.Max(1, defaultMaxPlayers));
                _connectionManager.AllPlayersDisconnected += HandleAllPlayersDisconnected;
                _multiplaySessionService = new MultiplaySessionService(runtimeConfig, Mathf.Max(1, defaultMaxPlayers));
                _startupRunner = new ServerStartupRunner(NetworkManager.Singleton, _connectionManager);
                var started = await _startupRunner.StartAsync(runtimeConfig, _multiplaySessionService);
                await LockSessionAsync();
                if (!started)
                {
                    ScheduleShutdown(ShutdownKind.Error, "Server startup failed", AllPlayersDisconnectedShutdownDelaySeconds);
                }

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
            _startupRunner?.Dispose();
            if (_connectionManager != null)
            {
                _connectionManager.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
            }
            _connectionManager?.Dispose();
            _multiplaySessionService?.Dispose();
            _shutdownScheduler.Cancel();
        }

        private void OnApplicationQuit()
        {
            Debug.Log("[ServerSingleton] Application quitting, cleaning up server");
            _startupRunner?.Dispose();
            if (_connectionManager != null)
            {
                _connectionManager.AllPlayersDisconnected -= HandleAllPlayersDisconnected;
            }
            _connectionManager?.Dispose();
            _multiplaySessionService?.Dispose();
            _shutdownScheduler.Cancel();
        }

        public void ScheduleShutdown(ShutdownKind kind, string reason, int timeoutSeconds)
        {
            TimeSpan delay = TimeSpan.FromSeconds(timeoutSeconds);
            _shutdownScheduler.Schedule(kind, reason, delay);
        }

        private async Task LockSessionAsync()
        {
            if (_multiplaySessionService != null && _multiplaySessionService.IsConnected)
            {
                await _multiplaySessionService.LockSessionAsync();
                await _multiplaySessionService.SetPlayerReadinessAsync(false);
            }
        }

        private void HandleAllPlayersDisconnected()
        {
            ScheduleShutdown(ShutdownKind.AllPlayersDisconnected, "All players disconnected", AllPlayersDisconnectedShutdownDelaySeconds);
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
