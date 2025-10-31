#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Provides a small utility façade around the Netcode scene manager to load a server scene with timeout handling,
    /// ensuring callbacks are unsubscribed correctly and notifying callers when the load completes.
    /// </summary>
    internal sealed class ServerSceneLoader
    {
        private readonly NetworkManager _networkManager;

        public ServerSceneLoader(NetworkManager networkManager)
        {
            _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        public async Task<bool> LoadAsync(string sceneName, int timeoutMilliseconds = 5000, Action onLoaded = null, CancellationToken ct = default)
        {
            var sceneMgr = _networkManager.SceneManager;
            var status = sceneMgr.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                return false;
            }

            var timeout = TimeSpan.FromMilliseconds(timeoutMilliseconds);
            using var awaiter = new SimpleSignalAwaiter(timeout, ct);
            NetworkSceneManager.OnEventCompletedDelegateHandler localHandler = null;

            localHandler = (loadedSceneName, _, __, ___) =>
            {
                if (loadedSceneName == sceneName)
                {
                    awaiter.OnSignal();
                }
            };

            sceneMgr.OnLoadEventCompleted += localHandler;

            try
            {
                var completed = await awaiter.WaitAsync(ct).ConfigureAwait(false);
                if (!completed)
                {
                    return false;
                }
            }
            finally
            {
                sceneMgr.OnLoadEventCompleted -= localHandler;
            }

            onLoaded?.Invoke();
            return true;
        }

    }
}
#endif
