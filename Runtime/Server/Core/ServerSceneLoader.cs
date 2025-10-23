#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine.SceneManagement;

namespace DedicatedServerMultiplayerSample.Server.Core
{
    /// <summary>
    /// Provides a small utility façade around the Netcode scene manager to load a server scene with timeout handling,
    /// ensuring callbacks are unsubscribed correctly and notifying callers when the load completes.
    /// </summary>
    internal sealed class ServerSceneLoader
    {
        private readonly NetworkManager m_NetworkManager;

        public ServerSceneLoader(NetworkManager networkManager)
        {
            m_NetworkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        }

        public async Task<bool> LoadAsync(string sceneName, int timeoutMilliseconds = 5000, Action onLoaded = null, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            void Handler(string loadedSceneName, LoadSceneMode _, List<ulong> __, List<ulong> ___)
            {
                if (loadedSceneName != sceneName)
                {
                    return;
                }

                m_NetworkManager.SceneManager.OnLoadEventCompleted -= Handler;
                onLoaded?.Invoke();
                tcs.TrySetResult(true);
            }

            m_NetworkManager.SceneManager.OnLoadEventCompleted += Handler;
            var status = m_NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

            if (status != SceneEventProgressStatus.Started)
            {
                m_NetworkManager.SceneManager.OnLoadEventCompleted -= Handler;
                return false;
            }

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMilliseconds, ct));
            if (completedTask != tcs.Task)
            {
                m_NetworkManager.SceneManager.OnLoadEventCompleted -= Handler;
                return false;
            }

            await tcs.Task;
            return true;
        }
    }
}
#endif
