using System.Threading.Tasks;
using DedicatedServerMultiplayerSample.Client;
using DedicatedServerMultiplayerSample.Samples.Client.Data;
using DedicatedServerMultiplayerSample.Samples.Client.UI.Menu;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Samples.Client.Testing
{
    internal sealed class AutoMatchTestBootstrap : MonoBehaviour
    {
        private static AutoMatchTestBootstrap s_instance;

        private bool _matchAttempted;
        private bool _quitRequested;
        private bool _matchFinished;

        public static bool IsEnabled => AutoMatchTestConfig.Enabled;

        public static void NotifyMatchFinished(string reason)
        {
            s_instance?.HandleMatchFinished(reason);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            if (!AutoMatchTestConfig.Enabled || s_instance != null)
            {
                return;
            }

            AuthenticationWrapper.PendingProfileName = AutoMatchTestConfig.AuthProfileName;

            var go = new GameObject(nameof(AutoMatchTestBootstrap));
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<AutoMatchTestBootstrap>();
        }

        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);

            // Keep load-test clients manageable on desktop by forcing a normal window.
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            _ = QuitOnGlobalTimeoutAsync();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!AutoMatchTestConfig.Enabled)
            {
                return;
            }

            Debug.Log($"[AutoMatchTestBootstrap] Scene loaded: {scene.name}");

            if (scene.name == "menu" && !_matchAttempted)
            {
                _ = StartRankedMatchAsync();
            }
            else if (scene.name == "menu" && _matchFinished && AutoMatchTestConfig.AutoQuitOnSuccess)
            {
                RequestQuit("match finished");
            }
        }

        private async Task StartRankedMatchAsync()
        {
            _matchAttempted = true;

            var delayMs = AutoMatchTestConfig.GetInitialDelayMilliseconds();
            if (delayMs > 0)
            {
                await Task.Delay(delayMs);
            }

            var rankedButton = await WaitForRankedMatchButtonAsync();
            if (rankedButton == null)
            {
                Debug.LogError("[AutoMatchTestBootstrap] RankedMatchButtonUI not found.");
                if (AutoMatchTestConfig.AutoQuitOnFailure)
                {
                    RequestQuit("ranked button missing");
                }
                return;
            }

            if (ClientData.Instance != null)
            {
                ClientData.Instance.PlayerName = AutoMatchTestConfig.PlayerName;
            }

            Debug.Log($"[AutoMatchTestBootstrap] Starting auto-match with {AutoMatchTestConfig.Describe()}");
            Debug.Log($"[AutoMatchTestBootstrap] Auth playerId={AuthenticationWrapper.PlayerId}");

            var result = await rankedButton.StartAutomatedMatchAsync();
            Debug.Log($"[AutoMatchTestBootstrap] Match result={result}");

            if (result != MatchResult.Success && AutoMatchTestConfig.AutoQuitOnFailure)
            {
                RequestQuit($"matchmaking {result}");
            }
        }

        private async Task<RankedMatchButtonUI> WaitForRankedMatchButtonAsync()
        {
            const int maxFrames = 600;

            for (var i = 0; i < maxFrames; i++)
            {
                var rankedButton = FindAnyObjectByType<RankedMatchButtonUI>();
                if (rankedButton != null && rankedButton.isActiveAndEnabled && ClientSingleton.Instance?.Matchmaker != null)
                {
                    return rankedButton;
                }

                await Task.Yield();
            }

            return null;
        }

        private void HandleMatchFinished(string reason)
        {
            _matchFinished = true;
            Debug.Log($"[AutoMatchTestBootstrap] Match finished: {reason}");
        }

        private async Task QuitOnGlobalTimeoutAsync()
        {
            await Task.Delay(AutoMatchTestConfig.AutoQuitTimeoutSeconds * 1000);

            if (_quitRequested || !AutoMatchTestConfig.Enabled)
            {
                return;
            }

            RequestQuit("global timeout");
        }

        private async void RequestQuit(string reason)
        {
            if (_quitRequested)
            {
                return;
            }

            _quitRequested = true;
            Debug.Log($"[AutoMatchTestBootstrap] Quitting application: {reason}");

            if (ClientSingleton.Instance != null)
            {
                await ClientSingleton.Instance.ShutdownAsync();
            }

            await Task.Delay(1000);
            Application.Quit();
        }
    }
}
