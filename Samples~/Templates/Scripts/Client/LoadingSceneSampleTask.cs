using System.Threading.Tasks;
using UnityEngine;
using DedicatedServerMultiplayerSample.Client;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// LoadingScene に処理を差し込むサンプルスクリプト。
    /// LoadingScene と同じ GameObject にアタッチして使用します。
    /// </summary>
    public class LoadingSceneSampleTask : LoadingSceneTaskBase
    {
        protected override async Task RunAsync()
        {
            Debug.Log("[LoadingSceneSampleTask] Custom task started");
            await Task.Delay(500); // ここで実際の処理（Cloud Save など）を行う
            Debug.Log("[LoadingSceneSampleTask] Custom task completed");
        }
    }
}
