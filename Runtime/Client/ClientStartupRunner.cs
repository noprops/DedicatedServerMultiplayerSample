using System.Threading.Tasks;
using UnityEngine;

namespace DedicatedServerMultiplayerSample.Client
{
    /// <summary>
    /// Performs client bootstrap initialization (Unity Services + auth) and transitions to loading.
    /// </summary>
    internal sealed class ClientStartupRunner
    {
        private bool _initialized;

        public async Task<bool> InitializeAsync()
        {
            if (_initialized)
            {
                Debug.LogWarning("[ClientStartupRunner] Already initialized.");
                return true;
            }

            Debug.Log("[ClientStartupRunner] Initializing Unity Services...");
            await Unity.Services.Core.UnityServices.InitializeAsync();

            Debug.Log("[ClientStartupRunner] Signing in anonymously...");
            if (!await AuthenticationWrapper.SignInAnonymouslyAsync())
            {
                Debug.LogError("[ClientStartupRunner] Authentication failed.");
                return false;
            }

            _initialized = true;
            return true;
        }
    }
}
