using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
#if UNITY_SERVER || ENABLE_UCS_SERVER
using DedicatedServerMultiplayerSample.Server;
#endif

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    /// <summary>
    /// ゲーム開始後に接続プレイヤー名をクライアントへ配信するサンプル用ブロードキャスター。
    /// </summary>
    public class PlayerInfoBroadcaster : NetworkBehaviour
    {
        /// <summary>
        /// プレイヤー表示名を受信した際に通知。
        /// </summary>
        public static event Action<Dictionary<ulong, string>> OnPlayerNamesReceived;

#if UNITY_SERVER || ENABLE_UCS_SERVER
        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsServer)
            {
                _ = BroadcastWhenReadyAsync();
            }
        }

        private async System.Threading.Tasks.Task BroadcastWhenReadyAsync()
        {
            var controller = GameSessionController.Instance;
            if (controller == null)
            {
                Debug.LogWarning("[PlayerInfoBroadcaster] GameSessionController not found");
                return;
            }

            bool started;
            try
            {
                started = await controller.WaitForGameStartAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[PlayerInfoBroadcaster] Failed while waiting for game start: {e.Message}");
                return;
            }

            if (!started)
            {
                Debug.LogWarning("[PlayerInfoBroadcaster] Game start failed; no player broadcast");
                return;
            }

            var snapshot = ServerSingleton.Instance?.GameManager?.GetAllConnectedPlayers();
            if (snapshot == null || snapshot.Count < 2)
            {
                Debug.LogWarning("[PlayerInfoBroadcaster] Player info broadcast skipped (need 2 players)");
                return;
            }

            var playerIds = new List<ulong>(snapshot.Keys);
            var firstId = playerIds[0];
            var secondId = playerIds[1];

            string firstName = ResolvePlayerName(snapshot[firstId], firstId);
            string secondName = ResolvePlayerName(snapshot[secondId], secondId);

            SendPlayerNamesClientRpc(firstId, firstName, secondId, secondName);
        }

        private static string ResolvePlayerName(Dictionary<string, object> payload, ulong clientId)
        {
            if (payload != null &&
                payload.TryGetValue("playerName", out var value) &&
                value is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return $"Player{clientId}";
        }
#endif

        [ClientRpc]
        private void SendPlayerNamesClientRpc(ulong player1Id, string player1Name, ulong player2Id, string player2Name)
        {
            var names = new Dictionary<ulong, string>();
            names[player1Id] = string.IsNullOrWhiteSpace(player1Name) ? $"Player{player1Id}" : player1Name;
            names[player2Id] = string.IsNullOrWhiteSpace(player2Name) ? $"Player{player2Id}" : player2Name;

            if (names.Count > 0)
            {
                OnPlayerNamesReceived?.Invoke(names);
            }
        }
    }
}
