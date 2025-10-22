#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using DedicatedServerMultiplayerSample.Server;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class RockPaperScissorsGame
    {
        private List<ulong> m_ClientIds = new();
        private bool m_SessionSubscribed;

        private void OnEnable()
        {
            TrySubscribeToSession();
        }

        private void OnDisable()
        {
            TryUnsubscribeFromSession();
        }

        // ========== Server Initialization ==========
        partial void OnServerSpawn()
        {
            Phase.Value = GamePhase.WaitingForPlayers;
            LastResult.Value = default;
            ParticipantIds.Clear();
            PlayerNames.Clear();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            TrySubscribeToSession();
        }

        partial void OnServerDespawn()
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            TryUnsubscribeFromSession();

        }

        private void TrySubscribeToSession()
        {
            if (m_SessionSubscribed)
            {
                return;
            }

            var controller = GameSessionController.Instance;
            if (controller == null)
            {
                return;
            }

            controller.GameStartSucceeded += OnGameStartSucceeded;
            controller.GameStartFailed += OnGameStartFailed;
            controller.GameEnded += OnGameEnded;
            m_SessionSubscribed = true;
        }

        private void TryUnsubscribeFromSession()
        {
            if (!m_SessionSubscribed)
            {
                return;
            }

            var controller = GameSessionController.Instance;
            if (controller != null)
            {
                controller.GameStartSucceeded -= OnGameStartSucceeded;
                controller.GameStartFailed -= OnGameStartFailed;
                controller.GameEnded -= OnGameEnded;
            }

            m_SessionSubscribed = false;
        }

        private void OnGameStartSucceeded(ulong[] participantIds)
        {
            if (!IsServer)
            {
                return;
            }

            ApplyPlayerIds(participantIds);
            Phase.Value = GamePhase.Choosing;
            BeginGameFlow();
        }

        private void OnGameStartFailed()
        {
            if (!IsServer)
            {
                return;
            }

            Phase.Value = GamePhase.StartFailed;
            ParticipantIds.Clear();
            PlayerNames.Clear();
            m_ClientIds.Clear();
        }

        private void OnGameEnded()
        {
            if (!IsServer)
            {
                return;
            }

            m_GameInProgress = false;
        }

        private void BeginGameFlow()
        {
            if (m_GameInProgress)
            {
                return;
            }

            _ = RunGameFlowAsync();
        }

        private async Task RunGameFlowAsync()
        {
            try
            {
                await ExecuteGameRoundAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[RockPaperScissorsGame] Fatal error: {e.Message}");
            }
            finally
            {
                GameSessionController.Instance?.NotifyGameEnded();
            }
        }

        private void ApplyPlayerIds(IReadOnlyList<ulong> participantIds)
        {
            m_ClientIds = participantIds != null ? new List<ulong>(participantIds) : new List<ulong>();

            while (m_ClientIds.Count < RequiredGamePlayers)
            {
                var cpuId = CpuPlayerBaseId;
                while (m_ClientIds.Contains(cpuId))
                {
                    cpuId++;
                }

                m_ClientIds.Add(cpuId);
            }

            ParticipantIds.Clear();
            foreach (var id in m_ClientIds)
            {
                ParticipantIds.Add(id);
            }

            UpdatePlayerNames();
        }

        private void UpdatePlayerNames()
        {
            PlayerNames.Clear();

            var snapshot = ServerSingleton.Instance?.GameManager?.GetAllConnectedPlayers();

            foreach (var id in m_ClientIds)
            {
                FixedString64Bytes name;

                if (IsCpuId(id))
                {
                    name = "CPU";
                }
                else if (snapshot != null && snapshot.TryGetValue(id, out var payload))
                {
                    name = ResolvePlayerName(payload, id);
                }
                else
                {
                    name = $"Player{id}";
                }

                PlayerNames.Add(new PlayerNameEntry
                {
                    ClientId = id,
                    Name = name
                });
            }
        }

        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;

        private async Task ExecuteGameRoundAsync()
        {
            Debug.Log("[RockPaperScissorsGame] Starting game round");

            if (m_ClientIds.Count == 0)
            {
                Debug.LogError("[RockPaperScissorsGame] Cannot start round without player IDs");
                return;
            }

            m_GameInProgress = true;
            m_PlayerChoices.Clear();
            m_AllPlayersChosenTcs = new TaskCompletionSource<bool>();

            LastResult.Value = default;

            SeedCpuChoicesIfNeeded();

            // 選択を待つ（30秒タイムアウト）
            bool allChose = await WaitForAllChoicesAsync(TimeSpan.FromSeconds(30));

            if (!allChose)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Timeout - some players didn't choose");
            }

            // 未選択のプレイヤーにランダム手を割り当てて切断
            foreach (var playerId in m_ClientIds)
            {
                if (IsCpuId(playerId))
                {
                    continue;
                }

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

            ResolveRound();

            m_GameInProgress = false;
        }

        private async Task<bool> WaitForAllChoicesAsync(TimeSpan timeout)
        {
            var timeoutTask = Task.Delay(timeout);
            var choicesTask = m_AllPlayersChosenTcs.Task;

            return await Task.WhenAny(choicesTask, timeoutTask) == choicesTask;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            if (IsCpuId(clientId))
            {
                return;
            }

            UpdatePlayerNames();

            // ゲーム中で、まだ選択していない場合はランダム手を割り当て
            if (m_GameInProgress && !m_PlayerChoices.ContainsKey(clientId))
            {
                AssignRandomHandToPlayer(clientId, "disconnected");

                // 必要な人数分の選択が揃ったらTCSを完了
                if (m_PlayerChoices.Count >= m_ClientIds.Count)
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

        private void SeedCpuChoicesIfNeeded()
        {
            foreach (var playerId in m_ClientIds)
            {
                if (IsCpuId(playerId) && !m_PlayerChoices.ContainsKey(playerId))
                {
                    AssignRandomHandToPlayer(playerId, "cpu");
                }
            }

            if (m_PlayerChoices.Count >= m_ClientIds.Count)
            {
                m_AllPlayersChosenTcs?.TrySetResult(true);
            }
        }

        private void AssignRandomHandToPlayer(ulong playerId, string reason)
        {
            var randomHand = (Hand)UnityEngine.Random.Range(1, 4); // Rock(1), Paper(2), Scissors(3)
            m_PlayerChoices[playerId] = randomHand;
            Debug.Log($"[RockPaperScissorsGame] Auto-assigned {randomHand} to {reason} player {playerId}");
            if (m_PlayerChoices.Count >= m_ClientIds.Count)
            {
                m_AllPlayersChosenTcs?.TrySetResult(true);
            }
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
            Debug.Log($"[Server] Choice received: {choice} from {clientId} ({m_PlayerChoices.Count}/{m_ClientIds.Count})");

            if (m_PlayerChoices.Count >= m_ClientIds.Count)
            {
                m_AllPlayersChosenTcs?.TrySetResult(true);
            }
        }

        // ========== Helper Methods ==========
        private static RoundOutcome DetermineOutcome(Hand myHand, Hand opponentHand)
        {
            if (myHand == opponentHand)
            {
                return RoundOutcome.Draw;
            }

            return ((myHand == Hand.Rock && opponentHand == Hand.Scissors) ||
                    (myHand == Hand.Paper && opponentHand == Hand.Rock) ||
                    (myHand == Hand.Scissors && opponentHand == Hand.Paper))
                ? RoundOutcome.Win
                : RoundOutcome.Lose;
        }

        private void ResolveRound()
        {
            if (m_ClientIds.Count < 2)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Not enough participants to resolve round");
                return;
            }

            ulong p1 = m_ClientIds[0];
            ulong p2 = m_ClientIds[1];

            if (!m_PlayerChoices.TryGetValue(p1, out var h1))
            {
                h1 = Hand.None;
            }

            if (!m_PlayerChoices.TryGetValue(p2, out var h2))
            {
                h2 = Hand.None;
            }

            var result = new RpsResult
            {
                P1 = p1,
                P2 = p2,
                H1 = h1,
                H2 = h2,
                P1Outcome = (byte)DetermineOutcome(h1, h2),
                P2Outcome = (byte)DetermineOutcome(h2, h1)
            };

            Phase.Value = GamePhase.Resolving;
            LastResult.Value = result;
            Phase.Value = GamePhase.Finished;
        }

        private void OnClientConnected(ulong clientId)
        {
            if (!IsServer) return;

            UpdatePlayerNames();
        }

        private static FixedString64Bytes ResolvePlayerName(Dictionary<string, object> payload, ulong clientId)
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
    }
}
#endif
