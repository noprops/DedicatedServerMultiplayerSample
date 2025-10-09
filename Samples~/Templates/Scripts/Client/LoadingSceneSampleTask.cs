using System.Threading.Tasks;
using UnityEngine;
using DedicatedServerMultiplayerSample.Client;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// LoadingScene に処理を差し込むサンプルスクリプト。
    /// LoadingScene 上にアタッチし、SerializeField から参照を設定してください。
    /// </summary>
    public class LoadingSceneSampleTask : MonoBehaviour
    {
        [SerializeField] private LoadingScene loadingScene;

        private void Awake()
        {
            if (loadingScene == null)
            {
                loadingScene = GetComponent<LoadingScene>();
            }

            if (loadingScene == null)
            {
                Debug.LogError("[LoadingSceneSampleTask] LoadingScene reference not set.");
            }
        }

        private void OnEnable()
        {
            if (loadingScene != null)
            {
                loadingScene.Register(RunAsync);
            }
        }

        private void OnDisable()
        {
            if (loadingScene != null)
            {
                loadingScene.Unregister(RunAsync);
            }
        }

        private async Task RunAsync()
        {
            Debug.Log("[LoadingSceneSampleTask] Custom task started");
            await Task.Delay(500); // ここで実際の処理（Cloud Save など）を行う
            Debug.Log("[LoadingSceneSampleTask] Custom task completed");
        }
    }
}
