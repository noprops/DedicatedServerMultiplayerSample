#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Server.Bootstrap;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class RockPaperScissorsGame
    {
        private const float RoundTimeoutSeconds = 30f;

        // ========== Server Initialization ==========
        /// <summary>
        /// Server-side setup that clears shared state, subscribes to network events, and starts the game flow.
        /// </summary>
        partial void OnServerSpawn()
        {
            Phase.Value = GamePhase.WaitingForPlayers;
            LastResult.Value = default;
            PlayerIds.Clear();
            PlayerNames.Clear();

            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            _ = RunServerGameFlowAsync(CancellationToken.None);
        }

        /// <summary>
        /// Cleans up server-side subscriptions when the game despawns.
        /// </summary>
        partial void OnServerDespawn()
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        // ========== Server Main Flow ==========
        /// <summary>
        /// Coordinates waiting for the session to start, running the round, and notifying completion.
        /// </summary>
        private async Task RunServerGameFlowAsync(CancellationToken ct)
        {
            var controller = GameSessionController.Instance;
            if (controller == null)
            {
                Debug.LogError("[RockPaperScissorsGame] No GameSessionController");
                return;
            }

            try
            {
                var startSucceededTask = controller.WaitForGameStartSucceededAsync(ct);
                var startFailedTask = controller.WaitForGameStartFailedAsync(ct);

                var completed = await Task.WhenAny(startSucceededTask, startFailedTask).ConfigureAwait(false);
                if (completed == startFailedTask)
                {
                    Phase.Value = GamePhase.StartFailed;
                    PlayerIds.Clear();
                    PlayerNames.Clear();
                    return;
                }

                var participantIds = await startSucceededTask.ConfigureAwait(false);

                ApplyPlayerIds(participantIds);
                Phase.Value = GamePhase.Choosing;

                await ExecuteGameRoundAsync().ConfigureAwait(false);

                controller.NotifyGameEnded();
            }
            catch (OperationCanceledException)
            {
                // ignore cancellations
            }
            catch (TimeoutException)
            {
                Phase.Value = GamePhase.StartFailed;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RockPaperScissorsGame] Fatal error: {e.Message}");
            }
        }

        /// <summary>
        /// Copies participant IDs into the network lists, fills CPU slots, and refreshes player names.
        /// </summary>
        private void ApplyPlayerIds(IReadOnlyList<ulong> participantIds)
        {
            var ids = participantIds != null ? new List<ulong>(participantIds) : new List<ulong>();
            var seen = new HashSet<ulong>(ids);

            while (ids.Count < RequiredGamePlayers)
            {
                var cpuId = NextCpuId(seen);
                seen.Add(cpuId);
                ids.Add(cpuId);
            }

            PlayerIds.Clear();
            PlayerNames.Clear();

            foreach (var id in ids)
            {
                PlayerIds.Add(id);
            }

            RebuildPlayerNames();
        }

        /// <summary>
        /// Builds the networked player names list using server-side snapshots.
        /// </summary>
        private void RebuildPlayerNames()
        {
            PlayerNames.Clear();

            var snapshot = ServerSingleton.Instance?.GameManager?.GetAllConnectedPlayers();

            foreach (var id in PlayerIds)
            {
                PlayerNames.Add(GetDisplayName(snapshot, id));
            }
        }

        private static bool IsCpuId(ulong clientId) => clientId >= CpuPlayerBaseId;

        private ulong NextCpuId(HashSet<ulong> existing)
        {
            var cpuId = CpuPlayerBaseId;
            while (existing.Contains(cpuId))
            {
                cpuId++;
            }

            return cpuId;
        }

        private FixedString64Bytes GetDisplayName(Dictionary<ulong, Dictionary<string, object>> snapshot, ulong id)
        {
            if (IsCpuId(id))
            {
                return "CPU";
            }

            if (snapshot != null && snapshot.TryGetValue(id, out var payload))
            {
                return ResolvePlayerName(payload, id);
            }

            return $"Player{id}";
        }

        private async Task ExecuteGameRoundAsync()
        {
            Debug.Log("[RockPaperScissorsGame] Starting game round");

            if (PlayerIds.Count == 0)
            {
                Debug.LogError("[RockPaperScissorsGame] Cannot start round without player IDs");
                return;
            }

            var participants = new List<ulong>(PlayerIds);

            m_GameInProgress = true;
            m_PlayerChoices.Clear();
            m_AllPlayersChosenTcs = new TaskCompletionSource<bool>();

            LastResult.Value = default;

            SeedCpuChoicesIfNeeded();

            bool allChose = await WaitForAllChoicesAsync(TimeSpan.FromSeconds(RoundTimeoutSeconds)).ConfigureAwait(false);

            if (!allChose)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Timeout - some players didn't choose");

                foreach (var playerId in participants)
                {
                    if (IsCpuId(playerId) || m_PlayerChoices.ContainsKey(playerId))
                    {
                        continue;
                    }

                    AssignRandomHandToPlayer(playerId, "timeout");
                    ServerSingleton.Instance?.GameManager?.DisconnectClient(playerId, "Selection timeout");
                }
            }

            ResolveRound(participants);

            m_GameInProgress = false;
        }

        private async Task<bool> WaitForAllChoicesAsync(TimeSpan timeout)
        {
            var timeoutTask = Task.Delay(timeout);
            var choicesTask = m_AllPlayersChosenTcs.Task;

            return await Task.WhenAny(choicesTask, timeoutTask).ConfigureAwait(false) == choicesTask;
        }

        private void OnClientDisconnected(ulong clientId)
        {
            RebuildPlayerNames();

            if (IsCpuId(clientId))
            {
                return;
            }

            if (m_GameInProgress && !m_PlayerChoices.ContainsKey(clientId))
            {
                AssignRandomHandToPlayer(clientId, "disconnected");

                if (m_PlayerChoices.Count >= PlayerIds.Count)
                {
                    m_AllPlayersChosenTcs?.TrySetResult(true);
                }
            }
            else if (m_GameInProgress && m_PlayerChoices.ContainsKey(clientId))
            {
                Debug.Log($"[RockPaperScissorsGame] Disconnected player {clientId} already chose {m_PlayerChoices[clientId]}");
            }
        }

        private void SeedCpuChoicesIfNeeded()
        {
            foreach (var playerId in PlayerIds)
            {
                if (IsCpuId(playerId) && !m_PlayerChoices.ContainsKey(playerId))
                {
                    AssignRandomHandToPlayer(playerId, "cpu");
                }
            }

            if (m_PlayerChoices.Count >= PlayerIds.Count)
            {
                m_AllPlayersChosenTcs?.TrySetResult(true);
            }
        }

        private void AssignRandomHandToPlayer(ulong playerId, string reason)
        {
            var randomHand = (Hand)UnityEngine.Random.Range(1, 4);
            m_PlayerChoices[playerId] = randomHand;
            Debug.Log($"[RockPaperScissorsGame] Auto-assigned {randomHand} to {reason} player {playerId}");
            if (m_PlayerChoices.Count >= PlayerIds.Count)
            {
                m_AllPlayersChosenTcs?.TrySetResult(true);
            }
        }

        // ========== ServerRpc Implementation ==========
        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            if (!m_GameInProgress) return;

            if (m_PlayerChoices.ContainsKey(clientId))
            {
                Debug.LogWarning($"[Server] Ignoring duplicate choice from {clientId} (already has {m_PlayerChoices[clientId]})");
                return;
            }

            m_PlayerChoices[clientId] = choice;
            Debug.Log($"[Server] Choice received: {choice} from {clientId} ({m_PlayerChoices.Count}/{PlayerIds.Count})");

            if (m_PlayerChoices.Count >= PlayerIds.Count)
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

        private void ResolveRound(IReadOnlyList<ulong> participants)
        {
            if (participants.Count < 2)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Not enough participants to resolve round");
                return;
            }

            ulong p1 = participants[0];
            ulong p2 = participants[1];

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
            RebuildPlayerNames();
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
