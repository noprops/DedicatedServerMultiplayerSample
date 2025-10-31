using System.Threading.Tasks;
using UnityEngine;
using DedicatedServerMultiplayerSample.Client;

namespace DedicatedServerMultiplayerSample.Samples.Client
{
    /// <summary>
    /// LoadingScene に処理を差し込むサンプルスクリプト。
    /// LoadingScene と同じ GameObject にアタッチし、`LoadingScene.tasks` リストに登録して使用します。
    /// </summary>
    public class LoadingSceneSampleTask : LoadingSceneTaskBase
    {
        protected override async Task RunAsync()
        {
            Debug.Log("[LoadingSceneSampleTask] Custom task started");

            // ================================================================
            // ここにローディング中に完了させたい処理を記述します。
            // 例:
            // 1. Cloud Save からプレイヤーデータを読み込む
            // 2. Remote Config や自前 API から最新コンフィグを取得する
            // 3. 取得した値を `ClientData` などローカルのシングルトンへ反映する
            // 4. 必要な ScriptableObject / Addressables / AssetBundle を事前ロードする
            // ※ 処理を並列化したい場合は Task.WhenAll を利用し、完了後にまとめて反映するのが安全です。
            // ================================================================

            await Task.Delay(500);

            Debug.Log("[LoadingSceneSampleTask] Custom task completed");
        }
    }
}
