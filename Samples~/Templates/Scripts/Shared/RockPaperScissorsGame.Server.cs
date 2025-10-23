#if UNITY_SERVER || ENABLE_UCS_SERVER
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using DedicatedServerMultiplayerSample.Server.Bootstrap;
using DedicatedServerMultiplayerSample.Server.Session;
using DedicatedServerMultiplayerSample.Shared;

namespace DedicatedServerMultiplayerSample.Samples.Shared
{
    public partial class RockPaperScissorsGame
    {
        private const float RoundTimeoutSeconds = 30f;

        private sealed class RoundState
        {
            public List<ulong> Players { get; } = new();
            public Dictionary<ulong, Hand> Choices { get; } = new();
            public TaskCompletionSource<bool> AllChosen { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private RoundState _currentRound;

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

        partial void OnServerDespawn()
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            _currentRound = null;
        }

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
                var successTask = controller.WaitForGameStartSucceededAsync(ct);
                var failTask = controller.WaitForGameStartFailedAsync(ct);

                if (await Task.WhenAny(successTask, failTask).ConfigureAwait(false) == failTask)
                {
                    Phase.Value = GamePhase.StartFailed;
                    PlayerIds.Clear();
                    PlayerNames.Clear();
                    return;
                }

                var participantIds = await successTask.ConfigureAwait(false);

                ApplyPlayerIds(participantIds);
                Phase.Value = GamePhase.Choosing;

                await ExecuteGameRoundAsync(ct).ConfigureAwait(false);

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

        private async Task ExecuteGameRoundAsync(CancellationToken ct)
        {
            var round = new RoundState();
            round.Players.AddRange(PlayerIds);

            if (round.Players.Count < RequiredGamePlayers)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Not enough players to start round");
                return;
            }

            _currentRound = round;

            foreach (var playerId in round.Players)
            {
                if (IsCpuId(playerId))
                {
                    AssignRandomHand(round, playerId, "cpu");
                }
            }

            if (round.Choices.Count >= round.Players.Count)
            {
                round.AllChosen.TrySetResult(true);
            }

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(RoundTimeoutSeconds));

                try
                {
                    await round.AllChosen.Task.WaitOrCancel(timeoutCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    Debug.LogWarning("[RockPaperScissorsGame] Timeout - assigning hands to non-responsive players");

                    foreach (var playerId in round.Players)
                    {
                        if (IsCpuId(playerId) || round.Choices.ContainsKey(playerId))
                        {
                            continue;
                        }

                        AssignRandomHand(round, playerId, "timeout");
                        ServerSingleton.Instance?.GameManager?.DisconnectClient(playerId, "Selection timeout");
                    }
                }
            }

            ResolveRound(round);
            _currentRound = null;
        }

        private static void AssignRandomHand(RoundState round, ulong playerId, string reason)
        {
            var randomHand = (Hand)UnityEngine.Random.Range(1, 4);
            round.Choices[playerId] = randomHand;
            Debug.Log($"[RockPaperScissorsGame] Auto-assigned {randomHand} to {reason} player {playerId}");

            if (round.Choices.Count >= round.Players.Count)
            {
                round.AllChosen.TrySetResult(true);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            RebuildPlayerNames();

            var round = _currentRound;
            if (round == null)
            {
                return;
            }

            if (!round.Players.Contains(clientId))
            {
                return;
            }

            if (round.Choices.ContainsKey(clientId))
            {
                Debug.Log($"[RockPaperScissorsGame] Disconnected player {clientId} already chose {round.Choices[clientId]}");
                return;
            }

            AssignRandomHand(round, clientId, "disconnected");
        }

        private void OnClientConnected(ulong clientId)
        {
            RebuildPlayerNames();
        }

        // ========== ServerRpc Implementation ==========
        partial void HandleSubmitChoice(ulong clientId, Hand choice)
        {
            var round = _currentRound;
            if (round == null)
            {
                return;
            }

            if (!round.Players.Contains(clientId))
            {
                Debug.LogWarning($"[Server] Ignoring choice from non-participant {clientId}");
                return;
            }

            if (round.Choices.ContainsKey(clientId))
            {
                Debug.LogWarning($"[Server] Ignoring duplicate choice from {clientId} (already has {round.Choices[clientId]})");
                return;
            }

            round.Choices[clientId] = choice;
            Debug.Log($"[Server] Choice received: {choice} from {clientId} ({round.Choices.Count}/{round.Players.Count})");

            if (round.Choices.Count >= round.Players.Count)
            {
                round.AllChosen.TrySetResult(true);
            }
        }

        private void ResolveRound(RoundState round)
        {
            if (round.Players.Count < 2)
            {
                Debug.LogWarning("[RockPaperScissorsGame] Not enough participants to resolve round");
                return;
            }

            ulong p1 = round.Players[0];
            ulong p2 = round.Players[1];

            var h1 = round.Choices.TryGetValue(p1, out var choice1) ? choice1 : Hand.None;
            var h2 = round.Choices.TryGetValue(p2, out var choice2) ? choice2 : Hand.None;

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
