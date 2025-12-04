using System.Threading.Tasks;
using UnityEngine;
using DedicatedServerMultiplayerSample.Client;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// Sample task that injects custom work into the LoadingScene lifecycle.
    /// Attach this to the same GameObject as LoadingScene and register it in the `LoadingScene.tasks` list.
    /// </summary>
    public class LoadingSceneSampleTask : LoadingSceneTaskBase
    {
        protected override async Task RunAsync()
        {
            Debug.Log("[LoadingSceneSampleTask] Custom task started");

            // ================================================================
            // Add any work that should finish during loading.
            // Examples:
            // 1. Load player data from Cloud Save
            // 2. Fetch the latest config from Remote Config or your own API
            // 3. Apply the values to local singletons such as `ClientData`
            // 4. Preload required ScriptableObjects / Addressables / AssetBundles
            // Tip: If you need parallel work, use Task.WhenAll and apply the results afterward.
            // ================================================================

            await Task.Delay(500);

            Debug.Log("[LoadingSceneSampleTask] Custom task completed");
        }
    }
}
