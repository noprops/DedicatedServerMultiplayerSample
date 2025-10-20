#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Server;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class RockPaperScissorsGame
    {
        private List<ulong> m_PlayerIds = new();

        // ========== Server Initialization ==========
        partial void OnServerSpawn()
        {
            if (!IsServer) return;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            _ = RunServerGameFlowAsync();
        }

        partial void OnServerDespawn()
        {
            if (!IsServer) return;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // ========== Server Main Flow ==========
        private async Task RunServerGameFlowAsync()
        {
            try
            {
                var controller = GameSessionController.Instance;
                if (controller == null)
                {
                    Debug.LogError("[RockPaperScissorsGame] No GameSessionController");
                    return;
                }

                // 1. ゲーム開始可能状態を待つ
                var gameStarted = await controller.WaitForGameStartAsync();

                if (!gameStarted)
                {
                    HandleStartFailure();
                    return;
                }

                // 2. プレイヤー情報を配信
                CacheConnectedPlayers();

                // 3. ゲーム実行
                await ExecuteGameRoundAsync();

                // 4. 終了通知
                GameSessionController.Instance.NotifyGameEnded();
            }
            catch (Exception e)
            {
                Debug.LogError($"[RockPaperScissorsGame] Fatal error: {e.Message}");
            }
        }

        private void HandleStartFailure()
        {
            Debug.LogError("[RockPaperScissorsGame] Game start failed");
            UpdateStatusClientRpc("Game aborted - not enough players. Server will shutdown soon...");
            // サーバーがシャットダウンするのを待つ（GameSessionControllerが10秒後にシャットダウン）
        }

        private void CacheConnectedPlayers()
        {
            var manager = ServerSingleton.Instance?.GameManager;
            var snapshot = manager?.GetAllConnectedPlayers();
            if (snapshot == null || snapshot.Count == 0)
            {
                Debug.LogWarning("[RockPaperScissorsGame] No connected player snapshot available");
                m_PlayerIds.Clear();
                return;
            }

            m_PlayerIds = new List<ulong>(snapshot.Keys);
        }

        private async Task ExecuteGameRoundAsync()
        {
            Debug.Log("[RockPaperScissorsGame] Starting game round");

            m_GameInProgress = true;
            m_PlayerChoices.Clear();
            m_AllPlayersChosenTcs = new TaskCompletionSource<bool>();

            UpdateStatusClientRpc("Game started! Make your choice.");

            // 選択を待つ（30秒タイムアウト）
            bool allChose = await WaitForAllChoicesAsync(TimeSpan.FromSeconds(30));

            if (!allChose)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Timeout - some players didn't choose");
            }

            // 未選択のプレイヤーにランダム手を割り当てて切断
            foreach (var playerId in m_PlayerIds)
            {
                if (!m_PlayerChoices.ContainsKey(playerId))
                {
                    // ランダム手を割り当て
                    AssignRandomHandToPlayer(playerId, "timeout");

                    // 切断
                    Debug.Log($"[RockPaperScissorsGame] Disconnecting timeout player {playerId}");
                    var manager = ServerSingleton.Instance?.GameManager;
                    manager?.DisconnectClient(playerId, "Selection timeout");
                }
            }

            // 結果を処理・送信
            ProcessAndSendResults();

            m_GameInProgress = false;
        }

        private async Task<bool> WaitForAllChoicesAsync(TimeSpan timeout)
        {
            var timeoutTask = Task.Delay(timeout);
            var choicesTask = m_AllPlayersChosenTcs.Task;

            return await Task.WhenAny(choicesTask, timeoutTask) == choicesTask;
        }

        private void ProcessAndSendResults()
        {
            var players = new List<ulong>(m_PlayerChoices.Keys);
            if (players.Count < 2) return;

            ulong p1 = players[0], p2 = players[1];
            Hand h1 = m_PlayerChoices[p1], h2 = m_PlayerChoices[p2];

            var result1 = DetermineResult(h1, h2);
            var result2 = DetermineResult(h2, h1);

            SendGameResultClientRpc(p1, h1, result1, p2, h2, result2);
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            // ゲーム中で、まだ選択していない場合はランダム手を割り当て
            if (m_GameInProgress && !m_PlayerChoices.ContainsKey(clientId))
            {
                AssignRandomHandToPlayer(clientId, "disconnected");

                // 必要な人数分の選択が揃ったらTCSを完了
                if (m_PlayerChoices.Count >= 2)
                {
                    m_AllPlayersChosenTcs?.TrySetResult(true);
                }
            }
            // すでに選択済みの場合は何もしない（その手を使用）
            else if (m_GameInProgress && m_PlayerChoices.ContainsKey(clientId))
            {
                Debug.Log($"[RockPaperScissorsGame] Disconnected player {clientId} already chose {m_PlayerChoices[clientId]}");
            }
        }

        private void AssignRandomHandToPlayer(ulong playerId, string reason)
        {
            var randomHand = (Hand)UnityEngine.Random.Range(1, 4); // Rock(1), Paper(2), Scissors(3)
            m_PlayerChoices[playerId] = randomHand;
            Debug.Log($"[RockPaperScissorsGame] Auto-assigned {randomHand} to {reason} player {playerId}");
        }

        // ========== ServerRpc Implementation ==========
        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            if (!m_GameInProgress) return;

            // すでに選択済みの場合は無視（ランダム割り当て後の遅延RPC対策）
            if (m_PlayerChoices.ContainsKey(clientId))
            {
                Debug.LogWarning($"[Server] Ignoring duplicate choice from {clientId} (already has {m_PlayerChoices[clientId]})");
                return;
            }

            m_PlayerChoices[clientId] = choice;
            Debug.Log($"[Server] Choice received: {choice} from {clientId} ({m_PlayerChoices.Count}/2)");

            if (m_PlayerChoices.Count >= 2)
            {
                m_AllPlayersChosenTcs?.TrySetResult(true);
            }
            else
            {
                UpdateStatusClientRpc($"Waiting for other player... ({m_PlayerChoices.Count}/2)");
            }
        }

        // ========== Helper Methods ==========
        private GameResult DetermineResult(Hand myHand, Hand opponentHand)
        {
            if (myHand == opponentHand) return GameResult.Draw;

            return ((myHand == Hand.Rock && opponentHand == Hand.Scissors) ||
                    (myHand == Hand.Paper && opponentHand == Hand.Rock) ||
                    (myHand == Hand.Scissors && opponentHand == Hand.Paper))
                ? GameResult.Win
                : GameResult.Lose;
        }
    }
}
#endif
